using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

// Logs/claude_m4shot.txt -> puts the Benelli M4 GLB in the open scene for a moment, takes review pictures
// (Logs/M4Shots/*.jpg) under the scene's own lighting, then removes it. Nothing is saved to the scene.
static class ElderboomClaudeM4Shot
{
    const string Flag = "Logs/claude_m4shot.txt", Result = "Logs/claude_m4shot_result.txt", Dir = "Logs/M4Shots";
    const string Asset = "Assets/GameAssets/Benelli_M4/Benelli_M4_FPS.glb";
    static double next;
    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 1.5;
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Asset);
        if (prefab == null) { AssetDatabase.ImportAsset(Asset, ImportAssetOptions.ForceUpdate); return; }
        File.Delete(Flag);
        var sb = new StringBuilder();
        try
        {
            Directory.CreateDirectory(Dir);
            Light best = null; float bi = 0;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (!l.isActiveAndEnabled || l.type == LightType.Directional) continue;
                if (l.transform.position.y > 12) continue;
                float i = l.intensity * Mathf.Max(1, l.range);
                if (i > bi) { bi = i; best = l; }
            }
            Vector3 p = new Vector3(0, 1.6f, 0);
            if (best)
            {
                var f = best.type == LightType.Spot ? best.transform.forward : Vector3.down;
                p = best.transform.position + f * Mathf.Min(3f, best.range * .3f);
                if (Physics.Raycast(p + Vector3.up * 2, Vector3.down, out var hit, 20)) p.y = hit.point.y + 1.3f;
                sb.AppendLine("light " + best.name + " " + best.type + " int " + best.intensity + " at " + best.transform.position);
            }
            var gun = (GameObject)Object.Instantiate(prefab, p, Quaternion.Euler(0, 30, 0));
            gun.name = "M4_Review";
            var b = new Bounds(p, Vector3.zero); foreach (var r in gun.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            sb.AppendLine("pos " + p + " bounds " + b.center + " size " + b.size);
            foreach (Transform t in gun.GetComponentsInChildren<Transform>()) if (t.name.StartsWith("MainWeapon") || t.name == "Muzzle") sb.AppendLine(t.name + " " + t.position);
            Vector3 c = b.center; var g = gun.transform;
            Vector3 W(float x, float y, float z) => c + g.right * x + g.up * y + g.forward * z;
            Still("fps", W(0.07f, 0.13f, -0.55f), W(0, 0.05f, 0.5f), 60);
            Still("right", W(0.75f, 0.18f, 0.05f), c, 50);
            Still("left", W(-0.75f, 0.18f, -0.05f), c, 50);
            Still("close", W(0.28f, 0.12f, -0.12f), W(0, 0.03f, -0.12f), 50);
            Object.DestroyImmediate(gun);
            sb.AppendLine("DONE");
        }
        catch (System.Exception e) { sb.AppendLine("FAIL " + e); }
        File.WriteAllText(Result, sb.ToString());
    }
    static void Still(string name, Vector3 eye, Vector3 target, float fov)
    {
        var go = new GameObject("M4_Review_Camera"); var cam = go.AddComponent<Camera>(); go.AddComponent<HDAdditionalCameraData>();
        cam.fieldOfView = fov; cam.nearClipPlane = .02f; cam.farClipPlane = 500;
        go.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, Vector3.up));
        var rt = new RenderTexture(1600, 900, 24); var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false); cam.targetTexture = rt;
        for (int i = 0; i < 16; i++) cam.Render();
        RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply();
        File.WriteAllBytes(Dir + "/" + name + ".jpg", tex.EncodeToJPG(92));
        RenderTexture.active = null; cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); Object.DestroyImmediate(go);
    }
}
