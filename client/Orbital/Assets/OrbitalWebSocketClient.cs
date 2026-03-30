using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// Connects to server websocket, sends movement input, and applies authoritative ship position.
public sealed class OrbitalWebSocketClient : MonoBehaviour
{
    [Header("Connection")]
    [Tooltip("If set, uses this websocket URL instead of the default.")]
    [SerializeField] private string websocketUrl = ""; // Empty => use env var or default

    [SerializeField] private string shipName = "Ship";
    [SerializeField] private Color shipColor = new Color(0.2f, 0.9f, 0.5f, 1f);

    private System.Net.WebSockets.ClientWebSocket _ws;
    private CancellationTokenSource _cts;

    private readonly object _inputLock = new object();
    private float _thrust01;
    private float _yawRad;
    private float _pitchRad;

    private string _shipIdStr = string.Empty;
    private Guid _shipId;
    private Transform _shipTransform;

    // Received from background thread; applied on Unity main thread.
    private readonly object _stateLock = new object();
    private bool _hasState;
    private ShipStateUnity _latestState;

    private const int SendRateHz = 30;

    private async void Start()
    {
        _shipId = Guid.NewGuid();
        _shipIdStr = _shipId.ToString("D");

        SpawnShip();

        _cts = new CancellationTokenSource();

        var url = ResolveWebSocketUrl();
        Debug.Log($"[Orbital] Connecting to websocket: {url}");

        _ws = new System.Net.WebSockets.ClientWebSocket();
        try
        {
            await _ws.ConnectAsync(new Uri(url), _cts.Token);
            Debug.Log("[Orbital] WebSocket connected.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Orbital] WebSocket connect failed: {e}");
            return;
        }

        // Kick off background send/receive loops.
        _ = Task.Run(() => ReceiveLoop(_cts.Token));
        _ = Task.Run(() => SendLoop(_cts.Token));
    }

    private void Update()
    {
        ReadInput(Time.deltaTime);

        // Apply any server updates (authoritative position).
        ShipStateUnity stateToApply = null;
        lock (_stateLock)
        {
            if (_hasState)
            {
                stateToApply = _latestState;
                _hasState = false;
            }
        }

        if (stateToApply != null)
        {
            var state = stateToApply;
            _shipTransform.position = new Vector3(state.position.x, state.position.y, state.position.z);

            // Visual orientation follows current input, not server physics (server only sends pos/vel).
            float yaw, pitch;
            lock (_inputLock)
            {
                yaw = _yawRad;
                pitch = _pitchRad;
            }

            _shipTransform.rotation = Quaternion.LookRotation(GetForwardFromYawPitch(yaw, pitch), Vector3.up);
        }
    }

    private void OnApplicationQuit()
    {
        try { _cts.Cancel(); } catch { /* best effort */ }
    }

    private void SpawnShip()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = shipName;
        go.transform.position = Vector3.zero;
        go.transform.localScale = Vector3.one * 0.5f;

        // Make it look nicer.
        var renderer = go.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material = new Material(Shader.Find("Standard"));
            renderer.material.color = shipColor;
        }

        _shipTransform = go.transform;
    }

    private string ResolveWebSocketUrl()
    {
        if (!string.IsNullOrWhiteSpace(websocketUrl))
            return websocketUrl;

        var env = Environment.GetEnvironmentVariable("ORBITAL_WS_URL");
        if (!string.IsNullOrWhiteSpace(env))
            return env;

        // Must match server endpoint: server/Gateway/Program.cs uses app.Map("/ws", ...).
        // Server dev http URL defaults to http://localhost:5165
        // => websocket is ws://localhost:5165/ws
        return "ws://localhost:5165/ws";
    }

    private void ReadInput(float dt)
    {
        // Thrust: W/S.
        float thrust = 0f;
        if (Input.GetKey(KeyCode.W)) thrust += 1f;
        if (Input.GetKey(KeyCode.S)) thrust -= 1f;
        thrust = Mathf.Clamp(thrust, 0f, 1f);

        // Yaw/Pitch: arrows.
        float yawDelta = 0f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) yawDelta += 1f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) yawDelta -= 1f;

        float pitchDelta = 0f;
        if (Input.GetKey(KeyCode.UpArrow)) pitchDelta += 1f;
        if (Input.GetKey(KeyCode.DownArrow)) pitchDelta -= 1f;

        // Convert to radians. (Server expects yaw/pitch radians for cos/sin.)
        const float yawSpeedRadPerSec = 1.5f;
        const float pitchSpeedRadPerSec = 1.2f;

        lock (_inputLock)
        {
            _thrust01 = thrust;
            _yawRad += yawDelta * yawSpeedRadPerSec * dt;
            _pitchRad += pitchDelta * pitchSpeedRadPerSec * dt;

            // Prevent flipping and keep stable.
            _pitchRad = Mathf.Clamp(_pitchRad, -1.4f, 1.4f);
        }
    }

    private async Task SendLoop(CancellationToken token)
    {
        var delay = TimeSpan.FromSeconds(1d / SendRateHz);

        while (!token.IsCancellationRequested && _ws != null && _ws.State == System.Net.WebSockets.WebSocketState.Open)
        {
            float thrust, yaw, pitch;
            lock (_inputLock)
            {
                thrust = _thrust01;
                yaw = _yawRad;
                pitch = _pitchRad;
            }

            // Server supported format:
            // { "shipId": "...", "input": { "thrust": 1, "yaw": 0, "pitch": 0 } }
            // Keys are case-insensitive on server, but we keep them consistent.
            // Use invariant formatting without locale-dependent commas.
            var json = $"{{\"shipId\":\"{_shipIdStr}\",\"input\":{{\"thrust\":{thrust.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                       $"\"yaw\":{yaw.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                       $"\"pitch\":{pitch.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}}}";

            try
            {
                var payload = Encoding.UTF8.GetBytes(json);
                await _ws.SendAsync(payload, System.Net.WebSockets.WebSocketMessageType.Text, endOfMessage: true, token);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Orbital] WebSocket send failed: {e.Message}");
                return;
            }

            try { await Task.Delay(delay, token); } catch { /* token canceled */ }
        }
    }

    private async Task ReceiveLoop(CancellationToken token)
    {
        var buffer = new byte[8192];

        try
        {
            while (!token.IsCancellationRequested && _ws.State == System.Net.WebSockets.WebSocketState.Open)
            {
                var messageBuilder = new StringBuilder();
                System.Net.WebSockets.WebSocketReceiveResult receiveResult;

                do
                {
                    receiveResult = await _ws.ReceiveAsync(buffer, token);
                    if (receiveResult.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                        return;

                    var chunk = Encoding.UTF8.GetString(buffer, 0, receiveResult.Count);
                    messageBuilder.Append(chunk);
                }
                while (!receiveResult.EndOfMessage);

                var message = messageBuilder.ToString();
                var state = TryExtractOurShipState(message);
                if (state != null)
                {
                    lock (_stateLock)
                    {
                        _latestState = state;
                        _hasState = true;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        catch (Exception e)
        {
            Debug.LogError($"[Orbital] WebSocket receive failed: {e.Message}");
        }
    }

    // Server sends a snapshot array:
    // [ { "id": "...", "position": { "x": ..., "y": ..., "z": ... }, "velocity": { ... } }, ... ]
    private ShipStateUnity TryExtractOurShipState(string snapshotJsonArray)
    {
        if (string.IsNullOrWhiteSpace(snapshotJsonArray))
            return null;

        // JsonUtility is case-sensitive; normalize common casing variants from the server.
        snapshotJsonArray = snapshotJsonArray
            .Replace("\"Id\"", "\"id\"")
            .Replace("\"Position\"", "\"position\"")
            .Replace("\"Velocity\"", "\"velocity\"")
            .Replace("\"X\"", "\"x\"")
            .Replace("\"Y\"", "\"y\"")
            .Replace("\"Z\"", "\"z\"");

        // Unity's JsonUtility can only parse root objects, not arrays.
        // Wrap: {"items": <array>}
        var wrapped = $"{{\"items\":{snapshotJsonArray}}}";

        SnapshotWrapperUnity wrapper = null;
        try
        {
            wrapper = JsonUtility.FromJson<SnapshotWrapperUnity>(wrapped);
        }
        catch
        {
            // Ignore malformed payloads.
        }

        if (wrapper == null || wrapper.items == null)
            return null;

        for (int i = 0; i < wrapper.items.Length; i++)
        {
            var item = wrapper.items[i];
            if (item == null) continue;

            // Server may camelCase the Guid string keys. Compare exact d-format string.
            if (string.Equals(item.id, _shipIdStr, StringComparison.OrdinalIgnoreCase))
                return item;
        }

        return null;
    }

    private static Vector3 GetForwardFromYawPitch(float yawRad, float pitchRad)
    {
        // Must mirror server logic (server/Simulation/GameLoop.cs)
        // forward = normalize(new Vector3(cp * cy, sp, cp * sy));
        var cp = Mathf.Cos(pitchRad);
        var sp = Mathf.Sin(pitchRad);
        var cy = Mathf.Cos(yawRad);
        var sy = Mathf.Sin(yawRad);
        var f = new Vector3(cp * cy, sp, cp * sy);
        return f.sqrMagnitude > 0.000001f ? f.normalized : Vector3.forward;
    }

    [Serializable]
    private struct Vector3FieldsUnity
    {
        public float x;
        public float y;
        public float z;
    }

    [Serializable]
    private sealed class ShipStateUnity
    {
        public string id;
        public Vector3FieldsUnity position;
        public Vector3FieldsUnity velocity;
    }

    [Serializable]
    private sealed class SnapshotWrapperUnity
    {
        public ShipStateUnity[] items;
    }

}

