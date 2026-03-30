using System;
using System.Threading;
using UnityEngine;

// Connects to server websocket, sends movement input, and applies authoritative ship position.
public sealed class OrbitalWebSocketClient : MonoBehaviour
{
    [Header("Connection")]
    [Tooltip("If set, uses this websocket URL instead of the default.")]
    [SerializeField] private string websocketUrl = ""; // Empty => use env var or default

    [SerializeField] private string shipName = "Ship";
    [SerializeField] private Color shipColor = new Color(0.2f, 0.9f, 0.5f, 1f);
    [Header("Render Smoothing")]
    [Tooltip("Fallback server frame interval when not enough packets have arrived.")]
    [SerializeField] private float defaultServerFrameInterval = 1f / 30f;
    [Tooltip("How strongly we smooth measured packet interval (0=none, 1=very smooth).")]
    [Range(0f, 0.99f)]
    [SerializeField] private float serverIntervalSmoothing = 0.85f;
    [Tooltip("How much each incoming server frame influences the visual target (lower = softer corrections).")]
    [Range(0.05f, 1f)]
    [SerializeField] private float serverFrameInfluence = 0.35f;
    [Tooltip("Per-frame lerp strength relative to (deltaTime / estimatedServerFrameInterval).")]
    [Range(0.05f, 1.5f)]
    [SerializeField] private float perFrameFollowStrength = 0.45f;
    [Tooltip("Hard cap on render correction speed in world units per second.")]
    [SerializeField] private float maxRenderCorrectionSpeed = 35f;
    [Header("Debug")]
    [SerializeField] private bool showSimpleNetDebugOverlay = true;
    [SerializeField] private Vector2 debugOverlayOffset = new Vector2(12f, 12f);
    [SerializeField] private bool enableRuntimeTuningHotkeys = true;

    private CancellationTokenSource _cts;
    private OrbitalWebSocketSession _session;
    private OrbitalInput _input;
    private OrbitalSnapshotParser _parser;
    private readonly object _stateGate = new object();
    private Vector3 _latestServerPosition;
    private bool _hasServerPosition;
    private Vector3 _latestReceivedPosition;
    private int _receivedFrames;
    private float _estimatedServerFrameInterval = 1f / 30f;
    private long _lastReceiveTimestamp;

    private string _shipIdStr = string.Empty;
    private Transform _shipTransform;

    private async void Start()
    {
        _shipIdStr = Guid.NewGuid().ToString("D");
        _estimatedServerFrameInterval = Mathf.Max(0.001f, defaultServerFrameInterval);

        _shipTransform = OrbitalShipSpawner.SpawnCubeShip(shipName, shipColor);

        _cts = new CancellationTokenSource();
        _input = new OrbitalInput();
        _parser = new OrbitalSnapshotParser(_shipIdStr);

        var url = ResolveWebSocketUrl();
        UnityEngine.Debug.Log($"[Orbital] Connecting to websocket: {url}");

        _session = new OrbitalWebSocketSession(_shipIdStr, sendRateHz: 30);
        try
        {
            await _session.ConnectAsync(url, _cts.Token);
            UnityEngine.Debug.Log("[Orbital] WebSocket connected.");
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[Orbital] WebSocket connect failed: {e}");
            return;
        }

        _session.RunBackgroundLoops(
            _cts.Token,
            buildInputJson: () => _session.BuildInputJson(_input.GetSnapshot()),
            onTextMessage: OnServerMessage);
    }

    private void Update()
    {
        _input?.TickFromUnity(Time.deltaTime);
        HandleRuntimeTuningHotkeys();

        var hasServerPosition = false;
        var latestServerPosition = Vector3.zero;
        var estimatedFrameInterval = defaultServerFrameInterval;
        lock (_stateGate)
        {
            hasServerPosition = _hasServerPosition;
            latestServerPosition = _latestServerPosition;
            estimatedFrameInterval = _estimatedServerFrameInterval;
        }

        if (hasServerPosition)
        {
            var frameInterval = Mathf.Max(0.001f, estimatedFrameInterval);
            var t = Mathf.Clamp01((Time.deltaTime / frameInterval) * perFrameFollowStrength);
            var lerped = Vector3.Lerp(_shipTransform.position, latestServerPosition, t);
            _shipTransform.position = Vector3.MoveTowards(
                _shipTransform.position,
                lerped,
                Mathf.Max(0f, maxRenderCorrectionSpeed) * Time.deltaTime);
        }

        // Visual orientation follows current input, not server physics (server only sends pos/vel).
        var input = _input.GetSnapshot();
        _shipTransform.rotation = Quaternion.LookRotation(
            OrbitalMath.GetForwardFromYawPitch(input.yawRad, input.pitchRad),
            Vector3.up);
    }

    private void OnServerMessage(string message)
    {
        var state = _parser?.TryExtractOurShipState(message);
        if (state != null)
        {
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            var receivedPosition = new Vector3(state.position.x, state.position.y, state.position.z);
            lock (_stateGate)
            {
                if (_hasServerPosition)
                    _latestServerPosition = Vector3.Lerp(_latestServerPosition, receivedPosition, serverFrameInfluence);
                else
                    _latestServerPosition = receivedPosition;

                _hasServerPosition = true;
                _latestReceivedPosition = receivedPosition;
                _receivedFrames++;

                if (_lastReceiveTimestamp != 0)
                {
                    var dt = (float)(now - _lastReceiveTimestamp) / System.Diagnostics.Stopwatch.Frequency;
                    if (dt > 0f && dt < 1f)
                    {
                        var a = Mathf.Clamp01(serverIntervalSmoothing);
                        _estimatedServerFrameInterval = (a * _estimatedServerFrameInterval) + ((1f - a) * dt);
                    }
                }
                _lastReceiveTimestamp = now;
            }
        }
    }

    private void OnGUI()
    {
        if (!showSimpleNetDebugOverlay)
            return;

        var hasServerPosition = false;
        var latestServerPosition = Vector3.zero;
        var latestReceived = Vector3.zero;
        var received = 0;
        var estimatedInterval = defaultServerFrameInterval;

        lock (_stateGate)
        {
            hasServerPosition = _hasServerPosition;
            latestServerPosition = _latestServerPosition;
            latestReceived = _latestReceivedPosition;
            received = _receivedFrames;
            estimatedInterval = _estimatedServerFrameInterval;
        }

        var rect = new Rect(debugOverlayOffset.x, debugOverlayOffset.y, 640f, 168f);
        GUI.Box(rect, string.Empty);

        var labelRect = new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 16f);
        var text =
            "Orbital Net Debug (Per-Frame Lerp)\n" +
            $"Received Frames: {received}\n" +
            $"Have Server Pos: {(hasServerPosition ? "Yes" : "No")}\n" +
            $"Estimated Server Interval: {estimatedInterval * 1000f:F1} ms\n" +
            $"Frame Influence: {serverFrameInfluence:F2}  |  Follow Strength: {perFrameFollowStrength:F2}\n" +
            $"Max Correction Speed: {maxRenderCorrectionSpeed:F1} u/s\n" +
            $"Latest Received Pos: {latestReceived.x:F2}, {latestReceived.y:F2}, {latestReceived.z:F2}\n" +
            $"Current Target Pos: {latestServerPosition.x:F2}, {latestServerPosition.y:F2}, {latestServerPosition.z:F2}\n" +
            "Tune: [ / ] influence, ; / ' follow strength, , / . max correction speed";
        GUI.Label(labelRect, text);
    }

    private void HandleRuntimeTuningHotkeys()
    {
        if (!enableRuntimeTuningHotkeys)
            return;

        if (Input.GetKeyDown(KeyCode.LeftBracket))
            serverFrameInfluence = Mathf.Clamp(serverFrameInfluence - 0.05f, 0.05f, 1f);
        if (Input.GetKeyDown(KeyCode.RightBracket))
            serverFrameInfluence = Mathf.Clamp(serverFrameInfluence + 0.05f, 0.05f, 1f);

        if (Input.GetKeyDown(KeyCode.Semicolon))
            perFrameFollowStrength = Mathf.Clamp(perFrameFollowStrength - 0.05f, 0.05f, 1.5f);
        if (Input.GetKeyDown(KeyCode.Quote))
            perFrameFollowStrength = Mathf.Clamp(perFrameFollowStrength + 0.05f, 0.05f, 1.5f);

        if (Input.GetKeyDown(KeyCode.Comma))
            maxRenderCorrectionSpeed = Mathf.Clamp(maxRenderCorrectionSpeed - 2f, 1f, 200f);
        if (Input.GetKeyDown(KeyCode.Period))
            maxRenderCorrectionSpeed = Mathf.Clamp(maxRenderCorrectionSpeed + 2f, 1f, 200f);
    }

    private void OnApplicationQuit()
    {
        try { _cts.Cancel(); } catch { /* best effort */ }
    }

    private void OnDestroy()
    {
        try { _cts?.Cancel(); } catch { /* best effort */ }
        try { _session?.Dispose(); } catch { /* best effort */ }
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
}

