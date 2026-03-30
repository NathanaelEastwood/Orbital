using System;

internal static class OrbitalWebSocketUrlResolver
{
    public static string Resolve(string overrideUrl)
    {
        if (!string.IsNullOrWhiteSpace(overrideUrl))
            return overrideUrl;

        var env = Environment.GetEnvironmentVariable("ORBITAL_WS_URL");
        if (!string.IsNullOrWhiteSpace(env))
            return env;

        return "ws://localhost:5165/ws";
    }
}

