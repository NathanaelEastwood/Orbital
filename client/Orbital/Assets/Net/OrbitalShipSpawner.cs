using UnityEngine;

internal static class OrbitalShipSpawner
{
    public static Transform SpawnCubeShip(string name, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = string.IsNullOrWhiteSpace(name) ? "Ship" : name;
        go.transform.position = Vector3.zero;
        go.transform.localScale = Vector3.one * 0.5f;

        var renderer = go.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material = new Material(Shader.Find("Standard"));
            renderer.material.color = color;
        }

        return go.transform;
    }
}

