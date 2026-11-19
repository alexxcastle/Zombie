using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Video;

// Logs/claude_menutest.txt: play the main menu for a few seconds, check the looping video and take two pictures.
static class ElderboomMenuVideoTest
{
    const string Flag = "Logs/claude_menutest.txt", Result = "Logs/claude_menutest_result.txt", Key = "claude_menutest";
    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Poll; }
    static double start;
    static void Poll()
    {
        if (File.Exists(Flag) && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling)
        {
            File.Delete(Flag);
            if (EditorSceneManager.GetActiveScene().isDirty) EditorSceneManager.SaveOpenScenes();
            EditorSceneManager.OpenScene("Assets/Scenes/ZombieTest_Menu.unity");
            File.WriteAllText(Result, "started\n"); SessionState.SetInt(Key, 1); start = 0; EditorApplication.isPlaying = true; return;
        }
        int stage = SessionState.GetInt(Key, 0);
        if (stage == 0) return;
        if (!EditorApplication.isPlaying)
        {
            if (stage == 9) { SessionState.SetInt(Key, 0); EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity"); File.AppendAllText(Result, "DONE\n"); }
            return;
        }
        double now = EditorApplication.timeSinceStartup; if (start <= 0) { start = now; return; }
        if (stage == 1 && now - start > 6) { Log(); ScreenCapture.CaptureScreenshot("Logs/menu_video_a.png"); SessionState.SetInt(Key, 2); }
        if (stage == 2 && now - start > 9.5) { Log(); ScreenCapture.CaptureScreenshot("Logs/menu_video_b.png"); SessionState.SetInt(Key, 3); }
        if (stage == 3 && now - start > 11) { SessionState.SetInt(Key, 9); EditorApplication.isPlaying = false; }
    }
    static void Log()
    {
        foreach (var v in Object.FindObjectsByType<VideoPlayer>(FindObjectsSortMode.None))
            File.AppendAllText(Result, $"video {Path.GetFileName(v.url)} playing {v.isPlaying} prepared {v.isPrepared} frame {v.frame}/{v.frameCount} loop {v.isLooping}\n");
    }
}
