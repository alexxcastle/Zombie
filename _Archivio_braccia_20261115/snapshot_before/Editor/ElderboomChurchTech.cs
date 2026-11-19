using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Logs/claude_churchtech.txt -> "place": the tech field kit around the church altar (weapon upgrader included), every
// powered piece cabled to the distribution cabinet, and the transport helicopter on open ground in the East Cemetery.
// Re-running replaces the previous layout. Navigation is baked again, the scene saved, pictures in Logs/ArtReview/Church.
static partial class ElderboomClaudeBlocks
{
    const string TechFlag = "Logs/claude_churchtech.txt", TechResult = "Logs/claude_churchtech_result.txt", Kit = "Assets/GameAssets/TechKit/Prefabs/";

    [InitializeOnLoadMethod]
    static void ChurchTechWatch() { EditorApplication.update += ChurchTechPoll; }
    static double techNext;
    static void ChurchTechPoll()
    {
        if (EditorApplication.timeSinceStartup < techNext) return; techNext = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(TechFlag) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        string mode = File.ReadAllText(TechFlag).Trim(); File.Delete(TechFlag);
        try { File.WriteAllText(TechResult, ChurchTech(mode)); } catch (System.Exception e) { File.WriteAllText(TechResult, "FAIL " + e); }
    }

    static readonly Vector2[] CemeteryPoly = { new(28.5f,25), new(55,26), new(80,24), new(108,22), new(113,-14), new(92,-46), new(72,-80), new(62,-60), new(50,-47), new(50,-40), new(40,-26), new(28.5f,-24) };
    static bool InPoly(Vector2 p, Vector2[] poly)
    {
        bool c = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) c = !c;
        return c;
    }
    static float EdgeDist(Vector2 p, Vector2[] poly)
    {
        float d = float.MaxValue;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            Vector2 a = poly[j], b = poly[i], ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            d = Mathf.Min(d, Vector2.Distance(p, a + ab * t));
        }
        return d;
    }

    static string ChurchTech(string mode)
    {
        if (EditorSceneManager.GetActiveScene().path != "Assets/Scenes/Elderboom_Unity_Port.unity") EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        terrain = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude).Where(t => t.gameObject.layer != 30).OrderByDescending(t => t.terrainData.size.x).First(); data = terrain.terrainData;
        var sb = new StringBuilder();
        var old = GameObject.Find("Church_Tech_Setup"); if (old) Object.DestroyImmediate(old);
        var oldH = GameObject.Find("Cemetery_Helicopter"); if (oldH) Object.DestroyImmediate(oldH);
        Physics.SyncTransforms();
        var church = GameObject.Find(ChurchRootName); if (!church) return "FAIL church";
        var upgrader = Object.FindObjectsByType<ElderboomAltarUpgrader>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        if (!upgrader) return "FAIL upgrader";
        var cb = church.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
        var root = new GameObject("Church_Tech_Setup"); var placed = new List<GameObject>();
        Vector3 A = upgrader.transform.position;
        Vector3 n = cb.center - A; n.y = 0; n.Normalize();           // towards the nave
        Vector3 s = Vector3.Cross(Vector3.up, n);                       // across the altar
        var ub = upgrader.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
        sb.AppendLine("altar upgrader " + A + " size " + ub.size + ", nave direction " + n);

        bool Ours(Collider c) => placed.Any(p => c.transform.IsChildOf(p.transform)) || c.isTrigger;
        float FloorAt(Vector3 p, float from)
        {
            var hits = Physics.RaycastAll(new Vector3(p.x, from, p.z), Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (var h in hits) if (!Ours(h.collider) && h.normal.y > .8f && h.point.y < from - .05f && h.point.y > best) best = h.point.y;
            return float.IsNegativeInfinity(best) ? A.y : best;
        }
        GameObject Put(string prefab, Vector3 want, Vector3 face, float lift = float.NaN)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Kit + prefab + ".prefab"); if (!asset) { sb.AppendLine("  missing " + prefab); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, root.transform);
            var box = go.GetComponent<BoxCollider>(); var rot = Quaternion.LookRotation(face, Vector3.up);
            // Try the wanted spot, then rings around it, until nothing of the church, benches or other kit is in the way.
            for (float r = 0; r <= 2.4f; r += .3f)
                for (int k = 0; k < (r == 0 ? 1 : 12); k++)
                {
                    var p = want + (Quaternion.Euler(0, k * 30, 0) * Vector3.forward) * r;
                    float y = float.IsNaN(lift) ? FloorAt(p, A.y + 2.5f) : lift;
                    var c = new Vector3(p.x, y, p.z) + rot * Vector3.Scale(box.center, Vector3.one) + Vector3.up * .04f;
                    var hits = Physics.OverlapBox(c, box.size * .5f * .92f, rot, ~0, QueryTriggerInteraction.Ignore).Where(h => !Ours(h) && !h.transform.IsChildOf(go.transform)).ToArray();
                    if (hits.Length > 0 && !float.IsNaN(lift)) hits = hits.Where(h => h.bounds.max.y > y + .05f).ToArray();
                    if (hits.Length == 0) { go.transform.SetPositionAndRotation(new Vector3(p.x, y, p.z), rot); placed.Add(go); Physics.SyncTransforms(); sb.AppendLine("  " + prefab + " at " + go.transform.position.ToString("F2") + (r > 0 ? " (moved " + r.ToString("F1") + " m)" : "")); return go; }
                }
            sb.AppendLine("  NO ROOM for " + prefab + " near " + want); Object.DestroyImmediate(go); return null;
        }
        Vector3 At(float along, float across) => A + n * along + s * across;
        float Top(GameObject g) => g.GetComponentsInChildren<Renderer>().Max(r => r.bounds.max.y);

        var cabinet = Put("02_distribution", At(-.4f, 3.2f), n);
        var generator = Put("01_generator", At(-.8f, 5.4f), s);
        var reel = Put("07_cable_reel", At(.9f, 4.7f), n);
        var battery = Put("08_battery", At(-.2f, -3.1f), n);
        var light1 = Put("04_floor_floodlight", At(2.6f, 2.3f), -n + s * .35f);
        var light2 = Put("04_floor_floodlight", At(2.6f, -2.3f), -n - s * .35f);
        var crate1 = Put("05_ammo_crate", At(1.3f, -4.7f), n);
        if (crate1) Put("05_ammo_crate", crate1.transform.position + Vector3.up * .001f, n + s * .15f, Top(crate1));
        Put("05_ammo_crate", At(1.3f, -5.5f), n - s * .1f);
        var case1 = Put("06_weapon_case", At(2.5f, -4.6f), s);
        if (case1) Put("06_weapon_case", case1.transform.position, s + n * .1f, Top(case1));
        var work = AssetDatabase.LoadAssetAtPath<GameObject>(Kit + "12_work_floodlight.prefab") ? new[] { Put("12_work_floodlight", At(3.8f, 3.4f), -n + s * .3f), Put("12_work_floodlight", At(3.8f, -3.4f), -n - s * .3f) } : new GameObject[0];

        // Cables from the distribution cabinet to every powered piece (and the generator feeding it).
        var cablePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Kit + "Cable.prefab");
        int cables = 0;
        Vector3 Edge(GameObject g, Vector3 toward)
        {
            var b = g.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; });
            var d = toward - b.center; d.y = 0; d.Normalize();
            float t = Mathf.Min(Mathf.Abs(d.x) > 1e-3f ? b.extents.x / Mathf.Abs(d.x) : 999, Mathf.Abs(d.z) > 1e-3f ? b.extents.z / Mathf.Abs(d.z) : 999);
            var e = b.center + d * (t + .12f); e.y = b.min.y; return e;
        }
        void Cable(GameObject from, GameObject to, float bend)
        {
            if (!from || !to || !cablePrefab) return;
            var a = Edge(from, to.transform.position); var b = Edge(to, from.transform.position);
            var c = (GameObject)PrefabUtility.InstantiatePrefab(cablePrefab, root.transform); c.name = "Cable_" + from.name + "_" + to.name;
            c.transform.position = a;
            var side = Vector3.Cross(Vector3.up, (b - a).normalized) * bend;
            var cab = c.GetComponent<ElderboomCable>();
            cab.points = new List<Vector3> { Vector3.zero, Vector3.Lerp(a, b, .3f) + side - a, Vector3.Lerp(a, b, .7f) + side * .6f - a, b - a };
            cab.Rebuild(); cables++;
        }
        Cable(generator, cabinet, .3f); Cable(reel, generator, -.2f);
        Cable(cabinet, upgrader.gameObject, .5f); Cable(cabinet, battery, .8f);
        Cable(cabinet, light1, -.3f); Cable(battery, light2, .3f);
        foreach (var w in work) Cable(cabinet, w, .4f);
        sb.AppendLine("church: " + placed.Count + " pieces, " + cables + " cables");

        // Helicopter: the most open, flattest ground in the cemetery, away from its edges, graves and zombie spawns.
        var heliAsset = AssetDatabase.LoadAssetAtPath<GameObject>(Kit + "11_transport_helicopter.prefab");
        if (heliAsset)
        {
            var spawns = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => t.name.StartsWith("Spawn_East_Cemetery")).Select(t => t.position).ToList();
            var blockers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.GetComponentInParent<Terrain>() == null && r.bounds.size.magnitude < 60 && !r.name.ToLower().Contains("fog") && !r.name.ToLower().Contains("grass") && !r.name.ToLower().Contains("ground") && !r.name.ToLower().Contains("decal")).Select(r => r.bounds).ToList();
            blockers.Add(new Bounds(cb.center, cb.size + new Vector3(8, 0, 8)));   // the church itself, with a margin
            var centroid = CemeteryPoly.Aggregate(Vector2.zero, (a, b) => a + b) / CemeteryPoly.Length;
            float best = float.MaxValue; Vector3 bestPos = Vector3.zero; float bestYaw = 0;
            for (float x = 25; x <= 115; x += 2.5f)
                for (float z = -80; z <= 28; z += 2.5f)
                {
                    var p = new Vector2(x, z); if (!InPoly(p, CemeteryPoly) || EdgeDist(p, CemeteryPoly) < 8f) continue;
                    if (spawns.Any(sp => Vector2.Distance(new Vector2(sp.x, sp.z), p) < 9f)) continue;
                    for (float yaw = 0; yaw < 180; yaw += 22.5f)
                    {
                        var rot = Quaternion.Euler(0, yaw, 0);
                        var g = new List<float>();
                        foreach (var o in new[] { new Vector3(-1.3f, 0, -2.4f), new Vector3(1.3f, 0, -2.4f), new Vector3(-1.3f, 0, 2.4f), new Vector3(1.3f, 0, 2.4f), Vector3.zero })
                        { var w = new Vector3(x, 0, z) + rot * o; g.Add(Ground(new Vector2(w.x, w.z))); }
                        float slope = g.Max() - g.Min(); if (slope > .7f) continue;
                        // Clear volume: rotor disc and tail at full size.
                        var box = new Bounds(new Vector3(x, g.Average() + 2.6f, z), Vector3.zero);
                        foreach (var corner in new[] { new Vector3(-4.8f, -2.3f, -5.6f), new Vector3(4.8f, 2.4f, 5.6f), new Vector3(-4.8f, 2.4f, 5.6f), new Vector3(4.8f, -2.3f, -5.6f) }) box.Encapsulate(box.center + rot * corner);
                        if (blockers.Any(b => b.Intersects(box))) continue;
                        float score = slope * 6 + Vector2.Distance(p, centroid) * .04f - Mathf.Min(EdgeDist(p, CemeteryPoly), 20) * .1f;
                        if (score < best) { best = score; bestPos = new Vector3(x, g.Average() - .04f, z); bestYaw = yaw; }
                    }
                }
            if (best < float.MaxValue)
            {
                var h = (GameObject)PrefabUtility.InstantiatePrefab(heliAsset); h.name = "Cemetery_Helicopter";
                h.transform.SetPositionAndRotation(bestPos, Quaternion.Euler(0, bestYaw, 0));
                // Collide with the fuselage only, not with the empty air under the rotor.
                var bc = h.GetComponent<BoxCollider>(); var size = bc.size; bc.size = new Vector3(Mathf.Min(size.x, 2.6f), Mathf.Min(size.y, 3.2f), size.z); bc.center = new Vector3(bc.center.x, bc.size.y * .5f, bc.center.z);
                sb.AppendLine("helicopter at " + bestPos.ToString("F1") + " yaw " + bestYaw + " (score " + best.ToString("F2") + ")");
            }
            else sb.AppendLine("NO open ground for the helicopter in the cemetery");
        }
        sb.AppendLine(Bake());
        EditorSceneManager.MarkSceneDirty(church.scene); EditorSceneManager.SaveScene(church.scene);
        // Pictures
        ChurchStill("tech_altar", A + n * 7.5f + Vector3.up * 2.6f + s * 1.2f, A + Vector3.up * .6f);
        ChurchStill("tech_altar_side", A + n * 3.5f + s * 6.5f + Vector3.up * 2.2f, A + s * -1 + Vector3.up * .5f);
        ChurchStill("tech_altar_top", A + n * 1.5f + Vector3.up * 7f, A + n * 1.2f);
        var heli = GameObject.Find("Cemetery_Helicopter");
        if (heli) { var hp = heli.transform.position; ChurchStill("tech_helicopter", hp + new Vector3(14, 6, -14), hp + Vector3.up * 2); ChurchStill("tech_helicopter_wide", hp + new Vector3(-25, 14, -22), hp + Vector3.up * 1); }
        sb.AppendLine("DONE");
        return sb.ToString();
    }
}
