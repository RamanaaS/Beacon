// Playwright verification for Beacon.Admin (Phase 11).
// Registers a test Admin user directly against the API (Login page has no
// registration UI by design â€” this is an internal ops tool, and registration
// deliberately lets the caller pick their own role per AuthEndpoints' own
// doc comment). Then drives a real browser through login -> users ->
// analytics, and confirms the live-alerts ticker receives a real SignalR
// broadcast triggered by a separate API call mid-test.

import { chromium } from "playwright";
import { randomUUID } from "node:crypto";

const API_BASE = "http://localhost:5252";
const ADMIN_BASE = "http://localhost:5121";

const suffix = randomUUID().slice(0, 8);
const adminEmail = `admin-${suffix}@beacon.test`;
const otherEmail = `viewer-${suffix}@beacon.test`;
const password = "Verify!Passw0rd1";

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

async function main() {
  console.log(`Registering test admin user: ${adminEmail}`);
  const registerAdmin = await api("/api/auth/register", {
    method: "POST",
    body: JSON.stringify({ email: adminEmail, password, displayName: "Verify Admin", role: "Admin" }),
  });
  if (registerAdmin.status >= 300) throw new Error(`Register admin failed: ${registerAdmin.status} ${JSON.stringify(registerAdmin.body)}`);

  console.log(`Registering second (Viewer) user: ${otherEmail}`);
  const registerOther = await api("/api/auth/register", {
    method: "POST",
    body: JSON.stringify({ email: otherEmail, password, displayName: "Verify Viewer", role: "Viewer" }),
  });
  if (registerOther.status >= 300) throw new Error(`Register viewer failed: ${registerOther.status} ${JSON.stringify(registerOther.body)}`);

  const adminLogin = await api("/api/auth/login", {
    method: "POST",
    body: JSON.stringify({ email: adminEmail, password }),
  });
  if (adminLogin.status !== 200) throw new Error(`Admin login failed: ${adminLogin.status} ${JSON.stringify(adminLogin.body)}`);
  const adminToken = adminLogin.body.token;
  console.log(`Admin API login OK, roles: ${adminLogin.body.roles}`);

  const browser = await chromium.launch();
  const page = await browser.newPage();
  page.on("console", (msg) => console.log(`BROWSER CONSOLE [${msg.type()}]: ${msg.text()}`));
  page.on("pageerror", (err) => console.log(`BROWSER PAGE ERROR: ${err}`));

  try {
    console.log("Navigating to login page...");
    await page.goto(`${ADMIN_BASE}/login`);
    await page.screenshot({ path: "verify-screenshots/verify-01-login.png" });

    // Blazor's antiforgery machinery injects a hidden __RequestVerificationToken
    // <input> before the form fields. The email InputText has no explicit `type`
    // attribute (defaults to text per HTML spec but isn't literally present), so
    // select by absence of [type] rather than [type="text"].
    await page.locator('form input:not([type])').fill(adminEmail);
    await page.locator('input[type="password"]').fill(password);
    await page.click('button[type="submit"]');

    await page.waitForURL(/\/users$/, { timeout: 10_000 });
    console.log("Logged in via UI, landed on /users");
    await page.waitForTimeout(500);
    await page.screenshot({ path: "verify-screenshots/verify-02-users.png" });

    const bodyText = await page.textContent("body");
    if (bodyText?.includes("Admin role required")) {
      throw new Error("Users page shows the non-admin fallback view even though the test user registered with role=Admin.");
    }

    const rowCount = await page.locator("table tbody tr").count();
    console.log(`Users table rendered with ${rowCount} row(s).`);
    if (rowCount < 2) throw new Error(`Expected at least 2 user rows, got ${rowCount}.`);

    // Change the *other* (viewer) user's role, not our own, so the logged-in
    // session's cached JWT claims stay consistent with what the UI can do.
    const otherRow = page.locator("table tbody tr", { hasText: otherEmail });
    await otherRow.locator("select").selectOption("Responder");
    await page.waitForTimeout(500);
    const savedCount = await otherRow.locator("text=Saved").count();
    console.log(`Role change applied to ${otherEmail}, 'Saved' indicator present: ${savedCount > 0}`);
    await page.screenshot({ path: "verify-screenshots/verify-03-users-role-changed.png" });

    const verifyRoleApi = await api("/api/admin/users", { headers: { Authorization: `Bearer ${adminToken}` } });
    const updatedOther = verifyRoleApi.body.find((u) => u.email === otherEmail);
    console.log(`API confirms ${otherEmail} role is now: ${updatedOther?.roles}`);
    if (!updatedOther?.roles?.includes("Responder")) throw new Error("Role change did not persist server-side.");

    console.log("Navigating to /analytics via the nav link (a hard page.goto would start a new " +
        "circuit with no token, since AdminAuthState is in-memory-only per circuit by design)...");
    await page.click('a[href="analytics"], a[href="/analytics"]');
    await page.waitForURL(/\/analytics$/, { timeout: 10_000 });
    await page.waitForTimeout(1500); // allow OnAfterRenderAsync + ApexCharts JS interop to complete
    await page.screenshot({ path: "verify-screenshots/verify-04-analytics.png" });

    const chartContainerId = await page.evaluate(() => {
      const el = document.querySelector('[id^="severity-chart-"]');
      return el ? el.id : null;
    });
    const chartSvgCount = chartContainerId
      ? await page.locator(`#${chartContainerId} svg`).count()
      : 0;
    console.log(`ApexCharts container found: ${chartContainerId !== null}, rendered <svg> inside it: ${chartSvgCount}`);
    if (!chartContainerId || chartSvgCount === 0) throw new Error("ApexCharts bar chart did not actually render an SVG.");

    console.log("Triggering a live SignalR alert via a separate incident-declare API call...");
    const declareTitle = `Playwright live-alert verification ${suffix}`;
    const declare = await api("/api/incidents/", {
      method: "POST",
      headers: { Authorization: `Bearer ${adminToken}` },
      body: JSON.stringify({ title: declareTitle, severity: "Sev2_High", alertFingerprint: `verify-${suffix}` }),
    });
    console.log(`Declare-incident call status: ${declare.status}`);
    if (declare.status >= 300) throw new Error(`Declare incident failed: ${declare.status} ${JSON.stringify(declare.body)}`);

    await page.click('a[href="users"], a[href="/users"]');
    await page.waitForURL(/\/users$/, { timeout: 10_000 });
    await page.waitForTimeout(2500); // give the SignalR connection + broadcast time to land
    await page.screenshot({ path: "verify-screenshots/verify-05-live-alert.png" });

    const tickerText = await page.locator("body").textContent();
    const tickerGotIt = tickerText?.includes(declareTitle) ?? false;
    console.log(`Live alert ticker mentions the declared title: ${tickerGotIt}`);
    if (!tickerGotIt) throw new Error("Live-alerts ticker did not show the SignalR-broadcast incident.");

    console.log("Verification run complete â€” all checks passed.");
  } finally {
    await browser.close();
  }
}

main().catch((err) => {
  console.error("VERIFY FAILED:", err);
  process.exit(1);
});

