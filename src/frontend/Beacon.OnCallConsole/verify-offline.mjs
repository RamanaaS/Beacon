// Verifies the offline queue for real: log in while the API is up, wait for the API to be taken
// down externally, attempt an action (should queue locally), wait for the API to come back
// externally, then confirm the background flush timer actually drains the queue — not simulated,
// against the real main.js poll/flush loop on its real 15s interval.
import { _electron as electron } from "playwright";
import { mkdir } from "node:fs/promises";
import axios from "axios";

const screenshotDir = "verify-screenshots";
await mkdir(screenshotDir, { recursive: true });

const email = `oncall-offline-verify-${Date.now()}@beacon.test`;
const password = "Correct-Horse-Battery-9";

console.log("STEP registering test user (API must be up right now)");
await axios.post("http://localhost:5252/api/auth/register", {
  email, password, displayName: "Offline Verify Bot", role: "Responder",
});

const launchEnv = { ...process.env };
delete launchEnv.ELECTRON_RUN_AS_NODE;
const app = await electron.launch({ args: [".", "--no-sandbox"], env: launchEnv });
const window = await app.firstWindow();

try {
  await window.waitForSelector("text=On-Call Console");
  await window.fill("#email", email);
  await window.fill("#password", password);
  await window.click("button[type=submit]");
  await window.waitForSelector("#dashboard-view:not(.hidden)", { timeout: 10000 });
  console.log("STEP logged-in-ok");

  const incidentCount = await window.locator(".incident").count();
  if (incidentCount === 0) {
    console.log("STEP no-incidents-available — cannot test acknowledge, exiting");
    process.exit(1);
  }

  console.log("STEP waiting-for-api-down — stop the backend now");
  // Poll the health endpoint from inside this script until it actually fails, instead of a
  // blind sleep, so this isn't racing an arbitrary guess at how long the external stop takes.
  await waitUntil(async () => {
    try { await axios.get("http://localhost:5252/health", { timeout: 1000 }); return false; }
    catch { return true; }
  }, 60000);
  console.log("STEP api-confirmed-down");

  console.log("STEP attempting-acknowledge-while-offline");
  await window.locator(".incident").first().locator("button:has-text('Acknowledge')").click();
  await window.waitForSelector(".queued-note", { timeout: 8000 });
  await window.screenshot({ path: `${screenshotDir}/offline-01-queued.png` });
  console.log("STEP queued-note-appeared");

  const depthWhileOffline = await window.locator("#queue-count").textContent();
  console.log(`STEP queue-depth-while-offline=${depthWhileOffline}`);

  console.log("STEP waiting-for-api-up — start the backend now");
  await waitUntil(async () => {
    try { await axios.get("http://localhost:5252/health", { timeout: 1000 }); return true; }
    catch { return false; }
  }, 60000);
  console.log("STEP api-confirmed-up");

  console.log("STEP waiting-for-flush-tick (up to 25s, matching the 15s poll interval)");
  // waitForSelector defaults to state:"visible" — useless for confirming an element carries
  // the *hidden* class, since a hidden element is never "visible" by definition. Poll its class
  // list directly instead.
  await waitUntil(async () => {
    const classes = await window.locator("#queue-indicator").getAttribute("class");
    return classes?.includes("hidden") ?? false;
  }, 25000);
  await window.screenshot({ path: `${screenshotDir}/offline-02-flushed.png` });
  console.log("STEP queue-flushed-automatically");

  console.log("\nALL OFFLINE-QUEUE STEPS PASSED");
} catch (err) {
  await window.screenshot({ path: `${screenshotDir}/offline-FAILURE.png` });
  console.error("FAILED:", err);
  process.exitCode = 1;
} finally {
  await app.close();
}

function waitUntil(conditionFn, timeoutMs) {
  const start = Date.now();
  return new Promise((resolve, reject) => {
    const tick = async () => {
      if (await conditionFn()) return resolve();
      if (Date.now() - start > timeoutMs) return reject(new Error("waitUntil timed out"));
      setTimeout(tick, 1000);
    };
    void tick();
  });
}
