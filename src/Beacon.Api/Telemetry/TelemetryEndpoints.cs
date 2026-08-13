using System.Net.WebSockets;

namespace Beacon.Api.Telemetry;

public static class TelemetryEndpoints
{
    public static IEndpointRouteBuilder MapTelemetryEndpoints(this IEndpointRouteBuilder app)
    {
        app.Map("/ws/telemetry", async (HttpContext context, WebSocketConnectionManager manager) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var id = manager.Add(socket);

            var buffer = new byte[4096];
            try
            {
                // Server side is broadcast-only — just wait here until the client closes.
                while (socket.State == WebSocketState.Open)
                {
                    var result = await socket.ReceiveAsync(buffer, context.RequestAborted);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await manager.AcknowledgeCloseAsync(id, CancellationToken.None);
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // App shutting down / request aborted — nothing to acknowledge.
            }
            catch (WebSocketException)
            {
                // Client disconnected without a clean close handshake.
            }
            finally
            {
                manager.Remove(id);
            }
        });

        return app;
    }
}
