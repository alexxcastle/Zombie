using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Editor automation: Logs/claude_roads.txt rebuilds the road strips (Road_* in Elderboom_Unity_Port) so they lie on
// the ground everywhere. The authored strips were flat at one height with a vertex pair every few metres, so where
// the ground rises they sank under it (the brown, stretched ground texture showed through) and at crossings two
// strips at the same height flickered. Now: resampled every ~0.5 m along and in 4 steps across, each vertex set on
// the ground below + a small lift, a slightly different lift per road, anisotropic filtering on the road textures.
// Report in Logs/claude_roads_result.txt.
[InitializeOnLoad]
static class ElderboomClaudeRoadFix
{
    const string Flag = "Logs/claude_roads.txt", Result = "Logs/claude_roads_result.txt";
    static double nextPoll;

    static ElderboomClaudeRoadFix() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
        string mode = File.ReadAllText(Flag);
        File.Delete(Flag);
        if (mode.Contains("aniso")) { try { File.WriteAllText(Result, Aniso()); } catch (System.Exception e) { File.WriteAllText(Result, "FAIL " + e); } return; }
        if (mode.Contains("shot")) { try { File.WriteAllText(Result, Shot()); } catch (System.Exception e) { File.WriteAllText(Result, "FAIL " + e); } return; }
        try { File.WriteAllText(Result, Run()); }
        catch (System.Exception e) { File.WriteAllText(Result, "FAIL " + e); }
    }

    static bool IsGround(Collider c) =>
        c is TerrainCollider || c.name.IndexOf("Ground", System.StringComparison.OrdinalIgnoreCase) >= 0;

    // Highest ground surface under a point (ignores props, walls and roofs).
    static bool GroundAt(Vector3 p, out float y)
    {
        y = 0; bool found = false;
        foreach (var hit in Physics.RaycastAll(p + Vector3.up * 3f, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore))
            if (IsGround(hit.collider) && (!found || hit.point.y > y)) { y = hit.point.y; found = true; }
        return found;
    }

    // Anisotropic filtering forced on at every quality level (levels 0-1 had it off: textures seen at a grazing
    // angle, like a road under your feet, smeared into stretched stripes).
    static string Aniso()
    {
        var sb = new StringBuilder();
        int current = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            sb.AppendLine(QualitySettings.names[i] + ": " + QualitySettings.anisotropicFiltering + " -> ForceEnable");
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
        }
        QualitySettings.SetQualityLevel(current, false);
        AssetDatabase.SaveAssets();
        sb.AppendLine("DONE");
        return sb.ToString();
    }

    // Edit-mode picture along the longest road at walking height and at quality level 1 (the game's default).
    static string Shot()
    {
        var sb = new StringBuilder();
        MeshFilter road = null;
        foreach (var mf in Object.FindObjectsByType<MeshFilter>())
            if (mf.name.StartsWith("Road_") && mf.sharedMesh != null && (road == null || mf.sharedMesh.vertexCount > road.sharedMesh.vertexCount)) road = mf;
        if (road == null) return "FAIL no road";
        var v = road.sharedMesh.vertices;
        Vector3 a = road.transform.TransformPoint((v[0] + v[4]) * .5f), b = road.transform.TransformPoint((v[200] + v[204]) * .5f);
        int level = QualitySettings.GetQualityLevel();
        QualitySettings.SetQualityLevel(1, true);
        var go = new GameObject("RoadShotCam"); var cam = go.AddComponent<Camera>();
        go.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
        cam.fieldOfView = 60; cam.nearClipPlane = .05f; cam.farClipPlane = 500;
        go.transform.position = a + Vector3.up * 1.65f;
        go.transform.rotation = Quaternion.LookRotation((b - a).normalized + Vector3.down * .25f);
        var rt = new RenderTexture(1600, 900, 24);
        cam.targetTexture = rt;
        for (int i = 0; i < 3; i++) cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply();
        File.WriteAllBytes("Logs/road_shot.jpg", tex.EncodeToJPG(85));
        RenderTexture.active = null; cam.targetTexture = null;
        Object.DestroyImmediate(go); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        QualitySettings.SetQualityLevel(level, true);
        sb.AppendLine("road " + road.name + " from " + a.ToString("F1") + " to " + b.ToString("F1"));
        sb.AppendLine("DONE");
        return sb.ToString();
    }

    static string Run()
    {
        var sb = new StringBuilder();
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "Elderboom_Unity_Port")
        {
            if (scene.isDirty) return "FAIL: open scene has unsaved changes";
            scene = EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        }
        Physics.SyncTransforms();
        var roads = new List<MeshFilter>();
        foreach (var mf in Object.FindObjectsByType<MeshFilter>())
            if (mf.name.StartsWith("Road_") && mf.sharedMesh != null) roads.Add(mf);
        roads.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        sb.AppendLine("roads " + roads.Count);
        int index = 0;
        foreach (var mf in roads)
        {
            var mesh = mf.sharedMesh;
            var src = mesh.vertices; var srcUv = mesh.uv;
            int pairs = src.Length / 2;
            if (pairs < 2 || srcUv.Length != src.Length) { sb.AppendLine(mf.name + ": skipped"); continue; }
            float lift = .025f + .0015f * index++;
            var tr = mf.transform;
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            const int across = 4;
            int rows = 0, sunk = 0, missed = 0; float maxSink = 0;
            for (int i = 0; i < pairs - 1; i++)
            {
                Vector3 l0 = src[2 * i], r0 = src[2 * i + 1], l1 = src[2 * i + 2], r1 = src[2 * i + 3];
                Vector2 ul0 = srcUv[2 * i], ur0 = srcUv[2 * i + 1], ul1 = srcUv[2 * i + 2], ur1 = srcUv[2 * i + 3];
                float len = Vector3.Distance((l0 + r0) * .5f, (l1 + r1) * .5f);
                int steps = Mathf.Max(1, Mathf.CeilToInt(len / .5f));
                for (int s = (i == 0 ? 0 : 1); s <= steps; s++)
                {
                    float t = s / (float)steps;
                    Vector3 l = Vector3.Lerp(l0, l1, t), r = Vector3.Lerp(r0, r1, t);
                    Vector2 ul = Vector2.Lerp(ul0, ul1, t), ur = Vector2.Lerp(ur0, ur1, t);
                    for (int k = 0; k <= across; k++)
                    {
                        float a = k / (float)across;
                        Vector3 local = Vector3.Lerp(l, r, a);
                        Vector3 world = tr.TransformPoint(local);
                        if (GroundAt(world, out float gy))
                        {
                            if (gy > world.y) { sunk++; maxSink = Mathf.Max(maxSink, gy - world.y); }
                            world.y = gy + lift;
                        }
                        else { missed++; world.y += lift; }
                        verts.Add(tr.InverseTransformPoint(world));
                        uvs.Add(Vector2.Lerp(ul, ur, a));
                    }
                    if (rows > 0)
                    {
                        int b0 = (rows - 1) * (across + 1), b1 = rows * (across + 1);
                        for (int k = 0; k < across; k++)
                        {
                            tris.Add(b0 + k); tris.Add(b1 + k); tris.Add(b0 + k + 1);
                            tris.Add(b0 + k + 1); tris.Add(b1 + k); tris.Add(b1 + k + 1);
                        }
                    }
                    rows++;
                }
            }
            // Keep the original winding (faces up).
            Vector3 n0 = Vector3.Cross(verts[across + 1] - verts[0], verts[1] - verts[0]);
            if (tr.TransformDirection(n0).y < 0) for (int t = 0; t < tris.Count; t += 3) { int x = tris[t + 1]; tris[t + 1] = tris[t + 2]; tris[t + 2] = x; }
            mesh.Clear();
            mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            sb.AppendLine(mf.name + ": vertices " + src.Length + " -> " + verts.Count + ", was under ground at " + sunk + " points (max " + maxSink.ToString("F3") + " m), no ground under " + missed + ", lift " + lift.ToString("F4"));
        }
        foreach (var path in new[] { "Assets/GameAssets/Roads/albedo_4k.jpg", "Assets/GameAssets/Roads/normal_gl_2k.png" })
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;
            if (importer.anisoLevel < 8) { importer.anisoLevel = 8; importer.SaveAndReimport(); }
            sb.AppendLine(path + " aniso=" + importer.anisoLevel);
        }
        AssetDatabase.SaveAssets();
        sb.AppendLine("DONE");
        return sb.ToString();
    }
}
