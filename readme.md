# Orbital

This repo has:
- `client/Orbital`: a Unity scene that spawns a cube "ship" and connects to the server via WebSockets
- `server/`: a .NET WebSocket gateway that runs a simple fixed-step simulation

## Prerequisites

1. Unity
   - Version: `2022.3.49f1`
   - Project path: `client/Orbital/`
2. .NET SDK
   - Projects target `net10.0`
   - If `dotnet run` fails due to missing target framework, install the .NET 10 SDK (preview is ok).

## Start the server (terminal)

Open a terminal at the repo root and run:

```bash
cd server
dotnet run --project Gateway/Gateway.csproj
```

The server listens on:
- HTTP: `http://localhost:5165`
- WebSocket: `ws://localhost:5165/ws`

Optional quick check (another terminal):

```bash
curl http://localhost:5165/
```

Expected output: `Gateway is running`

## Start the client (Unity)

1. Open Unity Hub (or Unity directly)
2. Open the project at `client/Orbital/`
3. Open the scene `Assets/Scenes/SampleScene`
4. Click `Play`

When you press `Play`, `OrbitalBootstrap` will automatically:
- create a websocket client component
- connect to `ws://localhost:5165/ws`
- spawn the ship cube and send control inputs

### Controls
- `W/S`: thrust
- `A/D` or Left/Right arrows: yaw
- Up/Down arrows: pitch

## If your server isn't on localhost

The Unity client uses `ws://localhost:5165/ws` by default.

To override, set:
- environment variable `ORBITAL_WS_URL` to the full websocket URL (for example `ws://127.0.0.1:5166/ws`)

You can set it before launching Unity, e.g.:

```bash
export ORBITAL_WS_URL="ws://localhost:5165/ws"
```

## Stop

Stop the server by pressing `Ctrl+C` in the terminal where `dotnet run` is running.