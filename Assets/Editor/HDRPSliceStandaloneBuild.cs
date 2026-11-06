using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class HDRPSliceStandaloneBuild
{
    const string ScenePath = "Assets/Scenes/Elderboom_Unity_Port.unity";
    const string AppPath = "Builds/ElderboomUnityPort.app";
    const string StatusPath = "Logs/standalone_build_result.json";

    [Serializable]
    class BuildStatus
    {
        public string status;
        public string appPath;
        public string message;
        public int errors;
        public int warnings;
        public double seconds;
        public long bytes;
    }

    [MenuItem("Elderboom HDRP/Build Fullscreen Standalone Test")]
    public static void QueueBuild()
    {
        if (EditorApplication.isPlaying)
            throw new InvalidOperationException("Stop Play Mode before building the standalone test.");
        WriteStatus(new BuildStatus { status = "queued", appPath = Path.GetFullPath(AppPath) });
        EditorApplication.delayCall += Build;
    }

    static void Build()
    {
        try
        {
            WriteStatus(new BuildStatus { status = "building", appPath = Path.GetFullPath(AppPath) });
            if (SceneManager.GetActiveScene().path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            PlayerSettings.productName = "Elderboom Village";
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            Directory.CreateDirectory("Builds");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/ZombieTest_Menu.unity",ScenePath },
                locationPathName = AppPath,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None
            });
            var summary = report.summary;
            string dataPath = Path.Combine(AppPath, "Contents/Resources/Data");
            bool hasGameData = File.Exists(Path.Combine(dataPath, "globalgamemanagers"))
                || File.Exists(Path.Combine(dataPath, "data.unity3d"));
            if (summary.result == BuildResult.Succeeded && !hasGameData)
                throw new IOException("Unity did not finish writing the player data archive.");
            WriteStatus(new BuildStatus
            {
                status = summary.result == BuildResult.Succeeded ? "succeeded" : "failed",
                appPath = Path.GetFullPath(AppPath),
                message = summary.result.ToString(),
                errors = (int)summary.totalErrors,
                warnings = (int)summary.totalWarnings,
                seconds = summary.totalTime.TotalSeconds,
                bytes = (long)summary.totalSize
            });
            Debug.Log($"Standalone HDRP test: {summary.result}, {summary.totalErrors} errors, {summary.totalTime.TotalSeconds:F1}s. {Path.GetFullPath(AppPath)}");
        }
        catch (Exception exception)
        {
            WriteStatus(new BuildStatus { status = "failed", appPath = Path.GetFullPath(AppPath), message = exception.ToString() });
            Debug.LogException(exception);
        }
    }

    // Kept public so the same verified fullscreen build can run unattended.
    public static void BuildPort()
    {
        Build();
    }

    static void WriteStatus(BuildStatus status)
    {
        Directory.CreateDirectory("Logs");
        File.WriteAllText(StatusPath, JsonUtility.ToJson(status, true));
    }
}
