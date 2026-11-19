using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Elderboom HDRP > Dev > Scale Report: measures the open scene and the character / weapon prefabs in metres
// and writes Logs/scale_report.txt (edit mode).
static class ElderboomScaleReport
{
    static readonly string[] Keys =
    {
        "door", "gate", "lantern", "lamp", "tomb", "grave", "cross", "house", "cottage", "barn", "chapel", "church", "tree", "pine", "fir",
        "fence", "bench", "banc", "table", "chair", "barrel", "crate", "cart", "wagon", "well", "bed", "window", "stair", "station", "horse",
        "altar", "autel", "fountain", "bridge", "tower", "turret", "sign", "bucket", "ladder", "post", "wall", "arch", "colonne", "pied",
        "hut", "shed", "mill", "stall", "hay", "log", "rock", "bush", "hedge", "fern", "generator", "upgrader", "pickup", "spawn"
    };

    [MenuItem("Elderboom HDRP/Dev/Scale Report")]
    static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("SCENE " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + "   (metres: height / width / depth)");

        var player = Object.FindAnyObjectByType<HDRPSlicePlayer>(FindObjectsInactive.Include);
        if (player != null)
        {
            var cc = player.GetComponent<CharacterController>();
            var cam = player.GetComponentInChildren<Camera>(true);
            sb.AppendLine("PLAYER capsule height=" + (cc != null ? cc.height.ToString("F2") : "?") + " radius=" + (cc != null ? cc.radius.ToString("F2") : "?")
                + " eye above feet=" + (cam != null && cc != null ? (cam.transform.position.y - (player.transform.TransformPoint(cc.center).y - cc.height * .5f)).ToString("F2") : "?")
                + " stepOffset=" + (cc != null ? cc.stepOffset.ToString("F2") : "?") + " scale=" + player.transform.lossyScale);
        }

        // Scene objects grouped by cleaned name.
        var groups = new Dictionary<string, List<Vector3>>();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
        {
            string lower = t.name.ToLowerInvariant();
            if (!Keys.Any(k => lower.Contains(k))) continue;
            // Skip children of an already-matching parent, so a house is measured once.
            bool nested = false;
            for (var p = t.parent; p != null; p = p.parent) { string pl = p.name.ToLowerInvariant(); if (Keys.Any(k => pl.Contains(k))) { nested = true; break; } }
            if (nested) continue;
            var renderers = t.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer)).ToArray();
            if (renderers.Length == 0) continue;
            var b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds);
            string key = System.Text.RegularExpressions.Regex.Replace(t.name, @"[\s_\-\.]*\(?\d+\)?$", "");
            key = System.Text.RegularExpressions.Regex.Replace(key, @"_?\d+$", "");
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<Vector3>();
            list.Add(b.size);
        }
        sb.AppendLine(); sb.AppendLine("SCENE OBJECTS  name | count | median H x W x D | H range");
        foreach (var g in groups.OrderBy(g => g.Key))
        {
            var hs = g.Value.Select(v => v.y).OrderBy(v => v).ToList();
            var med = g.Value.OrderBy(v => v.y).ElementAt(g.Value.Count / 2);
            sb.AppendLine(g.Key.PadRight(46) + " | " + g.Value.Count.ToString().PadLeft(3) + " | " + med.y.ToString("F2") + " x " + Mathf.Max(med.x, med.z).ToString("F2") + " x " + Mathf.Min(med.x, med.z).ToString("F2")
                + " | " + hs.First().ToString("F2") + "-" + hs.Last().ToString("F2"));
        }

        sb.AppendLine(); sb.AppendLine("CHARACTER PREFABS (height of the skinned mesh at prefab scale)");
        var prefabs = new List<(string, GameObject)>();
        foreach (var key in new[] { "Elderboom/Zombie", "Elderboom/ZombieFemale" }) prefabs.Add((key, Resources.Load<GameObject>(key)));
        foreach (var z in Resources.LoadAll<GameObject>("Elderboom/Zombies")) prefabs.Add(("Zombies/" + z.name, z));
        foreach (var c in Resources.LoadAll<GameObject>("Elderboom/Characters")) prefabs.Add(("Characters/" + c.name, c));
        foreach (var m in Resources.LoadAll<GameObject>("Elderboom/Mounts")) prefabs.Add(("Mounts/" + m.name, m));
        foreach (var (name, prefab) in prefabs) sb.AppendLine(Measure(name, prefab));

        sb.AppendLine(); sb.AppendLine("WEAPON PREFABS (longest side of the weapon meshes, arms excluded)");
        foreach (var w in Resources.LoadAll<GameObject>("Elderboom/Weapons")) sb.AppendLine(Measure("Weapons/" + w.name, w, true));

        // Buildings: the children of the architecture groups, and each one's largest parts.
        sb.AppendLine(); sb.AppendLine("BUILDINGS  path | H x W x D | renderers | localScale");
        foreach (var rootName in new[] { "Village_Household_Compounds", "Elderboom_Fill_Architecture", "Elderboom_Access_Redesign", "02_Farm_Barn_Paddocks_And_Crops", "03_Sawmill_Cutting_Seasoning_And_Loading" })
        {
            var root = GameObject.Find(rootName);
            if (root == null) continue;
            sb.AppendLine("# " + rootName);
            foreach (Transform child in root.transform) Building(sb, child, 1);
        }
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/scale_report.txt", sb.ToString());
        Debug.Log("[ScaleReport] Logs/scale_report.txt (" + groups.Count + " groups)");
    }

    static string Measure(string name, GameObject prefab, bool weapon = false)
    {
        if (prefab == null) return name.PadRight(40) + " | missing";
        var go = Object.Instantiate(prefab); go.hideFlags = HideFlags.HideAndDontSave;
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        try
        {
            bool has = false; Bounds b = default;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                string n = r.name.ToLowerInvariant();
                if (weapon && (r is SkinnedMeshRenderer || n.Contains("arm") || n.Contains("hand") || n.Contains("body"))) continue;
                Bounds rb;
                if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                {
                    var mesh = new Mesh(); smr.BakeMesh(mesh, true);
                    var mb = mesh.bounds; Object.DestroyImmediate(mesh);
                    rb = TransformBounds(smr.transform.localToWorldMatrix, mb);
                }
                else rb = r.bounds;
                if (rb.size == Vector3.zero) continue;
                if (!has) { b = rb; has = true; } else b.Encapsulate(rb);
            }
            if (!has) return name.PadRight(40) + " | no meshes";
            var s = b.size;
            return name.PadRight(40) + " | " + (weapon ? "length " + Mathf.Max(s.x, s.y, s.z).ToString("F3") + "  (" + s.x.ToString("F2") + " x " + s.y.ToString("F2") + " x " + s.z.ToString("F2") + ")"
                : "height " + s.y.ToString("F2") + "  width " + Mathf.Max(s.x, s.z).ToString("F2") + "  rootScale " + prefab.transform.localScale.x.ToString("F2"));
        }
        finally { Object.DestroyImmediate(go); }
    }

    static Bounds TransformBounds(Matrix4x4 m, Bounds b)
    {
        var result = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
            result.Encapsulate(m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
        return result;
    }

    static void Building(StringBuilder sb, Transform t, int depth)
    {
        var rs = t.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer)).ToArray();
        if (rs.Length == 0) return;
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        if (b.size.y < 1.5f && depth > 1) return;
        sb.AppendLine(new string(' ', depth * 2) + t.name.PadRight(48 - depth * 2) + " | " + b.size.y.ToString("F2") + " x " + Mathf.Max(b.size.x, b.size.z).ToString("F2") + " x " + Mathf.Min(b.size.x, b.size.z).ToString("F2")
            + " | " + rs.Length + " | " + t.localScale.x.ToString("F2"));
        if (depth < 3 && t.childCount > 0 && t.childCount <= 40) foreach (Transform c in t) Building(sb, c, depth + 1);
    }
}
