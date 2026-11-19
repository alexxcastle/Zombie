using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Logs/claude_placement.txt -> plays the game scene, waits for the runtime-built map, then checks every placed asset
// against the ground: floating, gaps under a corner (slopes), sunk too deep, outside the terrain, buildings overlapping.
// Report: Logs/placement/report.txt, screenshots of the worst cases: Logs/placement/NN_name.png
[InitializeOnLoad]
static class ElderboomPlacementAudit
{
    const string Flag = "Logs/claude_placement.txt", Pending = "claude_placement_pending", Out = "Logs/placement";
    static double next, startAt;
    static ElderboomPlacementAudit() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 1;
        if (File.Exists(Flag) && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling)
        {
            File.Delete(Flag);
            SessionState.SetBool(Pending, true);
            EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
            EditorApplication.isPlaying = true;
            startAt = 0;
            return;
        }
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying) return;
        if (startAt == 0) { startAt = EditorApplication.timeSinceStartup + 12; return; }
        if (EditorApplication.timeSinceStartup < startAt) return;
        SessionState.SetBool(Pending, false);
        try { Audit(); } catch (System.Exception e) { File.WriteAllText(Out + "/report.txt", "FAIL " + e); }
        EditorApplication.isPlaying = false;
    }

    sealed class Unit { public Transform t; public Bounds b; public float ground, gMin, gMax, support; public string issue; public float score; }

    static bool Skip(Transform t)
    {
        string n = t.name.ToLowerInvariant();
        if (t.GetComponentInParent<Camera>() != null || t.GetComponentInParent<CharacterController>() != null || t.GetComponentInParent<Animator>() != null) return true;
        if (t.GetComponentInParent<ElderboomZombie>() != null) return true;
        return n.Contains("sky") || n.Contains("water") || n.Contains("river") || n.Contains("road") || n.Contains("path") || n.Contains("ocean") || n.Contains("fog") || n.Contains("decal") || n.Contains("vfx") || n.Contains("particle") || n.Contains("bridge") || n.Contains("light");
    }

    static readonly Dictionary<Transform, (bool, Bounds)> boundsCache = new Dictionary<Transform, (bool, Bounds)>();
    static bool BoundsOf(Transform t, out Bounds b)
    {
        if (boundsCache.TryGetValue(t, out var cached)) { b = cached.Item2; return cached.Item1; }
        bool ok = BoundsRaw(t, out b); boundsCache[t] = (ok, b); return ok;
    }
    static bool BoundsRaw(Transform t, out Bounds b)
    {
        b = default; bool any = false;
        foreach (var r in t.GetComponentsInChildren<Renderer>(false))
        {
            if (!(r is MeshRenderer || r is SkinnedMeshRenderer) || !r.enabled) continue;
            if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
        }
        return any;
    }

    static void Audit()
    {
        Directory.CreateDirectory(Out); boundsCache.Clear();
        foreach (var f in Directory.GetFiles(Out)) File.Delete(f);
        var terrains = Terrain.activeTerrains;
        float Ground(Vector3 p)
        {
            foreach (var tr in terrains)
            {
                var o = tr.GetPosition(); var s = tr.terrainData.size;
                if (p.x >= o.x && p.z >= o.z && p.x <= o.x + s.x && p.z <= o.z + s.z) return tr.SampleHeight(p) + o.y;
            }
            return float.NaN;
        }
        // Placement units: the highest ancestor whose renderers fit in 40 m (groups bigger than that are split).
        var units = new Dictionary<Transform, Unit>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!(r is MeshRenderer || r is SkinnedMeshRenderer) || !r.enabled || r.GetComponentInParent<Terrain>() != null) continue;
            var t = r.transform; if (Skip(t)) continue;
            Transform unit = t;
            for (var p = t.parent; p != null; p = p.parent)
            {
                if (!BoundsOf(p, out var pb) || Mathf.Max(pb.size.x, pb.size.z) > 40f) break;
                unit = p;
            }
            if (units.ContainsKey(unit) || Skip(unit)) continue;
            if (!BoundsOf(unit, out var b) || b.size.y < .15f) continue;
            units[unit] = new Unit { t = unit, b = b };
        }
        var list = new List<Unit>();
        foreach (var u in units.Values)
        {
            var b = u.b; var c = b.center;
            u.ground = Ground(c);
            if (float.IsNaN(u.ground)) { u.issue = "OUTSIDE TERRAIN"; u.score = 50; list.Add(u); continue; }
            float ex = b.extents.x * .85f, ez = b.extents.z * .85f;
            var gs = new[] { Ground(c + new Vector3(ex, 0, ez)), Ground(c + new Vector3(-ex, 0, ez)), Ground(c + new Vector3(ex, 0, -ez)), Ground(c + new Vector3(-ex, 0, -ez)), u.ground }.Where(g => !float.IsNaN(g)).ToArray();
            u.gMin = gs.Min(); u.gMax = gs.Max();
            // What holds it up: terrain or another collider right below the bottom (not its own).
            u.support = u.ground;
            var hits = Physics.RaycastAll(new Vector3(c.x, b.min.y + .3f, c.z), Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (var h in hits) if (!h.transform.IsChildOf(u.t) && h.point.y > best) best = h.point.y;
            if (best > u.support) u.support = best;
            // Pieces resting on other meshes without colliders (upper walls, beams on posts, roofs): another unit whose
            // top is at this bottom and whose footprint overlaps this one holds it up.
            foreach (var o in units.Values)
            {
                if (o == u) continue; var ob = o.b;
                if (ob.max.y < b.min.y - .35f || ob.min.y > b.min.y - .02f) continue;
                if (ob.max.x < b.min.x || ob.min.x > b.max.x || ob.max.z < b.min.z || ob.min.z > b.max.z) continue;
                float top = Mathf.Min(ob.max.y, b.min.y);
                if (top > u.support) u.support = top;
            }
            float bottom = b.min.y, height = b.size.y, size = Mathf.Max(b.size.x, b.size.z);
            float gap = bottom - u.support;
            if (gap > .25f) { u.issue = "FLOATING " + gap.ToString("F2") + " m"; u.score = gap * 10 + size * .2f; }
            else if (size > 3f && bottom - u.gMin > .35f && u.gMax - u.gMin > .3f) { u.issue = "GAP UNDER A CORNER " + (bottom - u.gMin).ToString("F2") + " m (slope " + (u.gMax - u.gMin).ToString("F1") + " m)"; u.score = (bottom - u.gMin) * 8 + size * .1f; }
            else if (u.ground - bottom > Mathf.Max(.6f, height * .4f)) { u.issue = "SUNK " + (u.ground - bottom).ToString("F2") + " m of " + height.ToString("F1"); u.score = (u.ground - bottom) / height * 6; }
            if (u.issue != null) list.Add(u);
        }
        // Buildings (>4 m) whose footprints overlap a lot.
        var big = units.Values.Where(u => Mathf.Max(u.b.size.x, u.b.size.z) > 4f && u.b.size.y > 2.5f).ToList();
        int overlaps = 0; var overlapText = new StringBuilder();
        for (int i = 0; i < big.Count; i++)
            for (int j = i + 1; j < big.Count; j++)
            {
                var a = big[i].b; var b = big[j].b;
                float ox = Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x), oz = Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z);
                if (ox <= 0 || oz <= 0) continue;
                float frac = ox * oz / Mathf.Min(a.size.x * a.size.z, b.size.x * b.size.z);
                if (frac < .35f || big[i].t.IsChildOf(big[j].t) || big[j].t.IsChildOf(big[i].t)) continue;
                overlaps++; if (overlaps <= 40) overlapText.AppendLine("  OVERLAP " + (frac * 100).ToString("F0") + "%  " + PathOf(big[i].t) + "  <>  " + PathOf(big[j].t) + "  at " + V(a.center));
            }
        var sb = new StringBuilder();
        sb.AppendLine("units " + units.Count + ", problems " + list.Count + ", building overlaps " + overlaps);
        foreach (var g in list.GroupBy(u => u.issue.Split(' ')[0])) sb.AppendLine("  " + g.Key + ": " + g.Count());
        sb.AppendLine("-- worst first");
        var sorted = list.OrderByDescending(u => u.score).ToList();
        foreach (var u in sorted.Take(150)) sb.AppendLine(u.issue + "  |  " + PathOf(u.t) + "  at " + V(u.b.center) + " size " + V(u.b.size));
        sb.AppendLine("-- overlaps"); sb.Append(overlapText);
        // Screenshots of the 16 worst.
        var go = new GameObject("PlacementCam"); var cam = go.AddComponent<Camera>(); cam.fieldOfView = 50; cam.nearClipPlane = .1f; cam.farClipPlane = 600;
        var rt = new RenderTexture(960, 540, 24); cam.targetTexture = rt;
        int k = 0;
        foreach (var u in sorted.Take(16))
        {
            var b = u.b; float d = Mathf.Max(4f, b.extents.magnitude * 2.2f);
            go.transform.position = b.center + new Vector3(d * .7f, d * .45f, -d * .7f);
            go.transform.LookAt(new Vector3(b.center.x, b.min.y, b.center.z));
            cam.Render(); cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Out + "/" + (k++).ToString("00") + "_" + Safe(u.t.name) + ".png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
        sb.AppendLine("DONE");
        File.WriteAllText(Out + "/report.txt", sb.ToString());
    }

    static string Safe(string s) => new string(s.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray()).Substring(0, Mathf.Min(40, s.Length));
    static string V(Vector3 v) => "(" + v.x.ToString("F1") + ", " + v.y.ToString("F1") + ", " + v.z.ToString("F1") + ")";
    static string PathOf(Transform t) { var s = t.name; for (var p = t.parent; p != null && s.Length < 120; p = p.parent) s = p.name + "/" + s; return s; }
}
