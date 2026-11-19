using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.HighDefinition;

// Editor automation: Logs/claude_areashots.txt -> plays the game scene and saves eye-level views of every map area
// (ElderboomArea) to Logs/area_shots/<area>_<n>.png, with Logs/area_shots/report.txt.
// Each area gets four views from walkable spots near its edges, looking across the area, at the player's eye height
// and field of view, without the weapon. Flag content: optional "WxH" (default 1920x1080).
static class ElderboomAreaShots
{
    const string Flag = "Logs/claude_areashots.txt", State = "Library/claude_areashots_state.txt", Out = "Logs/area_shots";
    static double next, startAt;
    static List<(string file, Vector3 eye, Vector3 target)> shots;
    static int index; static Camera cam; static StringBuilder report;

    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + .5;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (File.Exists(Flag) && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            string size = File.ReadAllText(Flag).Trim();
            File.Delete(Flag);
            File.WriteAllText(State, size);
            SessionState.SetInt("areashots_tries", 0);
            if (Directory.Exists(Out)) foreach (var old in Directory.GetFiles(Out, "*.png")) File.Delete(old);
            EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
            EditorApplication.isPlaying = true;
            return;
        }
        if (!File.Exists(State)) return;
        if (!EditorApplication.isPlaying)
        {
            // Play mode ended before the last shot (another tool, a click): go back in and continue with the missing shots.
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            int tries = SessionState.GetInt("areashots_tries", 0);
            if (tries >= 8) { File.Delete(State); return; }
            SessionState.SetInt("areashots_tries", tries + 1);
            shots = null; index = 0; startAt = 0;
            EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
            EditorApplication.isPlaying = true;
            return;
        }
        try { Step(); }
        catch (System.Exception e) { File.WriteAllText(Out + "_error.txt", e.ToString()); Finish(); }
    }

    static void Step()
    {
        var player = Object.FindAnyObjectByType<HDRPSlicePlayer>();
        if (player == null || player.ViewCamera == null) return;
        if (shots == null)
        {
            if (startAt == 0) { startAt = EditorApplication.timeSinceStartup + 6; return; } // let the scene settle (lights, fog, terrain)
            if (EditorApplication.timeSinceStartup < startAt) return;
            Build(player);
            return;
        }
        if (index >= shots.Count) { File.WriteAllText(Out + "/report.txt", report.ToString()); Finish(); return; }
        var s = shots[index++];
        if (File.Exists(Out + "/" + s.file)) return;
        cam.transform.position = s.eye;
        cam.transform.rotation = Quaternion.LookRotation(s.target - s.eye, Vector3.up);
        int w = 1920, h = 1080;
        var parts = File.ReadAllText(State).ToLowerInvariant().Split('x');
        if (parts.Length == 2 && int.TryParse(parts[0], out int pw) && int.TryParse(parts[1], out int ph)) { w = pw; h = ph; }
        cam.aspect = (float)w / h;
        string stats = ElderboomAbilityShots.Capture(cam, Out + "/" + s.file, w, h);
        report.AppendLine(s.file + " eye=" + s.eye.ToString("F1") + " look=" + s.target.ToString("F1") + " " + stats);
    }

    static void Build(HDRPSlicePlayer player)
    {
        Directory.CreateDirectory(Out);
        report = new StringBuilder();
        var go = new GameObject("AreaShotCamera");
        cam = go.AddComponent<Camera>();
        cam.CopyFrom(player.ViewCamera);
        cam.cullingMask &= ~(1 << 19); // no first-person weapon
        cam.targetTexture = null; cam.enabled = false;
        var hd = go.AddComponent<HDAdditionalCameraData>();
        hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        float eyeHeight = Mathf.Clamp(player.ViewCamera.transform.position.y - player.transform.position.y, 1.4f, 1.9f);
        report.AppendLine("fov=" + cam.fieldOfView.ToString("F1") + " eyeHeight=" + eyeHeight.ToString("F2") + " player=" + player.transform.position.ToString("F1"));

        shots = new List<(string, Vector3, Vector3)>(); index = 0;
        foreach (var area in ElderboomArea.All.OrderBy(a => a.areaName))
        {
            // Ground outline of the area (polygon, or the box's corners).
            var pts = new List<Vector2>();
            if (area.polygon != null && area.polygon.Length >= 3) pts.AddRange(area.polygon);
            else
                foreach (var c in new[] { new Vector3(-.5f, 0, -.5f), new Vector3(.5f, 0, -.5f), new Vector3(.5f, 0, .5f), new Vector3(-.5f, 0, .5f) })
                { var wp = area.transform.TransformPoint(Vector3.Scale(c, area.size)); pts.Add(new Vector2(wp.x, wp.z)); }
            Vector2 lo = pts[0], hi = pts[0], centre = Vector2.zero;
            foreach (var p in pts) { lo = Vector2.Min(lo, p); hi = Vector2.Max(hi, p); centre += p; }
            centre /= pts.Count;
            if (!Ground(new Vector3(centre.x, area.transform.position.y, centre.y), 25f, out var mid)) mid = new Vector3(centre.x, area.transform.position.y, centre.y);
            string name = new string(area.areaName.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
            report.AppendLine("AREA " + area.areaName + " min=" + lo + " max=" + hi + " centre=" + mid.ToString("F1") + " startUnlocked=" + area.unlockedAtStart);
            // Four viewpoints: from each side of the outline (W, S, E, N), pulled a little inside, on walkable ground.
            var sides = new[] { new Vector2(lo.x, centre.y), new Vector2(centre.x, lo.y), new Vector2(hi.x, centre.y), new Vector2(centre.x, hi.y) };
            string[] tags = { "from_west", "from_south", "from_east", "from_north" };
            for (int i = 0; i < 4; i++)
            {
                Vector3 found = default; bool ok = false;
                foreach (float t in new[] { .06f, .15f, .25f, .35f, .45f })
                {
                    var p2 = Vector2.Lerp(sides[i], centre, t);
                    if (Ground(new Vector3(p2.x, mid.y, p2.y), 6f, out found) && area.Contains(new Vector3(found.x, area.transform.position.y, found.z))) { ok = true; break; }
                }
                if (!ok) { report.AppendLine("  " + tags[i] + ": no walkable spot"); continue; }
                Vector3 eye = found + Vector3.up * eyeHeight;
                Vector3 target = mid + Vector3.up * (eyeHeight - .2f);
                if ((target - eye).sqrMagnitude < 9f) continue;
                shots.Add((name + "_" + (i + 1) + "_" + tags[i] + ".png", eye, target));
            }
        }
        // Where the match starts, looking the way the player looks.
        shots.Add(("_start_view.png", player.ViewCamera.transform.position, player.ViewCamera.transform.position + player.transform.forward * 10f));
    }

    static bool Ground(Vector3 p, float radius, out Vector3 hit)
    {
        if (NavMesh.SamplePosition(p, out var h, radius, NavMesh.AllAreas)) { hit = h.position; return true; }
        hit = p; return false;
    }

    static void Finish()
    {
        if (File.Exists(State)) File.Delete(State);
        shots = null; index = 0; startAt = 0;
        File.WriteAllText("Logs/claude_areashots_result.txt", "done " + System.DateTime.Now.ToString("HH:mm:ss"));
        EditorApplication.isPlaying = false;
    }
}
