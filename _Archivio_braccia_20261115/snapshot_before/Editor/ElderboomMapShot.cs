using UnityEditor;
using UnityEngine;

// Elderboom HDRP > Dev > Capture Map (Play mode, village scene): orthographic daylight top views in Logs/mapshot.
static class ElderboomMapShot
{
    [MenuItem("Elderboom HDRP/Dev/Capture Map")]
    static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[MapShot] enter Play mode first"); return; }
        var host = Object.FindAnyObjectByType<ElderboomPerfTuning>();
        if (host == null) { Debug.LogWarning("[MapShot] no host"); return; }
        // Plain picture: no grading, no light fading, full resolution.
        ElderboomAtmosphere.Enabled = false;
        ElderboomPerfTuning.Enabled = false;
        host.StartCoroutine(Go(host));
    }

    static System.Collections.IEnumerator Go(MonoBehaviour host)
    {
        yield return new WaitForSecondsRealtime(1.5f);
        yield return ElderboomMapCapture.Capture("Logs/mapshot");
        Debug.Log("[MapShot] done");
    }
}
