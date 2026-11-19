using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;

// Logs/claude_playrange.txt -> opens Elderboom_TestRange and enters play mode (asks first if the open scene has unsaved changes).
[InitializeOnLoad]
static class ElderboomClaudePlayRange
{
    const string Flag = "Logs/claude_playrange.txt", Result = "Logs/claude_playrange_result.txt";
    static double next;
    static ElderboomClaudePlayRange() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        File.Delete(Flag);
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { File.WriteAllText(Result, "CANCELLED"); return; }
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_TestRange.unity", OpenSceneMode.Single);
        EditorApplication.isPlaying = true;
        File.WriteAllText(Result, "PLAYING");
    }
}
