// One-off verification driver, not part of the app or its build — run manually with
// `node verify.mjs` while both dev servers are up. Not committed as a permanent test; Playwright
// itself is the real E2E tool slated for a later phase (per ARCHITECTURE.md Table C). This exists
// purely to actually look at the running app in a real browser before calling Phase 9 done,
// per the "run it, don't just claim it works" rule for UI changes.
import { chromium } from "playwright";

const screenshotDir = "verify-screenshots";
await import("node:fs/promises").then((fs) => fs.mkdir(screenshotDir, { recursive: true }));

const browser = await chromium.launch();
const page = await browser.newPage();
const consoleErrors = [];
page.on("console", (msg) => {
  if (msg.type() === "error") consoleErrors.push(msg.text());
});
page.on("pageerror", (err) => consoleErrors.push(String(err)));

const email = `warroom-verify-${Date.now()}@beacon.test`;

try {
  console.log("1) Loading login page...");
  await page.goto("http://localhost:5173");
  await page.waitForSelector("text=Beacon");
  await page.screenshot({ path: `${screenshotDir}/01-login.png` });

  console.log("2) Switching to register mode and registering...");
  await page.click("text=Need an account? Register");
  await page.fill("input[type=email]", email);
  await page.fill("input[type=password]", "Correct-Horse-Battery-9");
  await page.fill("form.auth-form input:not([type])", "Verify Bot");
  await page.selectOption("select", "Responder");
  await page.screenshot({ path: `${screenshotDir}/02-register-form.png` });
  await page.click("button[type=submit]");

  console.log("3) Waiting for dashboard...");
  await page.waitForSelector("text=Declare Incident", { timeout: 10000 });
  await page.screenshot({ path: `${screenshotDir}/03-dashboard.png` });

  console.log("4) Waiting for telemetry to connect...");
  await page.waitForSelector("text=● live", { timeout: 10000 });
  await page.waitForTimeout(2500); // let at least one batch of samples arrive
  await page.screenshot({ path: `${screenshotDir}/04-telemetry-live.png` });

  console.log("5) Declaring an incident...");
  await page.click("text=Declare Incident");
  await page.fill(".declare-form input", "Verify script test incident");
  await page.selectOption(".declare-form select", "Sev2_High");
  await page.click(".declare-form button[type=submit]");
  await page.waitForSelector("text=Verify script test incident");
  await page.screenshot({ path: `${screenshotDir}/05-declared-in-list.png` });

  console.log("6) Opening incident detail...");
  await page.click("text=Verify script test incident");
  await page.waitForSelector("text=Timeline");
  await page.waitForSelector("li:has-text('Declared')");
  await page.screenshot({ path: `${screenshotDir}/06-detail-declared.png` });

  console.log("7) Acknowledging...");
  await page.click("button:has-text('Acknowledge')");
  await page.waitForSelector("li:has-text('Acknowledged')");
  await page.screenshot({ path: `${screenshotDir}/07-acknowledged.png` });

  console.log("8) Resolving...");
  await page.fill("input[placeholder='Resolution summary']", "Fixed by the verification script.");
  await page.click("button:has-text('Resolve')");
  await page.waitForSelector("text=Download Excel report");
  await page.waitForSelector("li:has-text('Resolved')");
  await page.screenshot({ path: `${screenshotDir}/08-resolved-with-reports.png` });

  console.log("\nAll steps completed.");
  console.log("Console errors captured:", consoleErrors.length === 0 ? "none" : consoleErrors);
} catch (err) {
  await page.screenshot({ path: `${screenshotDir}/FAILURE.png` });
  console.error("FAILED:", err);
  console.error("Console errors so far:", consoleErrors);
  process.exitCode = 1;
} finally {
  await browser.close();
}
