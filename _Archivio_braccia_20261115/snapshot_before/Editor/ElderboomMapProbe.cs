using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Elderboom HDRP > Dev > Map Probe: lists what stands at the flagged points of the map review (Logs/map_probe.txt).
static class ElderboomMapProbe
{
    // Pixel on the 1600 px village map -> world XZ (centre 5.7,-0.7, span 190 m, up = +Z).
    public static Vector3 World(float px, float py) => new Vector3(5.7f + (px / 1600f - .5f) * 190f, 0f, -.7f + (.5f - py / 1600f) * 190f);

    static readonly (string tag, float px, float py, float r)[] Points =
    {
        ("1 house broken", 600, 1450, 7), ("2 bush broken", 582, 582, 4),
        ("3a frag", 391, 585, 2.5f), ("3b frag", 297, 1051, 2.5f), ("3c frag", 403, 1279, 2.5f), ("3d frag", 788, 1580, 2.5f), ("3e frag", 983, 1440, 2.5f),
        ("3f frag", 1003, 1471, 2.5f), ("3g frag", 1188, 1286, 2.5f), ("3h frag", 1320, 1203, 2.5f), ("3i frag", 1525, 885, 2.5f),
        ("5 slab", 1100, 900, 3), ("8 pond", 490, 550, 3), ("8 pond fence", 450, 520, 2), ("12 rock", 1400, 1238, 5), ("13 tower", 1420, 150, 6),
        ("7 railing", 560, 1163, 2.5f), ("7 creek bed", 620, 1200, 3), ("10a fence", 300, 585, 3), ("10b fence", 1300, 592, 3), ("10c fence", 1205, 960, 3),
        ("15 yellow tree", 110, 160, 6), ("15 magenta bush", 165, 370, 4), ("9 house copy", 718, 288, 5), ("6 bridge", 810, 1192, 4), ("glass", 135, 735, 4), ("glass2", 490, 1385, 4),
    };

    [MenuItem("Elderboom HDRP/Dev/Map Probe")]
    static void Run()
    {
        var sb = new StringBuilder();
        var renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude).Where(r => !(r is ParticleSystemRenderer)).ToArray();
        foreach (var (tag, px, py, radius) in Points)
        {
            Vector3 w = World(px, py);
            sb.AppendLine("== " + tag + "  world x=" + w.x.ToString("F1") + " z=" + w.z.ToString("F1"));
            var near = new List<(float d, Renderer r)>();
            foreach (var r in renderers)
            {
                var b = r.bounds;
                if (b.size.x > 150f || b.size.z > 150f) continue; // terrain-sized
                Vector3 c = b.ClosestPoint(new Vector3(w.x, b.center.y, w.z));
                float d = new Vector2(c.x - w.x, c.z - w.z).magnitude;
                if (d <= radius) near.Add((d, r));
            }
            foreach (var (d, r) in near.OrderBy(n => n.d).Take(7))
            {
                var mats = r.sharedMaterials.Select(m => m == null ? "NULL" : m.name + "[" + (m.shader != null ? m.shader.name : "no shader") + (m.shader != null && !m.shader.isSupported ? " UNSUPPORTED" : "") + "]");
                var mf = r.GetComponent<MeshFilter>();
                sb.AppendLine("  d=" + d.ToString("F1") + " " + Path(r.transform) + " | mesh=" + (mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "-") + " | size=" + r.bounds.size.ToString("F1")
                    + " y=" + r.bounds.min.y.ToString("F1") + ".." + r.bounds.max.y.ToString("F1") + " | " + string.Join(", ", mats));
            }
            // Terrain trees near the point.
            foreach (var t in Terrain.activeTerrains)
            {
                var data = t.terrainData; int count = 0; var kinds = new Dictionary<string, int>();
                foreach (var tree in data.treeInstances)
                {
                    Vector3 p = Vector3.Scale(tree.position, data.size) + t.transform.position;
                    if (new Vector2(p.x - w.x, p.z - w.z).magnitude > radius + 3f) continue;
                    string n = data.treePrototypes[tree.prototypeIndex].prefab != null ? data.treePrototypes[tree.prototypeIndex].prefab.name : "?";
                    kinds[n] = kinds.TryGetValue(n, out int c) ? c + 1 : 1; count++;
                }
                if (count > 0) sb.AppendLine("  terrain '" + t.name + "' trees: " + string.Join(", ", kinds.Select(k => k.Key + "x" + k.Value)));
            }
        }
        sb.AppendLine(); sb.AppendLine("TERRAINS");
        foreach (var t in Terrain.activeTerrains)
            sb.AppendLine("  " + t.name + " pos=" + t.transform.position + " size=" + t.terrainData.size + " trees=" + t.terrainData.treeInstanceCount + " protos=" + string.Join(", ", t.terrainData.treePrototypes.Select(p => p.prefab != null ? p.prefab.name : "?"))
                + " layers=" + string.Join(", ", t.terrainData.terrainLayers.Select(l => l != null ? l.name : "?")));
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/map_probe.txt", sb.ToString());
        Debug.Log("[MapProbe] Logs/map_probe.txt");
    }

    static string Path(Transform t)
    {
        var parts = new List<string>();
        for (var p = t; p != null && parts.Count < 5; p = p.parent) parts.Add(p.name);
        parts.Reverse();
        return string.Join("/", parts);
    }
}
