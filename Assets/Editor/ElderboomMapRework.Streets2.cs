using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Street plans for South Creek and the Outer Woods (same method as the Village Square, Map Rework 19b):
// the tangle of tracks inside the area is erased (kept near the area border, where the outer tracks and gates meet,
// and inside the "keep" circles, e.g. the ring around the amphitheatre), a few real streets are painted, and the
// gravel strips crossing the area are cut at its border.
static partial class ElderboomMapRework
{
    static readonly (string area, Vector3[] keep, (string name, float half, Vector2[] pts)[] streets)[] AreaPlans =
    {
        ("South Creek", new Vector3[0], new[]
        {
            ("Lungofosso",       1.7f, new[] { new Vector2(-30, -54.5f), new Vector2(-12, -55), new Vector2(0, -55.5f), new Vector2(6.8f, -57), new Vector2(12, -57), new Vector2(18, -55), new Vector2(30, -54.9f), new Vector2(48, -54.9f) }),
            ("Via del Mercato",  2.0f, new[] { new Vector2(12, -57), new Vector2(13, -66), new Vector2(13, -75), new Vector2(13, -86) }),
            ("Via di Mezzo",     2.2f, new[] { new Vector2(-40, -74), new Vector2(-20, -74), new Vector2(0, -74.5f), new Vector2(13, -75), new Vector2(18, -78), new Vector2(30, -77.5f), new Vector2(45, -78), new Vector2(60, -73) }),
        }),
        ("Outer Woods", new[] { new Vector3(79, 77, 22) }, new[]
        {
            ("Via del Bosco",    1.8f, new[] { new Vector2(39, 51), new Vector2(46, 49.7f), new Vector2(55, 49.8f), new Vector2(58.5f, 50.2f), new Vector2(63, 52.5f), new Vector2(75, 52.5f), new Vector2(86, 52.5f), new Vector2(95, 49) }),
            ("Via del Custode",  2.0f, new[] { new Vector2(76, 24), new Vector2(80, 30), new Vector2(82, 40), new Vector2(82, 52), new Vector2(85, 60), new Vector2(82, 69), new Vector2(79, 72) }),
        }),
    };

    [MenuItem("Elderboom HDRP/Map Rework/19e Streets South Creek + Outer Woods (dry run)")]
    static void AreaStreetsDry() => AreaStreets(false);
    [MenuItem("Elderboom HDRP/Map Rework/19f Streets South Creek + Outer Woods Apply")]
    static void AreaStreetsApply() => AreaStreets(true);

    static void AreaStreets(bool apply)
    {
        var sb = new StringBuilder(); var scene = EditorSceneManager.GetActiveScene();
        void Done() { File.WriteAllText(apply ? "Logs/streets2.txt" : "Logs/streets2_plan.txt", sb.ToString()); }
        try
        {
            var terrain = MainTerrain(); var td = terrain.terrainData; var o = terrain.transform.position;
            int aw = td.alphamapWidth, ah = td.alphamapHeight, layers = td.alphamapLayers; float cell = td.size.x / aw;
            var alpha = td.GetAlphamaps(0, 0, aw, ah);
            int mud = System.Array.FindIndex(td.terrainLayers, l => l && l.diffuseTexture && l.diffuseTexture.name.ToLowerInvariant().Contains("mud")); if (mud < 0) mud = 5;
            int soil = System.Array.FindIndex(td.terrainLayers, l => l && l.diffuseTexture && l.diffuseTexture.name.ToLowerInvariant().Contains("soil")); if (soil < 0) soil = 0;
            var allAreas = Object.FindObjectsByType<ElderboomArea>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (apply)
            {
                Directory.CreateDirectory("Logs/backup_streets2");
                File.Copy(scene.path, "Logs/backup_streets2/" + System.IO.Path.GetFileName(scene.path), true);
                File.Copy(AssetDatabase.GetAssetPath(td), "Logs/backup_streets2/" + System.IO.Path.GetFileName(AssetDatabase.GetAssetPath(td)), true);
                if (File.Exists("Assets/Resources/Elderboom/Navigation.asset")) File.Copy("Assets/Resources/Elderboom/Navigation.asset", "Logs/backup_streets2/Navigation.asset", true);
            }
            if (apply)
            {
                // Perimeter_Building_7 (grown 15%) stood 1 m from the south end of the bridge: 3.5 m back, a small square opens.
                var p7 = GameObject.Find("Perimeter_Building_7");
                if (p7 != null && p7.transform.position.z > -63f) { p7.transform.position += new Vector3(0, 0, -3.5f); EditorUtility.SetDirty(p7.transform); sb.AppendLine("Perimeter_Building_7 moved 3.5 m south of the bridge head"); }
                Physics.SyncTransforms();
                foreach (var plan in AreaPlans)
                    foreach (var st in plan.streets)
                        foreach (var p in Dense(st.pts, 1f))
                            foreach (var c in Physics.OverlapBox(new Vector3(p.x, TerrainY(p.x, p.y) + 1.2f, p.y), new Vector3(st.half, .9f, st.half), Quaternion.identity, ~((1 << 2) | (1 << 30)), QueryTriggerInteraction.Ignore))
                            {
                                var t = c.transform; while (t.parent != null && !t.name.StartsWith("fence_")) t = t.parent;
                                if (!t.name.StartsWith("fence_") || !t.gameObject.activeSelf) continue;
                                var fb = Bounds(t.gameObject); if (Mathf.Max(fb.size.x, fb.size.z) > 12f) continue;
                                t.gameObject.SetActive(false); EditorUtility.SetDirty(t.gameObject); sb.AppendLine("   opening in " + t.name + " for " + st.name);
                            }
            }
            var weight = new float[ah, aw];
            foreach (var plan in AreaPlans)
            {
                var area = allAreas.First(a => a.areaName == plan.area);
                bool Deep(float x, float z, float inset)
                {
                    if (!area.Contains(new Vector3(x, 0, z))) return false;
                    foreach (var k in plan.keep) if (new Vector2(x - k.x, z - k.y).magnitude < k.z) return false;
                    var poly = area.polygon; var q = new Vector2(x, z);
                    for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++) if (SegDist(q, poly[j], poly[i]) < inset) return false;
                    return true;
                }
                // ground mix of the area away from tracks
                var mix = new float[layers]; int mixN = 0;
                for (int z = 0; z < ah; z += 2) for (int x = 0; x < aw; x += 2)
                    {
                        float wx = o.x + x * cell, wz = o.z + z * cell; if (!Deep(wx, wz, 0f) || alpha[z, x, mud] > .02f) continue;
                        for (int l = 0; l < layers; l++) mix[l] += alpha[z, x, l]; mixN++;
                    }
                if (mixN > 0) for (int l = 0; l < layers; l++) mix[l] /= mixN;
                var streets = plan.streets.Select(s => (s.name, s.half, pts: Dense(s.pts, .5f))).ToList();
                foreach (var s in streets)
                {
                    var hits = new HashSet<string>();
                    foreach (var p in s.pts)
                        foreach (var c in Physics.OverlapBox(new Vector3(p.x, TerrainY(p.x, p.y) + 1.2f, p.y), new Vector3(s.half, .9f, s.half), Quaternion.identity, ~((1 << 2) | (1 << 30)), QueryTriggerInteraction.Ignore))
                            if (!(c is TerrainCollider) && c.GetComponentInParent<ElderboomGate>() == null && !c.name.Contains("wide_stone_bridge")) hits.Add(c.transform.root.name + "/" + c.name + "@" + c.bounds.center.x.ToString("F0") + "," + c.bounds.center.z.ToString("F0"));
                    sb.AppendLine(plan.area + " / " + s.name + " blocked by: " + (hits.Count == 0 ? "nothing" : string.Join(", ", hits.Take(10))));
                }
                int erased = 0;
                for (int z = 0; z < ah; z++) for (int x = 0; x < aw; x++)
                    {
                        float m = alpha[z, x, mud]; if (m <= .02f) continue;
                        float wx = o.x + x * cell, wz = o.z + z * cell; if (!Deep(wx, wz, 2f)) continue;
                        erased++;
                        if (!apply) continue;
                        alpha[z, x, mud] = 0f; for (int l = 0; l < layers; l++) alpha[z, x, l] += m * mix[l];
                        float sum = 0; for (int l = 0; l < layers; l++) sum += alpha[z, x, l]; if (sum > 0) for (int l = 0; l < layers; l++) alpha[z, x, l] /= sum;
                    }
                sb.AppendLine(plan.area + ": track cells erased " + erased + ", ground mix " + string.Join(" ", mix.Select((v, i) => i + "=" + v.ToString("F2"))));
                foreach (var s in streets) foreach (var p in s.pts) PaintDot(weight, p, o, cell, aw, ah, s.half, 1.8f);
                // gravel strips crossing the area: cut at its border
                foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (!mf.name.StartsWith("Road_") || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                    var v = mf.sharedMesh.vertices; var tris = mf.sharedMesh.triangles; var keep = new List<int>(); int cut = 0;
                    for (int k = 0; k + 2 < tris.Length; k += 3)
                    {
                        var c = mf.transform.TransformPoint((v[tris[k]] + v[tris[k + 1]] + v[tris[k + 2]]) / 3f);
                        if (Deep(c.x, c.z, 2f)) { cut++; continue; }
                        keep.Add(tris[k]); keep.Add(tris[k + 1]); keep.Add(tris[k + 2]);
                    }
                    if (cut == 0) continue;
                    sb.AppendLine("   strip " + mf.name + ": " + cut + " of " + tris.Length / 3 + " triangles in " + plan.area);
                    if (!apply) continue;
                    if (keep.Count == 0) { mf.gameObject.SetActive(false); EditorUtility.SetDirty(mf.gameObject); continue; }
                    var m2 = Object.Instantiate(mf.sharedMesh); m2.name = mf.name + "_clipped"; m2.triangles = keep.ToArray(); m2.RecalculateBounds();
                    Directory.CreateDirectory("Assets/Resources/Elderboom/ClippedRoads");
                    string path = "Assets/Resources/Elderboom/ClippedRoads/" + mf.name + "_" + plan.area.Replace(" ", "") + ".asset";
                    AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(m2, path);
                    mf.sharedMesh = m2; var mc = mf.GetComponent<MeshCollider>(); if (mc) mc.sharedMesh = m2; EditorUtility.SetDirty(mf);
                }
            }
            if (!apply) { Done(); return; }
            for (int z = 0; z < ah; z++) for (int x = 0; x < aw; x++)
                {
                    float w = weight[z, x]; if (w <= 0) continue;
                    float coreW = Mathf.SmoothStep(0, 1, (w - .35f) / .65f);
                    for (int l = 0; l < layers; l++) alpha[z, x, l] *= 1f - w;
                    alpha[z, x, mud] += w * coreW; alpha[z, x, soil] += w * (1f - coreW);
                }
            td.SetAlphamaps(0, 0, alpha); EditorUtility.SetDirty(td);
            sb.AppendLine(ElderboomClaudeBlocks.BakeAccessNavigation());
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            sb.AppendLine("SAVED"); Done();
        }
        catch (System.Exception e) { sb.AppendLine("EXCEPTION " + e); Done(); }
    }
}
