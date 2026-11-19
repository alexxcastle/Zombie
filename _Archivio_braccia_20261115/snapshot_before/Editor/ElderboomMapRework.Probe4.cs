using System.Linq;
using System.Text;
using System.IO;
using UnityEditor;
using UnityEngine;

static partial class ElderboomMapRework
{
    [MenuItem("Elderboom HDRP/Map Rework/30 Probe Kit 2")]
    static void Probe4()
    {
        var sb = new StringBuilder();
        string[] keys = { "bench", "stall", "cart", "barrel", "crate", "basket", "sack", "awning", "table", "market", "mossystonewall", "modularfence", "fence_planks", "fence_farm", "bank_north", "bank_south", "streetlamp", "forged_lantern", "planter", "flower", "well", "woodpile", "hay" };
        var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var k in keys)
        {
            var hits = all.Where(t => t.name.ToLowerInvariant().Contains(k) && (t.parent == null || !t.parent.name.ToLowerInvariant().Contains(k)) && t.GetComponentInChildren<Renderer>() != null).ToList();
            sb.AppendLine("== " + k + " " + hits.Count);
            foreach (var t in hits.Take(k.Contains("bank") || k.Contains("lamp") || k.Contains("lantern") ? 60 : 8))
            {
                var b = Bounds(t.gameObject);
                sb.AppendLine("  " + Path(t) + " pos " + t.position.ToString("F1") + " rotY " + t.eulerAngles.y.ToString("F0") + " scl " + t.lossyScale.ToString("F2") + " size " + b.size.ToString("F1") + " minY " + b.min.y.ToString("F2") + " cols " + t.GetComponentsInChildren<Collider>().Length);
            }
        }
        var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameAssets/Environment/BridgeCemetery/wide_stone_bridge_metric/WideBridge_Stone_HDRP.mat");
        foreach (var n in m.GetTexturePropertyNames()) { var tx = m.GetTexture(n); if (tx) sb.AppendLine("bridge " + n + " " + AssetDatabase.GetAssetPath(tx)); }
        sb.AppendLine("bridge color " + m.GetColor("_BaseColor"));
        var road = Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).FirstOrDefault(f => f.name.StartsWith("Road_") && f.sharedMesh && f.sharedMesh.isReadable);
        if (road) { var v = road.sharedMesh.vertices; var uv = road.sharedMesh.uv; var mr = road.GetComponent<MeshRenderer>();
            sb.AppendLine("road " + road.name + " v0 " + road.transform.TransformPoint(v[0]).ToString("F2") + " uv " + uv[0] + " v1 " + road.transform.TransformPoint(v[1]).ToString("F2") + " uv " + uv[1] + " v2 " + road.transform.TransformPoint(v[2]).ToString("F2") + " uv " + uv[2] + " mat " + AssetDatabase.GetAssetPath(mr.sharedMaterial) + " layer " + road.gameObject.layer + " col " + (road.GetComponent<Collider>() != null) + " parent " + Path(road.transform.parent)); }
        File.WriteAllText("Logs/probe4.txt", sb.ToString());
    }
}
