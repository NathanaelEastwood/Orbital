using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Shared;

namespace Simulation;

/// <summary>
/// Simple fixed-step game loop that updates ship positions based on latest client inputs.
/// </summary>
public sealed class GameLoop
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, ShipStateDto> _ships = new();
    private readonly Dictionary<Guid, PlayerInputDto> _latestInputs = new();

    private readonly float _tickRateHz;
    private readonly float _dt;

    // Physics params (tweak as needed).
    private readonly float _thrustScale;
    private readonly float _dragPerSecond;
    private readonly float _maxSpeed;

    public event Action<IReadOnlyList<ShipStateDto>>? StateUpdated;

    public float DeltaTimeSeconds => _dt;

    public GameLoop(
        float tickRateHz = 30f,
        float thrustScale = 20f,
        float dragPerSecond = 0.25f,
        float maxSpeed = 200f)
    {
        if (tickRateHz <= 0) throw new ArgumentOutOfRangeException(nameof(tickRateHz));

        _tickRateHz = tickRateHz;
        _dt = 1f / tickRateHz;
        _thrustScale = thrustScale;
        _dragPerSecond = dragPerSecond;
        _maxSpeed = maxSpeed;
    }

    /// <summary>
    /// Accepts latest client instructions. Unknown ships are spawned at origin with zero velocity.
    /// </summary>
    public void SubmitCommands(IEnumerable<PlayerCommandDto> commands)
    {
        if (commands is null) return;

        lock (_sync)
        {
            foreach (var cmd in commands)
            {
                var id = cmd.ShipId;
                if (!_ships.ContainsKey(id))
                {
                    _ships[id] = new ShipStateDto
                    {
                        Id = id,
                        Position = Vector3.Zero,
                        Velocity = Vector3.Zero
                    };
                }

                _latestInputs[id] = cmd.Input;
            }
        }
    }

    public void SubmitCommand(PlayerCommandDto command)
    {
        if (command is null) return;
        SubmitCommands(new[] { command });
    }

    public IReadOnlyList<ShipStateDto> GetSnapshot()
    {
        lock (_sync)
        {
            // Clone so callers can't mutate internal state.
            var snapshot = new List<ShipStateDto>(_ships.Count);
            foreach (var ship in _ships.Values)
            {
                snapshot.Add(new ShipStateDto
                {
                    Id = ship.Id,
                    Position = ship.Position,
                    Velocity = ship.Velocity
                });
            }
            return snapshot;
        }
    }

    public void StepOnce()
    {
        StepOnce(_dt);
    }

    /// <summary>
    /// Deterministic step using provided dt (in seconds).
    /// </summary>
    public void StepOnce(float dt)
    {
        if (dt <= 0) return;

        lock (_sync)
        {
            foreach (var kvp in _ships)
            {
                var ship = kvp.Value;
                if (!_latestInputs.TryGetValue(kvp.Key, out var input) || input is null)
                {
                    input = new PlayerInputDto(); // No input -> no thrust.
                }

                var forward = ForwardFromYawPitch(input.Yaw, input.Pitch);
                var acceleration = forward * (input.Thrust * _thrustScale);

                ship.Velocity += acceleration * dt;

                // Exponential drag (stable for varying dt).
                var dragFactor = MathF.Exp(-_dragPerSecond * dt);
                ship.Velocity *= dragFactor;

                // Clamp speed to avoid numerical blow-ups.
                var speedSq = ship.Velocity.LengthSquared();
                if (speedSq > _maxSpeed * _maxSpeed && speedSq > 0)
                {
                    var speed = MathF.Sqrt(speedSq);
                    ship.Velocity = (ship.Velocity / speed) * _maxSpeed;
                }

                ship.Position += ship.Velocity * dt;
            }
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(_dt);
        while (!cancellationToken.IsCancellationRequested)
        {
            StepOnce();

            var snapshot = GetSnapshot();
            StateUpdated?.Invoke(snapshot);

            await Task.Delay(delay, cancellationToken);
        }
    }

    private static Vector3 ForwardFromYawPitch(float yaw, float pitch)
    {
        // Yaw rotates around Y; pitch rotates around X.
        var cp = MathF.Cos(pitch);
        var sp = MathF.Sin(pitch);
        var cy = MathF.Cos(yaw);
        var sy = MathF.Sin(yaw);

        // Direction of thrust in world space.
        return Vector3.Normalize(new Vector3(cp * cy, sp, cp * sy));
    }
}

