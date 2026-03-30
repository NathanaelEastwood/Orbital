using System;
using UnityEngine;

internal readonly struct OrbitalInputSnapshot
{
    public readonly float thrust01;
    public readonly float yawRad;
    public readonly float pitchRad;

    public OrbitalInputSnapshot(float thrust01, float yawRad, float pitchRad)
    {
        this.thrust01 = thrust01;
        this.yawRad = yawRad;
        this.pitchRad = pitchRad;
    }
}

internal sealed class OrbitalInput
{
    private readonly object _gate = new object();
    private float _thrust01;
    private float _yawRad;
    private float _pitchRad;

    public void TickFromUnity(float dt)
    {
        float thrust = 0f;
        if (Input.GetKey(KeyCode.W)) thrust += 1f;
        if (Input.GetKey(KeyCode.S)) thrust -= 1f;
        thrust = Mathf.Clamp(thrust, 0f, 1f);

        float yawDelta = 0f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) yawDelta += 1f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) yawDelta -= 1f;

        float pitchDelta = 0f;
        if (Input.GetKey(KeyCode.UpArrow)) pitchDelta += 1f;
        if (Input.GetKey(KeyCode.DownArrow)) pitchDelta -= 1f;

        const float yawSpeedRadPerSec = 1.5f;
        const float pitchSpeedRadPerSec = 1.2f;

        lock (_gate)
        {
            _thrust01 = thrust;
            _yawRad += yawDelta * yawSpeedRadPerSec * dt;
            _pitchRad += pitchDelta * pitchSpeedRadPerSec * dt;
            _pitchRad = Mathf.Clamp(_pitchRad, -1.4f, 1.4f);
        }
    }

    public OrbitalInputSnapshot GetSnapshot()
    {
        lock (_gate)
            return new OrbitalInputSnapshot(_thrust01, _yawRad, _pitchRad);
    }
}

