using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Editor automation: Logs/claude_smoke.txt plays the game scene and checks the loadout and stations:
// starting weapons, what every station sells, the ammo station, buying from each one.
// Result in Logs/claude_smoke_result.txt.
[InitializeOnLoad]
static class ElderboomClaudeSmoke
{
    const string Flag = "Logs/claude_smoke.txt", Result = "Logs/claude_smoke_result.txt", Pending = "claude_smoke_pending";
    static double startAt;

    static ElderboomClaudeSmoke() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (SessionState.GetBool(Pending, false))
        {
            if (!EditorApplication.isPlaying) { if (!EditorApplication.isPlayingOrWillChangePlaymode) SessionState.SetBool(Pending, false); return; }
            if (startAt <= 0) startAt = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - startAt < 6) return;
            SessionState.SetBool(Pending, false); startAt = 0;
            try { File.WriteAllText(Result, Run()); }
            catch (System.Exception e) { File.WriteAllText(Result, "FAIL " + e); }
            EditorApplication.isPlaying = false;
            return;
        }
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
        string mode = File.ReadAllText(Flag);
        File.Delete(Flag);
        if (mode.Contains("settings"))
        {
            // Multiplayer: the game must keep running (and answering the network) when its window loses focus.
            PlayerSettings.runInBackground = true;
            AssetDatabase.SaveAssets();
            File.WriteAllText(Result, "runInBackground=" + PlayerSettings.runInBackground + "\nDONE\n");
            return;
        }
        if (EditorSceneManager.GetActiveScene().isDirty) { File.WriteAllText(Result, "FAIL: unsaved scene"); return; }
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        SessionState.SetBool(Pending, true);
        EditorApplication.isPlaying = true;
    }

    static string Run()
    {
        var sb = new StringBuilder();
        var game = ElderboomGame.Active; var player = game != null ? game.Player : null;
        if (player == null) return "FAIL no player";
        HDRPSlicePlayer.DevInvulnerable = true;
        var arsenal = player.GetComponent<ElderboomArsenal>();
        sb.AppendLine("operator " + game.Operator.id + " slots=" + arsenal.Count + "/" + arsenal.MaxSlots + " weapon=" + player.WeaponDisplayName);
        typeof(ElderboomGame).GetField("credits", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(game, 100000);
        foreach (var s in Object.FindObjectsByType<ElderboomStation>())
            sb.AppendLine("station " + s.name + " [" + s.category + "] cost=" + s.cost + " sells=" + string.Join(",", s.weapons ?? new string[0]) + " at " + s.transform.position.ToString("F1"));
        foreach (var s in Object.FindObjectsByType<ElderboomStation>())
        {
            if (s.category != "weapons" && s.category != ElderboomIds.Station.Ammo) continue;
            for (int i = 0; i < 3; i++)
            {
                bool ok = s.TryPurchase(player);
                sb.AppendLine("  buy " + s.label + " -> " + ok + " now=" + player.WeaponDisplayName + " notice=" + game.Notice);
            }
        }
        sb.AppendLine("slots after=" + arsenal.Count + " credits=" + game.Credits);
        sb.AppendLine("horse=" + (ElderboomMount.Instance != null) + " drops=" + string.Join(",", ElderboomSpecialPickup.DropIds));
        sb.AppendLine("DONE");
        return sb.ToString();
    }
}
