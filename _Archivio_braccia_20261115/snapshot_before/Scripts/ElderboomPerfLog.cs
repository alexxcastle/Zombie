using System;
using System.IO;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

// Two jobs that start by themselves with the game:
// 1) perf_log.txt next to the game (Mac: inside the .app's Contents, Windows: beside the .exe): every 10 s of a
//    match, the wave, zombies alive, average FPS, 1% low FPS, worst frame, memory, resolution and graphics level.
// 2) Zombie budget: zombies far from the camera stop casting shadows, skin with fewer bones and use cheaper
//    crowd avoidance, so bigger hordes cost less. Near zombies are untouched.
public sealed class ElderboomPerfLog : MonoBehaviour
{
    const float Interval = 10f, ShadowDistance = 22f, DetailDistance = 18f;
    static string path;
    static readonly System.Collections.Generic.List<SkinnedMeshRenderer> skins = new System.Collections.Generic.List<SkinnedMeshRenderer>();
    readonly float[] frames = new float[4096];
    int frameCount;
    float windowStart, nextBudget, sessionStart = -1f;
    int lastWave = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("ElderboomPerfLog");
        DontDestroyOnLoad(go);
        go.AddComponent<ElderboomPerfLog>();
    }

    void Update()
    {
        var game = ElderboomGame.Active;
        bool playing = game != null && game.IsPlaying;
        if (!playing) { frameCount = 0; windowStart = Time.unscaledTime; sessionStart = -1f; return; }
        if (sessionStart < 0f)
        {
            sessionStart = windowStart = Time.unscaledTime;
            Write("=== match " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " · " + SystemInfo.graphicsDeviceName + " · " + SystemInfo.systemMemorySize + " MB RAM · " + SystemInfo.operatingSystem);
            Write("time   wave alive  fps  1%low worst_ms  mem_MB  res        quality");
        }
        if (frameCount < frames.Length) frames[frameCount++] = Time.unscaledDeltaTime;
        if (Time.unscaledTime >= nextBudget) { nextBudget = Time.unscaledTime + .25f; Budget(game); }
        if (Time.unscaledTime - windowStart >= Interval || game.Wave != lastWave) Flush(game);
    }

    void Flush(ElderboomGame game)
    {
        if (frameCount > 10)
        {
            float total = 0, worst = 0;
            for (int i = 0; i < frameCount; i++) { total += frames[i]; worst = Mathf.Max(worst, frames[i]); }
            var sorted = new float[frameCount]; Array.Copy(frames, sorted, frameCount); Array.Sort(sorted);
            // 1% low: average FPS of the slowest 1% of frames.
            int n = Mathf.Max(1, frameCount / 100); float slow = 0;
            for (int i = frameCount - n; i < frameCount; i++) slow += sorted[i];
            int alive = 0;
            foreach (var z in game.Zombies) if (z != null && z.IsAlive && z.gameObject.activeInHierarchy) alive++;
            string quality = ElderboomPerformanceMode.Enabled ? "PERFORMANCE" : QualitySettings.names[QualitySettings.GetQualityLevel()];
            Write(TimeSpan.FromSeconds(Time.unscaledTime - sessionStart).ToString(@"mm\:ss") + "  " + game.Wave.ToString().PadLeft(4) + " " + alive.ToString().PadLeft(5)
                + " " + (frameCount / total).ToString("0").PadLeft(5) + " " + (n / slow).ToString("0").PadLeft(6) + " " + (worst * 1000f).ToString("0").PadLeft(8)
                + " " + (UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576).ToString().PadLeft(7)
                + "  " + (Screen.width + "x" + Screen.height).PadRight(10) + " " + quality);
        }
        lastWave = game.Wave; frameCount = 0; windowStart = Time.unscaledTime;
    }

    static void Budget(ElderboomGame game)
    {
        var cam = Camera.main; if (cam == null) return;
        var eye = cam.transform.position;
        foreach (var z in game.Zombies)
        {
            if (z == null || !z.gameObject.activeInHierarchy) continue;
            float d = Vector3.Distance(eye, z.transform.position);
            bool near = d < DetailDistance;
            z.GetComponentsInChildren(skins);
            foreach (var r in skins)
            {
                var shadows = d < ShadowDistance ? ShadowCastingMode.On : ShadowCastingMode.Off;
                if (r.shadowCastingMode != shadows) r.shadowCastingMode = shadows;
                var skin = near ? SkinQuality.Auto : SkinQuality.Bone2;
                if (r.quality != skin) r.quality = skin;
            }
            var agent = z.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                var avoid = d < 30f ? ObstacleAvoidanceType.MedQualityObstacleAvoidance : ObstacleAvoidanceType.LowQualityObstacleAvoidance;
                if (agent.obstacleAvoidanceType != avoid) agent.obstacleAvoidanceType = avoid;
            }
        }
    }

    static void Write(string line)
    {
        try
        {
            path ??= Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "perf_log.txt");
            File.AppendAllText(path, line + "\n");
        }
        catch { }
    }
}
