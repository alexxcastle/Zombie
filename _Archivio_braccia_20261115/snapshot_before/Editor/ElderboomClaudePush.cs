using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;

// Editor automation: Logs/claude_push.txt uploads the current builds to itch.io with butler (only changed bytes are
// sent; players with the itch app get the update automatically). Content: "windows" (default), "mac" or "all".
//   Windows: Builds/Windows               -> alexxcastle/zombie-test:windows
//   macOS:   Builds/ElderboomUnityPort.app -> alexxcastle/zombie-test:mac
// butler is looked up in ../tools/butler/butler (next to the project) or in the itch app's folder; it must have been
// authorized once with "butler login" in Terminal. Output goes to Logs/claude_push_result.txt.
[InitializeOnLoad]
static class ElderboomClaudePush
{
    const string Flag = "Logs/claude_push.txt", Result = "Logs/claude_push_result.txt";
    const string Target = "alexxcastle/zombie-test";
    static double nextPoll;
    static Process running;
    static string[] queue;
    static int queueIndex;

    static ElderboomClaudePush() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (running != null)
        {
            if (!running.HasExited) return;
            Append("exit code " + running.ExitCode);
            running = null;
            StartNext();
            return;
        }
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Flag) || BuildPipeline.isBuildingPlayer) return;
        string what = File.ReadAllText(Flag).ToLowerInvariant();
        File.Delete(Flag);
        File.WriteAllText(Result, "push " + DateTime.Now.ToString("HH:mm:ss") + "\n");
        // Windows only unless "mac" or "all" is asked for.
        bool win = !what.Contains("mac") || what.Contains("all") || what.Contains("win");
        bool mac = what.Contains("all") || what.Contains("mac");
        var list = new System.Collections.Generic.List<string>();
        if (win) list.Add("windows");
        if (mac) list.Add("mac");
        queue = list.ToArray(); queueIndex = 0;
        StartNext();
    }

    static string Butler()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        var candidates = new System.Collections.Generic.List<string>
        {
            Path.GetFullPath("../tools/butler/butler"),
            Path.Combine(home, "zombie-test/tools/butler/butler"),
        };
        string broth = Path.Combine(home, "Library/Application Support/itch/broth/butler/versions");
        if (Directory.Exists(broth))
            foreach (var dir in Directory.GetDirectories(broth)) candidates.Add(Path.Combine(dir, "butler"));
        foreach (var c in candidates) if (File.Exists(c)) return c;
        return null;
    }

    static void StartNext()
    {
        if (queue == null || queueIndex >= queue.Length) { Append("DONE"); queue = null; return; }
        string channel = queue[queueIndex++];
        string butler = Butler();
        if (butler == null) { Append("FAIL butler not found (expected ../tools/butler/butler)"); queue = null; return; }
        string folder = channel == "windows" ? "Builds/Windows" : "Builds/ElderboomUnityPort.app";
        if (!Directory.Exists(folder)) { Append("SKIP " + channel + ": " + folder + " missing"); StartNext(); return; }
        string version = DateTime.Now.ToString("yyyy.MM.dd.HHmm");
        string args = "push \"" + Path.GetFullPath(folder) + "\" " + Target + ":" + channel +
                      " --userversion " + version + " --ignore \"*BackUpThisFolder_ButDontShipItWithYourGame*\"";
        Append("$ butler " + args);
        var info = new ProcessStartInfo(butler, args)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
        };
        try
        {
            running = Process.Start(info);
            running.OutputDataReceived += (_, e) => { if (e.Data != null) Append(e.Data); };
            running.ErrorDataReceived += (_, e) => { if (e.Data != null) Append(e.Data); };
            running.BeginOutputReadLine(); running.BeginErrorReadLine();
        }
        catch (Exception e) { Append("FAIL " + e.Message); running = null; queue = null; }
    }

    static readonly object fileLock = new object();
    static void Append(string line)
    {
        lock (fileLock) { try { File.AppendAllText(Result, line + "\n", Encoding.UTF8); } catch { } }
    }
}
