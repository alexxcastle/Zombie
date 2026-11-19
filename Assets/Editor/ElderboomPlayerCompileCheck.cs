using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEngine;

// Compiles the game scripts the way the macOS and Windows player builds do (no UNITY_EDITOR), without building.
// Terminal: Unity -batchmode -quit -projectPath ... -executeMethod ElderboomPlayerCompileCheck.Run
public static class ElderboomPlayerCompileCheck
{
    [MenuItem("Elderboom HDRP/Dev/Check Player Script Compile")]
    public static void Run()
    {
        var report = "";
        foreach (var target in new[] { BuildTarget.StandaloneOSX, BuildTarget.StandaloneWindows64 })
        {
            var settings = new ScriptCompilationSettings { group = BuildTargetGroup.Standalone, target = target, options = ScriptCompilationOptions.None };
            string dir = "Temp/PlayerCompileCheck_" + target; Directory.CreateDirectory(dir);
            var result = PlayerBuildInterface.CompilePlayerScripts(settings, dir);
            bool ok = result.assemblies != null && result.assemblies.Contains("Assembly-CSharp.dll");
            report += target + ": " + (ok ? "OK" : "FAILED") + "\n";
        }
        File.WriteAllText("Logs/player_compile_check.txt", report);
        Debug.Log("[PlayerCompileCheck]\n" + report);
    }
}
