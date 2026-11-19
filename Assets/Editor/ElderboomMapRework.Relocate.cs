using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

// Edge buildings move inward onto empty plots along the streets.
//  - civic/residential buildings at the map edge ("inward"): toward the centre, preferably into the Village Square;
//  - farm buildings ("farm"): stay in their area (farms belong at the edge) but close in on the farm road.
// A plot is inside a play area, clear of spawns, stations, gates and objectives, flat, with no building, wall or road
// under it and 5 m from other houses; the house's long side runs along the street, 2.5-8 m from it.
// Terrain trees and small props under a chosen plot are removed/hidden. Each moved house gets a dirt drive to its
// street; navigation is rebaked. "Plan" only writes Logs/relocate_plan.txt; "Apply" moves and saves.
static partial class ElderboomMapRework
{
    const string RelocMarker = "Rework_Relocate_v5"; // houses leave the ring around the church for the emptiest street plots
    static readonly Vector2 MapCenter = new Vector2(5.7f, -0.7f);
    static readonly (string name, bool farm)[] ToMove =
    {
        ("bakers_house_19", false), ("cooper_workshop", false), ("greenhouse_large_45", false),
    };

    // Answers from Alessandro (11/11): the timber inn and the weavers' house fill the south-west block of the square,
    // facing Via Maestra; the town hall and the infirmary stay toward the edge but on a visible block, on a street.
    static bool SwPair(string n) => false; // the timber inn already stands in the south-west block (v3)
    static bool Landmark(string n) => n == "municipal_council_hall_0" || n == "stone_infirmary_2" || n == "weavers_house_44";
    static bool Fill(string n) => n == "bakers_house_19" || n == "cooper_workshop" || n == "greenhouse_large_45";
    // the church with its front square (west), stall wings (north/south) and rear courtyard (east)
    public static bool InPrecinct(Vector2 p) => p.x > -16f && p.x < 53f && p.y > -38f && p.y < 12f;
    static bool InSw(Vector2 p) => p.x > -42f && p.x < -8f && p.y > -27f && p.y < 5f;

    class Spot { public Vector2 p, road, tangent; public float roadDist, r; public string area; }
    class Plan { public Transform t; public UnityEngine.Bounds b; public Vector2 c, axis, road, front; public float len, wid, baseY, yaw, level; public string area; public List<GameObject> hide = new List<GameObject>(); public List<int> trees = new List<int>(); public List<Transform> spawns = new List<Transform>(); public List<Transform> lamps = new List<Transform>(); }

    static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);

    [MenuItem("Elderboom HDRP/Map Rework/14 Relocate Plan (dry run)")]
    static void RelocatePlanMenu() => Relocate(false);
    [MenuItem("Elderboom HDRP/Map Rework/14b Relocate Apply")]
    static void RelocateApplyMenu() => Relocate(true);

    static void Relocate(bool apply)
    {
        L = new StringBuilder();
        var scene = EditorSceneManager.GetActiveScene();
        void Done() { Directory.CreateDirectory("Logs"); File.WriteAllText(apply ? "Logs/relocate.txt" : "Logs/relocate_plan.txt", L.ToString()); }
        if (GameObject.Find(RelocMarker) != null) { L.AppendLine("already applied (marker present)"); Done(); return; }
        try
        {
            var terrain = MainTerrain(); var td = terrain.terrainData;
            if (apply)
            {
                Directory.CreateDirectory("Logs/backup_relocate5");
                File.Copy(scene.path, "Logs/backup_relocate5/" + System.IO.Path.GetFileName(scene.path), true);
                File.Copy(AssetDatabase.GetAssetPath(td), "Logs/backup_relocate5/" + System.IO.Path.GetFileName(AssetDatabase.GetAssetPath(td)), true);
                if (File.Exists("Assets/Resources/Elderboom/Navigation.asset")) File.Copy("Assets/Resources/Elderboom/Navigation.asset", "Logs/backup_relocate5/Navigation.asset", true);
            }
            Physics.SyncTransforms();

            // ---- streets
            // Street frontage uses all the dirt paint; only the solid core of a lane (and the authored road strips) blocks a plot,
            // so a house may stand on the trodden edge of a yard but never across a lane.
            var roads = RoadPoints(terrain, out int mud, out int soil);
            var core = RoadPoints(terrain, out _, out _, .85f);
            var coreGrid = new Dictionary<Vector2Int, List<Vector2>>();
            foreach (var p in core) { var k = new Vector2Int(Mathf.FloorToInt(p.x / 4f), Mathf.FloorToInt(p.y / 4f)); if (!coreGrid.TryGetValue(k, out var l)) coreGrid[k] = l = new List<Vector2>(); l.Add(p); }
            List<Vector2> CoreNear(Vector2 p, float r)
            {
                var res = new List<Vector2>(); int n = Mathf.CeilToInt(r / 4f); var k0 = new Vector2Int(Mathf.FloorToInt(p.x / 4f), Mathf.FloorToInt(p.y / 4f));
                for (int dx = -n; dx <= n; dx++) for (int dz = -n; dz <= n; dz++)
                        if (coreGrid.TryGetValue(new Vector2Int(k0.x + dx, k0.y + dz), out var l)) foreach (var q in l) if ((q - p).sqrMagnitude <= r * r) res.Add(q);
                return res;
            }
            var roadGrid = new Dictionary<Vector2Int, List<Vector2>>();
            Vector2Int Cell(Vector2 p, float s) => new Vector2Int(Mathf.FloorToInt(p.x / s), Mathf.FloorToInt(p.y / s));
            foreach (var p in roads) { var k = Cell(p, 4f); if (!roadGrid.TryGetValue(k, out var l)) roadGrid[k] = l = new List<Vector2>(); l.Add(p); }
            List<Vector2> RoadNear(Vector2 p, float r)
            {
                var res = new List<Vector2>(); int n = Mathf.CeilToInt(r / 4f); var k0 = Cell(p, 4f);
                for (int dx = -n; dx <= n; dx++) for (int dz = -n; dz <= n; dz++)
                        if (roadGrid.TryGetValue(new Vector2Int(k0.x + dx, k0.y + dz), out var l)) foreach (var q in l) if ((q - p).sqrMagnitude <= r * r) res.Add(q);
                return res;
            }

            // ---- all houses (moving or not) for spacing
            var church = GameObject.Find("FallenCity_Church_HumanScale");
            var houses = new List<Transform>();
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!IsHouseName(t.name) || t.GetComponentInChildren<Renderer>() == null) continue;
                if (church != null && t.IsChildOf(church.transform)) continue;
                var b = Bounds(t.gameObject);
                if (b.size.y < 2.6f || b.size.y > 30f || Mathf.Min(b.size.x, b.size.z) < 2.5f || Mathf.Max(b.size.x, b.size.z) > 45f) continue;
                houses.Add(t);
            }
            houses = houses.Where(h => !houses.Any(o => o != h && h.IsChildOf(o))).ToList();
            var hBounds = houses.ToDictionary(h => h, h => Bounds(h.gameObject));
            if (church) hBounds[church.transform] = Bounds(church);

            // ---- keep clear
            var avoid = new List<(Vector2 p, float r)>();
            // Spawn markers are moved out of a chosen plot, not avoided.
            var spawnPts = Object.FindObjectsByType<ElderboomSpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Select(sp => sp.transform).ToList();
            foreach (var s in Object.FindObjectsByType<ElderboomStation>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) avoid.Add((XZ(s.transform.position), 7f));
            foreach (var g in Object.FindObjectsByType<ElderboomGate>(FindObjectsInactive.Include, FindObjectsSortMode.None)) avoid.Add((XZ(g.transform.position), 9f));
            foreach (var n in new[] { "Horse_Salvage_Objective", "Church_Altar_Weapon_Upgrader", "Church_Tech_Setup", "SouthCreek_WideStoneBridge" }) { var go = GameObject.Find(n); if (go) avoid.Add((XZ(Bounds(go).center), 10f)); }
            var player = Object.FindAnyObjectByType<HDRPSlicePlayer>(); if (player) avoid.Add((XZ(player.transform.position), 10f));
            avoid.Add((new Vector2(-30.4f, 29.4f), 21f)); // the pond (flat, so not an obstacle by height)
            foreach (var a in avoid) if (InSw(a.p) || (a.p - new Vector2(-24, -11)).magnitude < 30f) L.AppendLine("avoid near SW block: " + a.p.ToString("F1") + " r " + a.r);
            var areas = Object.FindObjectsByType<ElderboomArea>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(a => a.polygon != null && a.polygon.Length > 2).ToArray();
            float EdgeDist(ElderboomArea a, Vector2 p) { float d = 1e9f; for (int i = 0, j = a.polygon.Length - 1; i < a.polygon.Length; j = i++) d = Mathf.Min(d, SegDist(p, a.polygon[j], a.polygon[i])); return d; }
            ElderboomArea AreaOf(Vector2 p) => areas.FirstOrDefault(a => a.Contains(new Vector3(p.x, 0, p.y)));

            // ---- obstacles: big ones block, small props get hidden
            var moving = new HashSet<Transform>();
            foreach (var (n, _) in ToMove) { var h = houses.FirstOrDefault(x => x.name == n); if (h) moving.Add(h); }
            var houseRenderers = new HashSet<Renderer>(houses.SelectMany(h => h.GetComponentsInChildren<Renderer>(true)));
            if (church) foreach (var r in church.GetComponentsInChildren<Renderer>(true)) houseRenderers.Add(r);
            var obs = new List<(UnityEngine.Bounds b, GameObject go, bool small)>(); var obsGrid = new Dictionary<Vector2Int, List<int>>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (r is ParticleSystemRenderer || !r.enabled || houseRenderers.Contains(r)) continue;
                var b = r.bounds; if (b.size.y < .5f || Mathf.Max(b.size.x, b.size.z) > 60f) continue;
                string n = r.name.ToLowerInvariant();
                if (n.Contains("grass") || n.Contains("fern") || n.Contains("flower") || n.Contains("puddle") || n.StartsWith("road") || n.Contains("decal") || n.Contains("landscape") || n.Contains("water") || n.Contains("creek")) continue;
                bool small = b.size.y < 2.6f && Mathf.Max(b.size.x, b.size.z) < 3.2f && !n.Contains("wall") && !n.Contains("fence") && !n.Contains("rail") && !n.Contains("lantern") && !n.Contains("lamp");
                // the object to hide: the top-most parent that is still small
                var go = r.gameObject;
                while (small && go.transform.parent != null && go.transform.parent.parent != null)
                {
                    var pb = Bounds(go.transform.parent.gameObject);
                    if (pb.size.y < 2.6f && Mathf.Max(pb.size.x, pb.size.z) < 3.2f) go = go.transform.parent.gameObject; else break;
                }
                int idx = obs.Count; obs.Add((b, go, small));
                for (int x = Mathf.FloorToInt(b.min.x / 8f); x <= Mathf.FloorToInt(b.max.x / 8f); x++)
                    for (int z = Mathf.FloorToInt(b.min.z / 8f); z <= Mathf.FloorToInt(b.max.z / 8f); z++)
                    { var k = new Vector2Int(x, z); if (!obsGrid.TryGetValue(k, out var l)) obsGrid[k] = l = new List<int>(); l.Add(idx); }
            }
            var trees = td.treeInstances; var treePos = trees.Select(ti => XZ(Vector3.Scale(ti.position, td.size) + terrain.transform.position)).ToArray();

            // ---- candidate plots
            var cands = new List<Spot>();
            for (float x = MapCenter.x - 92; x <= MapCenter.x + 92; x += 2f)
                for (float z = MapCenter.y - 92; z <= MapCenter.y + 92; z += 2f)
                {
                    var p2 = new Vector2(x, z); var area = AreaOf(p2);
                    if (area == null || EdgeDist(area, p2) < 5f) continue;
                    if (CoreNear(p2, 1.5f).Count > 0) continue;
                    var near = RoadNear(p2, 26f).Where(q => (q - p2).sqrMagnitude > 2.25f).ToList(); if (near.Count == 0) continue;
                    var rp = near.OrderBy(q => (q - p2).sqrMagnitude).First();
                    cands.Add(new Spot { p = p2, road = rp, roadDist = (rp - p2).magnitude, tangent = Principal(RoadNear(rp, 4.5f), rp, p2), r = (p2 - MapCenter).magnitude, area = area.areaName });
                }
            L.AppendLine("candidate plots " + cands.Count + ", road points " + roads.Count + ", lane cores " + core.Count);

            var why = new Dictionary<string, int>();
            string lastWhy = ""; void No(string k) { lastWhy = k; why.TryGetValue(k, out int v); why[k] = v + 1; }
            bool Fits(Transform self, Plan pl)
            {
                pl.hide.Clear(); pl.trees.Clear(); pl.spawns.Clear(); pl.lamps.Clear();
                Vector2 c = pl.c, axis = pl.axis, perp = new Vector2(-axis.y, axis.x); float len = pl.len, wid = pl.wid;
                bool Inside(Vector2 q, float m) { var d = q - c; return Mathf.Abs(Vector2.Dot(d, axis)) <= len * .5f + m && Mathf.Abs(Vector2.Dot(d, perp)) <= wid * .5f + m; }
                float lo = 1e9f, hi = -1e9f;
                for (int i = -1; i <= 1; i++) for (int j = -1; j <= 1; j++) { var q = c + axis * len * .5f * i + perp * wid * .5f * j; float y = TerrainY(q.x, q.y); lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y); }
                // Slopes up to 4 m are levelled (terrain flattened under the plot with a soft edge); the creek bed is not.
                if (hi - lo > 4f || lo < .2f) { No("slope/creek"); return false; }
                pl.level = (lo + hi) * .5f; pl.baseY = pl.level;
                foreach (var a in avoid) if (Inside(a.p, a.r * .5f)) { No("station/gate/objective"); return false; }
                foreach (var sp in spawnPts) if (Inside(XZ(sp.position), 2.5f)) pl.spawns.Add(sp);
                float diag = Mathf.Sqrt(len * len + wid * wid) * .5f + 2f;
                float edgeMin = 1e9f; Vector2 edgeRoad = c;
                foreach (var q in CoreNear(c, diag)) if (Inside(q, 1f)) { No("road under"); lastWhy = "road under:" + q.x.ToString("F0") + "," + q.y.ToString("F0"); return false; }
                foreach (var q in RoadNear(c, diag + 9f))
                {
                    if (Inside(q, -1f)) continue; // dirt under the plot (yard) is fine
                    var d = q - c; float ox = Mathf.Max(0, Mathf.Abs(Vector2.Dot(d, axis)) - len * .5f), oz = Mathf.Max(0, Mathf.Abs(Vector2.Dot(d, perp)) - wid * .5f);
                    float e2 = Mathf.Sqrt(ox * ox + oz * oz); if (e2 < edgeMin) { edgeMin = e2; edgeRoad = q; }
                }
                if (edgeMin > 9f) { No("far from street"); return false; }
                pl.road = edgeRoad;
                // area: the whole footprint inside the same area, 3 m from its border
                foreach (var q in new[] { c + axis * len * .5f + perp * wid * .5f, c + axis * len * .5f - perp * wid * .5f, c - axis * len * .5f + perp * wid * .5f, c - axis * len * .5f - perp * wid * .5f })
                { var a = AreaOf(q); if (a == null || a.areaName != pl.area || EdgeDist(a, q) < 2f) { No("area border"); return false; } }
                float ex = Mathf.Abs(axis.x) * len * .5f + Mathf.Abs(perp.x) * wid * .5f, ez = Mathf.Abs(axis.y) * len * .5f + Mathf.Abs(perp.y) * wid * .5f;
                // other houses: 5 m apart
                float gapM = wid < 6f ? 4f : 7f; var hb = new UnityEngine.Bounds(new Vector3(c.x, 0, c.y), new Vector3(ex * 2f + gapM, 1000f, ez * 2f + gapM));
                foreach (var kv in hBounds) { if (kv.Key == self) continue; var o = kv.Value; o.center = new Vector3(o.center.x, 0, o.center.z); o.size = new Vector3(o.size.x, 1000f, o.size.z); if (o.Intersects(hb)) { No("house near"); return false; } }
                var seen = new HashSet<int>();
                float minx = c.x - ex - 1.5f, maxx = c.x + ex + 1.5f, minz = c.y - ez - 1.5f, maxz = c.y + ez + 1.5f;
                for (int x = Mathf.FloorToInt(minx / 8f); x <= Mathf.FloorToInt(maxx / 8f); x++)
                    for (int z = Mathf.FloorToInt(minz / 8f); z <= Mathf.FloorToInt(maxz / 8f); z++)
                        if (obsGrid.TryGetValue(new Vector2Int(x, z), out var ol))
                            foreach (int i in ol)
                            {
                                if (!seen.Add(i)) continue; var (o, go, small) = obs[i];
                                if (o.max.y < lo + .3f || (self != null && go.transform.IsChildOf(self))) continue;
                                if (!(o.min.x < maxx && o.max.x > minx && o.min.z < maxz && o.max.z > minz)) continue;
                                string on = go.name.ToLowerInvariant();
                                if (!small && (on.Contains("mounting_post") || on.Contains("streetlamp") || on.Contains("lantern")))
                                {
                                    var lamp = go.transform; while (lamp.parent != null && lamp.parent.parent != null && Bounds(lamp.parent.gameObject).size.x < 4f && Bounds(lamp.parent.gameObject).size.z < 4f) lamp = lamp.parent;
                                    if (!pl.lamps.Contains(lamp)) pl.lamps.Add(lamp); continue;
                                }
                                if (!small) { No("building/wall/fence"); lastWhy = "building/wall/fence:" + go.name + "@" + o.center.x.ToString("F0") + "," + o.center.z.ToString("F0"); return false; }
                                if (Inside(XZ(o.center), 1f) && !pl.hide.Contains(go)) pl.hide.Add(go);
                            }
                for (int i = 0; i < treePos.Length; i++) if (Inside(treePos[i], 1.5f)) pl.trees.Add(i);
                foreach (var col in Physics.OverlapBox(new Vector3(c.x, lo + 2f, c.y), new Vector3(wid * .5f + .5f, 1.6f, len * .5f + .5f), Quaternion.LookRotation(new Vector3(axis.x, 0, axis.y)), ~((1 << 2) | (1 << 30)), QueryTriggerInteraction.Ignore))
                {
                    if (col is TerrainCollider || (self != null && col.transform.IsChildOf(self))) continue;
                    if (pl.hide.Any(h => col.transform.IsChildOf(h.transform))) continue;
                    var cb = col.bounds; if (cb.size.y < 2.6f && Mathf.Max(cb.size.x, cb.size.z) < 3.2f) continue;
                    No("collider"); return false;
                }
                return true;
            }

            // ---- choose plots, farthest from the centre first
            var plans = new List<Plan>();
            var order = moving.OrderBy(h => SwPair(h.name) ? 0 : 1).ThenByDescending(h => (XZ(hBounds[h].center) - MapCenter).magnitude).ToList();
            foreach (var h in order)
            {
                bool farm = ToMove.First(x => x.name == h.name).farm;
                var b = hBounds[h]; float r0 = (XZ(b.center) - MapCenter).magnitude;
                float len = Mathf.Max(b.size.x, b.size.z), wid = Mathf.Min(b.size.x, b.size.z);
                string fromArea = AreaOf(XZ(b.center))?.areaName;
                why.Clear();
                var list = cands.Where(s =>
                {
                    if (s.roadDist > len * .5f + 9f) return false;
                    if (farm) return s.area == fromArea && s.r <= r0 - 4f;
                    if (SwPair(h.name)) return s.area == "Village Square" && InSw(s.p);
                    if (Fill(h.name)) return !InPrecinct(s.p) && !(s.p.x > -20f && s.p.x < 56f && s.p.y > -47f && s.p.y < -36f) && s.r < (h.name == "cooper_workshop" ? 86f : 95f) && s.area != "East Cemetery"; // anywhere on a street, away from the church precinct
                    if (Landmark(h.name)) return (s.area == fromArea || s.area == "Village Square") && s.r <= r0 - 4f;
                    return s.r <= r0 - 15f && (s.area == fromArea || s.area == "Village Square");
                }).OrderBy(s =>
                {
                    float e = Mathf.Max(0f, s.roadDist - wid * .5f);
                    float gap = 1e9f; foreach (var kv in hBounds) if (kv.Key != h) gap = Mathf.Min(gap, Mathf.Max(0, (XZ(kv.Value.center) - s.p).magnitude - (Mathf.Max(kv.Value.size.x, kv.Value.size.z) + len) * .5f));
                    if (Fill(h.name)) return 3f * Mathf.Abs(e - 4f) - .9f * Mathf.Min(gap, 30f); // the emptiest street plots first
                    if (SwPair(h.name)) return 3f * Mathf.Abs(e - 4f) + 1.2f * Mathf.Max(0f, 2f - s.p.y) + .3f * Mathf.Abs(s.p.x + 24f); // front on Via Maestra
                    return s.r + 3f * Mathf.Abs(e - 4f) + .6f * Mathf.Abs(Mathf.Min(gap, 40f) - 8f) + (farm ? 0f : s.area == "Village Square" ? 0f : 6f);
                }).ToList();
                Plan pick = null;
                foreach (var s in list)
                {
                    var pl = new Plan { t = h, b = b, c = s.p, axis = s.tangent, len = len, wid = wid, road = s.road, area = s.area };
                    if (Fits(h, pl)) { pick = pl; break; }
                }
                if (pick == null) { L.AppendLine("STAYS " + h.name + " (" + list.Count + " plots tried: " + string.Join(", ", why.Select(kv => kv.Key + " " + kv.Value)) + ")"); continue; }
                var longAxis = b.size.x >= b.size.z ? Vector2.right : Vector2.up;
                pick.yaw = -Vector2.SignedAngle(longAxis, pick.axis); while (pick.yaw > 90f) pick.yaw -= 180f; while (pick.yaw < -90f) pick.yaw += 180f;
                var perp = new Vector2(-pick.axis.y, pick.axis.x);
                { var d = pick.road - pick.c; float da = Vector2.Dot(d, pick.axis), dp = Vector2.Dot(d, perp);
                  pick.front = Mathf.Abs(dp) - wid * .5f >= Mathf.Abs(da) - len * .5f ? pick.c + perp * Mathf.Sign(dp) * (wid * .5f + .3f) + pick.axis * Mathf.Clamp(da, -len * .3f, len * .3f)
                                                                                     : pick.c + pick.axis * Mathf.Sign(da) * (len * .5f + .3f) + perp * Mathf.Clamp(dp, -wid * .3f, wid * .3f); }
                plans.Add(pick);
                // reserve the plot for the next houses
                hBounds[h] = new UnityEngine.Bounds(new Vector3(pick.c.x, b.center.y, pick.c.y), new Vector3(Mathf.Abs(pick.axis.x) * len + Mathf.Abs(perp.x) * wid, b.size.y, Mathf.Abs(pick.axis.y) * len + Mathf.Abs(perp.y) * wid));
                L.AppendLine("MOVE " + h.name + "|" + b.center.x.ToString("F1") + "," + b.center.z.ToString("F1") + "|" + pick.c.x.ToString("F1") + "," + pick.c.y.ToString("F1") + "|" + len.ToString("F1") + "," + wid.ToString("F1") + "|" + (Mathf.Atan2(pick.axis.y, pick.axis.x) * Mathf.Rad2Deg).ToString("F0") + "|" + pick.road.x.ToString("F1") + "," + pick.road.y.ToString("F1") + "|" + (fromArea ?? "-") + " -> " + pick.area + "|hide " + pick.hide.Count + " trees " + pick.trees.Count + " spawns " + pick.spawns.Count + " lamps " + pick.lamps.Count);
            }
            L.AppendLine("== plan: " + plans.Count + " of " + order.Count + " move");
            if (!apply)
            {
                var free = new StringBuilder();
                foreach (var (fl, fw) in new[] { (16f, 16f), (11.5f, 10f) })
                    foreach (var s in cands)
                    {
                        var pl = new Plan { t = null, c = s.p, axis = s.tangent, len = fl, wid = fw, road = s.road, area = s.area };
                        lastWhy = "";
                        bool okFit = Fits(null, pl);
                        if (fl == 11.5f) free.AppendLine("WHY|" + s.p.x.ToString("F0") + "," + s.p.y.ToString("F0") + "|" + (okFit ? "OK" : lastWhy));
                        if (okFit) free.AppendLine(fl + "|" + s.p.x.ToString("F0") + "," + s.p.y.ToString("F0") + "|" + s.area + "|" + (Mathf.Atan2(s.tangent.y, s.tangent.x) * Mathf.Rad2Deg).ToString("F0"));
                    }
                File.WriteAllText("Logs/relocate_free.txt", free.ToString());
            }
            if (!apply) { Done(); return; }

            // ---- apply
            // level the ground under each plot
            int hr = td.heightmapResolution; var heights = td.GetHeights(0, 0, hr, hr);
            float hcell = td.size.x / (hr - 1); var to = terrain.transform.position;
            foreach (var pl in plans)
            {
                var perp = new Vector2(-pl.axis.y, pl.axis.x); float blend = 3.5f, ext = Mathf.Sqrt(pl.len * pl.len + pl.wid * pl.wid) * .5f + blend + 1f;
                int x0 = Mathf.FloorToInt((pl.c.x - ext - to.x) / hcell), x1 = Mathf.CeilToInt((pl.c.x + ext - to.x) / hcell);
                int z0 = Mathf.FloorToInt((pl.c.y - ext - to.z) / hcell), z1 = Mathf.CeilToInt((pl.c.y + ext - to.z) / hcell);
                float target = (pl.level - to.y) / td.size.y;
                for (int z = Mathf.Max(0, z0); z <= Mathf.Min(hr - 1, z1); z++)
                    for (int x = Mathf.Max(0, x0); x <= Mathf.Min(hr - 1, x1); x++)
                    {
                        var d = new Vector2(to.x + x * hcell, to.z + z * hcell) - pl.c;
                        float ox = Mathf.Max(0, Mathf.Abs(Vector2.Dot(d, pl.axis)) - pl.len * .5f - .8f), oz = Mathf.Max(0, Mathf.Abs(Vector2.Dot(d, perp)) - pl.wid * .5f - .8f);
                        float w = 1f - Mathf.SmoothStep(0, 1, Mathf.Sqrt(ox * ox + oz * oz) / blend);
                        if (w > 0) heights[z, x] = Mathf.Lerp(heights[z, x], target, w);
                    }
            }
            td.SetHeights(0, 0, heights); EditorUtility.SetDirty(td);
            var removeTrees = new HashSet<int>();
            foreach (var pl in plans)
            {
                var h = pl.t; var b = pl.b;
                float sink = Mathf.Clamp(b.min.y - TerrainY(b.center.x, b.center.z), -1.2f, 0f);
                h.RotateAround(b.center, Vector3.up, pl.yaw);
                Physics.SyncTransforms(); var nb = Bounds(h.gameObject);
                h.position += new Vector3(pl.c.x - nb.center.x, pl.baseY + sink - nb.min.y, pl.c.y - nb.center.z);
                EditorUtility.SetDirty(h);
                foreach (var go in pl.hide) { go.SetActive(false); EditorUtility.SetDirty(go); }
                foreach (var i in pl.trees) removeTrees.Add(i);
            }
            Physics.SyncTransforms();
            if (removeTrees.Count > 0) { td.treeInstances = trees.Where((t, i) => !removeTrees.Contains(i)).ToArray(); EditorUtility.SetDirty(td); }
            // drives to the streets
            int ah = td.alphamapHeight, aw = td.alphamapWidth, layers = td.alphamapLayers;
            var alpha = td.GetAlphamaps(0, 0, aw, ah); var weight = new float[ah, aw];
            float cell = td.size.x / aw; var origin = terrain.transform.position;
            foreach (var pl in plans)
            {
                float len = (pl.road - pl.front).magnitude;
                for (float d = 0; d <= len; d += .5f) PaintDot(weight, Vector2.Lerp(pl.front, pl.road, len < .01f ? 0 : d / len), origin, cell, aw, ah, 1.2f, 1f);
            }
            for (int z = 0; z < ah; z++)
                for (int x = 0; x < aw; x++)
                {
                    float w = weight[z, x]; if (w <= 0) continue;
                    float coreW = Mathf.SmoothStep(0, 1, (w - .35f) / .65f);
                    for (int l = 0; l < layers; l++) alpha[z, x, l] *= 1f - w;
                    alpha[z, x, mud] += w * coreW; alpha[z, x, soil] += w * (1f - coreW);
                }
            td.SetAlphamaps(0, 0, alpha); EditorUtility.SetDirty(td);
            L.AppendLine("trees removed " + removeTrees.Count + ", drives " + plans.Count);
            foreach (var pl in plans)
                for (int i = 0; i < pl.lamps.Count; i++)
                {
                    var lamp = pl.lamps[i]; var lb = Bounds(lamp.gameObject);
                    var side = (pl.front - pl.c).normalized; var along = new Vector2(-side.y, side.x);
                    var at = pl.front + side * 1.2f + along * ((i % 2 == 0 ? 1 : -1) * (pl.len * .35f + .5f * (i / 2)));
                    lamp.position += new Vector3(at.x - lb.center.x, TerrainY(at.x, at.y) - lb.min.y, at.y - lb.center.z);
                    EditorUtility.SetDirty(lamp); L.AppendLine("   lamp " + lamp.name + " -> " + at.ToString("F1"));
                }
            var pushed = new List<Transform>();
            foreach (var pl in plans)
                foreach (var sp in pl.spawns)
                {
                    var perp = new Vector2(-pl.axis.y, pl.axis.x); var d = XZ(sp.position) - pl.c;
                    float da = Vector2.Dot(d, pl.axis), dp = Vector2.Dot(d, perp);
                    // out through the nearest side, 3.5 m clear of the wall
                    Vector2 outP = (pl.len * .5f - Mathf.Abs(da)) < (pl.wid * .5f - Mathf.Abs(dp))
                        ? pl.c + pl.axis * Mathf.Sign(da == 0 ? 1 : da) * (pl.len * .5f + 3.5f) + perp * dp
                        : pl.c + perp * Mathf.Sign(dp == 0 ? 1 : dp) * (pl.wid * .5f + 3.5f) + pl.axis * da;
                    sp.position = new Vector3(outP.x, TerrainY(outP.x, outP.y) + .05f, outP.y); EditorUtility.SetDirty(sp); pushed.Add(sp);
                }
            L.AppendLine(ElderboomClaudeBlocks.BakeAccessNavigation());
            foreach (var sp in pushed)
                if (NavMesh.SamplePosition(sp.position, out var hit, 4f, NavMesh.AllAreas)) sp.position = hit.position + Vector3.up * .05f;
                else L.AppendLine("   spawn " + sp.name + " has no ground nearby at " + sp.position.ToString("F1"));
            L.AppendLine("spawns moved out of the new plots: " + pushed.Count);
            new GameObject(RelocMarker);
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            L.AppendLine("SAVED"); Done();
        }
        catch (System.Exception e) { L.AppendLine("EXCEPTION " + e); Done(); }
    }

    static void PaintDot(float[,] weight, Vector2 p, Vector3 origin, float cell, int aw, int ah, float half, float edge)
    {
        int cx = Mathf.RoundToInt((p.x - origin.x) / cell), cz = Mathf.RoundToInt((p.y - origin.z) / cell), rad = Mathf.CeilToInt((half + edge) / cell);
        for (int z = cz - rad; z <= cz + rad; z++)
            for (int x = cx - rad; x <= cx + rad; x++)
            {
                if (x < 0 || z < 0 || x >= aw || z >= ah) continue;
                float d = Vector2.Distance(new Vector2(origin.x + x * cell, origin.z + z * cell), p);
                float w = 1f - Mathf.SmoothStep(0, 1, (d - half) / edge);
                w *= Mathf.Lerp(.75f, 1f, Mathf.PerlinNoise(x * .37f, z * .37f));
                if (w > weight[z, x]) weight[z, x] = w;
            }
    }

    static List<Vector2> RoadPoints(Terrain terrain, out int mud, out int soil, float threshold = .45f)
    {
        var td = terrain.terrainData; var res = new List<Vector2>();
        mud = System.Array.FindIndex(td.terrainLayers, l => l && l.diffuseTexture && l.diffuseTexture.name.ToLowerInvariant().Contains("mud")); if (mud < 0) mud = Mathf.Min(5, td.alphamapLayers - 1);
        soil = System.Array.FindIndex(td.terrainLayers, l => l && l.diffuseTexture && l.diffuseTexture.name.ToLowerInvariant().Contains("soil")); if (soil < 0) soil = 0;
        int aw = td.alphamapWidth, ah = td.alphamapHeight; var a = td.GetAlphamaps(0, 0, aw, ah);
        float cell = td.size.x / aw; var o = terrain.transform.position;
        int step = Mathf.Max(1, Mathf.RoundToInt(1f / cell));
        for (int z = 0; z < ah; z += step)
            for (int x = 0; x < aw; x += step)
            {
                float wx = o.x + x * cell, wz = o.z + z * cell;
                if (Mathf.Abs(wx - MapCenter.x) > 100 || Mathf.Abs(wz - MapCenter.y) > 100) continue;
                if (a[z, x, mud] > threshold) res.Add(new Vector2(wx, wz));
            }
        foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!mf.name.StartsWith("Road_") || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
            // triangle centres: clipped strips keep their vertices but lose the triangles inside the square
            var v = mf.sharedMesh.vertices; var tr = mf.sharedMesh.triangles;
            for (int k = 0; k + 2 < tr.Length; k += 6) res.Add(XZ(mf.transform.TransformPoint((v[tr[k]] + v[tr[k + 1]] + v[tr[k + 2]]) / 3f)));
        }
        return res;
    }

    static Vector2 Principal(List<Vector2> pts, Vector2 at, Vector2 plot)
    {
        if (pts.Count >= 3)
        {
            var m = Vector2.zero; foreach (var p in pts) m += p; m /= pts.Count;
            float sxx = 0, sxz = 0, szz = 0; foreach (var p in pts) { var d = p - m; sxx += d.x * d.x; sxz += d.x * d.y; szz += d.y * d.y; }
            if (sxx + szz > .5f) { float ang = .5f * Mathf.Atan2(2f * sxz, sxx - szz); return new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)); }
        }
        var n = (plot - at).normalized; return n.sqrMagnitude < .1f ? Vector2.right : new Vector2(-n.y, n.x);
    }
}
