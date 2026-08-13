using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace Beacon.Api.Telemetry;

/// <summary>
/// Raw System.Net.WebSockets, not SignalR — deliberately, to mirror the production telemetry-
/// streaming pattern from GRL rather than defaulting to the higher-level abstraction.
///
/// Every write to a given socket — a broadcast from the timer, or the close-frame acknowledgment
/// from the connection's own receive loop — goes through that connection's SendLock. .NET's
/// WebSocket type only supports one concurrent send and one concurrent receive; two concurrent
/// *sends* (a broadcast racing a close handshake) corrupt the connection. This is what actually
/// broke the first version of this class: no lock, and a client that received one broadcast and
/// immediately tried to close would sometimes race the next broadcast tick.
/// </summary>
public sealed class WebSocketConnectionManager
{
    private sealed class Connection(WebSocket socket)
    {
        public WebSocket Socket { get; } = socket;
        public SemaphoreSlim SendLock { get; } = new(1, 1);
    }

    private readonly ConcurrentDictionary<Guid, Connection> _connections = new();

    public Guid Add(WebSocket socket)
    {
        var id = Guid.NewGuid();
        _connections[id] = new Connection(socket);
        return id;
    }

    public void Remove(Guid id) => _connections.TryRemove(id, out _);

    public int ConnectionCount => _connections.Count;

    public async Task BroadcastAsync(string message, CancellationToken ct = default)
    {
        var bytes = Encoding.UTF8.GetBytes(message);

        foreach (var (id, connection) in _connections)
        {
            if (connection.Socket.State != WebSocketState.Open)
            {
                Remove(id);
                continue;
            }

            await connection.SendLock.WaitAsync(ct);
            try
            {
                if (connection.Socket.State == WebSocketState.Open)
                    await connection.Socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
            }
            catch (Exception)
            {
                // A send failing means the connection is dead in some way ReceiveAsync hasn't
                // noticed yet — drop it rather than let one bad socket break the broadcast loop.
                Remove(id);
            }
            finally
            {
                connection.SendLock.Release();
            }
        }
    }

    /// <summary>
    /// Call this once the connection's own receive loop sees a Close message — CloseOutputAsync,
    /// not CloseAsync, because the peer already sent its close frame; CloseAsync would try to
    /// start a second handshake and wait for a reply the peer isn't going to send.
    /// </summary>
    public async Task AcknowledgeCloseAsync(Guid id, CancellationToken ct = default)
    {
        if (!_connections.TryGetValue(id, out var connection))
            return;

        await connection.SendLock.WaitAsync(ct);
        try
        {
            if (connection.Socket.State is WebSocketState.CloseReceived or WebSocketState.Open)
                await connection.Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "ack", ct);
        }
        finally
        {
            connection.SendLock.Release();
        }
    }
}
