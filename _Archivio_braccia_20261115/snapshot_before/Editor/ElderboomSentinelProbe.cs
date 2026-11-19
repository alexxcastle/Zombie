using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Logs/claude_sentinel.txt (lines "clip time [yaw]") -> Logs/claude_sentinel_result.txt + Logs/sentinel_shots/*.png
// Checks the Steel Sentinel import (clips, size, bones) and renders the model posed by a clip in the loadout stage.
static class ElderboomSentinelProbe
{
    const string Flag = "Logs/claude_sentinel.txt", Result = "Logs/claude_sentinel_result.txt";
    static double next;
    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        var lines = File.ReadAllLines(Flag); File.Delete(Flag);
        try { File.WriteAllText(Result, Run(lines)); } catch (System.Exception e) { File.WriteAllText(Result, "FAIL " + e); }
    }

    static string Run(string[] lines)
    {
        var sb = new StringBuilder("OK\n");
        const string path = "Assets/Resources/Elderboom/Sentinel/SteelSentinel.fbx";
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        sb.AppendLine("importer " + (importer != null ? importer.animationType + " scale=" + importer.globalScale + " units=" + importer.useFileScale + " anim=" + importer.importAnimation : "MISSING"));
        var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();
        sb.AppendLine("clips " + clips.Length + ": " + string.Join(", ", clips.Select(c => c.name + "(" + c.length.ToString("0.00") + ")")));
        var prefab = Resources.Load<GameObject>("Elderboom/Sentinel/SteelSentinel");
        sb.AppendLine("root rot " + prefab.transform.localRotation.eulerAngles + " scale " + prefab.transform.localScale);
        var stage = ElderboomLoadoutStage.Create();
        var go = Object.Instantiate(prefab);
        try
        {
            ElderboomSentinel.ApplyBodyMaterial(go.transform);
            var b = Bounds(go);
            sb.AppendLine("rest bounds center " + b.center.ToString("F2") + " size " + b.size.ToString("F2"));
            foreach (var n in new[] { "Hips", "RightHand", "StaffA", "StaffB", "RightHandIndex1" })
                sb.AppendLine("bone " + n + " " + (Find(go.transform, n) != null ? "ok" : "MISSING"));
            Directory.CreateDirectory("Logs/sentinel_shots");
            var cam = stage.Camera;
            var rt = new RenderTexture(900, 1100, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt; cam.aspect = 900f / 1100f;
            var camHome = cam.transform.localPosition; go.transform.localScale = Vector3.one * .3f;
            int k = 0;
            foreach (var raw in lines)
            {
                var p = raw.Trim().Split(' ');
                if (p.Length < 2) continue;
                var clip = clips.FirstOrDefault(c => c.name == p[0] || c.name.EndsWith("|" + p[0]));
                if (clip == null) { sb.AppendLine("no clip " + p[0]); continue; }
                float t = float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture);
                float yaw = p.Length > 2 ? float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture) : 150f;
                go.transform.SetPositionAndRotation(stage.transform.position + new Vector3(0f, -.29f, 0f), Quaternion.Euler(0f, yaw, 0f) * prefab.transform.localRotation);
                clip.SampleAnimation(go, Mathf.Min(t, clip.length));
                var bb = Bounds(go);
                cam.transform.localPosition = camHome * 1.15f;
                cam.Render(); cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(900, 1100, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 900, 1100), 0, 0); tex.Apply();
                RenderTexture.active = prev;
                string file = "Logs/sentinel_shots/" + (k++).ToString("00") + "_" + p[0] + ".png";
                File.WriteAllBytes(file, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                var sa = Find(go.transform, "StaffA"); var rh = Find(go.transform, "RightHand");
                sb.AppendLine(file + " bounds " + bb.size.ToString("F2") + " staffA-hand " + (sa != null && rh != null ? Vector3.Distance(sa.position, rh.position).ToString("F2") : "?"));
            }
            cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt);
        }
        finally { Object.DestroyImmediate(go); Object.DestroyImmediate(stage.gameObject); }
        return sb.ToString();
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
    }
    static Transform Find(Transform t, string n)
    {
        if (t.name == n) return t;
        foreach (Transform c in t) { var f = Find(c, n); if (f != null) return f; }
        return null;
    }
}
