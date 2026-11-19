using System.IO;
using UnityEditor;

// Entry point for ElderboomClaudeArmsTest (QA task: 9-weapon hip/ADS/fire/reload check).
// The test itself runs from ElderboomClaudeArmsTest.Poll once Logs/claude_armstest.txt exists.
static class ElderboomClaudeArmsTestMenu
{
    public const string NineWeapons = "m4 mp7 glock19 rhino shotgun mp133 grenade minigun flame";

    [MenuItem("Elderboom/QA/Run 9-Weapon Arms Test")]
    public static void RunNine()
    {
        Directory.CreateDirectory("Logs");
        if (File.Exists("Logs/claude_armstest_result.txt")) File.Delete("Logs/claude_armstest_result.txt");
        File.WriteAllText("Logs/claude_armstest.txt", NineWeapons);
        UnityEngine.Debug.Log("[ArmsTest] queued: " + NineWeapons + " -> Logs/armstest/, report Logs/claude_armstest_result.txt");
    }
}
