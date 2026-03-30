using System;
using UnityEngine;

internal sealed class OrbitalSnapshotParser
{
    private readonly string _shipIdStr;

    public OrbitalSnapshotParser(string shipIdStr)
    {
        _shipIdStr = shipIdStr ?? string.Empty;
    }

    public ShipStateUnity TryExtractOurShipState(string snapshotJsonArray)
    {
        if (string.IsNullOrWhiteSpace(snapshotJsonArray))
            return null;

        snapshotJsonArray = snapshotJsonArray
            .Replace("\"Id\"", "\"id\"")
            .Replace("\"Position\"", "\"position\"")
            .Replace("\"Velocity\"", "\"velocity\"")
            .Replace("\"X\"", "\"x\"")
            .Replace("\"Y\"", "\"y\"")
            .Replace("\"Z\"", "\"z\"");

        var wrapped = $"{{\"items\":{snapshotJsonArray}}}";

        SnapshotWrapperUnity wrapper = null;
        try { wrapper = JsonUtility.FromJson<SnapshotWrapperUnity>(wrapped); }
        catch { /* ignore malformed payloads */ }

        if (wrapper?.items == null)
            return null;

        for (int i = 0; i < wrapper.items.Length; i++)
        {
            var item = wrapper.items[i];
            if (item == null) continue;
            if (string.Equals(item.id, _shipIdStr, StringComparison.OrdinalIgnoreCase))
                return item;
        }

        return null;
    }

    [Serializable]
    public struct Vector3FieldsUnity
    {
        public float x;
        public float y;
        public float z;
    }

    [Serializable]
    public sealed class ShipStateUnity
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

