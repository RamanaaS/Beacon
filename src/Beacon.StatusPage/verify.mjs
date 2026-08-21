// Playwright verification for Beacon.StatusPage (Phase 12 frontend).
// The dev DB already carries leftover incidents from every earlier phase's verification runs, so
// this deliberately never assumes a clean "Operational" baseline — it declares one distinctly
// titled incident, confirms it (and the resulting banner) shows up, resolves it, and confirms it
// disappears again. It also genuinely waits out the API's 15s output cache rather than asserting
// against a response that might just be a stale cached hit.

import { chromium } from "playwright";
import { randomUUID } from "node:crypto";

const API_BASE = "http://localhost:5252";
const PAGE_BASE = "http://localhost:5146";
const CACHE_WAIT_MS = 16_000;

async function api(path, options = {}) {
  const res = await fetch(`${API_BASE}${path}`, {
    ...options,
    headers: { "Content-Type": "application/json", ...(options.headers ?? {}) },
  });
  const text = await res.text();
  let body;
  try { body = text ? JSON.parse(text) : null; } catch { body = text; }
  return { status: res.status, body };
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function main() {
  const suffix = randomUUID().slice(0, 8);
  const email = `responder-${suffix}@beacon.test`;
  const password = "Verify!Passw0rd1";

  console.log(`Registering a Responder test user: ${email}`);
  const register = await api("/api/auth/register", {
    method: "POST",
    body: JSON.stringify({ email, password, displayName: "Verify Responder", role: "Responder" }),
  });
  if (register.status >= 300) throw new Error(`Register failed: ${register.status} ${JSON.stringify(register.body)}`);

  const login = await api("/api/auth/login", { method: "POST", body: JSON.stringify({ email, password }) });
  if (login.status !== 200) throw new Error(`Login failed: ${login.status} ${JSON.stringify(login.body)}`);
  const token = login.body.token;

  const browser = await chromium.launch();
  const page = await browser.newPage();
  page.on("pageerror", (err) => console.log(`BROWSER PAGE ERROR: ${err}`));

  try {
    console.log("Loading the status page before declaring anything...");
    await page.goto(PAGE_BASE);
    await page.screenshot({ path: "verify-screenshots/verify-01-baseline.png" });

    const bannerText = await page.locator(".status-banner").textContent();
    console.log(`Baseline banner: "${bannerText?.trim()}"`);
    if (!bannerText || bannerText.includes("temporarily unavailable")) {
      throw new Error("Status page reports Beacon.Api as unreachable — check the API is actually running.");
    }

    const componentCount = await page.locator(".component-dot").count();
    console.log(`Components listed: ${componentCount}`);
    if (componentCount !== 3) throw new Error(`Expected 3 monitored components, found ${componentCount}.`);

    const uptimeCells = await page.locator(".uptime-day").count();
    console.log(`Uptime history cells: ${uptimeCells}`);
    if (uptimeCells !== 30) throw new Error(`Expected 30 uptime cells, found ${uptimeCells}.`);

    const declareTitle = `Playwright status-page verification ${suffix}`;
    console.log(`Declaring a new Sev1 incident: "${declareTitle}"`);
    const declare = await api("/api/incidents/", {
      method: "POST",
      headers: { Authorization: `Bearer ${token}` },
      body: JSON.stringify({ title: declareTitle, severity: "Sev1_Critical", alertFingerprint: `status-verify-${suffix}` }),
    });
    if (declare.status >= 300) throw new Error(`Declare failed: ${declare.status} ${JSON.stringify(declare.body)}`);
    const incidentId = declare.body.id;

    console.log(`Waiting ${CACHE_WAIT_MS / 1000}s for /api/status's output cache to expire...`);
    await sleep(CACHE_WAIT_MS);

    await page.goto(PAGE_BASE);
    await page.screenshot({ path: "verify-screenshots/verify-02-active-incident.png" });

    const bannerAfterDeclare = await page.locator(".status-banner").textContent();
    console.log(`Banner after declaring a Sev1: "${bannerAfterDeclare?.trim()}"`);
    if (!bannerAfterDeclare?.includes("Major Outage")) {
      throw new Error(`Expected "Major Outage" banner after declaring a Sev1, got "${bannerAfterDeclare?.trim()}".`);
    }

    const pageText = await page.locator("body").textContent();
    if (!pageText?.includes(declareTitle)) {
      throw new Error("Declared incident title did not appear in the active-incidents list.");
    }
    console.log("Declared incident appears in the active-incidents list.");

    console.log("Resolving the incident...");
    const resolve = await api(`/api/incidents/${incidentId}/resolve`, {
      method: "POST",
      headers: { Authorization: `Bearer ${token}` },
      body: JSON.stringify({ summary: "Resolved by verify.mjs." }),
    });
    if (resolve.status >= 300) throw new Error(`Resolve failed: ${resolve.status} ${JSON.stringify(resolve.body)}`);

    console.log(`Waiting ${CACHE_WAIT_MS / 1000}s for the cache to expire again...`);
    await sleep(CACHE_WAIT_MS);

    await page.goto(PAGE_BASE);
    await page.screenshot({ path: "verify-screenshots/verify-03-after-resolve.png" });

    const pageTextAfterResolve = await page.locator("body").textContent();
    if (pageTextAfterResolve?.includes(declareTitle)) {
      throw new Error("Resolved incident still appears in the active-incidents list.");
    }
    console.log("Resolved incident no longer appears in the active-incidents list.");

    console.log("Verification run complete — all checks passed.");
  } finally {
    await browser.close();
  }
}

main().catch((err) => {
  console.error("VERIFY FAILED:", err);
  process.exit(1);
});
