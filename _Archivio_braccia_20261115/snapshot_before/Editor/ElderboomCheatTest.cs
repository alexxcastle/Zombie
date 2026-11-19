using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Play-mode self test of the cheat console: runs a command script and writes the console output.
static class ElderboomCheatTest
{
    static readonly string[] Script =
    {
        "status", "credits 5000", "weapons", "give mp5", "slot3", "give m870", "ammo", "infammo", "god", "nocd",
        "spawn 4 runner", "status", "freeze", "killall", "freeze", "powerup double_points", "tp list", "tp chiesa", "pos",
        "day", "status"
    };

    [MenuItem("Elderboom HDRP/Dev/Cheat Console Self-Test")]
    static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("play mode only"); return; }
        var c = Object.FindAnyObjectByType<ElderboomCheats>();
        if (c == null) { File.WriteAllText("Logs/cheat_test.txt", "console missing"); return; }
        var run = typeof(ElderboomCheats).GetMethod("Run", BindingFlags.Instance | BindingFlags.NonPublic);
        var toggle = typeof(ElderboomCheats).GetMethod("Toggle", BindingFlags.Instance | BindingFlags.NonPublic);
        var lines = (System.Collections.Generic.List<string>)typeof(ElderboomCheats).GetField("lines", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(c);
        foreach (var cmd in Script) run.Invoke(c, new object[] { cmd });
        if (!ElderboomCheats.IsOpen) toggle.Invoke(c, null);
        File.WriteAllText("Logs/cheat_test.txt", string.Join("\n", lines));
    }
}
