using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Simulation;
using Shared;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseWebSockets();

// Shared simulation instance for all websocket clients.
var gameLoop = new GameLoop();
_ = gameLoop.RunAsync(app.Lifetime.ApplicationStopping);

// Lightweight server-side movement logging so we can observe ship motion.
var logger = app.Logger;
var tickCounter = 0;
gameLoop.StateUpdated += snapshot =>
{
    // Log roughly once per second (gameLoop default tick is 30 Hz).
    tickCounter++;
    if (tickCounter % 30 != 0)
    {
        return;
    }

    if (snapshot.Count == 0)
    {
        logger.LogInformation("Simulation tick: no ships active.");
        return;
    }

    foreach (var ship in snapshot)
    {
        logger.LogInformation(
            "Ship {ShipId} pos=({PX:F2}, {PY:F2}, {PZ:F2}) vel=({VX:F2}, {VY:F2}, {VZ:F2})",
            ship.Id,
            ship.Position.X, ship.Position.Y, ship.Position.Z,
            ship.Velocity.X, ship.Velocity.Y, ship.Velocity.Z);
    }
};

app.MapGet("/", () => Results.Ok("Gateway is running"));

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Expected a WebSocket request.");
        return;
    }

    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();

    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        // System.Numerics.Vector3 uses fields (X/Y/Z), not properties.
        IncludeFields = true,
        PropertyNameCaseInsensitive = true
    };

    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
    var cancellationToken = linkedCts.Token;

    var sendTask = SendSnapshotsAsync(gameLoop, webSocket, jsonOptions, cancellationToken);

    try
    {
        var receiveBuffer = new byte[8 * 1024];

        while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
        {
            var receiveResult = await webSocket.ReceiveAsync(receiveBuffer, cancellationToken);

            if (receiveResult.MessageType == WebSocketMessageType.Close)
            {
                await webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Connection closed by client",
                    cancellationToken);
                break;
            }

            if (receiveResult.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }

            var messageBuilder = new StringBuilder();
            messageBuilder.Append(Encoding.UTF8.GetString(receiveBuffer, 0, receiveResult.Count));

            while (!receiveResult.EndOfMessage)
            {
                receiveResult = await webSocket.ReceiveAsync(receiveBuffer, cancellationToken);
                messageBuilder.Append(Encoding.UTF8.GetString(receiveBuffer, 0, receiveResult.Count));
            }

            var incomingMessage = messageBuilder.ToString();
            SubmitCommandsFromMessage(gameLoop, incomingMessage, jsonOptions);
        }
    }
    finally
    {
        linkedCts.Cancel();
        try { await sendTask; } catch { /* best-effort shutdown */ }
    }
});

app.Run();

static void SubmitCommandsFromMessage(GameLoop gameLoop, string message, JsonSerializerOptions jsonOptions)
{
    if (string.IsNullOrWhiteSpace(message))
    {
        return;
    }

    var trimmed = message.TrimStart();

    // Supported formats:
    // - { "shipId": "...", "input": { "thrust": 1, "yaw": 0, "pitch": 0 } }
    // - [ { ... }, { ... } ]
    if (trimmed.StartsWith("[", StringComparison.Ordinal))
    {
        var commands = JsonSerializer.Deserialize<List<PlayerCommandDto>>(trimmed, jsonOptions);
        if (commands is not null)
        {
            gameLoop.SubmitCommands(commands);
        }
    }
    else
    {
        var command = JsonSerializer.Deserialize<PlayerCommandDto>(trimmed, jsonOptions);
        if (command is not null)
        {
            gameLoop.SubmitCommand(command);
        }
    }
}

static async Task SendSnapshotsAsync(
    GameLoop gameLoop,
    WebSocket webSocket,
    JsonSerializerOptions jsonOptions,
    CancellationToken cancellationToken)
{
    // Send at the same rate as the simulation tick by default.
    var sendInterval = TimeSpan.FromSeconds(gameLoop.DeltaTimeSeconds);

    while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
    {
        var snapshot = gameLoop.GetSnapshot();
        var json = JsonSerializer.Serialize(snapshot, jsonOptions);
        var payload = Encoding.UTF8.GetBytes(json);

        await webSocket.SendAsync(
            payload,
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);

        await Task.Delay(sendInterval, cancellationToken);
    }
}
