using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.HighDefinition;

// Replaces the narrow South Creek stone bridge with the wide metric bridge generated in the Codex pass
// (assets/generated/elderboom_bridge_cemetery_2026_11). The paid gate (1250) is refitted between the
// central pillars; bank stones overlapping the new footprint slide aside; the closed-gate connectivity is
// checked before and after, and nothing is saved if the creek border leaks.
static partial class ElderboomMapRework
{
    const string BridgeDir = "Assets/GameAssets/Environment/BridgeCemetery/wide_stone_bridge_metric";
    const string BridgeName = "SouthCreek_WideStoneBridge";

    [MenuItem("Elderboom HDRP/Map Rework/9 Install Wide Bridge")]
    static void InstallWideBridge()
    {
        L = new StringBuilder();
        var scene = EditorSceneManager.GetActiveScene();
        if (GameObject.Find(BridgeName) != null) { L.AppendLine("already installed"); FlushBridge(); return; }
        try
        {
            Physics.SyncTransforms();
            var before = Connectivity("BEFORE bridge (gates closed)");
            var old = GameObject.Find("SouthCreek_OldStoneBridge");
            var gate = GameObject.Find("Elderboom_Access_Redesign/Access_bridge_barricade");
            if (old == null || gate == null) throw new System.Exception("old bridge or gate missing");
            var ob = Bounds(old);
            L.AppendLine("old bridge " + ob.center.ToString("F2") + " / " + ob.size.ToString("F2"));

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BridgeDir + "/wide_stone_bridge_metric.prefab");
            var br = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(br, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            br.name = BridgeName; br.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Physics.SyncTransforms();
            var b0 = Bounds(br);
            float k = 18.45f / Mathf.Max(b0.size.x, b0.size.z);
            if (Mathf.Abs(k - 1f) > .05f) { br.transform.localScale *= k; L.AppendLine("   unit fix x" + k.ToString("F3")); }
            Physics.SyncTransforms(); b0 = Bounds(br);
            if ((ob.size.z > ob.size.x) != (b0.size.z > b0.size.x)) br.transform.rotation = Quaternion.Euler(0, 90, 0);

            // Material with the improved PBR maps, LOD0 colliders.
            var mat = BridgeMaterial();
            foreach (var r in br.GetComponentsInChildren<Renderer>(true)) { r.sharedMaterials = r.sharedMaterials.Select(_ => mat).ToArray(); }
            var lowLods = new HashSet<Renderer>();
            foreach (var lg in br.GetComponentsInChildren<LODGroup>(true)) { var lods = lg.GetLODs(); for (int i = 1; i < lods.Length; i++) foreach (var r in lods[i].renderers) if (r) lowLods.Add(r); }
            int cols = 0;
            foreach (var mf in br.GetComponentsInChildren<MeshFilter>(true))
            {
                var r = mf.GetComponent<Renderer>(); if (r == null || lowLods.Contains(r) || mf.sharedMesh == null) continue;
                if (lowLods.Count == 0 && (mf.name.Contains("LOD1") || mf.name.Contains("LOD2"))) continue;
                var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; cols++;
            }
            L.AppendLine("   renderers " + br.GetComponentsInChildren<Renderer>(true).Length + ", LOD0 colliders " + cols);

            Physics.SyncTransforms(); b0 = Bounds(br);
            br.transform.position += new Vector3(ob.center.x - b0.center.x, 0, ob.center.z - b0.center.z);
            Physics.SyncTransforms(); b0 = Bounds(br);
            bool alongZ = b0.size.z > b0.size.x;
            Vector3 axis = alongZ ? Vector3.forward : Vector3.right;
            float half = (alongZ ? b0.extents.z : b0.extents.x);
            var c = new Vector3(b0.center.x, 0, b0.center.z);
            var endA = c + axis * (half - .7f); var endB = c - axis * (half - .7f);
            float dA = DeckY(br, endA), dB = DeckY(br, endB);
            float gA = TerrainY((c + axis * (half + .3f)).x, (c + axis * (half + .3f)).z), gB = TerrainY((c - axis * (half + .3f)).x, (c - axis * (half + .3f)).z);
            float dy = ((gA - dA) + (gB - dB)) * .5f + .03f;
            br.transform.position += Vector3.up * dy; Physics.SyncTransforms(); b0 = Bounds(br);
            float mid = DeckY(br, c);
            L.AppendLine("   new bridge " + b0.center.ToString("F2") + " / " + b0.size.ToString("F2") + " deck ends " + DeckY(br, endA).ToString("F2") + "/" + DeckY(br, endB).ToString("F2") + " vs ground " + gA.ToString("F2") + "/" + gB.ToString("F2") + ", crown " + mid.ToString("F2"));

            old.SetActive(false); EditorUtility.SetDirty(old);

            // Bank stones (iron railing) overlapping the new footprint slide sideways against the parapets.
            var bank = GameObject.Find("Elderboom_Village_Expansion/Riverbank_Stone_And_Spearhead_Iron");
            if (bank)
                foreach (Transform st in bank.transform)
                {
                    if (!st.gameObject.activeInHierarchy || st.GetComponentInChildren<Renderer>() == null) continue;
                    var sb = Bounds(st.gameObject);
                    if (sb.max.x < b0.min.x || sb.min.x > b0.max.x || sb.max.z < b0.min.z || sb.min.z > b0.max.z) continue;
                    var before0 = st.position;
                    Vector3 shift = alongZ
                        ? new Vector3(sb.center.x < b0.center.x ? (b0.min.x + .15f) - sb.max.x : (b0.max.x - .15f) - sb.min.x, 0, 0)
                        : new Vector3(0, 0, sb.center.z < b0.center.z ? (b0.min.z + .15f) - sb.max.z : (b0.max.z - .15f) - sb.min.z);
                    var to = sb.center + shift;
                    shift.y = TerrainY(to.x, to.z) - TerrainY(sb.center.x, sb.center.z);
                    st.position += shift; EditorUtility.SetDirty(st);
                    L.AppendLine("   bank " + st.name + " slid " + shift.ToString("F2"));
                }

            // Path lantern that would end up inside the arch moves to the north bridge head.
            var lights = GameObject.Find("Elderboom_Environment_Art/Motivated_Path_Lighting");
            if (lights)
                foreach (Transform lt in lights.transform)
                {
                    if (lt.GetComponentInChildren<Renderer>() == null) continue;
                    var lb = Bounds(lt.gameObject);
                    if (lb.center.x < b0.min.x - .3f || lb.center.x > b0.max.x + .3f || lb.center.z < b0.min.z - .3f || lb.center.z > b0.max.z + .3f) continue;
                    var dest = alongZ ? new Vector3(b0.max.x + .45f, 0, b0.max.z - .4f) : new Vector3(b0.max.x - .4f, 0, b0.max.z + .45f);
                    float gy = TerrainY(dest.x, dest.z);
                    lt.position += new Vector3(dest.x - lb.center.x, gy - lb.min.y, dest.z - lb.center.z); EditorUtility.SetDirty(lt);
                    L.AppendLine("   lantern " + lt.name + " -> " + dest.ToString("F1"));
                }

            FitGate(gate, br, b0, alongZ, mid);

            Physics.SyncTransforms();
            var after = Connectivity("AFTER bridge (gates closed)");
            var leaks = before.Keys.Where(a => !before[a].reach && after.ContainsKey(a) && after[a].reach).ToList();
            if (leaks.Count > 0) { L.AppendLine("ABORT: leak into " + string.Join(",", leaks) + " -> scene reloaded, nothing saved"); FlushBridge(); NavMesh.RemoveAllNavMeshData(); EditorSceneManager.OpenScene(scene.path); return; }

            L.AppendLine("== navigation");
            L.AppendLine(ElderboomClaudeBlocks.BakeAccessNavigation());
            ValidateGameplay();
            var pth = new NavMeshPath(); var st0 = VillageStart();
            bool cross = NavMesh.SamplePosition(c - axis * (half + 3f), out var hs, 4f, NavMesh.AllAreas) && NavMesh.CalculatePath(st0, hs.position, NavMesh.AllAreas, pth) && pth.status == NavMeshPathStatus.PathComplete;
            L.AppendLine("   far bank reachable over the bridge (gate open) = " + cross);
            if (!cross) cross = NavMesh.SamplePosition(c + axis * (half + 3f), out hs, 4f, NavMesh.AllAreas) && NavMesh.CalculatePath(st0, hs.position, NavMesh.AllAreas, pth) && pth.status == NavMeshPathStatus.PathComplete;
            if (!cross) { L.AppendLine("ABORT: bridge not walkable -> scene reloaded, nothing saved"); FlushBridge(); NavMesh.RemoveAllNavMeshData(); EditorSceneManager.OpenScene(scene.path); return; }

            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            L.AppendLine("SAVED"); FlushBridge();

            var restore = Daylight();
            try
            {
                L.AppendLine(Shot("Logs/rework_shots/bridge_after_A.png", c + axis * (half + 12f) + Vector3.right * .5f, new Vector3(c.x, 1.2f, c.z)));
                L.AppendLine(Shot("Logs/rework_shots/bridge_after_B.png", c + (alongZ ? Vector3.right : Vector3.forward) * 16f + axis * 4f, new Vector3(c.x, .8f, c.z)));
                L.AppendLine(Shot("Logs/rework_shots/bridge_after_C.png", c + axis * 4.5f, new Vector3(c.x - axis.x * 9f, 1.4f, c.z - axis.z * 9f)));
            }
            finally { restore(); }
            FlushBridge();
        }
        catch (System.Exception e)
        {
            L.AppendLine("EXCEPTION " + e); FlushBridge();
            NavMesh.RemoveAllNavMeshData(); EditorSceneManager.OpenScene(scene.path);
        }
    }

    static void FlushBridge() { Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/map_bridge_install.txt", L.ToString()); }

    static float DeckY(GameObject br, Vector3 at)
    {
        float best = float.NegativeInfinity;
        foreach (var h in Physics.RaycastAll(new Vector3(at.x, 60f, at.z), Vector3.down, 120f, ~0, QueryTriggerInteraction.Ignore))
            if (h.collider.transform.IsChildOf(br.transform) && h.point.y > best) best = h.point.y;
        return best;
    }

    static float SideHit(GameObject br, Vector3 from, Vector3 dir)
    {
        float best = 10f;
        foreach (var h in Physics.RaycastAll(from, dir, 10f, ~0, QueryTriggerInteraction.Ignore))
            if (h.collider.transform.IsChildOf(br.transform) && h.distance < best) best = h.distance;
        return best;
    }

    static void FitGate(GameObject gate, GameObject br, Bounds b0, bool alongZ, float deck)
    {
        var lv = gate.GetComponent<ElderboomGateLeaves>();
        var c = new Vector3(b0.center.x, deck, b0.center.z);
        var across = alongZ ? Vector3.right : Vector3.forward;
        // Inner faces of the central pillars, just above the parapet walls.
        float bestL = 0, bestR = 0;
        foreach (float h in new[] { 1.9f, 1.6f, 1.3f, 1.0f })
        {
            float l = SideHit(br, c + Vector3.up * h, -across), r = SideHit(br, c + Vector3.up * h, across);
            if (l < 9f && r < 9f) { bestL = l; bestR = r; break; }
        }
        if (bestL == 0) { L.AppendLine("   gate: pillar faces not found, gate left in place"); return; }
        float inner = bestL + bestR;
        float span = lv != null && lv.leftHinge && lv.rightHinge ? Vector3.Distance(lv.leftHinge.position, lv.rightHinge.position) : 3.7f;
        float k = Mathf.Clamp((inner - .06f) / span, .8f, 2.2f);
        gate.transform.localScale *= k;
        Physics.SyncTransforms();
        var hingeMid = lv != null && lv.leftHinge && lv.rightHinge ? (lv.leftHinge.position + lv.rightHinge.position) * .5f : gate.transform.position;
        var target = c + across * ((bestR - bestL) * .5f);
        float leafBottom = float.PositiveInfinity;
        foreach (var r in gate.GetComponentsInChildren<Renderer>(true)) if (!r.name.Contains("Post") && !r.transform.parent.name.Contains("StationaryPost")) leafBottom = Mathf.Min(leafBottom, r.bounds.min.y);
        if (float.IsInfinity(leafBottom)) leafBottom = gate.GetComponent<Collider>().bounds.min.y;
        gate.transform.position += new Vector3(target.x - hingeMid.x, deck + .04f - leafBottom, target.z - hingeMid.z);
        // The bridge pillars replace the gate's own stationary posts (their colliders stay).
        foreach (Transform t in gate.GetComponentsInChildren<Transform>(true))
            if (t.name == "StationaryPost") foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        EditorUtility.SetDirty(gate.transform);
        Physics.SyncTransforms();
        L.AppendLine("   gate: pillar gap " + inner.ToString("F2") + " m (old leaves " + span.ToString("F2") + ") -> x" + k.ToString("F2") + ", at " + gate.transform.position.ToString("F2") + ", box " + gate.GetComponent<Collider>().bounds.size.ToString("F2"));
    }

    static Material BridgeMaterial()
    {
        string pbr = BridgeDir + "/pbr";
        string normalPath = pbr + "/0_normal.png", maskPath = pbr + "/0_mask.png", diffPath = pbr + "/0_diffuse.png";
        var ni = (TextureImporter)AssetImporter.GetAtPath(normalPath);
        if (ni != null && ni.textureType != TextureImporterType.NormalMap) { ni.textureType = TextureImporterType.NormalMap; ni.SaveAndReimport(); }
        if (!File.Exists(maskPath))
        {
            var mr = new Texture2D(2, 2, TextureFormat.RGBA32, false, true); mr.LoadImage(File.ReadAllBytes(pbr + "/0_metalrough.png"));
            var px = mr.GetPixels32();
            for (int i = 0; i < px.Length; i++) { var p = px[i]; px[i] = new Color32(p.b, 255, 0, (byte)(255 - p.g)); }
            var mask = new Texture2D(mr.width, mr.height, TextureFormat.RGBA32, false, true); mask.SetPixels32(px);
            File.WriteAllBytes(maskPath, mask.EncodeToPNG()); Object.DestroyImmediate(mr); Object.DestroyImmediate(mask);
            AssetDatabase.ImportAsset(maskPath);
            var mi = (TextureImporter)AssetImporter.GetAtPath(maskPath); mi.sRGBTexture = false; mi.SaveAndReimport();
        }
        string matPath = BridgeDir + "/WideBridge_Stone_HDRP.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (m == null) { m = new Material(Shader.Find("HDRP/Lit")); AssetDatabase.CreateAsset(m, matPath); }
        m.SetTexture("_BaseColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>(diffPath));
        m.SetColor("_BaseColor", new Color(.86f, .85f, .82f));
        m.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath)); m.SetFloat("_NormalScale", 1f);
        m.SetTexture("_MaskMap", AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath));
        m.SetFloat("_MetallicRemapMin", 0f); m.SetFloat("_MetallicRemapMax", 1f);
        m.SetFloat("_SmoothnessRemapMin", 0f); m.SetFloat("_SmoothnessRemapMax", .85f);
        m.SetFloat("_AORemapMin", 0f); m.SetFloat("_AORemapMax", 1f);
        HDMaterial.ValidateMaterial(m); EditorUtility.SetDirty(m);
        return m;
    }
}
