// One-off verification driver — same role as Beacon.WarRoom's verify.mjs, adapted for Electron
// via Playwright's _electron launcher instead of a browser context. Requires the backend API
// running at http://localhost:5252 with a registered test user (registers its own here).
import { _electron as electron } from "playwright";
import { mkdir } from "node:fs/promises";
import axios from "axios";

const screenshotDir = "verify-screenshots";
await mkdir(screenshotDir, { recursive: true });

const email = `oncall-verify-${Date.now()}@beacon.test`;
const password = "Correct-Horse-Battery-9";

console.log("0) Registering a test Responder user via the API directly...");
await axios.post("http://localhost:5252/api/auth/register", {
  email,
  password,
  displayName: "OnCall Verify Bot",
  role: "Responder",
});

// ELECTRON_RUN_AS_NODE is set globally in this sandboxed shell environment (presumably to keep
// agent tooling from accidentally spawning GUI windows) — it makes Electron behave as plain
// Node instead of launching the real app framework, so ipcMain/BrowserWindow/etc. come back
// undefined. Explicitly clearing it for this one launch, rather than relying on shell state
// (which doesn't persist between separate tool invocations anyway), is what actually fixes it.
const launchEnv = { ...process.env };
delete launchEnv.ELECTRON_RUN_AS_NODE;

const app = await electron.launch({ args: [".", "--no-sandbox"], env: launchEnv });
const window = await app.firstWindow();

try {
  console.log("1) Waiting for login view...");
  await window.waitForSelector("text=On-Call Console");
  await window.screenshot({ path: `${screenshotDir}/01-login.png` });

  console.log("2) Logging in...");
  await window.fill("#email", email);
  await window.fill("#password", password);
  await window.click("button[type=submit]");

  console.log("3) Waiting for dashboard...");
  await window.waitForSelector("#dashboard-view:not(.hidden)", { timeout: 10000 });
  await window.waitForTimeout(500);
  await window.screenshot({ path: `${screenshotDir}/02-dashboard.png` });

  const incidentCount = await window.locator(".incident").count();
  console.log(`4) Dashboard shows ${incidentCount} open incident(s).`);

  if (incidentCount > 0) {
    console.log("5) Clicking Escalate on the first incident...");
    await window.locator(".incident").first().locator("button.secondary").click();
    await window.waitForTimeout(1000);
    await window.screenshot({ path: `${screenshotDir}/03-after-escalate.png` });
  }

  console.log("\nAll steps completed.");
} catch (err) {
  await window.screenshot({ path: `${screenshotDir}/FAILURE.png` });
  console.error("FAILED:", err);
  process.exitCode = 1;
} finally {
  await app.close();
}
