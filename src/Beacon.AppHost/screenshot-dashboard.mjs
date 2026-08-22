import { chromium } from "playwright";

const url = process.argv[2];
if (!url) {
  console.error("Usage: node screenshot-dashboard.mjs <dashboard-login-url>");
  process.exit(1);
}

const browser = await chromium.launch();
const page = await browser.newPage({ ignoreHTTPSErrors: true });
await page.goto(url);
await page.waitForTimeout(3000);
await page.screenshot({ path: "aspire-dashboard.png", fullPage: true });
await browser.close();
console.log("Saved aspire-dashboard.png");
