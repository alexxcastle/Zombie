using System.IO;
using UnityEditor;

// Logs/claude_refresh.txt -> AssetDatabase.Refresh() (imports and recompiles changed files while the editor is in the
// background, without driving its menus).
static class ElderboomClaudeRefresh
{
    const string Flag = "Logs/claude_refresh.txt";
    static double next;
    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 1.5;
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        File.Delete(Flag);
        AssetDatabase.Refresh();
    }
}
