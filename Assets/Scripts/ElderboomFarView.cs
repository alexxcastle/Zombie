using UnityEngine;

// Lets the player camera see the mountain terrain around the valley (layer 30 "Backdrop") far away, while everything
// else keeps the camera's own view distance (layer cull distances), and the valley forest its tree distance.
public sealed class ElderboomFarView : MonoBehaviour
{
    public const int Layer = 30;
    public float far = 2600f, valleyTrees = 150f;
    Camera configured;

    void Start()
    {
        foreach (var t in Terrain.activeTerrains) if (t.gameObject.layer != Layer) t.treeDistance = Mathf.Min(t.treeDistance, valleyTrees);
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null || cam == configured) return;
#if UNITY_EDITOR
        // Performance experiments: Logs/claude_farview.txt = "<far> <valleyTrees> <cullDefault 0/1>"
        if (System.IO.File.Exists("Logs/claude_farview.txt"))
        {
            var a = System.IO.File.ReadAllText("Logs/claude_farview.txt").Split(' ');
            far = float.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture);
            foreach (var t in Terrain.activeTerrains) if (t.gameObject.layer != Layer) t.treeDistance = float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture);
            if (a.Length > 2 && a[2].Trim() == "0") { cam.farClipPlane = far; configured = cam; Debug.Log("[FarView] far only " + far); return; }
        }
#endif
        float baseFar = Mathf.Min(cam.farClipPlane, 400f);
        var d = new float[32];
        for (int i = 0; i < 32; i++) d[i] = baseFar;
        d[Layer] = 0f;
        cam.layerCullDistances = d; cam.layerCullSpherical = true; cam.farClipPlane = far;
        configured = cam;
    }
}
