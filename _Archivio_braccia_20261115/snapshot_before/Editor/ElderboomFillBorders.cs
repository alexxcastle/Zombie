using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// "prune": keeps only the road-network walls that line a road in a continuous run (2+ modules side by side) or stand
// against a building; removes the free walls in the fields and the lone pieces.
// "mounds" / "mounds apply": block borders (fences, stone walls, hedges of the access layout) are checked every 0.75 m
// on both sides for ground that lifts the player close to the top (terrain bumps, rocks pushed into the barrier).
// Apply lowers those terrain bumps to the barrier's foot, disables the rocks, and adds a tall invisible blocker on
// every border piece (Ignore Raycast layer: shots and sight lines pass, the player does not).
static partial class ElderboomFill
{
    const string BorderRoot = "Elderboom_Access_Redesign", BlockerName = "Border_PlayerBlocker";

    static string Prune()
    {
        var sb = new StringBuilder();
        var root = GameObject.Find(NetRoot); if (root == null) return "FAIL no " + NetRoot;
        var walls = root.transform.Cast<Transform>().ToList();
        var mask = ~((1 << 30) | (1 << 2));
        int removed = 0, kept = 0;
        var remove = new List<Transform>();
        foreach (var w in walls)
        {
            bool field = w.name.Contains("_field_");
            int neighbours = walls.Count(o => o != w && !o.name.Contains("_field_") && Vector3.Distance(o.position, w.position) < 4.8f);
            // Against a building: something solid (not terrain, not another network wall) within 2.5 m behind or in front.
            bool building = false;
            foreach (var d in new[] { w.forward, -w.forward })
                foreach (var h in Physics.RaycastAll(w.position + Vector3.up * 1.4f, d, 3f, mask, QueryTriggerInteraction.Ignore))
                    if (!(h.collider is TerrainCollider) && !h.transform.IsChildOf(root.transform)) { building = true; break; }
            bool keep = !field && (neighbours >= 1 || building);
            if (keep) kept++; else { remove.Add(w); removed++; sb.AppendLine("removed " + w.name + " at " + F(w.position.x) + "," + F(w.position.z) + (field ? " (field)" : " (lone)")); }
        }
        foreach (var w in remove) Object.DestroyImmediate(w.gameObject);
        Physics.SyncTransforms();
        sb.AppendLine($"walls kept {kept}, removed {removed}");
        sb.AppendLine(ElderboomClaudeBlocks.BakeAccessNavigation());
        EditorSceneManager.MarkSceneDirty(root.scene); EditorSceneManager.SaveOpenScenes();
        return sb.ToString();
    }

    static IEnumerable<BoxCollider> BorderPieces(Transform root) =>
        root.GetComponentsInChildren<BoxCollider>(true).Where(c => c.name != BlockerName && c.GetComponentInParent<ElderboomGate>() == null);

    static string Mounds(string mode)
    {
        bool apply = mode.Contains("apply");
        var sb = new StringBuilder();
        var rootGo = GameObject.Find(BorderRoot); if (rootGo == null) return "FAIL no " + BorderRoot;
        var root = rootGo.transform;
        var terrain = Valley(); var td = terrain.terrainData; var origin = terrain.transform.position;
        int mask = ~((1 << 30) | (1 << 2) | (1 << 19));
        var pieces = BorderPieces(root).ToList();
        sb.AppendLine("border pieces " + pieces.Count);
        var terrainFix = new List<(Vector3 p, float to)>();
        var props = new Dictionary<GameObject, int>();
        int risky = 0;
        foreach (var piece in pieces)
        {
            var t = piece.transform; var b = piece.bounds; float top = b.max.y;
            // Long axis of the piece in world space.
            Vector3 along = t.right * piece.size.x * t.lossyScale.x, across = t.forward;
            float len = along.magnitude; if (len < .05f) continue; along /= len;
            if (Mathf.Abs(piece.size.z * t.lossyScale.z) > len) { along = t.forward; across = t.right; len = piece.size.z * t.lossyScale.z; }
            across.y = 0; across.Normalize();
            float foot = terrain.SampleHeight(b.center) + origin.y;
            for (float s = -len * .5f; s <= len * .5f; s += .75f)
                foreach (int side in new[] { -1, 1 })
                    foreach (float d in new[] { .5f, .9f, 1.4f, 2f, 2.6f })
                    {
                        var p = b.center + along * s + across * side * (d + Mathf.Min(b.extents.x, b.extents.z));
                        var hits = Physics.RaycastAll(new Vector3(p.x, top + 6f, p.z), Vector3.down, 12f, mask, QueryTriggerInteraction.Ignore)
                            .Where(h => !h.transform.IsChildOf(root)).OrderBy(h => h.distance).ToArray();
                        if (hits.Length == 0) continue;
                        var h0 = hits[0]; float clearance = top - h0.point.y;
                        if (h0.point.y < foot + .35f || clearance > 1.55f) continue;
                        risky++;
                        if (h0.collider is TerrainCollider) terrainFix.Add((h0.point, foot));
                        else
                        {
                            var go = h0.collider.attachedRigidbody ? h0.collider.attachedRigidbody.gameObject : h0.collider.gameObject;
                            // The prop's own top-level object under its group.
                            var owner = go.transform; while (owner.parent != null && owner.parent.GetComponent<Renderer>() == null && owner.parent.childCount == 1) owner = owner.parent;
                            props[owner.gameObject] = props.TryGetValue(owner.gameObject, out var n) ? n + 1 : 1;
                        }
                    }
        }
        sb.AppendLine($"risky samples {risky}: terrain {terrainFix.Count}, props {props.Count}");
        foreach (var kv in props.OrderByDescending(k => k.Value))
        {
            var g = kv.Key; var r = g.GetComponentInChildren<Renderer>(); var size = r ? r.bounds.size : Vector3.zero;
            sb.AppendLine($"  prop {PathOf(g.transform)} x{kv.Value} at {F(g.transform.position.x)},{F(g.transform.position.z)} size {F(size.x)}x{F(size.y)}x{F(size.z)}");
        }
        foreach (var grp in terrainFix.GroupBy(f => new Vector2Int(Mathf.RoundToInt(f.p.x / 4), Mathf.RoundToInt(f.p.z / 4))))
            sb.AppendLine($"  terrain bump near {grp.Key.x * 4},{grp.Key.y * 4}: {grp.Count()} samples, up to {F(grp.Max(f => f.p.y - f.to))} m above the foot");
        if (!apply) return sb.ToString();

        // Terrain: lower every bump sample (and a soft 2.5 m skirt around it) to the barrier's foot.
        if (terrainFix.Count > 0)
        {
            int hw = td.heightmapResolution; float cell = td.size.x / (hw - 1);
            var heights = td.GetHeights(0, 0, hw, hw);
            foreach (var (p, to) in terrainFix)
            {
                if (p.y - to > 1.5f) continue; // a hillside the barrier stands on, not a bump: the blocker covers it
                float target = (to - .05f - origin.y) / td.size.y;
                int cx = Mathf.RoundToInt((p.x - origin.x) / cell), cz = Mathf.RoundToInt((p.z - origin.z) / cell), rad = Mathf.CeilToInt(2.5f / cell);
                for (int z = cz - rad; z <= cz + rad; z++)
                    for (int x = cx - rad; x <= cx + rad; x++)
                    {
                        if (x < 0 || z < 0 || x >= hw || z >= hw) continue;
                        float dd = Vector2.Distance(new Vector2(x, z), new Vector2(cx, cz)) * cell;
                        float w = 1f - Mathf.SmoothStep(0, 1, (dd - 1f) / 1.5f);
                        if (w <= 0 || heights[z, x] <= target) continue;
                        heights[z, x] = Mathf.Lerp(heights[z, x], target, w);
                    }
            }
            Undo.RegisterCompleteObjectUndo(td, "border mounds"); td.SetHeights(0, 0, heights); EditorUtility.SetDirty(td);
            sb.AppendLine("terrain lowered at " + terrainFix.Count + " samples");
        }
        // Rocks and other props pushed into the barriers: off.
        foreach (var g in props.Keys)
        {
            string n = PathOf(g.transform).ToLowerInvariant();
            // Only earth banks and rocks: walls, beams and buildings next to a border stay (the blocker covers them).
            if (!(n.Contains("grounded_bank") || n.Contains("rock"))) continue;
            if (g.GetComponentInParent<ElderboomArea>() != null && g.GetComponent<ElderboomArea>() != null) continue;
            if (g.GetComponentInParent<ElderboomGate>() != null || g.GetComponentInParent<ElderboomStation>() != null || g.GetComponentInParent<ElderboomSpawnPoint>() != null) continue;
            g.SetActive(false); sb.AppendLine("  disabled " + PathOf(g.transform));
        }
        // Safety net: tall invisible blocker on every border piece.
        int blockers = 0;
        foreach (var piece in pieces)
        {
            var old = piece.transform.Find(BlockerName); if (old) Object.DestroyImmediate(old.gameObject);
            var go = new GameObject(BlockerName); go.layer = 2; go.transform.SetParent(piece.transform, false);
            var bc = go.AddComponent<BoxCollider>(); var s = piece.transform.lossyScale;
            float extra = 3.2f / Mathf.Max(.01f, s.y);
            bc.size = new Vector3(piece.size.x, piece.size.y + extra, piece.size.z);
            bc.center = piece.center + new Vector3(0, extra * .5f, 0);
            blockers++;
        }
        sb.AppendLine("blockers " + blockers);
        Physics.SyncTransforms();
        EditorSceneManager.MarkSceneDirty(rootGo.scene); EditorSceneManager.SaveOpenScenes();
        return sb.ToString();
    }

    static string PathOf(Transform t) { string p = t.name; for (var x = t.parent; x != null; x = x.parent) p = x.name + "/" + p; return p; }
}
