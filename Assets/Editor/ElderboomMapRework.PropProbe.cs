using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

static partial class ElderboomMapRework
{
    // Inventory of the prop kinds in the village (active objects), plus the lamps and what stands around the church.
    [MenuItem("Elderboom HDRP/Map Rework/28 Prop Probe")]
    static void PropProbe()
    {
        var sb = new StringBuilder();
        var kinds = new Dictionary<string, (int n, Vector3 size, string example)>();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (t.parent == null || t.GetComponent<Renderer>() == null && t.childCount == 0) continue;
            var p = t.position; if (Mathf.Abs(p.x - 5.7f) > 95 || Mathf.Abs(p.z + .7f) > 95) continue;
            // the object whose children make one prop: its own renderer, or a parent named like a prop
            string k = Regex.Replace(t.name, @"(_lod\d+.*|_Var\d+.*|_LOD\d.*|_\d+$|\s*\(\d+\)$)", "");
            if (t.GetComponent<Renderer>() == null) continue;
            if (t.parent != null && Regex.Replace(t.parent.name, @"(_lod\d+.*|_Var\d+.*|_LOD\d.*|_\d+$)", "") != t.parent.name && t.parent.GetComponent<Renderer>() == null && t.parent.name.StartsWith(k.Split('_')[0])) continue;
            var b = t.GetComponent<Renderer>().bounds;
            if (!kinds.TryGetValue(k, out var v)) v = (0, b.size, Path(t));
            kinds[k] = (v.n + 1, v.size, v.example);
        }
        foreach (var kv in kinds.OrderBy(k => k.Key))
        {
            var n = kv.Key.ToLowerInvariant();
            if (!Regex.IsMatch(n, "stall|market|table|bench|crate|barrel|cart|hay|wheel|wood|trough|bucket|sack|basket|fence|wall|lamp|lantern|post|fountain|well|tent|awning|cloth|flag|pot|plant|flower|bollard|anvil|ladder|wagon|stand|shelf|box|log|stone|rock|statue|cross|bush|tree|alder")) continue;
            sb.AppendLine(kv.Value.n.ToString().PadLeft(4) + "  " + kv.Key + "  size " + kv.Value.size.ToString("F1") + "  e.g. " + kv.Value.example);
        }
        sb.AppendLine("== lamps");
        foreach (var t in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (t.type == LightType.Directional) continue;
            var p = t.transform.position; if (Mathf.Abs(p.x - 5.7f) > 95 || Mathf.Abs(p.z + .7f) > 95) continue;
            var top = t.transform; while (top.parent != null && top.parent.parent != null && Bounds(top.parent.gameObject).size.magnitude < 9f) top = top.parent;
            sb.AppendLine("   " + Path(top) + " light " + t.name + " at " + p.ToString("F1"));
        }
        sb.AppendLine("== around the church (x -16..56, z -44..12)");
        var seen = new HashSet<Transform>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            var c = r.bounds.center; if (c.x < -16 || c.x > 56 || c.z < -44 || c.z > 12) continue;
            if (r.transform.IsChildOf(GameObject.Find("FallenCity_Church_HumanScale").transform)) continue;
            var top = r.transform; while (top.parent != null && top.parent.parent != null) top = top.parent;
            if (!seen.Add(top)) continue;
            var b = Bounds(top.gameObject); if (b.size.magnitude > 80) { sb.AppendLine("   (group) " + Path(top)); continue; }
            sb.AppendLine("   " + Path(top) + " " + b.center.ToString("F1") + " / " + b.size.ToString("F1"));
        }
        File.WriteAllText("Logs/prop_probe.txt", sb.ToString());
    }
}
