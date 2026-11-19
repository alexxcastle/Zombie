using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

// Houses +15% (every building except the church), scaled about their base so they stay on the ground.
// A house that would swallow a station, gate, spawn or objective goes back to its size. The closed-gate area
// connectivity and the open-gate reachability are checked before saving; nothing is saved if they change.
// Terminal: Unity -batchmode -quit -projectPath ... -executeMethod ElderboomMapRework.ScaleHousesBatch
static partial class ElderboomMapRework
{
    static float HouseScale = 1.15f;
    static string HouseMarker = "Rework_HouseScale_v1";
    static readonly string[] HouseWords = { "house", "building", "cottage", "barn", "shed", "inn", "hall", "infirmary", "greenhouse", "workshop", "workroom",
        "kitchen", "bakery", "baker", "smithy", "forge", "stable", "granary", "storehouse", "store_", "shop", "cabin", "hut", "home", "tavern", "mill", "lodge", "dwelling", "loft", "wash" };
    static readonly string[] NotHouseWords = { "church", "chapel", "wall", "fence", "gate", "bridge", "tree", "alder", "rock", "terrain", "landscape", "station", "altar", "road",
        "path", "light", "lamp", "lantern", "cart", "tech", "window", "door", "roof_", "sign", "board", "bench", "barrel", "crate", "grass", "fern", "bush", "cemetery", "grave", "border", "invisible", "spawn", "area", "navigation", "paddock", "crops", "field", "yard" };

    public static void ScaleHousesV2Batch()
    {
        HouseScale = 1.2f; HouseMarker = "Rework_HouseScale_v2";
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        bool ok = ScaleHouses();
        EditorApplication.Exit(ok ? 0 : 1);
    }

    public static void ScaleHousesBatch()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        bool ok = ScaleHouses();
        EditorApplication.Exit(ok ? 0 : 1);
    }

    [MenuItem("Elderboom HDRP/Map Rework/13 Scale Houses +15%")]
    static void ScaleHousesMenu() => ScaleHouses();

    static bool IsHouseName(string n)
    {
        n = n.ToLowerInvariant();
        return HouseWords.Any(w => n.Contains(w)) && !NotHouseWords.Any(w => n.Contains(w));
    }

    static bool ScaleHouses()
    {
        L = new StringBuilder();
        var scene = EditorSceneManager.GetActiveScene();
        void Done() { Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/house_scale.txt", L.ToString()); }
        if (GameObject.Find(HouseMarker) != null) { L.AppendLine("already applied (marker present)"); Done(); return true; }
        try
        {
            Physics.SyncTransforms();
            var before = Connectivity("BEFORE (gates closed)");
            // What already fails before touching anything is not this tool's doing.
            int mark0 = L.Length; ValidateGameplay();
            var known = new HashSet<string>(L.ToString().Substring(mark0).Split('\n').Where(x => x.Contains("reachable=False") || x.Contains("cannot reach the square") || x.Contains("OFF navmesh")).Select(x => x.Trim()));
            L.AppendLine("== already failing before: " + known.Count);

            // Gameplay points that must stay outside every house.
            var keep = new List<(string what, Vector3 p)>();
            foreach (var s in Object.FindObjectsByType<ElderboomStation>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) keep.Add(("station " + s.name, s.transform.position));
            foreach (var g in Object.FindObjectsByType<ElderboomGate>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) keep.Add(("gate " + g.name, g.transform.position));
            foreach (var sp in Object.FindObjectsByType<ElderboomSpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) keep.Add(("spawn " + sp.name, sp.transform.position));
            foreach (var n in new[] { "Horse_Salvage_Objective", "Church_Altar_Weapon_Upgrader", "Church_Tech_Setup" }) { var go = GameObject.Find(n); if (go) keep.Add((n, go.transform.position)); }

            // Houses: top-most objects with a building name and a building-sized body.
            var church = GameObject.Find("FallenCity_Church_HumanScale");
            var houses = new List<Transform>();
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!IsHouseName(t.name) || t.GetComponentInChildren<Renderer>() == null) continue;
                if (church != null && t.IsChildOf(church.transform)) continue;
                if (t.GetComponentInChildren<ElderboomStation>() || t.GetComponentInChildren<ElderboomGate>() || t.GetComponentInChildren<ElderboomSpawnPoint>()) continue;
                var b = Bounds(t.gameObject);
                float side = Mathf.Min(b.size.x, b.size.z), span = Mathf.Max(b.size.x, b.size.z);
                if (b.size.y < 2.6f || b.size.y > 30f || side < 2.5f || span > 45f) continue;
                houses.Add(t);
            }
            houses = houses.Where(h => !houses.Any(o => o != h && h.IsChildOf(o))).ToList();
            L.AppendLine("== houses found " + houses.Count);

            int scaled = 0, reverted = 0;
            foreach (var h in houses)
            {
                var b = Bounds(h.gameObject);
                var c = new Vector3(b.center.x, b.min.y, b.center.z);
                Vector3 pos0 = h.position, scale0 = h.localScale;
                h.position = c + (h.position - c) * HouseScale; h.localScale = scale0 * HouseScale;
                Physics.SyncTransforms();
                var nb = Bounds(h.gameObject);
                var hit = keep.FirstOrDefault(k => k.p.x > nb.min.x + .3f && k.p.x < nb.max.x - .3f && k.p.z > nb.min.z + .3f && k.p.z < nb.max.z - .3f && k.p.y < nb.max.y);
                if (hit.what != null && !(k_inside_before(b, hit.p)))
                {
                    h.position = pos0; h.localScale = scale0; reverted++;
                    L.AppendLine("   kept size " + Path(h) + " (would cover " + hit.what + ")");
                    continue;
                }
                EditorUtility.SetDirty(h); scaled++;
                L.AppendLine("   +15% " + Path(h) + "  " + b.size.ToString("F1") + " -> " + nb.size.ToString("F1"));
            }
            Physics.SyncTransforms();
            L.AppendLine("== scaled " + scaled + ", kept " + reverted);

            var after = Connectivity("AFTER (gates closed)");
            var leaks = before.Keys.Where(k => !before[k].reach && after.ContainsKey(k) && after[k].reach).ToList();
            var lost = before.Keys.Where(k => before[k].reach && after.ContainsKey(k) && !after[k].reach).ToList();
            if (leaks.Count > 0 || lost.Count > 0) { L.AppendLine("ABORT: leaks " + string.Join(",", leaks) + " lost " + string.Join(",", lost) + " -> nothing saved"); Done(); return false; }

            L.AppendLine("== navigation");
            L.AppendLine(ElderboomClaudeBlocks.BakeAccessNavigation());
            int mark = L.Length;
            ValidateGameplay();
            string check = L.ToString().Substring(mark);
            var fresh = check.Split('\n').Where(x => x.Contains("reachable=False") || x.Contains("cannot reach the square") || x.Contains("OFF navmesh")).Select(x => x.Trim()).Where(x => !known.Contains(x)).ToList();
            foreach (var f in fresh) L.AppendLine("   NEW FAILURE " + f);
            if (fresh.Count > 0) { L.AppendLine("ABORT: something became unreachable -> nothing saved"); Done(); return false; }
            new GameObject(HouseMarker);
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            L.AppendLine("SAVED"); Done(); return true;
        }
        catch (System.Exception e) { L.AppendLine("EXCEPTION " + e); Done(); return false; }
    }

    // A gameplay point already inside a house before scaling (e.g. a spawn in a doorway) is not a reason to keep its size.
    static bool k_inside_before(Bounds b, Vector3 p) => p.x > b.min.x + .3f && p.x < b.max.x - .3f && p.z > b.min.z + .3f && p.z < b.max.z - .3f && p.y < b.max.y;
}
