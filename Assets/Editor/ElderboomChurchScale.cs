using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Logs/claude_church.txt: "scale 0.9" shrinks the church (and the weapon upgrader inside it) around the centre of the
// nave floor, keeping the benches at their present size; "shots" only takes the review pictures. The factor is
// relative to the original human-scale church (scale 4), so running it again with the same value changes nothing.
// Afterwards the navigation is baked again and the scene saved. Result: Logs/claude_church_result.txt
static partial class ElderboomClaudeBlocks
{
    const string ChurchFlag = "Logs/claude_church.txt", ChurchResult = "Logs/claude_church_result.txt";
    const float ChurchOriginalScale = 4f;

    [InitializeOnLoadMethod]
    static void ChurchScaleWatch() { EditorApplication.update += ChurchScalePoll; }
    static double churchNext;
    static void ChurchScalePoll()
    {
        if (EditorApplication.timeSinceStartup < churchNext) return; churchNext = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(ChurchFlag) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        string mode = File.ReadAllText(ChurchFlag).Trim(); File.Delete(ChurchFlag);
        try { File.WriteAllText(ChurchResult, ChurchScale(mode)); } catch (System.Exception e) { File.WriteAllText(ChurchResult, "FAIL " + e); }
    }

    static bool IsBench(Renderer r) => r.sharedMaterials.Any(m => m != null && (m.name.Contains("Bench") || m.name.ToLowerInvariant() == "banc"));

    static string ChurchScale(string mode)
    {
        if (EditorSceneManager.GetActiveScene().path != "Assets/Scenes/Elderboom_Unity_Port.unity") EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        terrain = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude).Where(t => t.gameObject.layer != 30).OrderByDescending(t => t.terrainData.size.x).First(); data = terrain.terrainData;
        var church = GameObject.Find(ChurchRootName); if (!church) return "FAIL church not found";
        Directory.CreateDirectory(ChurchReview);
        var sb = new StringBuilder();
        var renderers = church.GetComponentsInChildren<Renderer>();
        var all = renderers[0].bounds; foreach (var r in renderers) all.Encapsulate(r.bounds);
        var floorR = renderers.Where(r => r.sharedMaterials.Any(m => m && (m.name.Contains("Floor") || m.name.ToLowerInvariant() == "sol"))).ToArray();
        float floorY = floorR.Length > 0 ? floorR.Max(r => r.bounds.max.y) : all.min.y;
        var pivot = new Vector3(all.center.x, floorY, all.center.z);
        bool alongX = all.size.x >= all.size.z;
        void Shots(string tag)
        {
            Vector3 axis = alongX ? Vector3.right : Vector3.forward; float half = (alongX ? all.size.x : all.size.z) * .5f;
            ChurchStill("scale_" + tag + "_inside_a", pivot - axis * half * .78f + Vector3.up * 2.2f, pivot + axis * half + Vector3.up * 1.2f);
            ChurchStill("scale_" + tag + "_inside_b", pivot + axis * half * .78f + Vector3.up * 2.2f, pivot - axis * half + Vector3.up * 1.2f);
            ChurchStill("scale_" + tag + "_outside", pivot + new Vector3(-55, 22, -55), pivot + Vector3.up * 8);
        }
        if (mode.StartsWith("shots")) { Shots("now"); return "shots done, scale " + church.transform.localScale.x + "\nDONE"; }
        if (!mode.StartsWith("scale")) return "FAIL unknown mode " + mode;
        float k = float.Parse(mode.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture);
        float r0 = church.transform.localScale.x, ratio = ChurchOriginalScale * k / r0;
        sb.AppendLine("church scale " + r0 + " -> " + ChurchOriginalScale * k + " (x" + ratio.ToString("F4") + "), pivot " + pivot + ", bounds " + all.size);
        if (Mathf.Abs(ratio - 1) < 1e-4f) return sb.Append("nothing to do\nDONE").ToString();
        Shots("before");

        // Benches: remember their world bounds to restore their size afterwards.
        var benches = renderers.Where(IsBench).ToList();
        var benchBefore = benches.ToDictionary(b => b, b => b.bounds);
        // The weapon upgrader and anything else standing inside the church walls goes with it.
        var inside = new List<Transform>();
        foreach (var up in Object.FindObjectsByType<ElderboomAltarUpgrader>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (all.Contains(up.transform.position)) inside.Add(up.transform);
        Undo.RegisterFullObjectHierarchyUndo(church, "Scale church");
        foreach (var t in inside) Undo.RegisterFullObjectHierarchyUndo(t.gameObject, "Scale church");
        foreach (var t in inside.Prepend(church.transform))
        {
            t.position = pivot + (t.position - pivot) * ratio;
            t.localScale *= ratio;
            sb.AppendLine("  scaled " + t.name + " -> " + t.position);
        }
        foreach (var b in benches)
        {
            var old = benchBefore[b]; var t = b.transform;
            t.localScale /= ratio;
            var now = b.bounds;
            var want = new Vector3(pivot.x + (old.center.x - pivot.x) * ratio, pivot.y + (old.min.y - pivot.y) * ratio, pivot.z + (old.center.z - pivot.z) * ratio);
            t.position += new Vector3(want.x - now.center.x, want.y - now.min.y, want.z - now.center.z);
            sb.AppendLine("  bench " + t.name + " size " + old.size + " kept " + b.bounds.size);
        }
        // Things near the church that are not part of it (lights, props): listed for review, not moved.
        var near = new Bounds(all.center, all.size + Vector3.one * 2);
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            if (!r.transform.IsChildOf(church.transform) && !inside.Any(t => r.transform.IsChildOf(t)) && all.Contains(r.bounds.center))
                sb.AppendLine("  inside, not moved: " + r.transform.parent?.name + "/" + r.name);
        sb.AppendLine(Bake());
        EditorSceneManager.MarkSceneDirty(church.scene);
        EditorSceneManager.SaveScene(church.scene);
        var after = church.GetComponentsInChildren<Renderer>(); all = after[0].bounds; foreach (var r in after) all.Encapsulate(r.bounds);
        Shots("after");
        sb.AppendLine("new bounds " + all.size + "\nDONE");
        return sb.ToString();
    }
}
