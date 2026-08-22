using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace Beacon.E2ETests.SeleniumComparison;

/// <summary>
/// Automates the exact same register -> login -> declare -> acknowledge -> resolve flow against
/// Beacon.WarRoom that Phase 9's verify.mjs (Playwright, JS) already covers — deliberately, so the
/// two are a genuine side-by-side comparison of the same flow through two different tools/
/// languages (ARCHITECTURE.md's Table C: "Selenium (C#) ... Direct side-by-side comparison against
/// Playwright"), not two unrelated smoke tests that happen to sit near each other.
///
/// Requires Beacon.Api (http://localhost:5252) and Beacon.WarRoom's dev server
/// (http://localhost:5173) already running — this test drives real browsers against real running
/// services, the same "actually run it, don't just trust green" discipline every frontend phase in
/// this project has followed, not a mocked DOM.
///
/// Selenium 4's built-in Selenium Manager resolves a matching chromedriver automatically against
/// whatever Chrome is installed — no manual driver-version management, the single biggest
/// operational annoyance Selenium had a reputation for before this landed.
/// </summary>
public sealed class WarRoomIncidentLifecycleTests : IDisposable
{
    private const string AppUrl = "http://localhost:5173";
    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;

    public WarRoomIncidentLifecycleTests()
    {
        var options = new ChromeOptions();
        options.AddArgument("--headless=new");
        options.AddArgument("--window-size=1280,900");
        _driver = new ChromeDriver(options);
        _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Register_login_declare_acknowledge_resolve()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"selenium-{suffix}@beacon.test";
        const string password = "Verify!Passw0rd1";
        var incidentTitle = $"Selenium comparison incident {suffix}";

        _driver.Navigate().GoToUrl(AppUrl);

        // Switch the login page into register mode (no dedicated route — it's a mode toggle on
        // the same form, per LoginPage.tsx).
        ClickByText("Need an account? Register");

        _driver.FindElement(By.CssSelector("input[type='email']")).SendKeys(email);
        _driver.FindElement(By.CssSelector("input[type='password']")).SendKeys(password);
        // The display-name field is the one plain-text <input> with no explicit type attribute —
        // same "select by absence of [type]" trick used against Beacon.Admin's login form
        // (Phase 11), a recurring pattern worth recognizing once you've hit it twice.
        _driver.FindElement(By.CssSelector("form input:not([type])")).SendKeys("Selenium Comparison User");

        var roleSelect = new SelectElement(_driver.FindElement(By.CssSelector("select")));
        roleSelect.SelectByValue("Responder");

        _driver.FindElement(By.CssSelector("button[type='submit']")).Click();

        _wait.Until(d => d.FindElements(By.CssSelector(".dashboard header h1")).Count > 0);

        // Declare
        ClickByText("Declare Incident");
        var declareForm = _wait.Until(d => d.FindElement(By.CssSelector("form.declare-form")));
        declareForm.FindElement(By.CssSelector("input:not([type])")).SendKeys(incidentTitle);
        declareForm.FindElement(By.CssSelector("button[type='submit']")).Click();

        // Wait for the declared incident to appear in the table, then open it.
        var incidentLink = _wait.Until(d =>
        {
            var link = d.FindElements(By.XPath($"//a[contains(text(), '{incidentTitle}')]"));
            return link.Count > 0 ? link[0] : null;
        });
        incidentLink!.Click();

        // Acknowledge
        _wait.Until(d => d.FindElements(By.CssSelector("h1")).Any(e => e.Text == incidentTitle));
        ClickByText("Acknowledge");
        _wait.Until(d => d.FindElements(By.CssSelector(".badge")).Any(e => e.Text == "Acknowledged"));

        // Resolve
        var summaryInput = _driver.FindElement(By.CssSelector("input[placeholder='Resolution summary']"));
        summaryInput.SendKeys("Resolved by the Selenium comparison suite.");
        ClickByText("Resolve");

        _wait.Until(d => d.FindElements(By.CssSelector(".badge")).Any(e => e.Text == "Resolved"));

        // Screenshotted and actually looked at (Read tool), not just trusted from a green run —
        // the same discipline every Playwright verify.mjs script in this project has followed
        // since Phase 9 caught a real UX bug a passing assertion alone would have missed.
        var screenshotDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "verify-screenshots");
        Directory.CreateDirectory(screenshotDir);
        ((ITakesScreenshot)_driver).GetScreenshot().SaveAsFile(Path.Combine(screenshotDir, "verify-01-resolved.png"));

        var badges = _driver.FindElements(By.CssSelector(".badge")).Select(e => e.Text);
        Assert.Contains("Resolved", badges);
    }

    private void ClickByText(string text)
    {
        var element = _wait.Until(d =>
        {
            var matches = d.FindElements(By.XPath($"//button[contains(text(), '{text}')]"));
            return matches.Count > 0 ? matches[0] : null;
        });
        element!.Click();
    }

    public void Dispose()
    {
        _driver.Quit();
        _driver.Dispose();
    }
}
