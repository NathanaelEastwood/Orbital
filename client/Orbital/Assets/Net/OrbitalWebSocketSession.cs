using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

internal sealed class OrbitalWebSocketSession : IDisposable
{
    private readonly string _shipIdStr;
    private readonly int _sendRateHz;

    private ClientWebSocket _ws;

    public OrbitalWebSocketSession(string shipIdStr, int sendRateHz = 30)
    {
        _shipIdStr = shipIdStr ?? string.Empty;
        _sendRateHz = Mathf.Clamp(sendRateHz, 1, 120);
    }

    public async Task ConnectAsync(string url, CancellationToken token)
    {
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(new Uri(url), token);
    }

    public void RunBackgroundLoops(
        CancellationToken token,
        Func<string> buildInputJson,
        Action<string> onTextMessage)
    {
        _ = Task.Run(() => ReceiveLoop(token, onTextMessage));
        _ = Task.Run(() => SendLoop(token, buildInputJson));
    }

    private async Task SendLoop(CancellationToken token, Func<string> buildInputJson)
    {
        var delay = TimeSpan.FromSeconds(1d / _sendRateHz);

        while (!token.IsCancellationRequested && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var json = buildInputJson();
                var payload = Encoding.UTF8.GetBytes(json);
                await _ws.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken: token);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Orbital] WebSocket send failed: {e.Message}");
                return;
            }

            try { await Task.Delay(delay, token); } catch { /* canceled */ }
        }
    }

    private async Task ReceiveLoop(CancellationToken token, Action<string> onTextMessage)
    {
        var buffer = new byte[8192];

        try
        {
            while (!token.IsCancellationRequested && _ws != null && _ws.State == WebSocketState.Open)
            {
                var sb = new StringBuilder();
                WebSocketReceiveResult result;

                do
                {
                    result = await _ws.ReceiveAsync(buffer, token);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return;
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);

                onTextMessage?.Invoke(sb.ToString());
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
        catch (Exception e)
        {
            Debug.LogError($"[Orbital] WebSocket receive failed: {e.Message}");
        }
    }

    public string BuildInputJson(OrbitalInputSnapshot input)
    {
        var c = System.Globalization.CultureInfo.InvariantCulture;
        return
            $"{{\"shipId\":\"{_shipIdStr}\",\"input\":{{\"thrust\":{input.thrust01.ToString(c)}," +
            $"\"yaw\":{input.yawRad.ToString(c)},\"pitch\":{input.pitchRad.ToString(c)}}}}}";
    }

    public void Dispose()
    {
        try { _ws?.Dispose(); } catch { /* best effort */ }
        _ws = null;
    }
}

