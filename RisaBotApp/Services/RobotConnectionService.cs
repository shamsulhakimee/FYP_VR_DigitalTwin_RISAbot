using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using RisaBotApp.Models;

namespace RisaBotApp.Services;

/// <summary>
/// Manages the WebSocket connection to the robot's /ws/log and (future) /ws/telemetry streams.
/// Exposes typed events so ViewModels never touch raw WebSocket frames.
/// </summary>
public class RobotConnectionService : IAsyncDisposable
{
    // ── Events ──────────────────────────────────────────────────────────────

    /// <summary>Fires on every WS /ws/log message (step updates, pongs, log lines).</summary>
    public event EventHandler<WsLogMessage>? WsMessageReceived;

    /// <summary>Fires when the WebSocket connection state changes.</summary>
    public event EventHandler<WsConnectionState>? ConnectionStateChanged;

    // ── State ────────────────────────────────────────────────────────────────

    private ClientWebSocket? _socket;
    private CancellationTokenSource? _receiveCts;
    private WsConnectionState _state = WsConnectionState.Disconnected;

    public WsConnectionState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;

            // The setter is hit from the receive loop, the ping loop and the connect path,
            // only one of which is on the UI thread. Subscribers are view-models that write
            // bound properties, so the hop happens here rather than at every call site.
            UiThread.Post(() => ConnectionStateChanged?.Invoke(this, value));
        }
    }

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Connect to WS /ws/log at the given base URL, then start the receive loop.
    /// Replaces any existing connection automatically.
    /// </summary>
    public async Task ConnectLogStreamAsync(string baseUrl, CancellationToken ct = default)
    {
        await DisconnectAsync();

        var wsUrl = baseUrl
            .Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase)
            .Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/')
            + "/ws/log";

        try
        {
            State = WsConnectionState.Connecting;
            _socket = new ClientWebSocket();
            _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            await _socket.ConnectAsync(new Uri(wsUrl), _receiveCts.Token);
            State = WsConnectionState.Connected;

            // Start background receive loop (does not block the caller)
            _ = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token), _receiveCts.Token);

            // Keep-alive ping every 20s
            _ = Task.Run(() => PingLoopAsync(_receiveCts.Token), _receiveCts.Token);
        }
        catch (Exception ex)
        {
            State = WsConnectionState.Failed;
            WsMessageReceived?.Invoke(this, new WsLogMessage
            {
                Type    = "error",
                Status  = "failed",
                Message = $"WebSocket error: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Connect to WS /ws/telemetry for future live telemetry push.
    /// Stub — wires up the same receive loop; ViewModel decides how to consume messages.
    /// </summary>
    public Task ConnectTelemetryStreamAsync(string baseUrl, CancellationToken ct = default)
    {
        // TODO: implement when /ws/telemetry endpoint is available on the backend.
        // Shape: same WsLogMessage JSON but type="telemetry", with battery/odom/imu fields.
        throw new NotImplementedException("WS /ws/telemetry not yet implemented on the robot backend.");
    }

    /// <summary>Disconnect and dispose the current WebSocket cleanly.</summary>
    public async Task DisconnectAsync()
    {
        if (_receiveCts is not null)
        {
            await _receiveCts.CancelAsync();
            _receiveCts.Dispose();
            _receiveCts = null;
        }

        if (_socket is not null)
        {
            try
            {
                if (_socket.State == WebSocketState.Open)
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", CancellationToken.None);
            }
            catch { /* Best-effort */ }

            _socket.Dispose();
            _socket = null;
        }

        State = WsConnectionState.Disconnected;
    }

    // ── Private receive loop ───────────────────────────────────────────────

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[4096];
        var sb = new StringBuilder();

        while (!ct.IsCancellationRequested && _socket?.State == WebSocketState.Open)
        {
            try
            {
                var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    State = WsConnectionState.Disconnected;
                    return;
                }

                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                if (result.EndOfMessage)
                {
                    var raw = sb.ToString();
                    sb.Clear();

                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        try
                        {
                            var msg = JsonSerializer.Deserialize<WsLogMessage>(raw, _json);
                            if (msg is not null)
                                UiThread.Post(() => WsMessageReceived?.Invoke(this, msg));
                        }
                        catch { /* Skip malformed frames */ }
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                State = WsConnectionState.Failed;
                break;
            }
        }
    }

    private async Task PingLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _socket?.State == WebSocketState.Open)
        {
            await Task.Delay(20_000, ct);
            if (_socket?.State == WebSocketState.Open)
            {
                try
                {
                    var ping = Encoding.UTF8.GetBytes("{\"type\":\"ping\"}");
                    await _socket.SendAsync(
                        new ArraySegment<byte>(ping),
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        ct);
                }
                catch (OperationCanceledException) { break; }
                catch
                {
                    // The receive loop does NOT reliably handle this. When Windows moves the
                    // adapter to another network the socket goes half-open rather than
                    // faulting, so ReceiveAsync blocks indefinitely and never reports the
                    // drop. A send is the only operation that finds out, which makes this
                    // catch the sole detector of that case — swallowing it is what let a
                    // provisioning run hang forever with no error.
                    State = WsConnectionState.Failed;
                    return;
                }
            }
        }
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync();
}

public enum WsConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Failed
}
