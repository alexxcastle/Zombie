using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

// Logs/claude_start.txt -> "inspect" | "apply": the player starts in the East Cemetery, in front of the helicopter's nose.
// The cemetery becomes the starting area (unlocked) and the Village Square is reached through its gate.
static class ElderboomStartSpawn
{
    const string Flag = "Logs/claude_start.txt", Result = "Logs/claude_start_result.txt";
    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Poll; }
    static double next;
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        string mode = File.ReadAllText(Flag).Trim(); File.Delete(Flag);
        try { File.WriteAllText(Result, Run(mode)); } catch (System.Exception e) { File.WriteAllText(Result, "FAIL " + e); }
    }

    // Nose direction (world, flat): the end of the long axis with the larger cross-section is the cabin.
    static Vector3 Nose(GameObject heli, StringBuilder sb)
    {
        var t = heli.transform; var pts = new System.Collections.Generic.List<Vector3>();
        foreach (var mf in heli.GetComponentsInChildren<MeshFilter>())
        {
            var m = mf.sharedMesh; if (m == null || !m.isReadable) continue;
            var v = m.vertices; for (int i = 0; i < v.Length; i += 7) pts.Add(t.InverseTransformPoint(mf.transform.TransformPoint(v[i])));
        }
        if (pts.Count == 0)
            foreach (var r in heli.GetComponentsInChildren<Renderer>()) { var b = r.bounds; foreach (var c in new[] { b.min, b.max, b.center }) pts.Add(t.InverseTransformPoint(c)); }
        var lo = new Vector3(pts.Min(p => p.x), pts.Min(p => p.y), pts.Min(p => p.z)); var hi = new Vector3(pts.Max(p => p.x), pts.Max(p => p.y), pts.Max(p => p.z));
        sb.AppendLine("heli local bounds " + lo + " .. " + hi + " pts " + pts.Count);
        bool alongZ = hi.z - lo.z >= hi.x - lo.x;
        float a0 = alongZ ? lo.z : lo.x, a1 = alongZ ? hi.z : hi.x, q = (a1 - a0) * .25f;
        float Section(bool high) { var s = pts.Where(p => { float a = alongZ ? p.z : p.x; return high ? a > a1 - q : a < a0 + q; }).ToList(); if (s.Count == 0) return 0; return (s.Max(p => p.y) - s.Min(p => p.y)) * (alongZ ? s.Max(p => p.x) - s.Min(p => p.x) : s.Max(p => p.z) - s.Min(p => p.z)); }
        float sHi = Section(true), sLo = Section(false);
        sb.AppendLine($"long axis {(alongZ ? "z" : "x")} section +end {sHi:F2} -end {sLo:F2}");
        Vector3 local = (alongZ ? Vector3.forward : Vector3.right) * (sHi >= sLo ? 1 : -1);
        var w = t.TransformDirection(local); w.y = 0; return w.normalized;
    }

    static string Run(string mode)
    {
        const string scenePath = "Assets/Scenes/Elderboom_Unity_Port.unity";
        if (EditorSceneManager.GetActiveScene().path != scenePath) EditorSceneManager.OpenScene(scenePath);
        var sb = new StringBuilder();
        if (mode == "look")
        {
            var pl = Object.FindAnyObjectByType<HDRPSlicePlayer>(FindObjectsInactive.Include); var eye = pl.transform.position + Vector3.up * 1.5f; var o = new StringBuilder("look from " + eye + "\n");
            for (int k = 0; k < 16; k++) { var d = Quaternion.Euler(0, k * 22.5f, 0) * Vector3.forward; o.AppendLine($"yaw {k * 22.5f} " + (Physics.Raycast(eye, d, out var lh, 60f, ~(1 << 30), QueryTriggerInteraction.Ignore) ? lh.distance.ToString("F1") + " " + lh.collider.name + " / " + (lh.collider.transform.root.name) : "clear")); }
            return o.ToString();
        }
        if (mode == "gates")
        {
            var navH = Object.FindAnyObjectByType<ElderboomNavigation>(); if (navH != null && navH.data != null) { NavMesh.RemoveAllNavMeshData(); NavMesh.AddNavMeshData(navH.data); }
            var o = new StringBuilder();
            foreach (var g in Object.FindObjectsByType<ElderboomGate>(FindObjectsSortMode.None).Where(x => x.name.StartsWith("Access_")))
            {
                var a = g.transform.position - g.transform.forward * 4f; var b = g.transform.position + g.transform.forward * 4f;
                bool sa = NavMesh.SamplePosition(a, out var ah, 2.5f, NavMesh.AllAreas), sb2 = NavMesh.SamplePosition(b, out var bh, 2.5f, NavMesh.AllAreas);
                var path = new NavMeshPath(); bool cp = sa && sb2 && NavMesh.CalculatePath(ah.position, bh.position, NavMesh.AllAreas, path);
                float len = 0; for (int i = 1; i < path.corners.Length; i++) len += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                var box = g.GetComponent<BoxCollider>();
                o.AppendLine($"{g.name} pos {g.transform.position} fwd {g.transform.forward} box {box.size} sampleA {sa} {(sa ? ah.position.ToString() : "")} sampleB {sb2} {(sb2 ? bh.position.ToString() : "")} path {cp} {path.status} len {len:F1} corners {path.corners.Length}");
                if (sa && sb2 && NavMesh.Raycast(ah.position, bh.position, out var nh, NavMesh.AllAreas))
                {
                    o.AppendLine($"   navray hit {nh.position} dist {nh.distance:F2}");
                    foreach (var c in Physics.OverlapSphere(nh.position + Vector3.up * .3f, 1.2f, ~0, QueryTriggerInteraction.Collide).Where(c => !(c is TerrainCollider)))
                        o.AppendLine($"     near {c.GetType().Name} {c.name} root {c.transform.root.name} layer {c.gameObject.layer} trig {c.isTrigger} en {c.enabled} bounds {c.bounds.center} {c.bounds.size}");
                    if (Physics.Raycast(nh.position + Vector3.up * 5f, Vector3.down, out var gh, 10f, ~0, QueryTriggerInteraction.Ignore)) o.AppendLine($"     ground {gh.collider.name} y {gh.point.y:F2}");
                }
                var cols = Physics.OverlapBox(g.transform.position, new Vector3(Mathf.Max(box.size.x, 3) * .5f, 1f, 1.5f), g.transform.rotation, ~(1 << 30), QueryTriggerInteraction.Ignore);
                foreach (var c in cols.Where(c => !(c is TerrainCollider)).Take(8)) o.AppendLine($"   col {c.GetType().Name} {c.name} parent {(c.transform.parent ? c.transform.parent.name : "-")} leaves {c.GetComponentInParent<ElderboomGateLeaves>() != null} gate {c.GetComponent<ElderboomGate>() != null} bounds {c.bounds.center} {c.bounds.size}");
            }
            return o.ToString();
        }
        if (mode == "save") { bool dirty = EditorSceneManager.GetActiveScene().isDirty; EditorSceneManager.SaveOpenScenes(); return "saved (was dirty " + dirty + ")"; }
        var player = Object.FindAnyObjectByType<HDRPSlicePlayer>(FindObjectsInactive.Include);
        var heli = GameObject.Find("Cemetery_Helicopter");
        if (!player || !heli) return "FAIL player " + (bool)player + " heli " + (bool)heli;
        sb.AppendLine("player " + player.name + " pos " + player.transform.position + " yaw " + player.transform.eulerAngles.y + " parent " + (player.transform.parent ? player.transform.parent.name : "-"));
        sb.AppendLine("heli pos " + heli.transform.position + " yaw " + heli.transform.eulerAngles.y);
        var navHolder = Object.FindAnyObjectByType<ElderboomNavigation>(); if (navHolder != null && navHolder.data != null) { NavMesh.RemoveAllNavMeshData(); NavMesh.AddNavMeshData(navHolder.data); }
        sb.AppendLine("navmesh verts " + NavMesh.CalculateTriangulation().vertices.Length);
        var nose = Nose(heli, sb); sb.AppendLine("nose dir " + nose);
        // Heading: the nose turns toward the most open ground of the cemetery, so the player starts in front of it with room to move.
        if (mode == "apply" || mode == "turns")
        {
            var heliCols = heli.GetComponentsInChildren<Collider>(); var baseRot = heli.transform.rotation; var localNose = Quaternion.Inverse(baseRot) * nose;
            var hbLocal = new Bounds(new Vector3(0, 2.0f, 0), new Vector3(3.2f, 2.6f, 10.6f));
            float bestScore = -1; Quaternion bestRot = baseRot;
            for (int k = 0; k < 16; k++)
            {
                var rot = Quaternion.Euler(0, k * 22.5f, 0) * Quaternion.Euler(0, baseRot.eulerAngles.y, 0) ; rot = Quaternion.Euler(0, k * 22.5f, 0);
                var n = rot * Quaternion.Euler(0, 0, 0) * localNose; n.y = 0; n.Normalize();
                // Rotation that makes the model's nose point along n.
                var r = Quaternion.FromToRotation(baseRot * localNose, n) * baseRot; r = Quaternion.Euler(0, r.eulerAngles.y, 0);
                var c = heli.transform.position;
                var hits = Physics.OverlapBox(c + r * hbLocal.center, hbLocal.extents, r, ~(1 << 30), QueryTriggerInteraction.Ignore).Where(h => !heliCols.Contains(h) && !(h is TerrainCollider) && h.bounds.size.x < 60f && h.bounds.size.z < 60f && h.bounds.max.y > c.y + .6f).ToList();
                var standAt = c + n * 11f; bool nv = NavMesh.SamplePosition(standAt, out var sh, 1.5f, NavMesh.AllAreas) && Mathf.Abs(sh.position.y - c.y) < 1.5f;
                bool room = nv && !Physics.CheckCapsule(sh.position + Vector3.up * 1.3f, sh.position + Vector3.up * 1.9f, 1.1f, ~(1 << 30), QueryTriggerInteraction.Ignore);
                var eye = (nv ? sh.position : standAt) + Vector3.up * 1.5f;
                float open = Physics.Raycast(eye, n, out var oh, 45f, ~(1 << 30), QueryTriggerInteraction.Ignore) ? oh.distance : 45f;
                float side = 0; foreach (float da in new[] { -35f, 35f }) side += Physics.Raycast(eye, Quaternion.Euler(0, da, 0) * n, out var s2, 30f, ~(1 << 30), QueryTriggerInteraction.Ignore) ? s2.distance : 30f;
                float score = hits.Count == 0 && room ? open + side * .5f : -1;
                sb.AppendLine($"  yaw {r.eulerAngles.y:F1} body hits {hits.Count}{(hits.Count > 0 ? " (" + string.Join(",", hits.Select(h => h.transform.parent ? h.transform.parent.name : h.name).Distinct().Take(3)) + " " + hits[0].bounds.size.ToString("F1") + ")" : "")} nav {nv} room {room} open {open:F1} side {side:F1} score {score:F1}");
                if (score > bestScore) { bestScore = score; bestRot = r; }
            }
            if (mode == "apply" && bestScore > 0)
            {
                Undo.RecordObject(heli.transform, "heli"); heli.transform.rotation = bestRot; Physics.SyncTransforms(); nose = bestRot * localNose; nose.y = 0; nose.Normalize();
                sb.AppendLine("heli yaw " + heli.transform.eulerAngles.y.ToString("F1") + " score " + bestScore.ToString("F1")); sb.AppendLine(ElderboomClaudeBlocks.BakeAccessNavigation());
            }
            if (mode == "turns") return sb.ToString();
        }
        var areas = Object.FindObjectsByType<ElderboomArea>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        ElderboomArea AreaAt(Vector3 p) => areas.FirstOrDefault(a => a.Contains(p));
        foreach (var g in Object.FindObjectsByType<ElderboomGate>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var p = g.transform.position; var near = new[] { g.transform.forward, -g.transform.forward, g.transform.right, -g.transform.right }.Select(d => AreaAt(p + d * 6f)).Where(a => a).Select(a => a.areaName).Distinct();
            sb.AppendLine($"gate {g.name} {p} {g.areaName}/{g.otherAreaName} {g.cost} adj [{string.Join(",", near)}]");
        }
        foreach (var a in areas) sb.AppendLine($"area {a.areaName} start {a.unlockedAtStart} spawns {Object.FindObjectsByType<ElderboomSpawnPoint>(FindObjectsSortMode.None).Count(s => s.area == a.areaName || (string.IsNullOrEmpty(s.area) && a.Contains(s.transform.position)))} containsHeli {a.Contains(heli.transform.position)}");

        // Standing spot: 7 m out from the nose tip, on the NavMesh, facing the same way as the helicopter.
        var hb = heli.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
        float reach = Mathf.Abs(Vector3.Dot(hb.extents, new Vector3(Mathf.Abs(nose.x), 0, Mathf.Abs(nose.z))));
        Vector3 tip = new Vector3(hb.center.x, heli.transform.position.y, hb.center.z) + nose * reach, spot = tip; bool onNav = false; NavMeshHit hit = default;
        foreach (float dist in new[] { 6f, 5f, 7f, 4f, 8f, 3f, 10f })
        {
            foreach (float ang in new[] { 0f, 15f, -15f, 30f, -30f, 45f, -45f, 60f, -60f })
            {
                var p = tip + Quaternion.Euler(0, ang, 0) * nose * dist;
                if (Physics.Raycast(p + Vector3.up * 40f, Vector3.down, out var ground, 80f, ~(1 << 30), QueryTriggerInteraction.Ignore)) { p.y = ground.point.y; if (mode == "debug") sb.AppendLine("    ground hit " + ground.collider.name + " " + ground.point.y.ToString("F2")); }
                bool nv = NavMesh.SamplePosition(p, out var h, 3f, NavMesh.AllAreas);
                if (mode == "debug") sb.AppendLine($"  try {dist} {ang} {p} nav {nv} {(nv ? h.position.ToString() + " d" + Vector3.Distance(h.position, p).ToString("F1") : "")} area {(nv ? AreaAt(h.position)?.areaName : AreaAt(p)?.areaName)}");
                if (nv && Mathf.Abs(h.position.y - heli.transform.position.y) > 1.5f) continue;
                if (!nv || Vector3.Distance(new Vector3(h.position.x, 0, h.position.z), new Vector3(p.x, 0, p.z)) > 1.5f || AreaAt(h.position)?.areaName != "East Cemetery") continue;
                if (Physics.CheckCapsule(h.position + Vector3.up * 1.3f, h.position + Vector3.up * 1.9f, .9f, ~(1 << 30), QueryTriggerInteraction.Ignore)) continue;
                spot = p; hit = h; onNav = true; sb.AppendLine($"chosen dist {dist} ang {ang}"); break;
            }
            if (onNav) break;
        }
        if (mode == "debug") foreach (var sp in Object.FindObjectsByType<ElderboomSpawnPoint>(FindObjectsSortMode.None).Where(x => x.name.Contains("Cemetery")).Take(3))
            sb.AppendLine($"  spawn {sp.transform.position} nav {NavMesh.SamplePosition(sp.transform.position, out var sh, 2f, NavMesh.AllAreas)} {sh.position} tris {NavMesh.CalculateTriangulation().vertices.Length}");
        sb.AppendLine($"spot {spot} reach {reach:F1} nav {onNav} {(onNav ? hit.position.ToString() : "")} area {AreaAt(spot)?.areaName}");
        if (mode != "apply") return sb.ToString();
        if (!onNav) return sb + "FAIL no navmesh at spot";
        var cc = player.GetComponent<CharacterController>();
        float feet = cc ? player.transform.position.y - player.transform.TransformPoint(cc.center - Vector3.up * (cc.height * .5f)).y : 0f;
        Undo.RecordObject(player.transform, "start");
        player.transform.SetPositionAndRotation(hit.position + Vector3.up * (feet + .05f), Quaternion.LookRotation(nose));
        foreach (var a in areas)
        {
            bool start = a.areaName == "East Cemetery";
            if (a.unlockedAtStart != start) { Undo.RecordObject(a, "area"); a.unlockedAtStart = start; EditorUtility.SetDirty(a); }
        }
        EditorUtility.SetDirty(heli.transform);
        EditorUtility.SetDirty(player.transform);
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene); EditorSceneManager.SaveOpenScenes();
        sb.AppendLine("APPLIED player " + player.transform.position + " yaw " + player.transform.eulerAngles.y);
        return sb.ToString();
    }
}
