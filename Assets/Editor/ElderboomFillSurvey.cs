using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

// Logs/claude_fill.txt -> "survey": 2 m grid over the playable valley (areas, walkable, clearance to the nearest
// built thing, slope, dominant ground layer) + spawns, gates, big existing structures -> Logs/Fill/survey.csv/.txt,
// and an orthographic top picture Logs/Fill/top.jpg (same frame as the grid) to choose where the new architecture goes.
static partial class ElderboomFill
{
    const string Flag = "Logs/claude_fill.txt", Result = "Logs/claude_fill_result.txt", Dir = "Logs/Fill";
    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Poll; }
    static double next;
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        string mode = File.ReadAllText(Flag).Trim(); File.Delete(Flag);
        try { File.WriteAllText(Result, Run(mode)); } catch (System.Exception e) { File.WriteAllText(Result, "FAIL " + e); }
    }
    static Terrain Valley() => Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Where(t => t.gameObject.layer != 30).OrderByDescending(t => t.terrainData.size.x).First();
    static string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);

    static string Run(string mode)
    {
        const string scenePath = "Assets/Scenes/Elderboom_Unity_Port.unity";
        if (EditorSceneManager.GetActiveScene().path != scenePath) EditorSceneManager.OpenScene(scenePath);
        Directory.CreateDirectory(Dir);
        var nav = Object.FindAnyObjectByType<ElderboomNavigation>(); if (nav && nav.data) { NavMesh.RemoveAllNavMeshData(); NavMesh.AddNavMeshData(nav.data); }
        if (mode.StartsWith("survey")) return Survey();
        if (mode.StartsWith("place")) return Place(mode);
        if (mode.StartsWith("shots")) return Shots(mode);
        if (mode.StartsWith("fixmats")) return FixMaterials();
        return "FAIL unknown " + mode;
    }

    static Rect MapRect(ElderboomArea[] areas)
    {
        float x0 = 1e9f, z0 = 1e9f, x1 = -1e9f, z1 = -1e9f;
        foreach (var a in areas)
        {
            IEnumerable<Vector2> pts = a.polygon != null && a.polygon.Length >= 3 ? a.polygon : new[] { -1, 1 }.SelectMany(i => new[] { -1, 1 }.Select(j => { var w = a.transform.TransformPoint(new Vector3(i * a.size.x * .5f, 0, j * a.size.z * .5f)); return new Vector2(w.x, w.z); }));
            foreach (var p in pts) { x0 = Mathf.Min(x0, p.x); z0 = Mathf.Min(z0, p.y); x1 = Mathf.Max(x1, p.x); z1 = Mathf.Max(z1, p.y); }
        }
        return Rect.MinMaxRect(Mathf.Floor(x0 / 2) * 2, Mathf.Floor(z0 / 2) * 2, Mathf.Ceil(x1 / 2) * 2, Mathf.Ceil(z1 / 2) * 2);
    }

    static string Survey()
    {
        var terrain = Valley(); var td = terrain.terrainData;
        var areas = Object.FindObjectsByType<ElderboomArea>(FindObjectsSortMode.None);
        var r = MapRect(areas);
        int mask = ~((1 << 30) | (1 << 2));
        var layers = td.terrainLayers; var alpha = td.GetAlphamaps(0, 0, td.alphamapWidth, td.alphamapHeight);
        var csv = new StringBuilder("x,z,y,area,walk,clear,slope,layer\n");
        int cells = 0;
        for (float z = r.yMin + 1; z < r.yMax; z += 2)
            for (float x = r.xMin + 1; x < r.xMax; x += 2)
            {
                var p = new Vector3(x, 0, z); p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
                var area = areas.FirstOrDefault(a => a.Contains(p)); if (area == null) continue;
                bool walk = NavMesh.SamplePosition(p, out var h, 1.0f, NavMesh.AllAreas) && Mathf.Abs(h.position.y - p.y) < 1f;
                float clear = 15f;
                for (float rad = 1f; rad <= 15f; rad += 1f)
                    if (Physics.CheckCapsule(p + Vector3.up * (rad + .3f), p + Vector3.up * (rad + 4f), rad, mask, QueryTriggerInteraction.Ignore)) { clear = rad - 1f; break; }
                var n = td.GetInterpolatedNormal((x - terrain.transform.position.x) / td.size.x, (z - terrain.transform.position.z) / td.size.z);
                int ax = Mathf.Clamp((int)((x - terrain.transform.position.x) / td.size.x * td.alphamapWidth), 0, td.alphamapWidth - 1), az = Mathf.Clamp((int)((z - terrain.transform.position.z) / td.size.z * td.alphamapHeight), 0, td.alphamapHeight - 1);
                int best = 0; for (int l = 1; l < layers.Length; l++) if (alpha[az, ax, l] > alpha[az, ax, best]) best = l;
                csv.Append(F(x)).Append(',').Append(F(z)).Append(',').Append(F(p.y)).Append(',').Append(area.areaName).Append(',').Append(walk ? 1 : 0).Append(',').Append(F(clear)).Append(',').Append(F(Vector3.Angle(n, Vector3.up))).Append(',').Append(best).Append('\n');
                cells++;
            }
        File.WriteAllText(Dir + "/survey.csv", csv.ToString());
        var sb = new StringBuilder();
        sb.AppendLine("rect " + r.xMin + " " + r.yMin + " " + r.xMax + " " + r.yMax + " cells " + cells);
        for (int l = 0; l < layers.Length; l++) sb.AppendLine("layer " + l + " " + layers[l].name + " " + (layers[l].diffuseTexture ? layers[l].diffuseTexture.name : ""));
        foreach (var s in Object.FindObjectsByType<ElderboomSpawnPoint>(FindObjectsSortMode.None)) sb.AppendLine("spawn " + s.name + " " + F(s.transform.position.x) + " " + F(s.transform.position.z));
        foreach (var g in Object.FindObjectsByType<ElderboomGate>(FindObjectsSortMode.None).Where(g => g.isActiveAndEnabled)) sb.AppendLine("gate " + g.name + " " + F(g.transform.position.x) + " " + F(g.transform.position.z));
        var pl = Object.FindAnyObjectByType<HDRPSlicePlayer>(); sb.AppendLine("player " + F(pl.transform.position.x) + " " + F(pl.transform.position.z));
        foreach (var a in areas) if (a.polygon != null) sb.AppendLine("area " + a.areaName + " " + string.Join(" ", a.polygon.Select(v => F(v.x) + ":" + F(v.y))));
        // Interactive / gameplay objects that must stay reachable.
        foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            var t = mb.GetType().Name;
            if (t.Contains("Station") || t.Contains("Altar") || t.Contains("Mount") || t.Contains("Perk") || t.Contains("Box") || t.Contains("Pickup") || t.Contains("Vendor") || t.Contains("Wall") && t.Contains("Buy"))
                sb.AppendLine("interact " + t + " " + mb.name + " " + F(mb.transform.position.x) + " " + F(mb.transform.position.z));
        }
        File.WriteAllText(Dir + "/survey.txt", sb.ToString());
        TopShot(r, Dir + "/top.jpg");
        return sb.ToString().Split('\n').Length + " lines, cells " + cells + "\n" + sb.ToString().Split('\n')[0];
    }

    static void TopShot(Rect r, string file)
    {
        int ppm = 8; int w = Mathf.RoundToInt(r.width * ppm), h = Mathf.RoundToInt(r.height * ppm);
        var go = new GameObject("FillTopCam"); var cam = go.AddComponent<Camera>(); go.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
        var light = new GameObject("FillSun"); var l = light.AddComponent<Light>(); l.type = LightType.Directional; light.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>(); l.intensity = 10f; light.transform.rotation = Quaternion.Euler(70, 30, 0);
        cam.orthographic = true; cam.orthographicSize = r.height * .5f; cam.aspect = r.width / r.height; cam.nearClipPlane = 1; cam.farClipPlane = 400; cam.cullingMask = ~(1 << 30);
        go.transform.SetPositionAndRotation(new Vector3(r.center.x, 200, r.center.y), Quaternion.Euler(90, 0, 0));
        var rt = new RenderTexture(w, h, 24); cam.targetTexture = rt; for (int i = 0; i < 4; i++) cam.Render();
        RenderTexture.active = rt; var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToJPG(88)); RenderTexture.active = null; cam.targetTexture = null;
        Object.DestroyImmediate(go); Object.DestroyImmediate(light); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
    }
}
