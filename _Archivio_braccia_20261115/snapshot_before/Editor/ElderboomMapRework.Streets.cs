using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Village Square street plan: the tangle of dirt tracks inside the square is erased and replaced by a few real streets
// that link the gates, the stations' plaza, the church and the bridge. Free blocks between them take the edge houses
// (ElderboomMapRework.Relocate). Outside the square nothing changes; streets end on the gates, where the outer tracks start.
static partial class ElderboomMapRework
{
    // Polylines (x, z) and half width of the beaten core.
    static readonly (string name, float half, Vector2[] pts)[] Streets =
    {
        // v2 (church precinct): Via Maestra moves north of the church to leave the north wing for stalls; Via della Chiesa
        // becomes the paved front square; Via del Ponte moves south toward the creek to leave the south wing for stalls.
        ("Via Maestra",   2.2f, new[] { new Vector2(-39, -1), new Vector2(-30, 3), new Vector2(-15, 6), new Vector2(-3, 8), new Vector2(12, 9.5f), new Vector2(30, 9.5f), new Vector2(37, 8f), new Vector2(44, 5f), new Vector2(52, 0) }),
        ("Strada del Mulino", 2.0f, new[] { new Vector2(0, 9), new Vector2(4, 12), new Vector2(6.3f, 16), new Vector2(6.5f, 22), new Vector2(7, 28) }),
        ("Via del Ponte",  2.2f, new[] { new Vector2(-21, -30), new Vector2(-12, -32.5f), new Vector2(0, -33.5f), new Vector2(20, -33.5f), new Vector2(36, -33.5f), new Vector2(46, -35) }),
        ("Rampa del Ponte", 2.2f, new[] { new Vector2(7, -33.5f), new Vector2(7, -36), new Vector2(6.8f, -39) }),
        ("Sagrato",        1.8f, new[] { new Vector2(-8, 7), new Vector2(-8, -27), new Vector2(-12, -32.5f) }),
    };
    const string StreetsMarker = "Rework_Streets_v1";

    [MenuItem("Elderboom HDRP/Map Rework/19 Village Streets (dry run)")]
    static void StreetsDry() => VillageStreets(false);
    [MenuItem("Elderboom HDRP/Map Rework/19b Village Streets Apply")]
    static void StreetsApply() => VillageStreets(true);

    static List<Vector2> Dense(Vector2[] pts, float step)
    {
        var res = new List<Vector2>();
        for (int i = 1; i < pts.Length; i++) { float len = (pts[i] - pts[i - 1]).magnitude; int n = Mathf.Max(1, Mathf.CeilToInt(len / step)); for (int k = 0; k < n; k++) res.Add(Vector2.Lerp(pts[i - 1], pts[i], (float)k / n)); }
        res.Add(pts[pts.Length - 1]);
        // gentle curves at the bends
        for (int pass = 0; pass < 4; pass++) for (int i = 1; i < res.Count - 1; i++) res[i] = (res[i - 1] + res[i] * 2 + res[i + 1]) * .25f;
        return res;
    }

    static void VillageStreets(bool apply)
    {
        var sb = new StringBuilder(); var scene = EditorSceneManager.GetActiveScene();
        void Done() { File.WriteAllText(apply ? "Logs/streets.txt" : "Logs/streets_plan.txt", sb.ToString()); }
        bool again = GameObject.Find(StreetsMarker) != null; // re-run: the square is erased again (old streets included) and repainted
        try
        {
            var terrain = MainTerrain(); var td = terrain.terrainData; var o = terrain.transform.position;
            var square = Object.FindObjectsByType<ElderboomArea>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).First(a => a.areaName == "Village Square");
            int aw = td.alphamapWidth, ah = td.alphamapHeight, layers = td.alphamapLayers; float cell = td.size.x / aw;
            var alpha = td.GetAlphamaps(0, 0, aw, ah);
            int mud = System.Array.FindIndex(td.terrainLayers, l => l && l.diffuseTexture && l.diffuseTexture.name.ToLowerInvariant().Contains("mud")); if (mud < 0) mud = 5;
            int soil = System.Array.FindIndex(td.terrainLayers, l => l && l.diffuseTexture && l.diffuseTexture.name.ToLowerInvariant().Contains("soil")); if (soil < 0) soil = 0;
            for (int l = 0; l < layers; l++) sb.AppendLine("layer " + l + " " + (td.terrainLayers[l] && td.terrainLayers[l].diffuseTexture ? td.terrainLayers[l].diffuseTexture.name : "?"));

            // ground mix of the square away from tracks: what the erased tracks turn back into
            var mix = new float[layers]; int mixN = 0;
            bool InSquare(float x, float z, float inset)
            {
                if (!square.Contains(new Vector3(x, 0, z))) return false;
                if (inset <= 0) return true;
                var poly = square.polygon; for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++) if (SegDist(new Vector2(x, z), poly[j], poly[i]) < inset) return false;
                return true;
            }
            for (int z = 0; z < ah; z += 2) for (int x = 0; x < aw; x += 2)
                {
                    float wx = o.x + x * cell, wz = o.z + z * cell; if (!InSquare(wx, wz, 0)) continue;
                    if (alpha[z, x, mud] > .02f) continue;
                    for (int l = 0; l < layers; l++) mix[l] += alpha[z, x, l]; mixN++;
                }
            if (mixN > 0) for (int l = 0; l < layers; l++) mix[l] /= mixN;
            sb.AppendLine("ground mix: " + string.Join(" ", mix.Select((v, i) => i + "=" + v.ToString("F2"))));

            // street corridors: report what stands on them
            var streets = Streets.Select(s => (s.name, s.half, pts: Dense(s.pts, .5f))).ToList();
            foreach (var s in streets)
            {
                var hits = new HashSet<string>();
                foreach (var p in s.pts)
                    foreach (var c in Physics.OverlapBox(new Vector3(p.x, TerrainY(p.x, p.y) + 1.2f, p.y), new Vector3(s.half, .9f, s.half), Quaternion.identity, ~((1 << 2) | (1 << 30)), QueryTriggerInteraction.Ignore))
                        if (!(c is TerrainCollider) && c.GetComponentInParent<ElderboomGate>() == null) hits.Add(Path(c.transform));
                float len = 0; for (int i = 1; i < s.pts.Count; i++) len += (s.pts[i] - s.pts[i - 1]).magnitude;
                sb.AppendLine("street " + s.name + " " + len.ToString("F0") + " m, blocked by: " + (hits.Count == 0 ? "nothing" : string.Join(", ", hits.Take(12))));
            }

            // track cells inside the square (kept 2 m from its border so the outer tracks still meet the gates)
            int erased = 0;
            var erase = new float[ah, aw];
            for (int z = 0; z < ah; z++) for (int x = 0; x < aw; x++)
                {
                    float m = alpha[z, x, mud]; if (m <= .02f) continue;
                    float wx = o.x + x * cell, wz = o.z + z * cell; if (!InSquare(wx, wz, 2f)) continue;
                    erase[z, x] = m; erased++;
                }
            sb.AppendLine("track cells to erase " + erased);

            // authored road strips and road walls inside the square
            var strips = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(r => r.name.StartsWith("Road_") && InSquare(r.bounds.min.x, r.bounds.min.z, 0) && InSquare(r.bounds.max.x, r.bounds.max.z, 0) && InSquare(r.bounds.min.x, r.bounds.max.z, 0) && InSquare(r.bounds.max.x, r.bounds.min.z, 0)).ToList(); // only strips wholly inside the square
            sb.AppendLine("road strips in the square: " + string.Join(", ", strips.Select(r => r.name)));
            var net = GameObject.Find("Elderboom_Road_Network");
            var allPts = streets.SelectMany(s => s.pts).ToList();
            var walls = new List<GameObject>();
            if (net) foreach (Transform w in net.transform)
                {
                    if (!w.gameObject.activeSelf || w.GetComponentInChildren<Renderer>() == null) continue;
                    var b = Bounds(w.gameObject); if (!InSquare(b.center.x, b.center.z, 0)) continue;
                    float d = allPts.Min(p => (p - XZ(b.center)).magnitude);
                    if (d > 6.5f) walls.Add(w.gameObject);
                }
            sb.AppendLine("road walls in the square off the new streets: " + walls.Count);
            if (!apply) { Done(); return; }

            if (again) goto Paint;
            Directory.CreateDirectory("Logs/backup_streets");
            File.Copy(scene.path, "Logs/backup_streets/" + System.IO.Path.GetFileName(scene.path), true);
            File.Copy(AssetDatabase.GetAssetPath(td), "Logs/backup_streets/" + System.IO.Path.GetFileName(AssetDatabase.GetAssetPath(td)), true);
            if (File.Exists("Assets/Resources/Elderboom/Navigation.asset")) File.Copy("Assets/Resources/Elderboom/Navigation.asset", "Logs/backup_streets/Navigation.asset", true);

            Paint:
            // erase: the mud and its share of soil shoulder go back to the square's ground mix
            for (int z = 0; z < ah; z++) for (int x = 0; x < aw; x++)
                {
                    float m = erase[z, x]; if (m <= 0) continue;
                    float back = m; alpha[z, x, mud] = 0f;
                    for (int l = 0; l < layers; l++) alpha[z, x, l] += back * mix[l];
                    float sum = 0; for (int l = 0; l < layers; l++) sum += alpha[z, x, l]; if (sum > 0) for (int l = 0; l < layers; l++) alpha[z, x, l] /= sum;
                }
            // paint the streets: beaten core, soil shoulders, ragged edge
            var weight = new float[ah, aw];
            foreach (var s in streets) foreach (var p in s.pts) PaintDot(weight, p, o, cell, aw, ah, s.half, 1.8f);
            for (int z = 0; z < ah; z++) for (int x = 0; x < aw; x++)
                {
                    float w = weight[z, x]; if (w <= 0) continue;
                    float coreW = Mathf.SmoothStep(0, 1, (w - .35f) / .65f);
                    for (int l = 0; l < layers; l++) alpha[z, x, l] *= 1f - w;
                    alpha[z, x, mud] += w * coreW; alpha[z, x, soil] += w * (1f - coreW);
                }
            td.SetAlphamaps(0, 0, alpha); EditorUtility.SetDirty(td);
            foreach (var r in strips) { r.gameObject.SetActive(false); EditorUtility.SetDirty(r.gameObject); }
            foreach (var w in walls) { w.SetActive(false); EditorUtility.SetDirty(w); }
            sb.AppendLine("erased, painted " + streets.Count + " streets, hid " + strips.Count + " strips and " + walls.Count + " walls");
            sb.AppendLine(ElderboomClaudeBlocks.BakeAccessNavigation());
            if (!again) new GameObject(StreetsMarker);
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            sb.AppendLine("SAVED"); Done();
        }
        catch (System.Exception e) { sb.AppendLine("EXCEPTION " + e); Done(); }
    }

    [MenuItem("Elderboom HDRP/Map Rework/19c Undo Village Streets")]
    static void UndoStreets()
    {
        const string B = "Logs/backup_streets/";
        File.Copy(B + "Elderboom_Unity_Port.unity", "Assets/Scenes/Elderboom_Unity_Port.unity", true);
        File.Copy(B + "Elderboom_Valley.asset", "Assets/Terrain/Elderboom_Valley.asset", true);
        File.Copy(B + "Navigation.asset", "Assets/Resources/Elderboom/Navigation.asset", true);
        foreach (var p in new[] { "Assets/Terrain/Elderboom_Valley.asset", "Assets/Resources/Elderboom/Navigation.asset", "Assets/Scenes/Elderboom_Unity_Port.unity" }) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
    }
}
