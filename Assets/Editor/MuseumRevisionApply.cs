using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

// [InitializeOnLoad] removed 31 Oct 2026: the saved scene is the source of truth.
// Flag files no longer trigger rebuilds, builds or Editor exits when Unity opens.
public static class MuseumRevisionApply
{
    const string Request = "Temp/MuseumRevision.ready";
    const string ScenePath = "Assets/Scenes/Museum_First_Map_HDRP.unity";
    const string Model = "Assets/GameAssets/Museum/Museum_Map.gltf";
    const string LayoutPath = "Assets/GameAssets/Museum/Museum_Layout.json";
    [Serializable] class PortalFile { public Portal[] portals; }
    [Serializable] class Portal { public string name; public float x,y,width,height,depth; }
    [Serializable] class Layout { public Marker[] markers; }
    [Serializable] class Marker { public string name,role; public float[] position; }
    [Serializable] class DoorResult { public string name; public bool clear,floor; public string obstruction; }
    [Serializable] class RevisionReport { public string status,backup,scene; public int meshColliders; public DoorResult[] doors; public bool materialsValid; }
    static bool busy;
    static MuseumRevisionApply() { EditorApplication.update += Tick; }

    static void Tick()
    {
        if (busy || !File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.isPlaying = false; return; }
        busy = true;
        try { Apply(); File.Delete(Request); }
        catch (Exception e) { File.WriteAllText("Logs/museum_revision_error.txt",e.ToString()); Debug.LogException(e); File.Move(Request, Request + ".failed"); }
        finally { busy = false; }
    }

    [MenuItem("Museum/Apply Layout and Dome Revision")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before applying the architectural revision.");
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
        Directory.CreateDirectory("Assets/Scenes/Backups");
        string backup = "Assets/Scenes/Backups/Museum_Before_Layout_Dome_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".unity";
        EditorSceneManager.SaveScene(scene,backup,true);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
        if (prefab == null) throw new InvalidOperationException("Revised museum prefab is unavailable.");
        var previous = scene.GetRootGameObjects().FirstOrDefault(x => x.name == "Museum_Architecture");
        if (previous == null) throw new InvalidOperationException("Expected museum environment was not found.");
        UnityEngine.Object.DestroyImmediate(previous);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
        instance.name = "Museum_Architecture";
        int count = 0;
        foreach (var node in instance.GetComponentsInChildren<Transform>(true))
        {
            node.gameObject.isStatic = true;
            if (!node.name.StartsWith("COLLIDER_",StringComparison.Ordinal)) continue;
            foreach (var filter in node.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.GetComponent<MeshCollider>() != null) continue;
                var collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh; collider.convex = false;
                collider.cookingOptions = MeshColliderCookingOptions.EnableMeshCleaning | MeshColliderCookingOptions.WeldColocatedVertices | MeshColliderCookingOptions.UseFastMidphase | MeshColliderCookingOptions.CookForFasterSimulation;
                count++;
            }
            foreach (var renderer in node.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        }
        RefreshRuntimeLayout();
        Physics.SyncTransforms();
        var cc = GameObject.Find("Soph_Player").GetComponent<CharacterController>();
        bool wasEnabled = cc.enabled; cc.enabled = false;
        bool backfaces = Physics.queriesHitBackfaces; Physics.queriesHitBackfaces = true;
        var portals = JsonUtility.FromJson<PortalFile>(File.ReadAllText("Assets/GameAssets/Museum/Museum_Portal_Checks.json"));
        var checks = portals.portals.GroupBy(x => x.name.Replace(" Recheck", "")).Select(g => g.Last()).Select(door =>
        {
            Vector3 a = new Vector3(-door.x,1f,-door.y+door.depth/2f+.08f);
            Vector3 b = new Vector3(-door.x,1f,-door.y-door.depth/2f-.08f);
            bool blocked = Physics.SphereCast(a,.2f,(b-a).normalized,out RaycastHit obstruction,Vector3.Distance(a,b),~0,QueryTriggerInteraction.Ignore);
            bool floor = Physics.Raycast(new Vector3(-door.x,.3f,-door.y),Vector3.down,out _, .8f);
            return new DoorResult { name=door.name,clear=!blocked,floor=floor,obstruction=blocked?obstruction.collider.name:"" };
        }).ToArray();
        Physics.queriesHitBackfaces = backfaces; cc.enabled = wasEnabled;
        bool materialOK = instance.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).SelectMany(r => r.sharedMaterials).All(m => m != null && m.shader != null && !m.shader.name.Contains("InternalErrorShader"));
        var report = new RevisionReport {status="applied",backup=backup,scene=ScenePath,meshColliders=count,doors=checks,materialsValid=materialOK};
        File.WriteAllText("Logs/museum_revision_validation.json",JsonUtility.ToJson(report,true));
        if (checks.Any(x => !x.clear || !x.floor) || !materialOK) throw new InvalidOperationException("Revision validation failed. See Logs/museum_revision_validation.json");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene,ScenePath);
        AssetDatabase.SaveAssets();
        File.WriteAllText("Logs/museum_revision_result.json", "{\"status\":\"applied\",\"meshColliders\":" + count + ",\"backup\":\"" + backup + "\",\"scene\":\""+ScenePath+"\"}");
        Debug.Log("MUSEUM_LAYOUT_DOME_REVISION_APPLIED " + count + " collision zones. " + backup);
        EditorApplication.delayCall += CaptureReview;
    }

    static Vector3 FromBlender(float[] position) => new Vector3(-position[0], position[2], -position[1]);

    static void DestroyGeneratedRoot(string name)
    {
        var root = GameObject.Find(name);
        if (root != null) UnityEngine.Object.DestroyImmediate(root);
    }

    static Light Point(string name, Vector3 position, float candela, float range, Color color, Transform parent)
    {
        var light = new GameObject(name).AddComponent<Light>();
        light.transform.SetParent(parent, false);
        light.transform.position = position;
        light.type = LightType.Point;
        light.intensity = candela;
        light.lightUnit = LightUnit.Candela;
        light.range = range;
        light.color = color;
        light.shadows = LightShadows.None;
        return light;
    }

    // Markers, player spawn and practical lights have world coordinates. They must be recreated
    // whenever an architectural scale revision replaces the environment mesh.
    static void RefreshRuntimeLayout()
    {
        var layout = JsonUtility.FromJson<Layout>(File.ReadAllText(LayoutPath));
        DestroyGeneratedRoot("Museum_Gameplay_Markers");
        DestroyGeneratedRoot("Museum_Interior_Lighting");
        var markers = new GameObject("Museum_Gameplay_Markers");
        if (layout.markers != null)
        {
            foreach (var marker in layout.markers)
            {
                if (marker.position == null || marker.position.Length < 3) continue;
                var point = new GameObject(marker.name);
                point.transform.SetParent(markers.transform, false);
                point.transform.position = FromBlender(marker.position);
            }
        }
        var spawn = layout.markers == null ? null : layout.markers.FirstOrDefault(marker => marker.role == "player_spawn");
        var player = GameObject.Find("Soph_Player");
        if (player != null && spawn != null && spawn.position != null && spawn.position.Length >= 3)
        {
            var controller = player.GetComponent<CharacterController>();
            bool active = controller != null && controller.enabled;
            if (controller != null) controller.enabled = false;
            player.transform.position = FromBlender(spawn.position);
            player.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            if (controller != null) controller.enabled = active;
            var camera = player.GetComponentInChildren<Camera>();
            if (camera != null) camera.farClipPlane = 500f;
        }
        var root = new GameObject("Museum_Interior_Lighting").transform;
        Color warm = new Color(1f, .83f, .64f);
        for (int x = -66; x <= 66; x += 12)
        {
            Point("North_Concourse_Practical", new Vector3(-x, 8.6f, -60f), 1000f, 15f, warm, root);
            Point("South_Concourse_Practical", new Vector3(-x, 8.6f, 60f), 1000f, 15f, warm, root);
        }
        foreach (float galleryX in new[] { -45f, 0f, 45f })
            for (int galleryY = 75; galleryY <= 135; galleryY += 18)
                Point("North_Gallery_Practical", new Vector3(-galleryX, 7.2f, -galleryY), 1300f, 18f, warm, root);
        foreach (float galleryX in new[] { -45f, 45f })
            for (int galleryY = -75; galleryY >= -135; galleryY -= 18)
                Point("South_Gallery_Practical", new Vector3(-galleryX, 7.2f, -galleryY), 1300f, 18f, warm, root);
        foreach (float galleryY in new[] { -24f, 0f, 24f })
            Point("Atrium_Soft_Fill", new Vector3(0f, 17f, -galleryY), 3800f, 34f, new Color(1f, .90f, .76f), root);
        Point("Reception_Practical", new Vector3(0f, 8.2f, 75f), 1400f, 18f, warm, root);
    }

    [MenuItem("Museum/Capture Layout and Dome Review")]
    public static void CaptureReview()
    {
        if (EditorApplication.isPlaying) return;
        Camera source = GameObject.Find("Soph_Player").GetComponentInChildren<Camera>();
        var go = UnityEngine.Object.Instantiate(source.gameObject);
        go.name = "Museum_Review_Camera_Temporary";
        go.transform.SetParent(null,false);
        foreach (var child in go.GetComponentsInChildren<Transform>(true).Where(x => x != go.transform).ToArray())
            if (child != null) UnityEngine.Object.DestroyImmediate(child.gameObject);
        var camera = go.GetComponent<Camera>(); camera.enabled = false; camera.fieldOfView = 85f;
        Directory.CreateDirectory("Logs/MuseumReview");
        try
        {
            Capture(camera, "Atrium_Dome",new Vector3(0f,1.7f,10f),new Vector3(0f,12f,0f));
            Capture(camera, "Sculpture_Layout",new Vector3(22f,1.7f,-31f),new Vector3(22f,1.8f,-49f));
            Capture(camera, "Science_Layout",new Vector3(-22f,1.7f,-28f),new Vector3(-22f,1.8f,-48f));
            Capture(camera, "Carriage_Layout",new Vector3(0f,1.7f,-28f),new Vector3(0f,1.7f,-48f));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
        Debug.Log("MUSEUM_REVIEW_CAPTURES_COMPLETE Logs/MuseumReview");
    }

    static void Capture(Camera camera,string name,Vector3 position,Vector3 target)
    {
        camera.transform.position = position; camera.transform.LookAt(target);
        var rt = new RenderTexture(1280,800,24,RenderTextureFormat.ARGB32) { name = name };
        rt.Create();
        var request = new RenderPipeline.StandardRequest { destination = rt };
        for (int i=0;i<3;i++) RenderPipeline.SubmitRenderRequest(camera,request);
        var old = RenderTexture.active; RenderTexture.active = rt;
        var image = new Texture2D(1280,800,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,1280,800),0,0);image.Apply();
        File.WriteAllBytes("Logs/MuseumReview/"+name+".png",image.EncodeToPNG());
        RenderTexture.active = old;
        UnityEngine.Object.DestroyImmediate(image);rt.Release();UnityEngine.Object.DestroyImmediate(rt);
    }
}
