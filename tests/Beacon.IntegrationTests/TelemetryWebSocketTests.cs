using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace Beacon.IntegrationTests;

/// <summary>
/// WebApplicationFactory's TestServer exposes a real WebSocket client that goes through the
/// actual middleware pipeline (UseWebSockets, the /ws/telemetry endpoint, AcceptWebSocketAsync)
/// in-process — this is genuinely exercising the same code path a real client would hit, not a
/// mock of it.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class TelemetryWebSocketTests(BeaconApiFactory factory)
{
    [Fact]
    public async Task Connecting_client_receives_at_least_one_broadcast_metric_sample()
    {
        var wsClient = factory.Server.CreateWebSocketClient();
        var wsUri = new UriBuilder(factory.Server.BaseAddress) { Scheme = "ws" }.Uri;
        using var socket = await wsClient.ConnectAsync(new Uri(wsUri, "/ws/telemetry"), CancellationToken.None);

        var buffer = new byte[4096];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await socket.ReceiveAsync(buffer, cts.Token);

        result.MessageType.Should().Be(WebSocketMessageType.Text);
        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("serviceName").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("cpuPercent").GetDouble().Should().BeInRange(0, 100);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test done", CancellationToken.None);
    }
}
