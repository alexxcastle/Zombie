using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

// "playtest" (Logs/claude_blocks.txt): plays the scene and checks the block rules in the real game: zombies come
// only from the open block, a locked block cannot reach the square, opening a passage unlocks its block and its
// path. Result appended to Logs/claude_blocks_result.txt.
static partial class ElderboomClaudeBlocks
{
    const string PlayPending = "claude_blocks_play";
    static double playStart; static int playStep;
    static readonly StringBuilder playLog = new StringBuilder();

    [InitializeOnLoadMethod] static void InitPlaytest() { EditorApplication.update += PlayPoll; }

    static string StartPlaytest()
    {
        if (EditorSceneManager.GetActiveScene().isDirty) return "FAIL unsaved scene";
        SessionState.SetBool(PlayPending, true); playStep = 0; playStart = 0;
        EditorApplication.isPlaying = true;
        return "playtest started\n";
    }

    static void PlayPoll()
    {
        if (!SessionState.GetBool(PlayPending, false)) return;
        if (!EditorApplication.isPlaying) { if (!EditorApplication.isPlayingOrWillChangePlaymode && playStep > 0) SessionState.SetBool(PlayPending, false); return; }
        if (playStart <= 0) { playStart = EditorApplication.timeSinceStartup; playStep = 1; playLog.Clear(); }
        double t = EditorApplication.timeSinceStartup - playStart;
        var game = ElderboomGame.Active;
        try
        {
            if (playStep == 1 && t > 5)
            {
                HDRPSlicePlayer.DevInvulnerable = true;
                if (game != null) typeof(ElderboomGame).GetField("credits", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(game, 100000);
                playLog.AppendLine("areas: " + string.Join(", ", ElderboomArea.All.Select(a => a.areaName + (a.Unlocked ? " open" : " locked"))));
                playLog.AppendLine("gates: " + string.Join(", ", Object.FindObjectsByType<ElderboomGate>(FindObjectsSortMode.None).Select(g => g.areaName + " " + g.cost)));
                playLog.AppendLine("player at " + (game != null && game.Player != null ? game.Player.transform.position.ToString("F1") : "?") + " in " + (game != null && game.Player != null ? ElderboomArea.At(game.Player.transform.position)?.areaName : "?"));
                playStep = 2;
            }
            else if (playStep == 2 && t > 40)
            {
                var zs = game != null ? game.Zombies.Where(z => z != null).ToList() : new System.Collections.Generic.List<ElderboomZombie>();
                var by = zs.GroupBy(z => ElderboomArea.At(z.transform.position)?.areaName ?? "outside").Select(g => g.Key + " " + g.Count());
                playLog.AppendLine("t=40s wave " + (game != null ? game.Wave : -1) + ", zombies " + zs.Count + " by area: " + string.Join(", ", by));
                playLog.AppendLine(PathReport("before opening"));
                var gate = Object.FindObjectsByType<ElderboomGate>(FindObjectsSortMode.None).FirstOrDefault(g => g.areaName == "West Farm");
                if (gate != null) gate.NetOpen();
                playStep = 3; playStart = EditorApplication.timeSinceStartup - 40;
            }
            else if (playStep == 3 && t > 45)
            {
                playLog.AppendLine("after opening West Farm: " + string.Join(", ", ElderboomArea.All.Select(a => a.areaName + (a.Unlocked ? " open" : " locked"))));
                playLog.AppendLine(PathReport("after opening"));
                playLog.AppendLine("DONE");
                System.IO.File.AppendAllText(Result, playLog.ToString());
                SessionState.SetBool(PlayPending, false); playStep = 9;
                EditorApplication.isPlaying = false;
            }
        }
        catch (System.Exception e)
        {
            System.IO.File.AppendAllText(Result, playLog + "FAIL " + e + "\n");
            SessionState.SetBool(PlayPending, false); EditorApplication.isPlaying = false;
        }
    }

    // From one spawn of each block to the player: complete only through open passages (gates carve when closed).
    static string PathReport(string title)
    {
        var game = ElderboomGame.Active; if (game == null || game.Player == null) return title + ": no player";
        var target = game.Player.transform.position;
        if (NavMesh.SamplePosition(target, out var hit, 3, NavMesh.AllAreas)) target = hit.position;
        var sb = new StringBuilder(title + " paths to the player:");
        var path = new NavMeshPath();
        foreach (var grp in Object.FindObjectsByType<ElderboomSpawnPoint>(FindObjectsSortMode.None).GroupBy(s => s.area))
        {
            var sp = grp.First();
            bool ok = NavMesh.CalculatePath(sp.transform.position, target, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
            sb.Append(" " + grp.Key + "=" + (ok ? "reach" : "blocked"));
        }
        return sb.ToString();
    }
}
