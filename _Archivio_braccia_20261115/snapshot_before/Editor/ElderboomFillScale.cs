using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// "buildings" (report) / "buildings apply <factor>": every house, hall, barn and similar structure in the scene a
// little bigger, the church excepted. A building is a scene object with a footprint of 4-45 m, at least 3.5 m tall,
// not thin (walls and fences stay), and not part of a bigger building. Each grows about its own ground centre, so it
// stays seated where it is. Navigation is baked again and the spawn routes checked.
static partial class ElderboomFill
{
    static readonly string[] NotBuildings = { "church", "chiesa", "terrain", "access", "gate", "wall", "fence", "palisade", "tree", "rock", "cliff", "bank",
        "helicopter", "horse", "bridge", "spawn", "area", "station", "altar", "lamp", "light", "road_network", "backdrop", "mountain", "valley", "foliage", "grass", "bush", "hedge", "cable", "player", "zombie", "cypress", "alder", "consecrated", "ground", "crop", "pine", "oak", "birch", "willow" };

    static bool Excluded(Transform t)
    {
        for (var x = t; x != null; x = x.parent)
        {
            string n = x.name.ToLowerInvariant();
            foreach (var k in NotBuildings) if (n.Contains(k)) return true;
            if (x.gameObject.layer == 30) return true;
        }
        return false;
    }

    static List<(Transform t, Bounds b)> FindBuildings()
    {
        var all = new List<(Transform t, Bounds b)>();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (t.GetComponentInChildren<Renderer>() == null || t.GetComponent<Terrain>() != null) continue;
            if (Excluded(t)) continue;
            // Thematic groups (02_Farm..., 05_Creek...): their buildings are the children.
            if (t.name.Length > 3 && char.IsDigit(t.name[0]) && char.IsDigit(t.name[1]) && t.name[2] == '_') continue;
            if (t.childCount > 25 && t.GetComponent<Renderer>() == null && t.GetComponentsInChildren<Renderer>().Length > 60 && !t.name.ToLowerInvariant().Contains("building")) continue;
            var rs = t.GetComponentsInChildren<Renderer>(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            float fx = b.size.x, fz = b.size.z, big = Mathf.Max(fx, fz), small = Mathf.Min(fx, fz);
            if (big < 4f || big > 45f || small < 2.5f || b.size.y < 3.5f) continue;
            all.Add((t, b));
        }
        // Keep the outermost: drop any candidate inside another candidate.
        var set = new HashSet<Transform>(all.Select(a => a.t));
        return all.Where(a => { for (var p = a.t.parent; p != null; p = p.parent) if (set.Contains(p)) return false; return true; })
                  // and not the group objects holding several buildings (their bounds are the union of many)
                  .Where(a => a.t.GetComponentsInChildren<Renderer>().Length < 400).ToList();
    }

    static string ScaleBuildings(string mode)
    {
        var sb = new StringBuilder();
        var parts = mode.Split(' '); bool apply = parts.Length > 1 && parts[1] == "apply";
        float factor = parts.Length > 2 ? P(parts[2]) : 1.08f;
        var list = FindBuildings();
        sb.AppendLine("buildings " + list.Count);
        foreach (var (t, b) in list.OrderBy(x => x.t.name))
            sb.AppendLine($"  {PathOf(t)}  {F(b.size.x)}x{F(b.size.y)}x{F(b.size.z)} at {F(b.center.x)},{F(b.center.z)}");
        if (!apply) return sb.ToString();
        var before = Reach();
        foreach (var (t, b) in list)
        {
            Undo.RecordObject(t, "scale buildings");
            Vector3 anchor = new Vector3(b.center.x, b.min.y, b.center.z);
            t.localScale *= factor;
            t.position = anchor + (t.position - anchor) * factor;
        }
        Physics.SyncTransforms();
        sb.AppendLine("scaled x" + F(factor));
        sb.AppendLine(ElderboomClaudeBlocks.BakeAccessNavigation());
        var after = Reach(); int broken = 0;
        foreach (var kv in before)
        {
            float a = after.TryGetValue(kv.Key, out var v) ? v : -1; bool bad = kv.Value > 0 && (a < 0 || a > kv.Value * 1.35f + 6f); if (bad) { broken++; sb.AppendLine($"ROUTE BROKEN {kv.Key} {kv.Value:F0} -> {a:F0}"); }
        }
        sb.AppendLine("routes checked " + before.Count + ", broken " + broken);
        EditorSceneManager.MarkSceneDirty(list.Count > 0 ? list[0].t.gameObject.scene : EditorSceneManager.GetActiveScene()); EditorSceneManager.SaveOpenScenes();
        return sb.ToString();
    }
}
