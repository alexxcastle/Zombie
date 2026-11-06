using System.IO;
using UnityEditor;

/// <summary>Reliable file-triggered entry point for long Unity imports when the UI is busy.</summary>
// [InitializeOnLoad] removed 31 Oct 2026: the saved scene is the source of truth.
// Flag files no longer trigger rebuilds, builds or Editor exits when Unity opens.
public static class ElderboomPortAutoBuild
{
    const string RequestPath = "Library/ElderboomPort.build-request";

    static ElderboomPortAutoBuild()
    {
        EditorApplication.delayCall += BuildIfRequested;
    }

    static void BuildIfRequested()
    {
        if (!File.Exists(RequestPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            // A build request is explicit; stop a stale preview first.  Scene
            // creation is an editor-only operation and Unity forbids it in play.
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += BuildIfRequested;
            return;
        }
        File.Delete(RequestPath);
        ElderboomPortBuilder.Build();
    }
}
