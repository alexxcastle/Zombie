using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-shot jobs requested from outside the Editor (no scene edits are saved, the Editor stays open):
//   Logs/claude_capture_now.txt  play the menu scene, select Eli, screenshot the operator set (hang + drop)
//                                to Logs/menu_stage_hang.png / menu_stage_drop.png, then stop play mode.
//   Logs/claude_refresh_now.txt  AssetDatabase.Refresh() (picks up files changed outside the Editor)
//   Logs/claude_build_now.txt    rebuild Builds/ElderboomUnityPort.app (after any capture), result in
//                                Logs/claude_build_result.json with the full-body model import check.
[InitializeOnLoad]
static class ElderboomClaudeBuild
{
    const string BuildFlag = "Logs/claude_build_now.txt";
    const string CaptureFlag = "Logs/claude_capture_now.txt";
    const string CaptureState = "Library/claude_capture_state.txt";
    const string AppPath = "Builds/ElderboomUnityPort.app";
    static double readyAt, captureAt;
    static int captureStep, skyIndex;
    const string SkySweepFlag = "Logs/claude_sky_sweep.txt";
    static readonly int[] SkyAngles = { 0, 45, 90, 135, 180, 225, 270, 315 };

    const string RefreshFlag = "Logs/claude_refresh_now.txt";
    const string MountFlag = "Logs/claude_capture_mount.txt";      // third-person ride check on the mechanical horse
    const string MountState = "Library/claude_mount_state.txt";
    static int mountStep; static double mountAt;
    static double nextPoll;
    static bool ticking;

    static ElderboomClaudeBuild()
    {
        // Lightweight watcher (every 2 s): lets jobs be queued while the Editor sits in the background,
        // where Unity does not auto-refresh changed files on its own.
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 2;
        if (File.Exists(RefreshFlag) && !Busy && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete(RefreshFlag);
            AssetDatabase.Refresh();
            return;
        }
        if (!ticking && (File.Exists(CaptureFlag) || File.Exists(CaptureState) || File.Exists(BuildFlag) || File.Exists(MountFlag) || File.Exists(MountState)))
        {
            ticking = true;
            readyAt = EditorApplication.timeSinceStartup + 3;
            EditorApplication.update += Tick;
        }
    }

    static bool Busy => EditorApplication.isCompiling || EditorApplication.isUpdating;

    static void Tick()
    {
        if (Busy) { readyAt = EditorApplication.timeSinceStartup + 3; return; }
        if (EditorApplication.timeSinceStartup < readyAt) return;

        // 1) start a capture
        if (File.Exists(CaptureFlag) && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete(CaptureFlag);
            File.WriteAllText(CaptureState, "play");
            EditorSceneManager.OpenScene("Assets/Scenes/ZombieTest_Menu.unity");
            EditorApplication.isPlaying = true;
            return;
        }
        // 2) drive the capture inside play mode
        if (File.Exists(CaptureState))
        {
            if (!EditorApplication.isPlaying) { if (!EditorApplication.isPlayingOrWillChangePlaymode) File.Delete(CaptureState); return; }
            CaptureTick();
            return;
        }
        // 2b) mount capture
        if (File.Exists(MountFlag) && !File.Exists(CaptureState) && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete(MountFlag);
            File.WriteAllText(MountState, "play");
            mountStep = 0;
            EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
            EditorApplication.isPlaying = true;
            return;
        }
        if (File.Exists(MountState))
        {
            if (!EditorApplication.isPlaying) { if (!EditorApplication.isPlayingOrWillChangePlaymode) File.Delete(MountState); return; }
            MountTick();
            return;
        }
        // 3) build
        if (File.Exists(BuildFlag) && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.update -= Tick; ticking = false;
            string targets = File.ReadAllText(BuildFlag).ToLowerInvariant();
            File.Delete(BuildFlag);
            // Flag content: empty or "mac" = macOS, "windows" = Windows x64, "all" = both.
            bool win = targets.Contains("win") || targets.Contains("all");
            bool mac = !targets.Contains("win") || targets.Contains("all") || targets.Contains("mac");
            if (mac) Build(false);
            if (win) Build(true);
        }
        else if (!File.Exists(BuildFlag) && !File.Exists(CaptureFlag) && !File.Exists(MountFlag)) { EditorApplication.update -= Tick; ticking = false; }
    }

    static void CaptureTick()
    {
        var menu = Object.FindAnyObjectByType<ElderboomMenu>();
        if (menu == null) return;
        var t = typeof(ElderboomMenu);
        double now = EditorApplication.timeSinceStartup;
        switch (captureStep)
        {
            case 0:
                ElderboomMenu.Operator = ElderboomIds.Eli;
                var pageType = t.GetNestedType("Page", BindingFlags.NonPublic);
                t.GetMethod("GoTo", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(menu, new[] { System.Enum.Parse(pageType, "Operators") });
                captureAt = now + .8; captureStep = 10; break;
            case 10:
                // The page focuses its first entry (which selects it): pick Eli again after that.
                if (now < captureAt) return;
                SelectEli(menu);
                captureAt = now + 4.0; captureStep = 1; break;
            case 1:
                if (now < captureAt) return;
                if (ElderboomMenu.Operator != ElderboomIds.Eli)
                {
                    // Something (focus / hover on the list) re-selected another entry: select Eli again and wait.
                    SelectEli(menu);
                    captureAt = now + 3.0; return;
                }
                ScreenCapture.CaptureScreenshot("Logs/menu_stage_hang.png");
                captureAt = now + 1.0; captureStep = File.Exists(SkySweepFlag) ? 20 : 2; skyIndex = 0; break;
            case 20:
                // Optional sweep of the sky rotation (Logs/claude_sky_sweep.txt): one shot per angle.
                if (now < captureAt) return;
                if (skyIndex > 0) ScreenCapture.CaptureScreenshot("Logs/menu_sky_" + SkyAngles[skyIndex - 1] + ".png");
                if (skyIndex >= SkyAngles.Length) { File.Delete(SkySweepFlag); captureAt = now + 1.0; captureStep = 2; break; }
                Object.FindAnyObjectByType<ElderboomOperatorStage>()?.SetSkyRotation(SkyAngles[skyIndex++]);
                captureAt = now + 1.2; break;
            case 2:
                if (now < captureAt) return;
                var stage = Object.FindAnyObjectByType<ElderboomOperatorStage>();
                if (stage != null) stage.Drop();
                captureAt = now + .75; captureStep = 3; break;
            case 3:
                if (now < captureAt) return;
                ScreenCapture.CaptureScreenshot("Logs/menu_stage_drop.png");
                captureAt = now + 1.0; captureStep = 4; break;
            case 4:
                if (now < captureAt) return;
                File.WriteAllText("Logs/menu_stage_report.txt", StageReport());
                File.Delete(CaptureState);
                captureStep = 0;
                EditorApplication.isPlaying = false;
                readyAt = now + 5;
                break;
        }
    }

    static void MountTick()
    {
        double now = EditorApplication.timeSinceStartup;
        var mount = Object.FindAnyObjectByType<ElderboomMount>();
        var t = typeof(ElderboomMount); const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
        switch (mountStep)
        {
            case 0:
                if (mount == null || Object.FindAnyObjectByType<HDRPSlicePlayer>() == null) return;
                mountAt = now + 4.0; mountStep = 1; break;
            case 1:
                if (now < mountAt) return;
                ElderboomMenu.Operator = ElderboomIds.Eli;
                if (ElderboomGame.Active != null)   // the match itself must run as Eli (third person needs her body)
                    typeof(ElderboomGame).GetField("selectedOperator", F)?.SetValue(ElderboomGame.Active, ElderboomIds.Eli);
                // This isolated ride-pose preview bypasses the salvage objective; ordinary play does not.
                mount.SetAssembled(true);
                t.GetMethod("Mount", F)?.Invoke(mount, null);
                mountAt = now + 1.0; mountStep = 2; break;
            case 2:
                if (now < mountAt) return;
                t.GetMethod("SetThirdPerson", F)?.Invoke(mount, new object[] { true });
                mountAt = now + 3.0; mountStep = 3; break;
            case 3:
                if (now < mountAt) return;
                ScreenCapture.CaptureScreenshot("Logs/mount_back.png");
                t.GetField("lookYaw", F)?.SetValue(mount, 90f); t.GetField("lookPitch", F)?.SetValue(mount, 8f);
                mountAt = now + 1.5; mountStep = 4; break;
            case 4:
                if (now < mountAt) return;
                ScreenCapture.CaptureScreenshot("Logs/mount_side.png");
                t.GetField("lookYaw", F)?.SetValue(mount, -90f);
                mountAt = now + 1.5; mountStep = 5; break;
            case 5:
                if (now < mountAt) return;
                ScreenCapture.CaptureScreenshot("Logs/mount_side2.png");
                var sb = new StringBuilder();
                var rider = mount != null ? mount.GetComponentInChildren<ElderboomCharacterBody>(true) : null;
                sb.AppendLine("mounted=" + (mount != null && mount.Mounted) + " third=" + (mount != null && mount.ThirdPerson) + " rider=" + (rider != null));
                if (rider != null)
                {
                    sb.AppendLine("riderLocal=" + mount.transform.InverseTransformPoint(rider.transform.position).ToString("F3") + " clip=" + rider.CurrentClip);
                    var hips = rider.Bone(HumanBodyBones.Hips); var lh = rider.Bone(HumanBodyBones.LeftHand); var rh = rider.Bone(HumanBodyBones.RightHand);
                    if (hips != null) sb.AppendLine("hipsLocal=" + mount.transform.InverseTransformPoint(hips.position).ToString("F3"));
                    if (lh != null && rh != null) sb.AppendLine("handsLocal=" + mount.transform.InverseTransformPoint((lh.position + rh.position) * .5f).ToString("F3"));
                }
                var cam = mount != null ? mount.transform.Find("RiderCamera") : null;
                File.WriteAllText("Logs/mount_report.txt", sb.ToString());
                File.Delete(MountState);
                mountStep = 0;
                EditorApplication.isPlaying = false;
                readyAt = now + 5;
                break;
        }
    }

    // Select Eli the way a player does: keyboard focus on its list entry (the list selects what has focus).
    static void SelectEli(ElderboomMenu menu)
    {
        var t = typeof(ElderboomMenu);
        var items = t.GetField("operatorItems", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(menu) as System.Collections.IEnumerable;
        if (items != null)
            foreach (var o in items)
            {
                var entry = ((UnityEngine.UIElements.VisualElement, string))o;
                if (entry.Item2 == ElderboomIds.Eli) entry.Item1.Focus();
            }
        ElderboomMenu.Operator = ElderboomIds.Eli;
        t.GetMethod("RefreshSelection", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(menu, null);
    }

    static string StageReport()
    {
        var sb = new StringBuilder();
        var stage = Object.FindAnyObjectByType<ElderboomOperatorStage>();
        sb.AppendLine("stage=" + (stage != null));
        if (stage != null)
        {
            sb.AppendLine("camera=" + (stage.Camera != null ? stage.Camera.transform.position.ToString("F2") : "none"));
            sb.AppendLine("body=" + (stage.Body != null ? stage.Body.transform.position.ToString("F2") + " clip=" + stage.Body.CurrentClip : "none"));
            foreach (var l in stage.GetComponentsInChildren<Light>()) sb.AppendLine("light " + l.name + " " + l.type + " i=" + l.intensity);
            sb.AppendLine("renderers=" + stage.GetComponentsInChildren<Renderer>().Length);
            var body = stage.Body;
            if (body != null)
            {
                var rh = body.Bone(HumanBodyBones.RightHand); var lh = body.Bone(HumanBodyBones.LeftHand);
                var gun = body.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name.Contains("AKMN"));
                sb.AppendLine("weapon=" + (gun != null ? gun.transform.parent.name : "none"));
                if (gun != null && lh != null && rh != null)
                    sb.AppendLine("leftHandToWeapon=" + Vector3.Distance(gun.bounds.ClosestPoint(lh.position), lh.position).ToString("F3") +
                                  " rightHandToWeapon=" + Vector3.Distance(gun.bounds.ClosestPoint(rh.position), rh.position).ToString("F3"));
                foreach (var r in body.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials) if (m != null) sb.AppendLine("mat " + r.name + " " + m.name + " tex=" + (m.HasProperty("_BaseColorMap") && m.GetTexture("_BaseColorMap") != null ? m.GetTexture("_BaseColorMap").name : "-"));
            }
        }
        return sb.ToString();
    }

    const string WinPath = "Builds/Windows/ElderboomUnityPort.exe";

    // Terminal (editor closed): -executeMethod ElderboomClaudeBuild.BuildWindowsBatch / BuildMacBatch
    public static void BuildWindowsBatch() { Build(true); EditorApplication.Exit(File.ReadAllText("Logs/claude_build_result_windows.json").Contains("\"Succeeded\"") ? 0 : 1); }
    public static void BuildMacBatch() { Build(false); EditorApplication.Exit(File.ReadAllText("Logs/claude_build_result.json").Contains("\"Succeeded\"") ? 0 : 1); }

    const int MinFreeGB = 8; // the integrity check after the build catches a disk that filled up anyway
    static long FreeBytes()
    {
        try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath("."))).AvailableFreeSpace; } catch { return long.MaxValue; }
    }
    static long DirBytes(string dir) => Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);

    // After a build: no empty data files and no data file whose start is all zeros (what a full disk leaves behind).
    static string Integrity(string dir)
    {
        if (!Directory.Exists(dir)) return "MISSING " + dir;
        int files = 0, bad = 0; long bytes = 0; var badNames = new StringBuilder();
        var buf = new byte[4096];
        foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
        {
            files++; long len = new FileInfo(f).Length; bytes += len;
            string n = Path.GetFileName(f);
            bool data = n.EndsWith(".assets") || n.EndsWith(".resS") || n.EndsWith(".resource") || n.StartsWith("level") || n == "globalgamemanagers" || n.EndsWith(".dll") || n.EndsWith(".exe");
            if (!data) continue;
            bool ok = len > 0;
            if (ok && !n.EndsWith(".resS")) // raw texture data may legitimately start with zeros
                using (var fs = File.OpenRead(f))
                {
                    int r = fs.Read(buf, 0, buf.Length); ok = false;
                    for (int i = 0; i < r; i++) if (buf[i] != 0) { ok = true; break; }
                }
            if (!ok) { bad++; if (bad < 6) badNames.Append(n).Append(' '); }
        }
        return (bad == 0 ? "OK " : "CORRUPT " + bad + " (" + badNames.ToString().Trim() + ") ") + files + " files " + (bytes / 1048576) + " MB";
    }

    static void Build(bool windows)
    {
        var check = new StringBuilder();
        foreach (var dir in Directory.GetDirectories("Assets/Resources/Elderboom/Characters"))
        {
            string op = Path.GetFileName(dir);
            var avatar = AssetDatabase.LoadAllAssetsAtPath(dir + "/" + op + "_Rigged.fbx").OfType<Avatar>().FirstOrDefault();
            check.Append(op).Append(" avatar=").Append(avatar != null && avatar.isValid ? (avatar.isHuman ? "human" : "generic") : "INVALID");
            int clips = 0, bad = 0;
            foreach (var f in Directory.GetFiles(dir + "/Anims", "*.fbx"))
            {
                var c = AssetDatabase.LoadAllAssetsAtPath(f).OfType<AnimationClip>().FirstOrDefault(x => !x.name.StartsWith("__preview"));
                if (c != null) clips++; else bad++;
            }
            check.Append(" clips=").Append(clips).Append(" bad=").Append(bad).Append("; ");
        }
        check.Append("set=").Append(Resources.Load<GameObject>("Elderboom/Menu/OperatorSet") != null ? "ok" : "MISSING");

        string path = windows ? WinPath : AppPath;
        string outDir = windows ? "Builds/Windows" : AppPath;
        // A build that runs out of disk is silently corrupt (crash at start): refuse below the margin.
        long free = FreeBytes(), old = Directory.Exists(outDir) ? DirBytes(outDir) : 0;
        if (free + old < MinFreeGB * (1L << 30))
        {
            File.WriteAllText(windows ? "Logs/claude_build_result_windows.json" : "Logs/claude_build_result.json",
                "{\"target\":\"" + (windows ? "windows" : "mac") + "\",\"status\":\"NoSpace\",\"freeGB\":" + (free >> 30) + ",\"needGB\":" + MinFreeGB + "}");
            Debug.LogError("[ElderboomClaudeBuild] not enough disk: " + (free >> 30) + " GB free (+" + (old >> 30) + " GB old build), need " + MinFreeGB);
            return;
        }
        if (!windows && Directory.Exists(AppPath)) Directory.Delete(AppPath, true);
        if (windows && Directory.Exists("Builds/Windows")) Directory.Delete("Builds/Windows", true);
        Directory.CreateDirectory(windows ? "Builds/Windows" : "Builds");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/ZombieTest_Menu.unity", "Assets/Scenes/Elderboom_Unity_Port.unity" },
            locationPathName = path,
            target = windows ? BuildTarget.StandaloneWindows64 : BuildTarget.StandaloneOSX,
            options = BuildOptions.None
        });
        var s = report.summary;
        var errors = new StringBuilder();
        foreach (var step in report.steps)
            foreach (var m in step.messages)
                if (m.type == LogType.Error || m.type == LogType.Exception) errors.Append(m.content.Replace("\\", "/").Replace("\"", "'").Replace("\n", " ")).Append(" | ");
        Directory.CreateDirectory("Logs");
        File.WriteAllText(windows ? "Logs/claude_build_result_windows.json" : "Logs/claude_build_result.json",
            "{\"target\":\"" + (windows ? "windows" : "mac") + "\",\"status\":\"" + s.result + "\",\"errors\":" + s.totalErrors + ",\"seconds\":" + s.totalTime.TotalSeconds.ToString("F1") +
            ",\"characters\":\"" + check + "\",\"integrity\":\"" + (s.result == BuildResult.Succeeded ? Integrity(outDir) : "-") + "\",\"freeGBAfter\":" + (FreeBytes() >> 30) + ",\"errorText\":\"" + errors + "\"}");
        Debug.Log("[ElderboomClaudeBuild] " + s.result + " errors=" + s.totalErrors + " " + check);
    }
}
