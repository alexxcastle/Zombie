using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Editor automation for the block map (Logs/claude_blocks.txt, result in Logs/claude_blocks_result.txt).
// "survey": what is in the scene (gameplay objects, building footprints, ground, roads) -> also
// Logs/claude_blocks_survey.json for planning.
[InitializeOnLoad]
static partial class ElderboomClaudeBlocks
{
    const string Flag = "Logs/claude_blocks.txt", Result = "Logs/claude_blocks_result.txt";
    static double nextPoll;
    static ElderboomClaudeBlocks() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
        string mode = File.ReadAllText(Flag).Trim(); File.Delete(Flag);
        string result;
        try
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name != "Elderboom_Unity_Port")
            {
                if (scene.isDirty) { File.WriteAllText(Result, "FAIL unsaved scene " + scene.name); return; }
                EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
            }
            result = Run(mode);
        }
        catch (System.Exception e) { result = "FAIL " + e; }
        File.WriteAllText(Result, result);
    }

    static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => "(" + F(v.x) + "," + F(v.y) + "," + F(v.z) + ")";
    static string PathOf(Transform t) { string p = t.name; for (var x = t.parent; x != null; x = x.parent) p = x.name + "/" + p; return p; }

    static string Run(string mode)
    {
        if (mode == "landmarks-build") return BuildRecoveredLandmarks();
        if (mode == "landmarks-review") return CaptureRecoveredLandmarks();
        if (mode == "landmarks-finish") return FinishRecoveredLandmarks();
        if (mode == "landmarks-playtest") return TestRecoveredLandmarks();
        if (mode == "horse-assembly") { ElderboomHorseAssemblyBuilder.Build(); return "Horse salvage objective saved and photographed"; }
        if (mode == "horse-playtest") { ElderboomHorseAssemblyValidation.Run(); return "Horse salvage Play Mode validation started"; }
        if (mode == "access-review")
        {
            if (EditorSceneManager.GetActiveScene().isDirty) return "FAIL unsaved scene; access review deferred";
            ElderboomAccessLayoutBuilder.Rebuild();
            ElderboomAccessLayoutBuilder.Validate();
            CaptureAccessReview();
            return "Access layout rebuilt and validated";
        }
        if (mode == "access-playtest")
        {
            if (EditorSceneManager.GetActiveScene().isDirty) return "FAIL unsaved scene";
            ElderboomMapValidation.Run();
            return "Map playtest started";
        }
        if (mode.StartsWith("survey")) return Survey();
        return RunBuild(mode);
    }

    static string Survey()
    {
        var sb = new StringBuilder();
        var json = new StringBuilder("{\n");
        sb.AppendLine("-- roots");
        foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            sb.AppendLine("  " + go.name + " " + V(go.transform.position) + " kids " + go.transform.childCount + (go.activeSelf ? "" : " (inactive)"));
        void List<T>(string title, System.Func<T, string> line) where T : Component
        {
            var items = Object.FindObjectsByType<T>(FindObjectsInactive.Include);
            sb.AppendLine("-- " + title + " " + items.Length);
            foreach (var c in items) sb.AppendLine("  " + PathOf(c.transform) + " " + V(c.transform.position) + " " + line(c) + (c.gameObject.activeInHierarchy ? "" : " (inactive)"));
        }
        List<ElderboomGate>("gates", g => "area=" + g.areaName + " cost=" + g.cost + " scale " + V(g.transform.lossyScale) + " rotY " + F(g.transform.eulerAngles.y));
        List<ElderboomArea>("areas", a => "name=" + a.areaName + " start=" + a.unlockedAtStart + " size " + V(a.size));
        List<ElderboomSpawnPoint>("spawns", s => "area=" + s.area);
        List<ElderboomStation>("stations", s => "label=" + s.label + " cat=" + s.category + " cost=" + s.cost + " rotY " + F(s.transform.eulerAngles.y));
        List<ElderboomTrigger>("triggers", t => "");
        List<HDRPSlicePlayer>("player", p => "");
        List<ElderboomMount>("horse", m => "");
        List<ElderboomNavigation>("navigation", n => n.data != null ? AssetDatabase.GetAssetPath(n.data) : "no data");
        List<ElderboomSpecialPickup>("special pickups", p => "");
        List<ElderboomPowerUp>("powerups", p => "");

        // Spawn point list names from the game component.
        var game = Object.FindAnyObjectByType<ElderboomGame>();
        if (game != null)
        {
            var f = typeof(ElderboomGame).GetField("spawnPoints", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            sb.AppendLine("-- game.spawnPoints field " + (f != null ? f.FieldType.Name : "missing"));
            if (f != null && f.GetValue(game) is System.Collections.IList list) sb.AppendLine("   count " + list.Count);
        }

        // Ground
        foreach (var name in new[] { "Elderboom_Playable_Ground", "Landscape" })
        {
            var g = GameObject.Find(name);
            if (g == null) { sb.AppendLine("-- " + name + " missing"); continue; }
            var r = g.GetComponentsInChildren<Renderer>();
            var b = new Bounds(g.transform.position, Vector3.zero); foreach (var x in r) b.Encapsulate(x.bounds);
            sb.AppendLine("-- " + name + " path " + PathOf(g.transform) + " renderers " + r.Length + " colliders " + g.GetComponentsInChildren<Collider>().Length + " bounds " + V(b.min) + ".." + V(b.max));
            int n = 0; foreach (Transform c in g.transform) { if (n++ >= 6) break; sb.AppendLine("   " + c.name + " " + V(c.position)); }
        }
        var terrain = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude).Where(t => t.gameObject.layer != 30).OrderByDescending(t => t.terrainData.size.x).First();
        sb.AppendLine("-- terrain " + terrain.name + " pos " + V(terrain.transform.position) + " size " + V(terrain.terrainData.size) + " res " + terrain.terrainData.heightmapResolution);
        sb.Append("   heights (dx,dz from village, step 20 m, +z rows top):\n");
        for (int dz = 120; dz >= -120; dz -= 20)
        {
            sb.Append("   ");
            for (int dx = -120; dx <= 120; dx += 20) sb.Append(F(terrain.SampleHeight(new Vector3(5.7f + dx, 0, -.7f + dz)) + terrain.transform.position.y).PadLeft(6));
            sb.AppendLine();
        }

        // Building footprints: big renderers (walls, houses), plus every collider box.
        json.Append("\"buildings\":[");
        int count = 0;
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
        {
            var b = r.bounds; string n = r.name;
            if (n.Contains("Ground") || n.StartsWith("Road_") || n.Contains("SkySphere") || n.Contains("BlackAlder") || r.GetComponentInParent<Terrain>() != null) continue;
            if (b.size.x > 150 || b.size.z > 150) continue;
            if (b.size.y < 1.2f || b.size.x * b.size.z < 1.5f) continue;
            if (count++ > 0) json.Append(",");
            json.Append("[" + F(b.min.x) + "," + F(b.min.z) + "," + F(b.max.x) + "," + F(b.max.z) + "," + F(b.min.y) + "," + F(b.max.y) + ",\"" + n.Replace("\"", "") + "\"]");
        }
        json.Append("],\n\"trees\":[");
        count = 0;
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
        {
            if (!r.name.Contains("BlackAlder")) continue;
            if (count++ > 0) json.Append(",");
            json.Append("[" + F(r.transform.position.x) + "," + F(r.transform.position.z) + "]");
        }
        json.Append("],\n\"roads\":[");
        count = 0;
        foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude))
        {
            if (!mf.name.StartsWith("Road_") || mf.sharedMesh == null) continue;
            var v = mf.sharedMesh.vertices; var pts = new List<string>();
            int across = 5; // rows of 5 after the road rebuild
            for (int i = 0; i < v.Length; i += across * 6) { var w = mf.transform.TransformPoint(v[Mathf.Min(i + 2, v.Length - 1)]); pts.Add("[" + F(w.x) + "," + F(w.z) + "]"); }
            if (count++ > 0) json.Append(",");
            json.Append("{\"name\":\"" + mf.name + "\",\"pts\":[" + string.Join(",", pts) + "]}");
        }
        json.Append("],\n\"gameplay\":[");
        count = 0;
        void G(string kind, Component c) { if (count++ > 0) json.Append(","); json.Append("[\"" + kind + "\"," + F(c.transform.position.x) + "," + F(c.transform.position.z) + ",\"" + c.name + "\"]"); }
        foreach (var c in Object.FindObjectsByType<ElderboomGate>(FindObjectsInactive.Include)) G("gate", c);
        foreach (var c in Object.FindObjectsByType<ElderboomStation>(FindObjectsInactive.Include)) G("station", c);
        foreach (var c in Object.FindObjectsByType<ElderboomSpawnPoint>(FindObjectsInactive.Include)) G("spawn", c);
        foreach (var c in Object.FindObjectsByType<HDRPSlicePlayer>(FindObjectsInactive.Include)) G("player", c);
        foreach (var c in Object.FindObjectsByType<ElderboomMount>(FindObjectsInactive.Include)) G("horse", c);
        json.Append("]\n}\n");
        File.WriteAllText("Logs/claude_blocks_survey.json", json.ToString());

        // Name census of the village children.
        var port = GameObject.Find("Elderboom_Village_Port");
        if (port != null)
        {
            var census = new Dictionary<string, int>();
            foreach (Transform c in port.transform)
            {
                string key = new string(c.name.TakeWhile(ch => !char.IsDigit(ch)).ToArray()).TrimEnd('_', ' ');
                census[key] = census.TryGetValue(key, out var k) ? k + 1 : 1;
            }
            sb.AppendLine("-- village census");
            foreach (var kv in census.OrderByDescending(k => k.Value).Take(60)) sb.AppendLine("  " + kv.Value + " " + kv.Key);
        }
        var colliders = Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude);
        sb.AppendLine("-- colliders " + string.Join(", ", colliders.GroupBy(c => c.GetType().Name).Select(g => g.Key + " " + g.Count())));
        sb.AppendLine("DONE");
        return sb.ToString();
    }
}
