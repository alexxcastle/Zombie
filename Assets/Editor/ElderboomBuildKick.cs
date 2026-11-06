using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// [InitializeOnLoad] removed 31 Oct 2026: the saved scene is the source of truth.
// Flag files no longer trigger rebuilds, builds or Editor exits when Unity opens.
public static class ElderboomBuildKick
{
    const string Flag = "Logs/build_now.txt";
    const string AppPath = "Builds/ElderboomUnityPort.app";

    static ElderboomBuildKick()
    {
        if (!File.Exists(Flag)) return;
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            return;
        }
        EditorApplication.update -= Tick;
        File.Delete(Flag);
        if (Directory.Exists(AppPath)) Directory.Delete(AppPath, true);
        const string backup = "Builds/ElderboomUnityPort_BackUpThisFolder_ButDontShipItWithYourGame";
        if (Directory.Exists(backup)) Directory.Delete(backup, true);
        Directory.CreateDirectory("Builds");
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
        PlayerSettings.defaultIsNativeResolution = true;
        ElderboomStreetLamps.Ensure();
        ElderboomValley.Ensure();
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/ZombieTest_Menu.unity", "Assets/Scenes/Elderboom_Unity_Port.unity" },
            locationPathName = AppPath,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None
        });
        var summary = report.summary;
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/standalone_build_result.json",
            "{\"status\":\"" + summary.result + "\",\"errors\":" + summary.totalErrors + ",\"seconds\":" + summary.totalTime.TotalSeconds.ToString("F1") + "}");
        Debug.Log("Elderboom build " + summary.result + " errors=" + summary.totalErrors);
        EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
