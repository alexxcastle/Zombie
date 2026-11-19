using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Editor automation: Logs/claude_loadout_shot.txt plays the menu scene, opens the weapon customization page on the
// weapon named in the flag (default glock19) and saves the Game view:
//   Logs/loadout_shot/<weapon>_a.png and _b.png (a couple of seconds apart, to see the video backdrop move),
//   status in Logs/loadout_shot/report.txt
[InitializeOnLoad]
static class ElderboomClaudeLoadoutShot
{
    const string Flag = "Logs/claude_loadout_shot.txt", Pending = "claude_loadout_shot_pending", WeaponKey = "claude_loadout_shot_weapon";
    const string Out = "Logs/loadout_shot";
    static double playSeen;
    static int step;

    static ElderboomClaudeLoadoutShot() { EditorApplication.update += Poll; }

    static void Report(string text) { Directory.CreateDirectory(Out); File.AppendAllText(Out + "/report.txt", text + "\n"); }

    static void Poll()
    {
        if (SessionState.GetBool(Pending, false))
        {
            if (!EditorApplication.isPlaying) { if (!EditorApplication.isPlayingOrWillChangePlaymode) { SessionState.SetBool(Pending, false); playSeen = 0; step = 0; } return; }
            if (playSeen <= 0) playSeen = EditorApplication.timeSinceStartup;
            double t = EditorApplication.timeSinceStartup - playSeen;
            string weapon = SessionState.GetString(WeaponKey, "glock19");
            try
            {
                if (step == 0 && t > 3) { Open(weapon); step = 1; }
                else if (step == 1 && t > 8) { Diag(); ScreenCapture.CaptureScreenshot(Out + "/" + weapon + "_a.png"); step = 2; }
                else if (step == 2 && t > 10.5) { ScreenCapture.CaptureScreenshot(Out + "/" + weapon + "_b.png"); step = 3; }
                else if (step == 3 && t > 12) { Report("DONE"); EditorApplication.isPlaying = false; }
            }
            catch (Exception e) { Report("FAIL " + e); EditorApplication.isPlaying = false; }
            return;
        }
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
        string w = File.ReadAllText(Flag).Trim().ToLowerInvariant();
        File.Delete(Flag);
        if (Directory.Exists(Out)) foreach (var f in Directory.GetFiles(Out)) File.Delete(f);
        if (EditorSceneManager.GetActiveScene().isDirty) { Report("FAIL: open scene has unsaved changes"); return; }
        EditorSceneManager.OpenScene("Assets/Scenes/ZombieTest_Menu.unity");
        SessionState.SetString(WeaponKey, string.IsNullOrEmpty(w) ? "glock19" : w);
        SessionState.SetBool(Pending, true);
        playSeen = 0; step = 0;
        EditorApplication.isPlaying = true;
    }

    static void Diag()
    {
        const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
        var stage = UnityEngine.Object.FindFirstObjectByType<ElderboomLoadoutStage>();
        if (stage == null) { Report("diag: no stage"); return; }
        var vp = typeof(ElderboomLoadoutStage).GetField("backdropVideo", F).GetValue(stage) as UnityEngine.Video.VideoPlayer;
        var mat = typeof(ElderboomLoadoutStage).GetField("backdropMaterial", F).GetValue(stage) as Material;
        var quad = typeof(ElderboomLoadoutStage).GetField("backdropQuad", F).GetValue(stage) as Transform;
        Report("video: " + (vp == null ? "null" : "prepared=" + vp.isPrepared + " playing=" + vp.isPlaying + " frame=" + vp.frame + " url=" + vp.url));
        if (mat != null) Report("mat shader=" + mat.shader.name + " colormap=" + mat.GetTexture("_UnlitColorMap") + " color=" + mat.GetColor("_UnlitColor") + " keywords=" + string.Join(",", mat.shaderKeywords));
        if (quad != null) Report("quad local=" + quad.localPosition + " scale=" + quad.localScale + " enabled=" + quad.GetComponent<Renderer>().enabled + " layer=" + quad.gameObject.layer);
        Report("cam pos=" + stage.Camera.transform.localPosition + " near=" + stage.Camera.nearClipPlane + " far=" + stage.Camera.farClipPlane + " aspect=" + stage.Camera.aspect + " weapon=" + stage.WeaponId);
        foreach (var r in stage.GetComponentsInChildren<Renderer>(false))
            if (r.transform != quad) Report("  r " + r.name + " en=" + r.enabled + " center=" + stage.transform.InverseTransformPoint(r.bounds.center).ToString("F3") + " size=" + r.bounds.size.ToString("F3"));
    }

    static void Open(string weapon)
    {
        var menu = UnityEngine.Object.FindFirstObjectByType<ElderboomMenu>();
        if (menu == null) throw new Exception("no ElderboomMenu in the scene");
        const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(ElderboomMenu).GetField("loadoutWeapon", F).SetValue(menu, weapon);
        var page = typeof(ElderboomMenu).GetNestedType("Page", BindingFlags.NonPublic);
        typeof(ElderboomMenu).GetMethod("OpenLoadout", F).Invoke(menu, new[] { Enum.Parse(page, "Home") });
        Report("opened loadout on " + typeof(ElderboomMenu).GetField("loadoutWeapon", F).GetValue(menu));
    }
}
