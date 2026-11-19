using UnityEngine;

// The world beyond the map (layer 30 "Backdrop", built by the editor tool ElderboomBackdrop): the player camera sees it
// up to BackdropFar while everything else keeps the camera's own view distance, so performance is unchanged.
public sealed class ElderboomBackdropRuntime : MonoBehaviour
{
    public const int Layer = 30;
    public float backdropFar = 3200f;
    public float forestTreeDistance = 150f; // the forest the player sees, as before; beyond it the ridge silhouette
    Camera configured;
    float baseFar;

    void Start()
    {
#if UNITY_EDITOR
        // A/B performance check: Logs/claude_no_backdrop.txt switches the backdrop off in the editor.
        if (System.IO.File.Exists("Logs/claude_no_backdrop.txt")) { gameObject.SetActive(false); return; }
#endif
        foreach (var t in Terrain.activeTerrains) if (t.terrainData.size.x > 400) t.treeDistance = forestTreeDistance;
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null || cam == configured) return;
        baseFar = Mathf.Min(cam.farClipPlane, 400f);
        var d = new float[32];
        for (int i = 0; i < 32; i++) d[i] = baseFar;
        d[Layer] = 0f; // 0 = up to the far plane
        cam.farClipPlane = backdropFar;
        cam.layerCullDistances = d;
        cam.layerCullSpherical = true;
        configured = cam;
    }
}

