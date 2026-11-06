using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

// The access layout is data-first. MapAccessLayout.json owns placement, functional
// graph and asset IDs; this editor builder only resolves existing catalog assets and
// instantiates them deterministically. It intentionally does not create new meshes.
static class ElderboomAccessLayoutBuilder
{
    const string LayoutPath = "Assets/Resources/Elderboom/MapAccessLayout.json";
    const string RootName = "Elderboom_Access_Redesign";
    const string ScenePath = "Assets/Scenes/Elderboom_Unity_Port.unity";

    [Serializable] sealed class Layout { public int schemaVersion; public float gridMeters; public Functional functional; public Aesthetic aesthetic; }
    [Serializable] sealed class Functional { public string designIntent; public Access[] accesses; }
    [Serializable] sealed class Aesthetic { public string styleAnchor; public AestheticAccess[] accesses; public Boundary[] boundaries; }
    [Serializable] sealed class Boundary { public string id, asset; public float height, depth, span; public int[] points; }
    [Serializable] sealed class Access {
        public string id, from, to, role, gameplayMotivation, interferenceRisk;
        public int gridX, gridZ, cost; public float alongX, alongZ, clearWidth, closureDepth;
    }
    [Serializable] sealed class AestheticAccess { public string id, treatment, recipe; public string[] catalogAssetIds; }

    [MenuItem("Elderboom HDRP/Rebuild Access Layout From JSON")]
    public static void Rebuild()
    {
        RequireMap();
        var json = AssetDatabase.LoadAssetAtPath<TextAsset>(LayoutPath);
        if (json == null) throw new InvalidOperationException("Missing access layout: " + LayoutPath);
        var layout = JsonUtility.FromJson<Layout>(json.text);
        if (layout?.functional?.accesses == null || layout.aesthetic?.accesses == null || layout.functional.accesses.Length != 7)
            throw new InvalidOperationException("Access JSON must contain exactly seven functional and aesthetic entries.");
        if (layout.functional.accesses.Select(x => x.id).Distinct().Count() != layout.functional.accesses.Length)
            throw new InvalidOperationException("Access JSON contains duplicate ids.");

        var log = new StringBuilder();
        var port = GameObject.Find("Elderboom_Village_Port");
        if (port == null) throw new InvalidOperationException("Elderboom catalog root missing.");
        var assets = new Catalog(port.transform);
        var root = new GameObject(RootName + "_Staging").transform;
        try
        {
            foreach (var access in layout.functional.accesses)
            {
                var art = layout.aesthetic.accesses.SingleOrDefault(x => x.id == access.id);
                if (art == null) throw new InvalidOperationException("Missing aesthetic data for " + access.id);
                BuildAccess(root, access, art, assets, log);
            }
            foreach (var line in layout.aesthetic.boundaries)
                BuildBoundary(root, line, layout.functional.accesses, assets);
        }
        catch
        {
            Object.DestroyImmediate(root.gameObject);
            throw;
        }
        var old = GameObject.Find(RootName);
        if (old) Object.DestroyImmediate(old);
        int retired = RetireLegacyAccesses(log);
        root.name = RootName;
        Physics.SyncTransforms();
        log.AppendLine(ElderboomClaudeBlocks.BakeAccessNavigation());
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Directory.CreateDirectory("Logs/MapAccessRedesign");
        File.WriteAllText("Logs/MapAccessRedesign/access-build.txt", log.AppendLine("legacy access roots disabled: " + retired).ToString());
        AssetDatabase.SaveAssets();
        Debug.Log("[Elderboom] Access layout rebuilt from JSON. " + root.childCount + " access roots.");
    }

    [MenuItem("Elderboom HDRP/Validate Access Layout")]
    public static void Validate()
    {
        RequireMap();
        var json = AssetDatabase.LoadAssetAtPath<TextAsset>(LayoutPath);
        var layout = json == null ? null : JsonUtility.FromJson<Layout>(json.text);
        var report = new StringBuilder(); int failures = 0;
        void Check(bool pass, string text) { report.AppendLine((pass ? "PASS " : "FAIL ") + text); if (!pass) failures++; }
        var root = GameObject.Find(RootName);
        Check(root != null, "redesign root exists");
        if (root != null && layout?.functional?.accesses != null)
        {
            var gates = root.GetComponentsInChildren<ElderboomGate>(true).Where(x => x.gameObject.activeInHierarchy).ToArray();
            Check(gates.Length == layout.functional.accesses.Length, "active access count " + gates.Length + "/" + layout.functional.accesses.Length);
            foreach (var access in layout.functional.accesses)
            {
                var gate = gates.SingleOrDefault(x => x.name == "Access_" + access.id);
                Check(gate != null, access.id + " present");
                if (gate == null) continue;
                Check(gate.cost == access.cost && gate.areaName == access.to && gate.otherAreaName == access.from, access.id + " function graph matches JSON");
                Check(Mathf.Abs(gate.GetComponent<BoxCollider>().size.x - access.clearWidth) < .01f, access.id + " declared clearance preserved");
                Check(gate.GetComponentsInChildren<Renderer>().Length > 0, access.id + " visible geometry");
                var path = new NavMeshPath();
                var a = gate.transform.position - gate.transform.forward * 4f;
                var b = gate.transform.position + gate.transform.forward * 4f;
                bool pathOk = NavMesh.SamplePosition(a, out var ah, 2.5f, NavMesh.AllAreas) && NavMesh.SamplePosition(b, out var bh, 2.5f, NavMesh.AllAreas)
                    && NavMesh.CalculatePath(ah.position, bh.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete
                    && Enumerable.Range(1,path.corners.Length-1).Sum(i=>Vector3.Distance(path.corners[i-1],path.corners[i])) < 15;
                Check(pathOk, access.id + " has an open NavMesh route before runtime blocker carving");
            }
        }
        var old = GameObject.Find("Elderboom_Blocks"); var oldPassages = old != null ? old.transform.Find("Passages") : null;
        Check(oldPassages == null || !oldPassages.gameObject.activeInHierarchy, "legacy passages disabled");
        var oldLights = old != null ? old.transform.Find("Route_Lights") : null;
        Check(oldLights == null || !oldLights.gameObject.activeInHierarchy, "legacy route lights disabled");
        Directory.CreateDirectory("Logs/MapAccessRedesign");
        report.AppendLine("Failures: " + failures); File.WriteAllText("Logs/MapAccessRedesign/access-validation.txt", report.ToString());
        if (failures > 0) throw new InvalidOperationException("Access layout validation failed: " + failures + ". See Logs/MapAccessRedesign/access-validation.txt");
        Debug.Log("[Elderboom] Access layout validation passed.");
    }

    static void RequireMap()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath || scene.isDirty) throw new InvalidOperationException("Open and save the port scene first.");
    }

    static void BuildBoundary(Transform root, Boundary line, Access[] gates, Catalog catalog)
    {
        var group = new GameObject(line.id).transform;
        group.SetParent(root, false);
        var source = catalog.Require(line.asset);
        for (int i = 2; i < line.points.Length; i += 2)
        {
            var a = new Vector2(line.points[i-2],line.points[i-1]);
            var b = new Vector2(line.points[i],line.points[i+1]);
            float length = Vector2.Distance(a,b);
            var dir = (b-a).normalized;
            var cuts = new List<float> {0,length};
            foreach (var gate in gates)
            {
                var offset = new Vector2(gate.gridX,gate.gridZ)-a;
                float t = Vector2.Dot(offset,dir);
                if ((offset-dir*t).magnitude > .8f || t < -.1f || t > length+.1f) continue;
                cuts.Add(Mathf.Clamp(t-gate.clearWidth/2,0,length));
                cuts.Add(Mathf.Clamp(t+gate.clearWidth/2,0,length));
            }
            cuts.Sort();
            for(int j=1;j<cuts.Count;j++)
            {
                float d = cuts[j]-cuts[j-1];
                var mid = a+dir*((cuts[j]+cuts[j-1])*.5f);
                if(d < .15f || gates.Any(g=>Vector2.Distance(mid,new Vector2(g.gridX,g.gridZ)) < g.clearWidth/2)) continue;
                int count = Mathf.CeilToInt(d/line.span);
                for(int k=0;k<count;k++)
                {
                    float width=d/count;
                    var point=a+dir*(cuts[j-1]+(k+.5f)*width);
                    var part=new GameObject(line.asset).transform;
                    part.SetParent(group,false);
                    var mesh=catalog.Clone(source,part);
                    mesh.SetActive(true);
                    catalog.AlignLong(mesh,Vector3.right);
                    catalog.Center(mesh,Vector3.zero);
                    var bounds=Catalog.Bounds(mesh);
                    if(bounds.size.x<.01f || bounds.size.z<.01f)throw new InvalidOperationException("Empty mesh: "+source.name);
                    float h=line.height*(line.asset=="Red_Bush"?1+.12f*Mathf.Sin(point.x+point.y):1);
                    part.localScale=new Vector3(width/bounds.size.x,h/bounds.size.y,line.depth/bounds.size.z);
                    part.SetPositionAndRotation(new Vector3(point.x,Ground(point)+h*.5f-.12f,point.y),
                        Quaternion.LookRotation(new Vector3(-dir.y,0,dir.x)));
                    var col=part.gameObject.AddComponent<BoxCollider>();col.size=bounds.size;
                }
            }
        }
    }

    static int RetireLegacyAccesses(StringBuilder log)
    {
        int n = 0;
        var oldBlocks = GameObject.Find("Elderboom_Blocks");
        if (oldBlocks != null)
            foreach (var name in new[] { "Passages", "Route_Lights" })
            {
                var t = oldBlocks.transform.Find(name);
                if (t != null && t.gameObject.activeSelf) { t.gameObject.SetActive(false); n++; log.AppendLine("disabled legacy " + t.name); }
            }
        if (oldBlocks == null) return n;
        foreach (var gate in Object.FindObjectsByType<ElderboomGate>(FindObjectsInactive.Include))
        {
            if (gate.transform.IsChildOf(oldBlocks.transform) && gate.gameObject.activeSelf)
            { gate.gameObject.SetActive(false); n++; log.AppendLine("disabled legacy gate " + gate.name); }
        }
        // Environmental building doors are intentionally retained: they are scenery, not unlock barriers.
        return n;
    }

    static void BuildAccess(Transform root, Access a, AestheticAccess art, Catalog catalog, StringBuilder log)
    {
        if (a.clearWidth < 3f || a.closureDepth < .7f) throw new InvalidOperationException("Unsafe clearance in " + a.id);
        var pos2 = new Vector2(a.gridX, a.gridZ);
        var along = new Vector3(a.alongX, 0, a.alongZ);
        if (along.sqrMagnitude < .01f) throw new InvalidOperationException("No orientation for " + a.id);
        var gate = new GameObject("Access_" + a.id); gate.transform.SetParent(root);
        gate.transform.SetPositionAndRotation(new Vector3(pos2.x, Ground(pos2) + 1.35f, pos2.y), Quaternion.LookRotation(along.normalized) * Quaternion.Euler(0, -90, 0));
        var box = gate.AddComponent<BoxCollider>(); box.size = new Vector3(a.clearWidth, 3.1f, a.closureDepth);
        var logic = gate.AddComponent<ElderboomGate>(); logic.areaName = a.to; logic.otherAreaName = a.from; logic.cost = a.cost;
        if (a.id == "bridge_barricade")
        {
            var bridge = GameObject.Find("SouthCreek_OldStoneBridge");
            float deck = ElderboomClaudeBlocks.BridgeSurface(bridge, pos2.x, pos2.y);
            gate.transform.position = new Vector3(pos2.x, deck + 1.35f, pos2.y);
        }
        switch (art.recipe)
        {
            case "farm_service_gate": FenceClosure(gate.transform, catalog, art.catalogAssetIds, false); break;
            case "sawmill_haul_gate": LogClosure(gate.transform, catalog, art.catalogAssetIds, 6, true); break;
            case "churchyard_iron_gate": IronClosure(gate.transform, catalog, art.catalogAssetIds, 2); break;
            case "bridge_barricade": LogClosure(gate.transform, catalog, art.catalogAssetIds, 5, false); AttachExistingBridge(logic, log); break;
            case "woods_fall": LogClosure(gate.transform, catalog, art.catalogAssetIds, 7, false); break;
            case "mill_cart_shortcut": FenceClosure(gate.transform, catalog, art.catalogAssetIds, true); break;
            case "caretaker_wicket": IronClosure(gate.transform, catalog, art.catalogAssetIds, 1); break;
            default: throw new InvalidOperationException("No catalog treatment for " + a.id);
        }
        log.AppendLine($"{a.id}: {a.from} -> {a.to}; {a.clearWidth:F1}m; {string.Join(",", art.catalogAssetIds)}");
    }

    // Fit catalog instances in gate-local metres. Imported meshes have different
    // native up axes, pivots and dimensions; their transforms are not interchangeable.
    static void FitProp(Transform gate,Catalog catalog,string id,Vector3 center,Vector3 size,bool horizontalBeam=false,float tilt=0,bool permanent=false)
    {
        var frame = new GameObject(id).transform;
        var go = catalog.Clone(catalog.Require(id),frame);
        go.transform.rotation=Quaternion.identity;
        var b=Catalog.Bounds(go);
        if(horizontalBeam && b.size.y>Mathf.Max(b.size.x,b.size.z))
            go.transform.rotation=Quaternion.Euler(0,0,90);
        else if(b.size.z>b.size.x)
            go.transform.rotation=Quaternion.Euler(0,90,0);
        catalog.Center(go,Vector3.zero);
        b=Catalog.Bounds(go);
        frame.localScale=new Vector3(size.x/Mathf.Max(.001f,b.size.x),size.y/Mathf.Max(.001f,b.size.y),size.z/Mathf.Max(.001f,b.size.z));
        frame.SetParent(permanent?gate.parent:gate,true);
        frame.SetPositionAndRotation(gate.TransformPoint(center),gate.rotation*Quaternion.AngleAxis(tilt,Vector3.forward));
        if(permanent)frame.gameObject.AddComponent<BoxCollider>().size=b.size;
    }

    static void FenceClosure(Transform gate,Catalog c,string[] ids,bool wheel)
    {
        float w=gate.GetComponent<BoxCollider>().size.x;
        for(int i=-1;i<=1;i+=2)
        {
            FitProp(gate,c,ids[0],new Vector3(i*w*.25f,-.53f,0),new Vector3(w*.49f,1.65f,.2f),tilt:i*2);
            FitProp(gate,c,ids[1],new Vector3(i*(w*.5f+.15f),-.45f,0),new Vector3(.27f,1.8f,.27f),permanent:true);
        }
        if(wheel)FitProp(gate,c,ids[2],new Vector3(w*.28f,-.75f,.25f),new Vector3(1.14f,1.14f,.18f));
    }

    static void LogClosure(Transform gate,Catalog c,string[] ids,int count,bool pile)
    {
        float w=gate.GetComponent<BoxCollider>().size.x;
        bool bridge=gate.name=="Access_bridge_barricade";
        for(int i=0;i<(bridge?3:count);i++)
        {
            float rise=bridge?.62f*i:(i/2)*.32f;
            FitProp(gate,c,ids[0],new Vector3(0,-1.10f+rise,(i%2-.5f)*.35f),
                new Vector3(w-.12f,bridge?.17f:.33f,bridge?.14f:.32f),true,bridge?0:(i%3-1)*5);
        }
        if(bridge)foreach(int sign in new[]{-1,1})
            FitProp(gate,c,ids[1],new Vector3(sign*(w*.5f-.15f),-.45f,0),new Vector3(.24f,1.8f,.24f));
        if(pile)FitProp(gate,c,ids[1],new Vector3(w*.24f,-.87f,.35f),new Vector3(w*.44f,.94f,.52f));
    }

    static void IronClosure(Transform gate,Catalog c,string[] ids,int leaves)
    {
        float w=gate.GetComponent<BoxCollider>().size.x;
        foreach(int side in new[]{-1,1})
            FitProp(gate,c,ids[1],new Vector3(side*(w*.5f+.35f),-.31f,0),new Vector3(.70f,2.08f,.78f),permanent:true);
        for(int i=0;i<leaves;i++)
            FitProp(gate,c,ids[0],new Vector3(((i+.5f)/leaves-.5f)*w,-.45f,0),new Vector3(w/leaves-.02f,1.80f,.13f));
    }

    static void AttachExistingBridge(ElderboomGate gate, StringBuilder log)
    {
        var bridge = GameObject.Find("SouthCreek_OldStoneBridge");
        if (bridge == null) { log.AppendLine("WARN bridge root missing; no reveal binding"); return; }
        // The imported bridge is the actual crossing; it is permanently visible and this
        // gate only removes the emergency barricade. Do not deactivate its mesh on buy.
        log.AppendLine("retained existing stone bridge as permanent route geometry");
    }

    static float Ground(Vector2 p)
    {
        var terrain = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude).OrderByDescending(t => t.terrainData.size.x).FirstOrDefault();
        return terrain == null ? 0 : terrain.SampleHeight(new Vector3(p.x, 0, p.y)) + terrain.transform.position.y;
    }

    sealed class Catalog
    {
        readonly Transform root;
        readonly Dictionary<string, string> prefixes = new Dictionary<string, string>
        {
            ["Elderboom_ModularFence"] = "S_Modular_Fence_ufflaitdw",
            ["Elderboom_WoodenBollard"] = "S_Wooden_Bollard_tlttae1fa",
            ["Elderboom_WornWoodenBeam"] = "S_Worn_Wooden_Beam",
            ["Elderboom_ChoppedWoodPile"] = "S_Chopped_Wood_Pile",
            ["Elderboom_MossyStoneWall"] = "S_Mossy_Stone_Wall_ukhgdfyga",
            ["Elderboom_WoodenWheel"] = "S_Wooden_Wheel_ugciag2ga"
        };
        public Catalog(Transform root) { this.root = root; }
        public GameObject Require(string id)
        {
            if(id=="Red_Bush")return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TerrainDemoScene_HDRP/Prefabs/Foliage/Red_Bush/Red_Bush.prefab") ?? throw new InvalidOperationException(id);
            if (id == "Elderboom_IronGateLeaf")
            {
                var imported = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameAssets/Ported/ElderboomVillage/generated/elderboom_gate_leaf.glb");
                if (imported != null) return imported;
            }
            if (!prefixes.TryGetValue(id, out var prefix)) throw new InvalidOperationException("Unknown catalog id " + id);
            foreach (var ch in root.GetComponentsInChildren<Transform>(true))
                if (ch.name.StartsWith(prefix, StringComparison.Ordinal) && ch.GetComponentInChildren<Renderer>(true) != null) return ch.gameObject;
            throw new InvalidOperationException("Catalog asset not found in scene: " + id + " / " + prefix);
        }
        public GameObject Clone(GameObject src, Transform parent)
        {
            var go = Object.Instantiate(src); go.name = src.name; go.transform.SetParent(parent, true); go.transform.localScale = src.transform.lossyScale;
            go.SetActive(true);
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
            return go;
        }
        public void Center(GameObject go, Vector3 target)
        {
            var b = Bounds(go); go.transform.position += target - b.center;
        }
        public void Place(GameObject src, Transform parent, Vector3 target)
        {
            var go = Clone(src, parent); Center(go, target); var b = Bounds(go); go.transform.position += Vector3.up * (Ground(new Vector2(target.x,target.z)) - b.min.y);
        }
        public void AlignLong(GameObject go, Vector3 direction)
        {
            var mf = go.GetComponentInChildren<MeshFilter>(); var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null) { go.transform.rotation = Quaternion.LookRotation(direction); return; }
            var size = mesh.bounds.size; bool x = Mathf.Abs(size.x) >= Mathf.Abs(size.z);
            go.transform.rotation = Quaternion.LookRotation(direction) * Quaternion.Euler(0, x ? -90 : 0, 0) * Quaternion.Inverse(mf.transform.localRotation);
        }
        internal static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true); if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = rs[0].bounds; for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds); return b;
        }
    }
}
