using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Logs/claude_range.txt ("dump" | "build") -> Assets/Scenes/Elderboom_TestRange.unity: the game scene without the map,
// a flat range with targets, waves off (ElderboomTestRange). Result in Logs/claude_range_result.txt.
static class ElderboomClaudeRange
{
    const string Flag = "Logs/claude_range.txt", Result = "Logs/claude_range_result.txt";
    const string Source = "Assets/Scenes/Elderboom_Unity_Port.unity", Target = "Assets/Scenes/Elderboom_TestRange.unity";
    static double next;
    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 1.5;
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        string mode = File.ReadAllText(Flag).Trim(); File.Delete(Flag);
        string r; try { r = Run(mode); } catch (System.Exception e) { r = "FAIL " + e; }
        File.WriteAllText(Result, r);
    }
    static string Run(string mode)
    {
        var sb = new StringBuilder();
        var scene = EditorSceneManager.GetActiveScene();
        sb.AppendLine("active " + scene.path);
        foreach (var g in scene.GetRootGameObjects())
        {
            var comps = string.Join(",", g.GetComponents<Component>().Where(c => c && !(c is Transform)).Select(c => c.GetType().Name));
            int rcount = g.GetComponentsInChildren<Renderer>(true).Length;
            sb.AppendLine(g.name + " active=" + g.activeSelf + " pos=" + g.transform.position + " renderers=" + rcount + " C:" + comps);
        }
        if (mode.StartsWith("build")) sb.Append(Build());
        sb.AppendLine("DONE");
        return sb.ToString();
    }
    static readonly string[] Keep = { "Elderboom_HDRP_Night_Volume", "Moonlight", "FPS_Player", "Elderboom_Game_Director" };
    static Material Mat(string name, Color c, float smooth = .3f)
    {
        string path = "Assets/Scenes/TestRange/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("HDRP/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); EditorUtility.SetDirty(m);
        return m;
    }
    static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material m, PrimitiveType type = PrimitiveType.Cube)
    {
        var g = GameObject.CreatePrimitive(type); g.name = name; g.transform.SetParent(parent, false);
        g.transform.localPosition = pos; g.transform.localScale = size; g.GetComponent<Renderer>().sharedMaterial = m; return g;
    }
    static string Build()
    {
        var sb = new StringBuilder();
        if (EditorSceneManager.GetActiveScene().isDirty) EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        string prev = EditorSceneManager.GetActiveScene().path;
        AssetDatabase.DeleteAsset(Target);
        if (!AssetDatabase.CopyAsset(Source, Target)) return "copy failed\n";
        if (!AssetDatabase.IsValidFolder("Assets/Scenes/TestRange")) AssetDatabase.CreateFolder("Assets/Scenes", "TestRange");
        var scene = EditorSceneManager.OpenScene(Target, OpenSceneMode.Single);
        int removed = 0;
        foreach (var g in scene.GetRootGameObjects()) if (!Keep.Contains(g.name)) { Object.DestroyImmediate(g); removed++; }
        sb.AppendLine("removed roots " + removed);
        var root = new GameObject("TestRange").transform;
        root.gameObject.AddComponent<ElderboomTestRange>();
        var floorM = Mat("Range_Floor", new Color(.32f, .33f, .34f), .15f);
        var wallM = Mat("Range_Berm", new Color(.25f, .22f, .19f), .05f);
        var boardM = Mat("Range_Board", new Color(.85f, .82f, .74f), .1f);
        var ringM = Mat("Range_Ring", new Color(.75f, .05f, .04f), .2f);
        var postM = Mat("Range_Post", new Color(.2f, .14f, .09f), .1f);
        var steelM = Mat("Range_Steel", new Color(.55f, .56f, .58f), .55f);
        var lineM = Mat("Range_Line", new Color(.9f, .75f, .1f), .2f);
        Box("Floor", root, new Vector3(0, -.5f, 30), new Vector3(60, 1, 100), floorM);
        Box("Backstop", root, new Vector3(0, 3, 62), new Vector3(60, 6, 2), wallM);
        Box("Wall_L", root, new Vector3(-20, 1.5f, 30), new Vector3(1, 3, 70), wallM);
        Box("Wall_R", root, new Vector3(20, 1.5f, 30), new Vector3(1, 3, 70), wallM);
        Box("Firing_Line", root, new Vector3(0, .005f, 1.5f), new Vector3(30, .01f, .15f), lineM);
        float[] dist = { 5, 10, 15, 25, 40 };
        for (int i = 0; i < dist.Length; i++)
        {
            float x = (i - 2) * 4.5f, z = dist[i];
            var t = new GameObject("Target_" + dist[i] + "m").transform; t.SetParent(root, false); t.localPosition = new Vector3(x, 0, z);
            Box("Post_L", t, new Vector3(-.45f, .6f, .05f), new Vector3(.08f, 1.2f, .08f), postM);
            Box("Post_R", t, new Vector3(.45f, .6f, .05f), new Vector3(.08f, 1.2f, .08f), postM);
            Box("Board", t, new Vector3(0, 1.5f, 0), new Vector3(1f, 1.4f, .04f), boardM);
            var ring = Box("Bull", t, new Vector3(0, 1.6f, -.025f), new Vector3(.3f, .006f, .3f), ringM, PrimitiveType.Cylinder);
            ring.transform.localRotation = Quaternion.Euler(90, 0, 0); Object.DestroyImmediate(ring.GetComponent<Collider>());
            var lamp = new GameObject("Light").AddComponent<Light>(); lamp.transform.SetParent(t, false);
            lamp.type = LightType.Spot; lamp.transform.localPosition = new Vector3(0, 5, -3); lamp.transform.localRotation = Quaternion.Euler(50, 0, 0);
            lamp.spotAngle = 60; lamp.range = 14; lamp.intensity = 4000; lamp.color = new Color(1f, .93f, .82f); lamp.shadows = LightShadows.Soft;
        }
        for (int i = 0; i < 6; i++)
        {
            var p = Box("Steel_" + i, root, new Vector3(-7.5f + i * 3f, 1.0f, 20), new Vector3(.4f, .4f, .4f), steelM, PrimitiveType.Sphere);
            Box("Stand_" + i, root, new Vector3(-7.5f + i * 3f, .4f, 20), new Vector3(.06f, .8f, .06f), postM);
        }
        var over = new GameObject("Light_FiringLine").AddComponent<Light>(); over.transform.SetParent(root, false);
        over.type = LightType.Point; over.transform.localPosition = new Vector3(0, 4, 0); over.range = 12; over.intensity = 3000; over.color = new Color(1f, .95f, .85f);
        var player = GameObject.Find("FPS_Player");
        if (player) { player.transform.SetPositionAndRotation(new Vector3(0, 1.06f, -1), Quaternion.identity); sb.AppendLine("player placed"); }
        EditorSceneManager.SaveScene(scene);
        sb.AppendLine("saved " + Target);
        EditorSceneManager.OpenScene(prev, OpenSceneMode.Single);
        return sb.ToString();
    }
}
