using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

// "network": dirt roads painted on the valley terrain between the areas (routed on the NavMesh, so they pass through
// the gates and around buildings), and broken runs of high walls along them: segments with gaps on alternating sides,
// plus a few free walls in the widest fields, to break sight lines and funnel the hordes without closing anything.
// Re-running restores the original terrain paint first (Logs/Fill/alphamaps_original.bin) and replaces the walls.
// "darken": the generated houses, fountains and walls get a darker, dirtier tint.
static partial class ElderboomFill
{
    const string NetRoot = "Elderboom_Road_Network", AlphaBackup = Dir + "/alphamaps_original.bin";
    static readonly (string name, Vector2 p)[] Hubs =
    {
        ("start", new Vector2(78, -31)), ("cemetery", new Vector2(88, 2)), ("churchyard_gate", new Vector2(52, 0)),
        ("square", new Vector2(0, 11)), ("square_south", new Vector2(-8, -40)), ("sawmill_gate", new Vector2(7, 28)),
        ("timber_yard", new Vector2(0, 56)), ("timber_west", new Vector2(-38, 58)), ("woods_gate", new Vector2(39, 51)),
        ("woods", new Vector2(82, 44)), ("wicket", new Vector2(76, 24)), ("farm_gate", new Vector2(-39, -1)),
        ("farm", new Vector2(-70, -12)), ("mill_shortcut", new Vector2(-50, 24)), ("bridge", new Vector2(7, -48)),
        ("creek", new Vector2(18, -80)), ("creek_west", new Vector2(-30, -78)),
    };
    static readonly (string a, string b)[] Roads =
    {
        ("start", "cemetery"), ("start", "churchyard_gate"), ("cemetery", "wicket"), ("wicket", "woods"), ("woods", "woods_gate"),
        ("woods_gate", "timber_yard"), ("timber_yard", "sawmill_gate"), ("sawmill_gate", "square"), ("square", "churchyard_gate"),
        ("square", "farm_gate"), ("farm_gate", "farm"), ("farm", "mill_shortcut"), ("mill_shortcut", "timber_west"), ("timber_west", "timber_yard"),
        ("square", "square_south"), ("square_south", "bridge"), ("bridge", "creek"), ("creek", "creek_west"),
    };

    static string Darken()
    {
        var sb = new StringBuilder();
        var tints = new Dictionary<string, Color>
        {
            { "bakers_house", new Color(.56f, .54f, .52f) }, { "tall_inn", new Color(.56f, .54f, .52f) }, { "watch_house", new Color(.58f, .56f, .54f) },
            { "weavers_house", new Color(.56f, .54f, .52f) }, { "village_fountain", new Color(.6f, .6f, .58f) }, { "wall_fountain", new Color(.58f, .57f, .55f) },
            { "fieldstone_wall_source", new Color(.55f, .55f, .53f) }, { "plaster_wall_source", new Color(.48f, .46f, .44f) },
        };
        foreach (var kv in tints)
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { Kit + kv.Key }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (!m.HasProperty("_BaseColor") || m.GetTexture("_BaseColorMap") == null) continue;
                m.SetColor("_BaseColor", kv.Value); EditorUtility.SetDirty(m); sb.AppendLine("darkened " + m.name + " (" + kv.Key + ")");
            }
        AssetDatabase.SaveAssets(); return sb.ToString();
    }

    static List<Vector3> Route(Vector2 a, Vector2 b, Terrain terrain)
    {
        float H(Vector2 p) => terrain.SampleHeight(new Vector3(p.x, 0, p.y)) + terrain.transform.position.y;
        if (!NavMesh.SamplePosition(new Vector3(a.x, H(a), a.y), out var ha, 6f, NavMesh.AllAreas) || !NavMesh.SamplePosition(new Vector3(b.x, H(b), b.y), out var hb, 6f, NavMesh.AllAreas)) return null;
        var path = new NavMeshPath(); if (!NavMesh.CalculatePath(ha.position, hb.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) return null;
        var dense = new List<Vector3>();
        for (int i = 1; i < path.corners.Length; i++)
        {
            var p0 = path.corners[i - 1]; var p1 = path.corners[i]; int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(p0, p1)));
            for (int k = 0; k < n; k++) dense.Add(Vector3.Lerp(p0, p1, (float)k / n));
        }
        dense.Add(path.corners[path.corners.Length - 1]);
        // Smooth the corners of the NavMesh path into a track (a few passes of neighbour averaging).
        for (int pass = 0; pass < 6; pass++)
            for (int i = 1; i < dense.Count - 1; i++) dense[i] = (dense[i - 1] + dense[i] * 2 + dense[i + 1]) * .25f;
        return dense;
    }

    // Under a roof (church, barns): no road paint and no walls.
    static bool Indoors(Vector3 p, Terrain terrain)
    {
        var o = new Vector3(p.x, terrain.SampleHeight(p) + terrain.transform.position.y + 1.2f, p.z);
        return Physics.Raycast(o, Vector3.up, 25f, ~((1 << 30) | (1 << 2)), QueryTriggerInteraction.Ignore);
    }

    static string Network()
    {
        var terrain = Valley(); var td = terrain.terrainData; var sb = new StringBuilder();
        int aw = td.alphamapWidth, ah = td.alphamapHeight, layers = td.alphamapLayers;
        float[,,] alpha;
        if (File.Exists(AlphaBackup))
        {
            var bytes = File.ReadAllBytes(AlphaBackup); alpha = new float[ah, aw, layers]; System.Buffer.BlockCopy(bytes, 0, alpha, 0, bytes.Length);
        }
        else
        {
            alpha = td.GetAlphamaps(0, 0, aw, ah); var bytes = new byte[alpha.Length * 4]; System.Buffer.BlockCopy(alpha, 0, bytes, 0, bytes.Length); File.WriteAllBytes(AlphaBackup, bytes);
        }
        var before = Reach();
        var old = GameObject.Find(NetRoot); if (old) Object.DestroyImmediate(old); Physics.SyncTransforms();
        var routes = new List<List<Vector3>>(); var hub = Hubs.ToDictionary(h => h.name, h => h.p);
        foreach (var (a, b) in Roads) { var r = Route(hub[a], hub[b], terrain); if (r == null) sb.AppendLine("NO ROUTE " + a + " - " + b); else { routes.Add(r); sb.AppendLine($"road {a} - {b}: {r.Count} m"); } }
        // Paint: dirt (mud layer) in the middle, soil at the shoulders, soft edges.
        int mud = System.Array.FindIndex(td.terrainLayers, l => l.diffuseTexture && l.diffuseTexture.name.Contains("mud")); if (mud < 0) mud = 5;
        int soil = System.Array.FindIndex(td.terrainLayers, l => l.diffuseTexture && l.diffuseTexture.name.Contains("Soil")); if (soil < 0) soil = 0;
        float cell = td.size.x / aw, half = 1.7f, edge = 1.6f; var origin = terrain.transform.position;
        var weight = new float[ah, aw];
        foreach (var r in routes)
            for (int i = 0; i < r.Count; i++)
            {
                var p = r[i]; if (Indoors(p, terrain)) continue; int cx = Mathf.RoundToInt((p.x - origin.x) / cell), cz = Mathf.RoundToInt((p.z - origin.z) / cell), rad = Mathf.CeilToInt((half + edge) / cell);
                for (int z = cz - rad; z <= cz + rad; z++)
                    for (int x = cx - rad; x <= cx + rad; x++)
                    {
                        if (x < 0 || z < 0 || x >= aw || z >= ah) continue;
                        float d = Vector2.Distance(new Vector2(origin.x + x * cell, origin.z + z * cell), new Vector2(p.x, p.z));
                        float w = 1f - Mathf.SmoothStep(0, 1, (d - half) / edge);
                        // Ragged edge so the track does not look ruled.
                        w *= Mathf.Lerp(.75f, 1f, Mathf.PerlinNoise(x * .37f, z * .37f));
                        if (w > weight[z, x]) weight[z, x] = w;
                    }
            }
        for (int z = 0; z < ah; z++)
            for (int x = 0; x < aw; x++)
            {
                float w = weight[z, x]; if (w <= 0) continue;
                float core = Mathf.SmoothStep(0, 1, (w - .35f) / .65f);
                for (int l = 0; l < layers; l++) alpha[z, x, l] *= 1f - w;
                alpha[z, x, mud] += w * core; alpha[z, x, soil] += w * (1f - core);
            }
        td.SetAlphamaps(0, 0, alpha); EditorUtility.SetDirty(td);
        // Walls along the roads.
        var root = new GameObject(NetRoot).transform;
        var stone = AssetDatabase.LoadAssetAtPath<GameObject>(Kit + "fieldstone_wall_source/fieldstone_wall_source.prefab");
        var plaster = AssetDatabase.LoadAssetAtPath<GameObject>(Kit + "plaster_wall_source/plaster_wall_source.prefab");
        var spawns = Object.FindObjectsByType<ElderboomSpawnPoint>(FindObjectsSortMode.None).Select(s => s.transform.position).ToList();
        var gates = Object.FindObjectsByType<ElderboomGate>(FindObjectsSortMode.None).Where(g => g.isActiveAndEnabled).Select(g => g.transform.position).ToList();
        var inter = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Where(m => m is ElderboomStation || m is ElderboomAltarUpgrader).Select(m => m.transform.position).ToList();
        var start = Object.FindAnyObjectByType<HDRPSlicePlayer>().transform.position;
        var areas = Object.FindObjectsByType<ElderboomArea>(FindObjectsSortMode.None);
        var allRoad = routes.SelectMany(r => r).ToList();
        int placed = 0, skipped = 0; var rng = new System.Random(11);
        float H(float x, float z) => terrain.SampleHeight(new Vector3(x, 0, z)) + origin.y;
        bool TryWall(Vector3 c, Vector3 dir, GameObject prefab, string why)
        {
            var flat = new Vector2(c.x, c.z);
            bool Near(List<Vector3> l, float r) => l.Any(q => Vector2.Distance(new Vector2(q.x, q.z), flat) < r);
            if (Near(gates, 9f) || Near(spawns, 4.5f) || Near(inter, 7f) || Vector2.Distance(new Vector2(start.x, start.z), flat) < 12f) { skipped++; return false; }
            var area = areas.FirstOrDefault(a => a.Contains(c)); if (area == null) { skipped++; return false; }
            if (Indoors(c, terrain)) { skipped++; return false; }
            // Never on the road itself.
            if (allRoad.Any(q => Vector2.Distance(new Vector2(q.x, q.z), flat) < 2.6f)) { skipped++; return false; }
            var e1 = c - dir * 2f; var e2 = c + dir * 2f; float h0 = H(e1.x, e1.z), h1 = H(e2.x, e2.z), hc = H(c.x, c.z);
            if (Mathf.Max(h0, h1, hc) - Mathf.Min(h0, h1, hc) > 1.5f) { skipped++; return false; }
            var rot = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up)); rot = Quaternion.FromToRotation(rot * Vector3.right, dir) * rot;
            var probe = Physics.OverlapBox(new Vector3(c.x, Mathf.Min(h0, h1, hc) + 1.4f, c.z), new Vector3(2.1f, .9f, .5f), rot, ~((1 << 30) | (1 << 2)), QueryTriggerInteraction.Ignore)
                .Where(h => !(h is TerrainCollider)).ToList();
            if (probe.Count > 0) { skipped++; return false; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root); go.name = prefab.name + "_" + why + "_" + placed;
            go.transform.SetPositionAndRotation(new Vector3(c.x, Mathf.Min(h0, h1, hc) - .1f, c.z), rot);
            // A box is enough for a wall (cheaper than the 20k-triangle mesh collider, cleaner for the NavMesh).
            foreach (var mc in go.GetComponentsInChildren<MeshCollider>()) Object.DestroyImmediate(mc);
            var bc = go.AddComponent<BoxCollider>(); var b = Bounds(go); bc.center = go.transform.InverseTransformPoint(b.center); bc.size = new Vector3(4.0f, b.size.y, .55f);
            Physics.SyncTransforms(); placed++; return true;
        }
        int ri = 0;
        foreach (var r in routes)
        {
            ri++; float along = 6f + (float)rng.NextDouble() * 6f; int side = ri % 2 == 0 ? 1 : -1;
            for (int i = 2; i < r.Count - 2; i++)
            {
                along -= 1f; if (along > 0) continue;
                // A run of 2-3 modules on one side, then a gap of 7-12 m, then the other side.
                var dir = (r[i + 2] - r[i - 2]); dir.y = 0; if (dir.sqrMagnitude < .01f) continue; dir.Normalize();
                var normal = Vector3.Cross(Vector3.up, dir) * side;
                var area = areas.FirstOrDefault(a => a.Contains(r[i]));
                var prefab = area != null && area.areaName == "Village Square" ? plaster : stone;
                int modules = rng.Next(3, 6), ok = 0;
                for (int m = 0; m < modules && i + m * 4 < r.Count - 2; m++)
                {
                    var c = r[Mathf.Min(r.Count - 1, i + m * 4)] + normal * 3.6f;
                    var d2 = (r[Mathf.Min(r.Count - 1, i + m * 4 + 2)] - r[Mathf.Max(0, i + m * 4 - 2)]); d2.y = 0; d2.Normalize();
                    if (TryWall(c, d2, prefab, "road")) ok++;
                }
                side = -side; along = 8f + (float)rng.NextDouble() * 6f + modules * 4f;
            }
        }
        // Free walls in the widest open fields: short staggered pieces (cover, broken sight lines).
        foreach (var (center, count) in new[] { (new Vector2(92, 45), 5), (new Vector2(20, -82), 5), (new Vector2(-72, 8), 3), (new Vector2(-45, 62), 3), (new Vector2(22, 62), 3), (new Vector2(95, -10), 3) })
        {
            int left = count;
            for (int k = 0; k < count * 3 && left > 0; k++)
            {
                var off = new Vector2((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1) * 14f; var c = new Vector3(center.x + off.x, 0, center.y + off.y);
                float yaw = (float)rng.NextDouble() * 180f; var dir = Quaternion.Euler(0, yaw, 0) * Vector3.right;
                int n = rng.Next(2, 4); bool any = false;
                for (int m = 0; m < n; m++) any |= TryWall(c + dir * (4f * m), dir, stone, "field");
                if (any && rng.NextDouble() < .5) { var leg = Vector3.Cross(Vector3.up, dir); var corner = c + dir * (4f * n - 2f) + leg * 2f; TryWall(corner, leg, stone, "field"); TryWall(corner + leg * 4f, leg, stone, "field"); }
                if (any) left--;
            }
        }
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.ReflectionProbeStatic);
        sb.AppendLine($"walls placed {placed}, skipped {skipped}");
        sb.AppendLine(ElderboomClaudeBlocks.BakeAccessNavigation());
        var after = Reach(); int broken = 0;
        foreach (var kv in before)
        {
            float a = after.TryGetValue(kv.Key, out var v) ? v : -1; bool bad = kv.Value > 0 && (a < 0 || a > kv.Value * 1.35f + 6f); if (bad) broken++;
            if (bad || (a > 0 && kv.Value > 0 && a > kv.Value + 4f)) sb.AppendLine($"{(bad ? "ROUTE BROKEN" : "route longer")} {kv.Key} {kv.Value:F0} -> {a:F0} m");
        }
        sb.AppendLine("routes checked " + before.Count + ", broken " + broken);
        EditorSceneManager.MarkSceneDirty(root.gameObject.scene); EditorSceneManager.SaveOpenScenes(); AssetDatabase.SaveAssets();
        return sb.ToString();
    }
}
