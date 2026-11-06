using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

// Editor automation: Logs/claude_pkg_add.txt (one package id per line) adds those packages through the
// Package Manager; the result is written to Logs/claude_pkg_result.txt.
[InitializeOnLoad]
static class ElderboomClaudePackages
{
    const string Flag = "Logs/claude_pkg_add.txt", Result = "Logs/claude_pkg_result.txt";
    static AddAndRemoveRequest request;
    static double nextPoll;

    static ElderboomClaudePackages()
    {
        EditorApplication.update += Poll;
        // Compile messages of every assembly go to Logs/claude_compile.txt (the Editor.log is not reachable from outside).
        CompilationPipeline.compilationStarted += _ => { try { File.WriteAllText("Logs/claude_compile.txt", "started " + System.DateTime.Now.ToString("HH:mm:ss") + "\n"); } catch { } };
        CompilationPipeline.assemblyCompilationFinished += (assembly, messages) =>
        {
            var sb = new System.Text.StringBuilder();
            int errors = 0;
            foreach (var m in messages) if (m.type == CompilerMessageType.Error) { errors++; sb.AppendLine("ERROR " + m.message); }
            sb.Insert(0, Path.GetFileName(assembly) + " errors=" + errors + "\n");
            try { File.AppendAllText("Logs/claude_compile.txt", sb.ToString()); } catch { }
        };
    }

    static void Poll()
    {
        if (request != null)
        {
            if (!request.IsCompleted) return;
            string text = request.Status == StatusCode.Success ? "Success\n" : "Failure " + request.Error?.message + "\n";
            if (request.Status == StatusCode.Success) foreach (var p in request.Result) text += p.packageId + "\n";
            File.WriteAllText(Result, text);
            Debug.Log("[ElderboomClaudePackages] " + text);
            request = null;
            return;
        }
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        var ids = File.ReadAllLines(Flag);
        File.Delete(Flag);
        var list = new System.Collections.Generic.List<string>();
        foreach (var id in ids) if (!string.IsNullOrWhiteSpace(id)) list.Add(id.Trim());
        if (list.Count > 0) request = Client.AddAndRemove(list.ToArray());
    }
}
