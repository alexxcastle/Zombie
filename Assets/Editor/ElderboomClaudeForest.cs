using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Editor automation: Logs/claude_forest.txt with "report" lists where the triangles of Elderboom_Unity_Port are
// (per top-level group, per mesh), with positions of the tree instances. Result in Logs/claude_forest_result.txt.
[InitializeOnLoad]
static class ElderboomClaudeForest
{
    const string Flag = "Logs/claude_forest.txt", Result = "Logs/claude_forest_result.txt";
    static double nextPoll;

    static ElderboomClaudeForest() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
        string mode = File.ReadAllText(Flag); File.Delete(Flag);
        try { File.WriteAllText(Result, mode.Contains("bias") ? Bias() : mode.Contains("lods") ? Lods(mode.Contains("dry")) : mode.Contains("apply") ? Apply() : mode.Contains("trees") ? Trees() : Report()); }
        catch (System.Exception e) { File.WriteAllText(Result, "FAIL " + e); }
    }

    static string Path(Transform t) { string p = t.name; for (var x = t.parent; x != null; x = x.parent) p = x.name + "/" + p; return p; }
    static long Tris(Mesh m) { long t = 0; if (m == null) return 0; for (int i = 0; i < m.subMeshCount; i++) t += m.GetIndexCount(i) / 3; return t; }

    static string Report()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "Elderboom_Unity_Port") { if (scene.isDirty) return "FAIL unsaved scene"; EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity"); }
        var byRoot = new Dictionary<string, long>(); var byMesh = new Dictionary<string, (long tris, int count, long each)>();
        long total = 0;
        foreach (var r in Object.FindObjectsByType<Renderer>())
        {
            Mesh m = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
            var lod = r.GetComponentInParent<LODGroup>();
            if (lod != null) { var lods = lod.GetLODs(); if (lods.Length > 0 && !lods[0].renderers.Contains(r)) continue; } // count LOD0 only
            long t = Tris(m); total += t;
            var root = r.transform.root.name;
            byRoot[root] = byRoot.TryGetValue(root, out var v) ? v + t : t;
            if (m != null) { var key = m.name; var e = byMesh.TryGetValue(key, out var x) ? x : (0, 0, Tris(m)); byMesh[key] = (e.tris + t, e.count + 1, e.each); }
        }
        var sb = new StringBuilder("total LOD0 triangles " + total + "\n-- top roots\n");
        foreach (var kv in byRoot.OrderByDescending(k => k.Value).Take(15)) sb.AppendLine(kv.Value + "  " + kv.Key);
        sb.AppendLine("-- top meshes (tris total, instances, tris each)");
        foreach (var kv in byMesh.OrderByDescending(k => k.Value.tris).Take(20)) sb.AppendLine(kv.Value.tris + "  x" + kv.Value.count + "  each " + kv.Value.each + "  " + kv.Key);
        // Structure of the biggest root
        var top = byRoot.OrderByDescending(k => k.Value).First().Key;
        var go = scene.GetRootGameObjects().FirstOrDefault(g => g.name == top) ?? GameObject.Find(top);
        if (go != null)
        {
            sb.AppendLine("-- children of " + top + ": " + go.transform.childCount + ", LODGroups " + go.GetComponentsInChildren<LODGroup>(true).Length);
            int n = 0;
            foreach (Transform c in go.transform) { if (n++ >= 12) break; sb.AppendLine("   " + c.name + " pos " + c.position.ToString("F0") + " kids " + c.childCount); }
            var b = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            sb.AppendLine("   bounds " + b.min.ToString("F0") + " .. " + b.max.ToString("F0"));
        }
        sb.AppendLine("-- trees (BlackAlder) and walls (duvar)");
        foreach (var mf in Object.FindObjectsByType<MeshFilter>())
        {
            var m = mf.sharedMesh; if (m == null) continue;
            if (!m.name.Contains("BlackAlder") && !m.name.Contains("duvar")) continue;
            var p = mf.transform.position;
            sb.AppendLine("   " + m.name + " @ " + p.ToString("F0") + " dist " + new Vector2(p.x, p.z).magnitude.ToString("F0") + " scale " + mf.transform.lossyScale.x.ToString("F2") + " path " + Path(mf.transform));
        }
#if UNITY_6000_2_OR_NEWER
        sb.AppendLine("MeshLodUtility available");
#endif
        var village = GameObject.Find("Elderboom_Playable_Ground");
        if (village != null) { var r = village.GetComponent<Renderer>(); if (r != null) sb.AppendLine("playable ground bounds " + r.bounds.min.ToString("F0") + " .. " + r.bounds.max.ToString("F0")); }
        sb.AppendLine("DONE");
        return sb.ToString();
    }
    static Terrain FindTerrain()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "Elderboom_Unity_Port") { if (scene.isDirty) return null; EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity"); }
        Terrain best = null;
        foreach (var t in Object.FindObjectsByType<Terrain>()) if (t.terrainData != null && (best == null || t.terrainData.treeInstanceCount > best.terrainData.treeInstanceCount)) best = t;
        return best;
    }

    static long[] LodTris(GameObject prefab)
    {
        if (prefab == null) return new long[0];
        var lg = prefab.GetComponentInChildren<LODGroup>();
        if (lg == null) { long t = 0; foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>()) t += Tris(mf.sharedMesh); return new[] { t }; }
        var lods = lg.GetLODs(); var res = new long[lods.Length];
        for (int i = 0; i < lods.Length; i++)
            foreach (var r in lods[i].renderers)
            {
                if (r == null) continue;
                if (r is BillboardRenderer) { res[i] += 2; continue; }
                var mf = r.GetComponent<MeshFilter>(); if (mf != null) res[i] += Tris(mf.sharedMesh);
            }
        return res;
    }

    static Vector3 WorldPos(Terrain t, TreeInstance ti) => t.transform.position + Vector3.Scale(ti.position, t.terrainData.size);

    static Bounds Playable()
    {
        var b = new Bounds(new Vector3(5.7f, 0, -.7f), new Vector3(120, 10, 120));
        var g = GameObject.Find("Elderboom_Playable_Ground");
        if (g != null && g.GetComponent<Renderer>() != null) b = g.GetComponent<Renderer>().bounds;
        return b;
    }

    static string Trees()
    {
        var t = FindTerrain(); if (t == null) return "FAIL no terrain";
        var d = t.terrainData; var sb = new StringBuilder();
        sb.AppendLine("terrain " + t.name + " pos " + t.transform.position.ToString("F0") + " size " + d.size.ToString("F0") + " trees " + d.treeInstanceCount);
        sb.AppendLine("treeDistance " + t.treeDistance + " billboard " + t.treeBillboardDistance + " fullLOD " + t.treeMaximumFullLODCount + " lodBiasMul " + t.treeLODBiasMultiplier + " crossfade " + t.treeCrossFadeLength + " drawTrees " + t.drawTreesAndFoliage);
        var pb = Playable(); var c = new Vector2(pb.center.x, pb.center.z);
        sb.AppendLine("playable " + pb.min.ToString("F0") + " .. " + pb.max.ToString("F0"));
        var count = new int[d.treePrototypes.Length]; var buckets = new int[12]; long lod0 = 0;
        var tris = d.treePrototypes.Select(p => LodTris(p.prefab)).ToArray();
        foreach (var ti in d.treeInstances)
        {
            count[ti.prototypeIndex]++;
            var w = WorldPos(t, ti); float dist = Vector2.Distance(new Vector2(w.x, w.z), c);
            buckets[Mathf.Clamp((int)(dist / 30), 0, 11)]++;
            if (tris[ti.prototypeIndex].Length > 0) lod0 += tris[ti.prototypeIndex][0];
        }
        for (int i = 0; i < count.Length; i++)
        {
            var p = d.treePrototypes[i];
            sb.AppendLine("proto " + i + " " + (p.prefab ? p.prefab.name : "null") + " x" + count[i] + " LOD tris [" + string.Join(",", tris[i]) + "]" + (p.prefab && p.prefab.GetComponentInChildren<LODGroup>() is LODGroup lg ? " transitions [" + string.Join(",", lg.GetLODs().Select(l => l.screenRelativeTransitionHeight.ToString("F3"))) + "]" : ""));
        }
        sb.AppendLine("all trees at LOD0 " + lod0);
        for (int i = 0; i < buckets.Length; i++) sb.AppendLine("  dist " + (i * 30) + "-" + (i * 30 + 30) + ": " + buckets[i]);
        sb.AppendLine("DONE");
        return sb.ToString();
    }

    // Back half of the far trees removed; part of them replanted in a ring just outside the village.
    static string Apply()
    {
        var t = FindTerrain(); if (t == null) return "FAIL no terrain";
        var d = t.terrainData; var sb = new StringBuilder();
        var pb = Playable(); var c = new Vector2(pb.center.x, pb.center.z);
        float inner = Mathf.Max(pb.extents.x, pb.extents.z) + 6f, outer = inner + 38f;
        var all = d.treeInstances.ToList();
        var dist = all.Select(ti => { var w = WorldPos(t, ti); return Vector2.Distance(new Vector2(w.x, w.z), c); }).ToList();
        var sorted = dist.OrderBy(x => x).ToList();
        float back = sorted[(int)(sorted.Count * .55f)];
        var rnd = new System.Random(1234);
        var keep = new List<TreeInstance>(); var moved = new List<TreeInstance>();
        for (int i = 0; i < all.Count; i++)
        {
            if (dist[i] < back) { keep.Add(all[i]); continue; }
            double r = rnd.NextDouble();
            if (r < .40) keep.Add(all[i]);            // stays (thinned forest)
            else if (r < .62) moved.Add(all[i]);      // replanted nearer
        }
        // Roads: keep replanted trees off them.
        var roadPts = new List<Vector2>();
        foreach (var mf in Object.FindObjectsByType<MeshFilter>())
            if (mf.name.StartsWith("Road_") && mf.sharedMesh != null) { var v = mf.sharedMesh.vertices; for (int k = 0; k < v.Length; k += 10) { var w = mf.transform.TransformPoint(v[k]); roadPts.Add(new Vector2(w.x, w.z)); } }
        var placed = keep.Select(ti => { var w = WorldPos(t, ti); return new Vector2(w.x, w.z); }).ToList();
        Physics.SyncTransforms();
        int ok = 0, tries = 0;
        foreach (var ti in moved)
        {
            for (int a = 0; a < 40; a++)
            {
                tries++;
                float ang = (float)(rnd.NextDouble() * Mathf.PI * 2), rad = Mathf.Lerp(inner, outer, Mathf.Sqrt((float)rnd.NextDouble()));
                var p = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;
                if (placed.Any(q => (q - p).sqrMagnitude < 5.5f * 5.5f)) continue;
                if (roadPts.Any(q => (q - p).sqrMagnitude < 6f * 6f)) continue;
                float h = t.SampleHeight(new Vector3(p.x, 0, p.y)) + t.transform.position.y;
                var pos3 = new Vector3(p.x, h, p.y);
                bool blocked = false;
                foreach (var col in Physics.OverlapSphere(pos3 + Vector3.up * 2f, 3.5f, ~0, QueryTriggerInteraction.Ignore))
                    if (!(col is TerrainCollider) && col.name.IndexOf("Ground", System.StringComparison.OrdinalIgnoreCase) < 0) { blocked = true; break; }
                if (blocked) continue;
                var n = d.GetInterpolatedNormal((p.x - t.transform.position.x) / d.size.x, (p.y - t.transform.position.z) / d.size.z);
                if (Vector3.Angle(n, Vector3.up) > 28f) continue;
                var nt = ti;
                nt.position = new Vector3((p.x - t.transform.position.x) / d.size.x, 0, (p.y - t.transform.position.z) / d.size.z);
                nt.position = new Vector3(nt.position.x, (h - t.transform.position.y) / d.size.y, nt.position.z);
                keep.Add(nt); placed.Add(p); ok++; break;
            }
        }
        sb.AppendLine("trees " + all.Count + " -> " + keep.Count + " (far cut at " + back.ToString("F0") + " m, " + moved.Count + " to move, " + ok + " replanted between " + inner.ToString("F0") + " and " + outer.ToString("F0") + " m, tries " + tries + ")");
        Undo.RegisterCompleteObjectUndo(d, "forest");
        d.SetTreeInstances(keep.ToArray(), true);
        // Far trees as billboards / low LODs, fewer full-detail trees.
        t.treeBillboardDistance = 45f; t.treeMaximumFullLODCount = 80; t.treeLODBiasMultiplier = .6f; t.treeDistance = Mathf.Min(t.treeDistance, 500f);
        EditorUtility.SetDirty(d); EditorUtility.SetDirty(t);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(t.gameObject.scene); EditorSceneManager.SaveScene(t.gameObject.scene);
        sb.AppendLine("treeDistance " + t.treeDistance + " billboard " + t.treeBillboardDistance + " fullLOD " + t.treeMaximumFullLODCount + " lodBiasMul " + t.treeLODBiasMultiplier);
        sb.AppendLine(Trees());
        return sb.ToString();
    }
    // Heavy village meshes (the 358K-triangle alders, walls...) had a single detail level: Mesh LODs generated so
    // that from a distance (and in the shadow maps) they draw a fraction of the triangles.
    static string Lods(bool dry)
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "Elderboom_Unity_Port") { if (scene.isDirty) return "FAIL unsaved scene"; EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity"); }
        var sb = new StringBuilder(); var done = new Dictionary<Mesh, Mesh>();
        const string dir = "Assets/GameAssets/Ported/ElderboomVillage/MeshLods";
        if (!dry && !AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/GameAssets/Ported/ElderboomVillage", "MeshLods");
        int swapped = 0;
        foreach (var mf in Object.FindObjectsByType<MeshFilter>())
        {
            var m = mf.sharedMesh; if (m == null || mf.transform.root.name != "Elderboom_Village_Port") continue;
            if (done.TryGetValue(m, out var lodMesh)) { if (lodMesh != null) { mf.sharedMesh = lodMesh; swapped++; } continue; }
            if (Tris(m) < 40000 || m.lodCount > 1) { done[m] = null; continue; }
            string path = AssetDatabase.GetAssetPath(m);
            if (dry) { sb.AppendLine("would " + m.name + " " + Tris(m)); done[m] = null; continue; }
            var copy = Object.Instantiate(m); copy.name = m.name + "_MeshLod";
            string what;
            try
            {
                MeshLodUtility.GenerateMeshLods(copy);
                var lt = new List<string>();
                for (int l = 0; l < copy.lodCount; l++) { long t = 0; for (int sm = 0; sm < copy.subMeshCount; sm++) t += copy.GetLod(sm, l).indexCount / 3; lt.Add(t.ToString()); }
                what = "lods " + copy.lodCount + " [" + string.Join(",", lt) + "]";
                AssetDatabase.CreateAsset(copy, dir + "/" + copy.name + ".asset");
                done[m] = copy; mf.sharedMesh = copy; swapped++;
            }
            catch (System.Exception e) { what = "ERR " + e.Message; done[m] = null; }
            sb.AppendLine(m.name + " " + Tris(m) + " tris: " + what + "  (" + path + ")");
        }
        sb.AppendLine("renderers switched " + swapped);
        if (!dry) { EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene()); EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene()); }
        if (!dry) AssetDatabase.SaveAssets();
        sb.AppendLine("DONE");
        return sb.ToString();
    }
    // Village Mesh LOD renderers switch one detail level earlier (the alders are 358K triangles at full detail).
    static string Bias()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "Elderboom_Unity_Port") { if (scene.isDirty) return "FAIL unsaved scene"; scene = EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity"); }
        int n = 0;
        foreach (var mr in Object.FindObjectsByType<MeshRenderer>())
        {
            var mf = mr.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.name.EndsWith("_MeshLod")) continue;
            mr.meshLodSelectionBias = mf.sharedMesh.name.Contains("BlackAlder") ? 1.5f : 1f; EditorUtility.SetDirty(mr); n++;
        }
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        return "biased " + n + "\nDONE\n";
    }
}
