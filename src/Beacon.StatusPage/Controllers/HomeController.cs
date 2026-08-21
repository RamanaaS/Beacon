using Beacon.StatusPage.Models;
using Beacon.StatusPage.Services;
using Microsoft.AspNetCore.Mvc;

namespace Beacon.StatusPage.Controllers;

public class HomeController(BeaconStatusClient statusClient, ILogger<HomeController> logger) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        try
        {
            var status = await statusClient.GetStatusAsync(ct);
            return View(StatusPageViewModel.Available(status));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Beacon.Api being unreachable is exactly the scenario a status page has to survive —
            // logged, not swallowed silently, but the page itself degrades instead of 500ing.
            logger.LogWarning(ex, "Could not reach Beacon.Api for /api/status");
            return View(StatusPageViewModel.Unavailable());
        }
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(new ErrorViewModel { RequestId = HttpContext.TraceIdentifier });
}
