using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Logs/claude_rangetest.txt -> plays Elderboom_TestRange for a few seconds, fires the weapon, saves Logs/range_*.jpg and
// errors in Logs/claude_rangetest_result.txt, then returns to the scene that was open.
[InitializeOnLoad]
static class ElderboomClaudeRangeTest
{
    const string Flag = "Logs/claude_rangetest.txt", Result = "Logs/claude_rangetest_result.txt";
    const string Stage = "claude_rangetest_stage", Prev = "claude_rangetest_prev", Log = "claude_rangetest_log";
    static double next, playStart;
    static ElderboomClaudeRangeTest()
    {
        EditorApplication.update += Poll;
        // A run cut short (recompile, crash) must not block the next one.
        if (!EditorApplication.isPlayingOrWillChangePlaymode && SessionState.GetInt(Stage, 0) != 0 && SessionState.GetInt(Stage, 0) != 3) { SessionState.SetInt(Stage, 0); SessionState.SetInt("claude_rangetest_shot", 0); }
        Application.logMessageReceived += (msg, st, type) =>
        {
            if (SessionState.GetInt(Stage, 0) == 0) return;
            if (type == LogType.Error || type == LogType.Exception) SessionState.SetString(Log, SessionState.GetString(Log, "") + type + ": " + msg + "\n" + (st.Length > 300 ? st.Substring(0, 300) : st) + "\n");
        };
    }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + .5;
        int stage = SessionState.GetInt(Stage, 0);
        if (stage == 0)
        {
            if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            SessionState.SetString("claude_rangetest_jobs", File.ReadAllText(Flag).Trim() == "go" ? "" : File.ReadAllText(Flag).Trim());
            File.Delete(Flag);
            SessionState.SetString(Prev, EditorSceneManager.GetActiveScene().path); SessionState.SetString(Log, "");
            EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_TestRange.unity", OpenSceneMode.Single);
            SessionState.SetInt(Stage, 1); EditorApplication.isPlaying = true; return;
        }
        if (stage == 1 && EditorApplication.isPlaying) { SessionState.SetInt(Stage, 2); playStart = EditorApplication.timeSinceStartup; return; }
        if (stage == 2 && EditorApplication.isPlaying)
        {
            if (playStart == 0) playStart = EditorApplication.timeSinceStartup;
            double t = EditorApplication.timeSinceStartup - playStart;
            if (ElderboomInput.HeldOverride == null) ElderboomInput.HeldOverride = a => false;
            var spec = SessionState.GetString("claude_rangetest_jobs", "");
            var jobs = new System.Collections.Generic.List<(string op, string w)>();
            foreach (var j in spec.Split(new[] { ' ', '\n' }, System.StringSplitOptions.RemoveEmptyEntries)) { var kv = j.Split(':'); jobs.Add((kv[0].ToUpperInvariant(), kv.Length > 1 ? kv[1] : "shotgun")); }
            if (jobs.Count == 0) { jobs.Add(("TACTICAL", "shotgun")); jobs.Add(("ELI", "shotgun")); jobs.Add(("SOPH", "shotgun")); }
            string[] ops = new string[jobs.Count]; for (int z = 0; z < jobs.Count; z++) ops[z] = jobs[z].op;
            int k = SessionState.GetInt("claude_rangetest_shot", 0);
            int op = k / 4, sub = k % 4; double t0 = 3 + op * 12;
            if (op < jobs.Count)
            {
                if (sub == 0 && t > t0) { ElderboomArsenal.StartingWeapon = jobs[op].w; ElderboomGame.Active?.BeginSession(ops[op], "NORMAL"); k++; }
                else if (sub == 1 && t > t0 + 6) { Shot("range_" + ops[op].ToLower() + "_" + jobs[op].w + "_hip"); Diagnose(ops[op]); ElderboomInput.HeldOverride = a => a == "aim"; k++; }
                else if (sub == 2 && t > t0 + 9.5) { Close(ops[op].ToLower() + "_" + jobs[op].w); Shot("range_" + ops[op].ToLower() + "_" + jobs[op].w + "_ads"); DiagnoseAds(ops[op] + ":" + jobs[op].w); ElderboomInput.HeldOverride = a => a == "aim" || a == "fire"; k++; }
                else if (sub == 3 && t > t0 + 9.6) { Shot("range_" + ops[op].ToLower() + "_" + jobs[op].w + "_fire"); ElderboomInput.HeldOverride = a => false; k++; }
                SessionState.SetInt("claude_rangetest_shot", k);
                return;
            }
            var p = Object.FindFirstObjectByType<HDRPSlicePlayer>();
            File.WriteAllText(Result, "player " + (p ? p.transform.position + " ammo " + p.Ammo + "/" + p.ReserveAmmo : "none") + "\n" + SessionState.GetString(Log, "") + "DONE\n");
            ElderboomInput.HeldOverride = null;
            SessionState.SetInt(Stage, 3); EditorApplication.isPlaying = false;
            return;
        }
        if (stage == 3 && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            SessionState.SetInt(Stage, 0); SessionState.SetInt("claude_rangetest_shot", 0);
            var prev = SessionState.GetString(Prev, ""); if (prev.Length > 0) EditorSceneManager.OpenScene(prev, OpenSceneMode.Single);
        }
    }
    static void DiagnoseAds(string tag)
    {
        var sb = new StringBuilder(SessionState.GetString(Log, ""));
        var p = Object.FindFirstObjectByType<HDRPSlicePlayer>(); var cam = Camera.main;
        var ars = p.GetComponent<ElderboomArsenal>();
        var af = typeof(ElderboomArsenal).GetField("active", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var slot = af?.GetValue(ars); Transform root = slot?.GetType().GetField("root")?.GetValue(slot) as Transform;
        if (root == null) return;
        sb.AppendLine("ADS " + tag + " root lp " + root.localPosition.ToString("F3") + " le " + root.localEulerAngles.ToString("F1"));
        foreach (var f in new[] { "adsPosition", "hipPosition", "adsRotation", "hipRotation", "hasAdsReference", "aiming" })
        { var fi = typeof(HDRPSlicePlayer).GetField(f, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); var v = fi?.GetValue(p); sb.AppendLine("   " + f + " " + (v is Quaternion q ? q.eulerAngles.ToString("F1") : v is Vector3 w ? w.ToString("F3") : "" + v)); }
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            var b = r.bounds; var c = cam.transform.InverseTransformPoint(b.center);
            sb.AppendLine("   R " + r.name + " c " + c.ToString("F2") + " size " + b.size.magnitude.ToString("F2"));
        }
        Transform mz = ElderboomWeaponMods.FindMuzzle(root);
        if (mz != null)
        {
            sb.AppendLine("   MUZZLE world rot " + mz.rotation.eulerAngles.ToString("F2") + " lossy " + mz.lossyScale.ToString("F4"));
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                var bb = r.bounds; Vector3 lo = mz.InverseTransformPoint(bb.center);
                // mesh-space bounds (tighter than world AABB)
                var mf = r.GetComponent<MeshFilter>(); string mb = "";
                if (mf != null && mf.sharedMesh != null) { var mbo = mf.sharedMesh.bounds; mb = " meshc " + mz.InverseTransformPoint(r.transform.TransformPoint(mbo.center)).ToString("F4"); }
                sb.AppendLine("   MZ " + r.name + " c " + lo.ToString("F4") + mb);
            }
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("Socket_")) sb.AppendLine("   MZ " + t.name + " " + mz.InverseTransformPoint(t.position).ToString("F4"));
        }
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == "MainWeapon_ADS_Rear" || t.name == "MainWeapon_ADS_Front") sb.AppendLine("   " + t.name + " " + cam.transform.InverseTransformPoint(t.position).ToString("F3") + " path " + t.parent.name);
        SessionState.SetString(Log, sb.ToString());
    }
    static void Diagnose(string op)
    {
        var sb = new StringBuilder(SessionState.GetString(Log, ""));
        var p = Object.FindFirstObjectByType<HDRPSlicePlayer>(); var cam = Camera.main.transform;
        Transform root = null;
        var ars = p.GetComponent<ElderboomArsenal>();
        var af = typeof(ElderboomArsenal).GetField("active", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var slot = af?.GetValue(ars);
        if (slot != null) root = slot.GetType().GetField("root")?.GetValue(slot) as Transform;
        if (root == null) root = p.ActiveWeaponRoot;
        sb.AppendLine("== " + op + " root " + (root ? root.name + " local " + root.localPosition.ToString("F3") + " active " + root.gameObject.activeInHierarchy : "null"));
        if (root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                bool interesting = t.parent == root || t.name.StartsWith("OperatorWeapon_") || t.name.EndsWith("_Grips") || t.name == "DEF-hand.R" || t.name == "DEF_hand.R" || t.name == "Hand.R" || t.name == "G_body";
                if (!interesting) continue;
                var r = t.GetComponent<Renderer>();
                sb.AppendLine("  " + t.name + " cam-local " + cam.InverseTransformPoint(t.position).ToString("F3") + " ls " + t.lossyScale.ToString("F3") + " act " + t.gameObject.activeInHierarchy + (r ? " bounds-c " + cam.InverseTransformPoint(r.bounds.center).ToString("F3") + " en " + r.enabled : ""));
            }
        }
        if (root)
        {
            var v = root.GetComponent<ElderboomViewmodel>();
            if (v != null)
            {
                Transform h = null; foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == "DEF-hand.R" || t.name == "DEF_hand.R" || t.name == "Hand.R") { h = t; break; }
                sb.AppendLine("  view model " + (v.model ? v.model.name : "-") + " idle " + (v.idle ? v.idle.name : "-") + " hand before " + (h ? cam.InverseTransformPoint(h.position).ToString("F3") : "-"));
                ElderboomAnimationSampling.Sample(v.idle, v.model, 0f);
                sb.AppendLine("  hand after manual sample " + (h ? cam.InverseTransformPoint(h.position).ToString("F3") : "-"));
                var an = v.model.GetComponent<Animator>();
                if (an) { sb.AppendLine("  animator avatar " + (an.avatar ? an.avatar.name + " human " + an.avatar.isHuman : "none") + " ctrl " + (an.runtimeAnimatorController ? an.runtimeAnimatorController.name : "none") + " graph " + an.playableGraph.IsValid() + " rootMotion " + an.applyRootMotion); an.enabled = false; }
                ElderboomAnimationSampling.Sample(v.idle, v.model, 0f);
                sb.AppendLine("  hand after sample, animator off " + (h ? cam.InverseTransformPoint(h.position).ToString("F3") : "-"));
                if (an) an.enabled = true;
                for (var q = h; q != null && q != root; q = q.parent) sb.AppendLine("   chain " + q.name + " lp " + q.localPosition.ToString("F3") + " lr " + q.localEulerAngles.ToString("F1") + " ls " + q.localScale.ToString("F3"));
                foreach (var c in root.GetComponentsInChildren<Behaviour>(true)) sb.AppendLine("   beh " + c.GetType().Name + " on " + c.name + " en " + c.enabled);
            }
        }
        SessionState.SetString(Log, sb.ToString());
    }

    // Close-up renders of the left wrist prop (watch / shockwave device) from a temporary camera.
    static void Close(string tag)
    {
        var cam = Camera.main; var p = Object.FindFirstObjectByType<HDRPSlicePlayer>(); if (cam == null || p == null) return;
        Transform target = null, fore = null, hand = null;
        foreach (var t in p.GetComponentsInChildren<Transform>(false))
        {
            if (target == null && (t.name.EndsWith("_Watch") || t.name == "Tactical_ShockwaveDevice") && t.GetComponent<Renderer>() != null) target = t;
            if (t.name == "DEF-forearm.L.001") fore = t; if (t.name == "DEF-hand.L") hand = t;
        }
        if (fore == null || hand == null) return;
        Vector3 pt = target != null ? target.GetComponent<Renderer>().bounds.center : hand.position;
        Vector3 axis = (hand.position - fore.position).normalized;
        Vector3 axisPt = fore.position + Vector3.Project(pt - fore.position, axis);
        Vector3 outw = Vector3.ProjectOnPlane(pt - axisPt, axis); if (outw.sqrMagnitude < 1e-8f) outw = -cam.transform.forward; outw.Normalize();
        var go = new GameObject("ClaudeCloseCam"); var c2 = go.AddComponent<Camera>(); c2.CopyFrom(cam); c2.fieldOfView = 30f; c2.nearClipPlane = .01f;
        Vector3[] dirs = { outw, (outw + (cam.transform.position - pt).normalized).normalized, Vector3.Cross(axis, outw).normalized * .6f + outw * .8f };
        for (int i = 0; i < dirs.Length; i++)
        {
            go.transform.position = pt + dirs[i].normalized * .2f; go.transform.rotation = Quaternion.LookRotation(pt - go.transform.position, axis);
            var rt = new RenderTexture(1200, 900, 24); var tex = new Texture2D(1200, 900, TextureFormat.RGB24, false);
            c2.targetTexture = rt; c2.Render(); c2.targetTexture = null;
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1200, 900), 0, 0); tex.Apply(); RenderTexture.active = null;
            File.WriteAllBytes("Logs/range_" + tag + "_wrist" + i + ".jpg", tex.EncodeToJPG(90)); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        }
        Object.DestroyImmediate(go);
    }
    static void Shot(string name)
    {
        var cam = Camera.main; if (cam == null) return;
        var rt = new RenderTexture(1600, 900, 24); var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        var old = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = old;
        RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply(); RenderTexture.active = null;
        File.WriteAllBytes("Logs/" + name + ".jpg", tex.EncodeToJPG(90)); ScreenCapture.CaptureScreenshot("Logs/" + name + "_ui.png"); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
    }
}
