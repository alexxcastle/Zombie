using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

public static class ElderboomWorldPort
{
    const string Root = "Assets/GameAssets/Ported/";
    [Serializable] public class Group { public string[] names; }
    [Serializable] public class Slot { public string key; public Group[] groups; }
    [Serializable] public class Point { public float x, z, width; }
    [Serializable] public class Route { public string id; public float width; public Point[] points; }
    [Serializable] public class Data { public Slot[] slots; public string[] chapel; public Route[] routes; }
    static Data data;
    static Dictionary<string, Material> materials;
    public static Vector3 Map(float x, float y, float z) => new Vector3(x, y, -z);

    public static void Apply(GameObject village)
    {
        data = JsonUtility.FromJson<Data>(File.ReadAllText(Root + "ElderboomPortData.json"));
        materials = new Dictionary<string, Material>();
        village.transform.position = Map(-20, 0, -25);
        village.transform.rotation = Quaternion.Euler(0, 180, 0);
        var renderers = village.GetComponentsInChildren<MeshRenderer>(true);
        foreach (var renderer in renderers)
        {
            string id = Identity(renderer);
            if (id.Contains("landscape") || id.Contains("skysphere") || renderer.name.StartsWith("FOLIAGE_TEMPLATE")) { renderer.gameObject.SetActive(false); continue; }
            var filter = renderer.GetComponent<MeshFilter>();
            string key = filter != null && filter.sharedMesh != null ? filter.sharedMesh.name : renderer.name;
            key = key.Replace("L_ElderboomVillage_", "");
            var slot = data.slots.Where(s => key.StartsWith(s.key, StringComparison.Ordinal)).OrderByDescending(s => s.key.Length).FirstOrDefault();
            if (slot == null) slot = data.slots.Where(s => renderer.name.StartsWith(s.key, StringComparison.Ordinal)).OrderByDescending(s => s.key.Length).FirstOrDefault();
            if (slot == null || slot.groups.Length == 0) continue;
            int group = 0;
            if (key.Length >= slot.key.Length && int.TryParse(key.Substring(slot.key.Length), out int suffix)) group = Math.Max(0, suffix - 1);
            var names = slot.groups[Math.Min(group, slot.groups.Length - 1)].names;
            renderer.sharedMaterials = Enumerable.Range(0, renderer.sharedMaterials.Length).Select(i => GetMaterial(names[Math.Min(i, names.Length - 1)])).ToArray();
        }
        Thin(renderers.Where(r => Identity(r).Contains("fence")), .2f);
        Thin(renderers.Where(r => Identity(r).Contains("mossy_stone_wall")), .3f);
        Thin(renderers.Where(r => (Identity(r).Contains("gravestone") || Identity(r).Contains("tombstone")) && r.bounds.min.y < 4f), .16f);
        var random = new System.Random(584903);
        foreach (var tree in renderers.Where(r => Identity(r).Contains("blackalder_field_04") || Identity(r).Contains("blackalder_forest_05")))
        {
            bool inner = Mathf.Abs(tree.bounds.center.x - 5.7f) < 38f && Mathf.Abs(tree.bounds.center.z + .7f) < 38f;
            bool keep = random.NextDouble() <= (inner ? .2 : .005);
            tree.gameObject.SetActive(keep);
            if (keep) tree.transform.position += Vector3.up * (1f - tree.bounds.min.y);
        }
        var chapelNames = new HashSet<string>(data.chapel);
        var chapel = renderers.Where(r => chapelNames.Contains(r.name)).ToArray();
        if (chapel.Length > 0)
        {
            var bounds = chapel[0].bounds; foreach (var part in chapel) bounds.Encapsulate(part.bounds);
            var pivot = new GameObject("CentralChapel_Scale_1_25").transform;
            pivot.SetParent(village.transform, true); pivot.position = new Vector3(bounds.center.x, 1f, bounds.center.z);
            foreach (var part in chapel) part.transform.SetParent(pivot, true);
            pivot.localScale *= 1.25f;
        }
        BuildPerimeter(village, renderers);
        foreach (var filter in village.GetComponentsInChildren<MeshFilter>(false))
        {
            string id = filter.name.ToLowerInvariant();
            if (id.Contains("grass") || id.Contains("alder") || id.Contains("foliage") || filter.sharedMesh == null) continue;
            var collider = filter.gameObject.AddComponent<MeshCollider>(); collider.sharedMesh = filter.sharedMesh;
        }
        Debug.Log($"ELDERBOOM_WORLD: {materials.Count} repaired material assets; {chapel.Length}/{data.chapel.Length} enlarged chapel components.");
    }

    static string Identity(Renderer renderer) => (renderer.name + " " + (renderer.GetComponent<MeshFilter>()?.sharedMesh?.name ?? "")).ToLowerInvariant();
    static void Thin(IEnumerable<MeshRenderer> sources, float fraction)
    {
        var items = sources.OrderBy(r => r.name, StringComparer.Ordinal).ToArray();
        int stride=Math.Max(1,Mathf.RoundToInt(1f/fraction));
        for (int i = 0; i < items.Length; i++) if (i % stride != 0) items[i].gameObject.SetActive(false);
    }

    static Material GetMaterial(string name)
    {
        if (materials.TryGetValue(name, out var result)) return result;
        string path = "Assets/Materials/Port_" + name + ".mat";
        result = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (result == null) { result = new Material(Shader.Find("HDRP/Lit")); AssetDatabase.CreateAsset(result, path); }
        string prefix = Root + "ElderboomVillage/ElderBoomVillage_Block_" + name;
        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(prefix + "_BaseColor.png");
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(prefix + "_Normal.png");
        string lower = name.ToLowerInvariant();
        bool foliage = lower.Contains("alder") || lower.Contains("grass");
        result.SetColor("_BaseColor", albedo != null ? Color.white : foliage ? new Color(.28f, .38f, .15f) : new Color(.32f, .29f, .25f));
        result.SetTexture("_BaseColorMap", albedo);
        result.SetFloat("_Metallic", 0f); result.SetFloat("_Smoothness", foliage ? .04f : .14f);
        if (normal != null)
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(normal)) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.NormalMap) { importer.textureType = TextureImporterType.NormalMap; importer.SaveAndReimport(); }
            result.SetTexture("_NormalMap", normal); result.SetFloat("_NormalScale", .65f); result.EnableKeyword("_NORMALMAP");
        }
        if (foliage)
        {
            result.SetFloat("_AlphaCutoffEnable", 1); result.SetFloat("_AlphaCutoff", .35f); result.EnableKeyword("_ALPHATEST_ON");
            result.SetFloat("_DoubleSidedEnable", 1); result.SetFloat("_CullMode", 0); result.SetFloat("_CullModeForward", 0); result.EnableKeyword("_DOUBLESIDED_ON");
        }
        EditorUtility.SetDirty(result); materials.Add(name, result); return result;
    }

    static void BuildPerimeter(GameObject village, MeshRenderer[] sources)
    {
        var specs = new[] { (-54f,40f,true,90f,1f),(-53f,24f,false,0f,1.05f),(-4f,-60f,true,0f,1f),(12f,-60f,false,-90f,1f),(-53f,-8f,false,90f,1.15f),(48f,-56f,false,0f,1.15f),(-24f,61f,false,180f,1.15f),(0f,62f,false,180f,1.2f),(37f,62f,true,180f,1.2f),(65f,25f,false,-90f,1.1f) };
        for (int i = 0; i < specs.Length; i++)
        {
            var s = specs[i]; var rect = s.Item3 ? new Rect(35, 0, 14, 8) : new Rect(-8,-10,12,6);
            Vector3 pivot = Map(s.Item3 ? 41.7f : -2.2f, 1, s.Item3 ? 3.9f : -6.9f);
            var group = new GameObject("Perimeter_Building_" + i).transform;
            group.SetParent(village.transform.parent, true); group.position = Map(s.Item1,1,s.Item2);
            foreach (var source in sources)
            {
                string id = Identity(source);
                if (!(id.Contains("medieval") || id.Contains("wall_400") || id.Contains("wooden_bollard") || id.Contains("worn_wooden_beam"))) continue;
                if (!rect.Contains(new Vector2(source.bounds.center.x, -source.bounds.center.z))) continue;
                var copy = new GameObject(source.name); copy.transform.SetParent(group, false);
                copy.transform.position = source.transform.position - pivot + group.position; copy.transform.rotation = source.transform.rotation; copy.transform.localScale = source.transform.lossyScale;
                copy.AddComponent<MeshFilter>().sharedMesh = source.GetComponent<MeshFilter>().sharedMesh;
                copy.AddComponent<MeshRenderer>().sharedMaterials = source.sharedMaterials;
                copy.AddComponent<MeshCollider>().sharedMesh = source.GetComponent<MeshFilter>().sharedMesh;
            }
            group.rotation = Quaternion.Euler(0,-s.Item4,0); group.localScale *= s.Item5;
        }
    }

    public static void BuildRoads()
    {
        if (data == null) data = JsonUtility.FromJson<Data>(File.ReadAllText(Root + "ElderboomPortData.json"));
        var material = new Material(Shader.Find("HDRP/Lit"));
        material.SetTexture("_BaseColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/GameAssets/Roads/albedo_4k.jpg"));
        material.SetColor("_BaseColor", new Color(.8f,.78f,.72f)); material.SetFloat("_Smoothness", .08f);
        material.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/GameAssets/Roads/normal_gl_2k.png")); material.EnableKeyword("_NORMALMAP");
        const string matPath = "Assets/Materials/Elderboom_Authored_Roads.mat";
        var old = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (old != null) { EditorUtility.CopySerialized(material,old); UnityEngine.Object.DestroyImmediate(material); material=old; } else AssetDatabase.CreateAsset(material,matPath);
        foreach (var route in data.routes)
        {
            var vertices = new Vector3[route.points.Length*2]; var uv=new Vector2[vertices.Length]; var triangles = new int[(route.points.Length-1)*6]; float length=0;
            for(int i=0;i<route.points.Length;i++)
            {
                var p=route.points[i]; var before=route.points[Math.Max(0,i-1)]; var after=route.points[Math.Min(route.points.Length-1,i+1)];
                Vector3 direction=Map(after.x-before.x,0,after.z-before.z).normalized; var side=Vector3.Cross(Vector3.up,direction)*p.width*.5f;
                Vector3 center=Map(p.x,1.014f,p.z); vertices[2*i]=center-side; vertices[2*i+1]=center+side;
                if(i>0) length+=Vector3.Distance(center,Map(before.x,1.014f,before.z));
                uv[2*i]=new Vector2(0,length/2.16f); uv[2*i+1]=new Vector2(p.width/2.16f,length/2.16f);
                if(i<route.points.Length-1) { int k=i*6,a=i*2; triangles[k]=a;triangles[k+1]=a+2;triangles[k+2]=a+1;triangles[k+3]=a+1;triangles[k+4]=a+2;triangles[k+5]=a+3; }
            }
            var mesh=new Mesh{name=route.id}; mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();
            string meshPath="Assets/Resources/Elderboom/Road_"+route.id+".asset"; AssetDatabase.DeleteAsset(meshPath); AssetDatabase.CreateAsset(mesh,meshPath);
            var go=new GameObject("Road_"+route.id);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;
        }
    }

    public static void BakeNavigation()
    {
        var sources = new List<NavMeshBuildSource>();
        foreach(var collider in UnityEngine.Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None))
        {
            if(collider.sharedMesh==null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
            sources.Add(new NavMeshBuildSource{shape=NavMeshBuildSourceShape.Mesh, sourceObject=collider.sharedMesh, transform=collider.transform.localToWorldMatrix, area=0});
        }
        var settings=NavMesh.GetSettingsByIndex(0); settings.agentRadius=.4f;settings.agentHeight=1.8f;settings.agentClimb=.3f;settings.agentSlope=46;
        foreach(var box in UnityEngine.Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None))
            if(box.enabled && box.gameObject.activeInHierarchy && box.GetComponent<ElderboomGate>()==null)
                sources.Add(new NavMeshBuildSource{shape=NavMeshBuildSourceShape.Box,transform=box.transform.localToWorldMatrix*Matrix4x4.Translate(box.center),size=box.size,area=0});
        var nav=NavMeshBuilder.BuildNavMeshData(settings,sources,new Bounds(new Vector3(5,2,0),new Vector3(155,22,155)),Vector3.zero,Quaternion.identity);
        if(nav==null) throw new InvalidOperationException("Elderboom navigation bake failed.");
        const string path="Assets/Resources/Elderboom/Navigation.asset";AssetDatabase.DeleteAsset(path);AssetDatabase.CreateAsset(nav,path);
        var holder=new GameObject("Elderboom_Navigation"); holder.AddComponent<ElderboomNavigation>().data=nav;
        var instance=NavMesh.AddNavMeshData(nav);
        var player=UnityEngine.Object.FindAnyObjectByType<HDRPSlicePlayer>();
        if(player!=null && NavMesh.SamplePosition(player.transform.position,out var playerHit,2f,NavMesh.AllAreas))player.transform.position=playerHit.position+Vector3.up*.03f;
        int connected=0;
        foreach(var marker in UnityEngine.Object.FindObjectsByType<ElderboomSpawnPoint>(FindObjectsSortMode.None))
        {
            if(NavMesh.SamplePosition(marker.transform.position+Vector3.up,out var hit,5f,NavMesh.AllAreas))
            {
                marker.transform.position=hit.position;
                var pathCheck=new NavMeshPath();
                if(player!=null && NavMesh.CalculatePath(hit.position,player.transform.position,NavMesh.AllAreas,pathCheck) && pathCheck.status==NavMeshPathStatus.PathComplete)connected++;
                else UnityEngine.Object.DestroyImmediate(marker.gameObject);
            }
            else UnityEngine.Object.DestroyImmediate(marker.gameObject);
        }
        instance.Remove();
        if(connected<4)throw new InvalidOperationException("Fewer than four reachable spawn points: "+connected);
        Debug.Log($"ELDERBOOM_NAVIGATION: {sources.Count} collision sources baked.");
    }
}
