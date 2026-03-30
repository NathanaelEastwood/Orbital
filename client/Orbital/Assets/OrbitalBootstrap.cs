using UnityEngine;

public static class OrbitalBootstrap
{
    // Runs without needing a scene GameObject with a script component.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        var go = new GameObject("OrbitalBootstrap");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<OrbitalWebSocketClient>();

        // Attach camera follow to the main camera if present.
        var mainCamera = Camera.main;
        if (mainCamera != null && mainCamera.GetComponent<OrbitalCameraFollow>() == null)
        {
            mainCamera.gameObject.AddComponent<OrbitalCameraFollow>();
        }

        // Simple background so movement is visible.
        CreateBackgroundPlane();
    }

    private static void CreateBackgroundPlane()
    {
        // Only create once per scene load.
        if (GameObject.Find("OrbitalBackground") != null)
            return;

        var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plane.name = "OrbitalBackground";
        plane.transform.position = new Vector3(0f, -1f, 0f);
        plane.transform.localScale = new Vector3(20f, 1f, 20f); // Big area to fly over.

        var renderer = plane.GetComponent<Renderer>();
        if (renderer != null)
        {
            var mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(0.05f, 0.05f, 0.08f, 1f); // Dark space-ish floor.
            mat.SetFloat("_Glossiness", 0.1f);
            renderer.material = mat;
        }
    }
}

