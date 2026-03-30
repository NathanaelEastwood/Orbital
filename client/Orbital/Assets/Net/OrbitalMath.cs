using UnityEngine;

internal static class OrbitalMath
{
    public static Vector3 GetForwardFromYawPitch(float yawRad, float pitchRad)
    {
        var cp = Mathf.Cos(pitchRad);
        var sp = Mathf.Sin(pitchRad);
        var cy = Mathf.Cos(yawRad);
        var sy = Mathf.Sin(yawRad);
        var f = new Vector3(cp * cy, sp, cp * sy);
        return f.sqrMagnitude > 0.000001f ? f.normalized : Vector3.forward;
    }
}

