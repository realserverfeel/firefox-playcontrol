using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace PlayControlAgent;

// A loopback WebSocket server the browser extension connects to. The server
// only listens on 127.0.0.1 and only accepts WebSocket upgrades whose Origin is
// a browser extension (moz-extension:// / chrome-extension://). When a hotkey
// fires, SendCommand pushes a {type:"command", id, action} message to the
// connected extension; the extension replies with a {type:"result", ...} JSON
// which is surfaced via ResultReceived for overlay feedback.
public class WsServer
{
    readonly int _port;
    HttpListener? _listener;
    WebSocket? _client;
    CancellationTokenSource? _cts;
    int _id;

    public event Action<bool>? ConnectionChanged;
    public event Action<string>? ResultReceived;
    public event Action<string>? ServerError;

    public bool ClientConnected => _client is { State: WebSocketState.Open };

    public WsServer(int port)
    {
        _port = port;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            _listener.Start();
        }
        catch (Exception ex)
        {
            ServerError?.Invoke($"Could not bind 127.0.0.1:{_port} ({ex.Message}). Is the port in use?");
            return;
        }
        _ = AcceptLoop(_cts.Token);
    }

    async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener!.GetContextAsync();
            }
            catch
            {
                break; // listener stopped
            }

            if (!ctx.Request.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = 400;
                ctx.Response.Close();
                continue;
            }

            var origin = ctx.Request.Headers["Origin"] ?? "";
            if (!origin.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase) &&
                !origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.StatusCode = 403;
                ctx.Response.Close();
                continue;
            }

            HttpListenerWebSocketContext wsCtx;
            try
            {
                wsCtx = await ctx.AcceptWebSocketAsync(null);
            }
            catch
            {
                continue;
            }

            // Replace any previous client with the newest connection.
            var old = _client;
            _client = wsCtx.WebSocket;
            if (old != null)
            {
                try { old.Abort(); } catch { /* ignore */ }
            }
            ConnectionChanged?.Invoke(true);
            _ = ReadLoop(_client, ct);
        }
    }

    async Task ReadLoop(WebSocket ws, CancellationToken ct)
    {
        var buf = new byte[8192];
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var res = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                if (res.MessageType == WebSocketMessageType.Close)
                    break;
                var text = Encoding.UTF8.GetString(buf, 0, res.Count);
                ResultReceived?.Invoke(text);
            }
        }
        catch
        {
            // connection dropped
        }
        finally
        {
            if (ReferenceEquals(_client, ws))
            {
                _client = null;
                ConnectionChanged?.Invoke(false);
            }
            try { ws.Dispose(); } catch { /* ignore */ }
        }
    }

    public async Task SendCommand(string action)
    {
        var ws = _client;
        if (ws is not { State: WebSocketState.Open }) return;
        int id = Interlocked.Increment(ref _id);
        var json = JsonSerializer.Serialize(new { type = "command", id, action });
        var bytes = Encoding.UTF8.GetBytes(json);
        try
        {
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch
        {
            // ignore send failures; read loop will detect disconnect
        }
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        try { _client?.Abort(); } catch { /* ignore */ }
        try { _listener?.Stop(); _listener?.Close(); } catch { /* ignore */ }
        _client = null;
    }
}
