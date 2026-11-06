using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Editor automation: Logs/claude_weapon_audit.txt plays the game scene and, for every operator and every weapon,
// equips it and saves the first-person view at hip and while aiming (ADS):
//   Logs/weapon_audit/<op>_<weapon>_hip.png / _ads.png, report in Logs/weapon_audit/report.txt
// Optional flag content: operator ids and/or weapon ids to restrict the sweep (e.g. "eli akmn rhino").
[InitializeOnLoad]
static class ElderboomClaudeWeaponAudit
{
    const string Flag = "Logs/claude_weapon_audit.txt", Pending = "claude_weapon_audit_pending", Filter = "claude_weapon_audit_filter";
    const string Out = "Logs/weapon_audit";
    static double nextAt, playSeen;
    static int step = -1;
    static List<(string op, string weapon)> jobs;
    static string currentOp;
    static StringBuilder report;
    static bool calibrating, abilityMode;
    static float abilityLength;
    static readonly List<ElderboomAuthoredGrip.SightLine> calibration = new List<ElderboomAuthoredGrip.SightLine>();

    static ElderboomClaudeWeaponAudit() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (SessionState.GetBool(Pending, false))
        {
            if (!EditorApplication.isPlaying) { if (!EditorApplication.isPlayingOrWillChangePlaymode) Stop(); return; }
            if (playSeen <= 0) playSeen = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - playSeen < 4 || EditorApplication.timeSinceStartup < nextAt) return;
            Tick();
            return;
        }
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
        string filter = File.ReadAllText(Flag).Trim().ToLowerInvariant();
        File.Delete(Flag);
        if (EditorSceneManager.GetActiveScene().isDirty) { Directory.CreateDirectory(Out); File.WriteAllText(Out + "/report.txt", "FAIL: open scene has unsaved changes\n"); return; }
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        SessionState.SetString(Filter, filter);
        SessionState.SetBool(Pending, true);
        EditorApplication.isPlaying = true;
    }

    static void Stop()
    {
        SessionState.SetBool(Pending, false);
        ElderboomInput.HeldOverride = null;
        step = -1; jobs = null; playSeen = 0; currentOp = null;
    }

    static void Shot(string name) => ScreenCapture.CaptureScreenshot(Out + "/" + name + ".png");

    static void Log(string line) { report.AppendLine(line); File.WriteAllText(Out + "/report.txt", report.ToString()); }

    static object Field(object o, string name) =>
        o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)?.GetValue(o);

    // Where the sights are on screen (viewport 0..1, centre 0.5/0.5) and the weapon root's pose vs its hip/ads targets.
    static string Sights(HDRPSlicePlayer player, string tag)
    {
        var cam = player.ViewCamera;
        var rear = Field(player, "adsRearSight") as Transform;
        var front = Field(player, "adsFrontSight") as Transform;
        var root = player.ActiveWeaponRoot;
        string V(Transform t) { if (t == null || cam == null) return "-"; var v = cam.WorldToViewportPoint(t.position); return v.x.ToString("F3") + "," + v.y.ToString("F3") + " d" + v.z.ToString("F2"); }
        string P(object v) => v is Vector3 x ? x.ToString("F3") : "-";
        // The visible weapon (renderers under OperatorWeapon_*): its bounds centre on screen, and the markers' parents.
        string gun = "-";
        if (root != null && cam != null)
        {
            Bounds b = default; bool any = false;
            foreach (var t in root.GetComponentsInChildren<Transform>(false))
                if (t.name.StartsWith("OperatorWeapon_"))
                    foreach (var r in t.GetComponentsInChildren<Renderer>(false)) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            if (any) { var v = cam.WorldToViewportPoint(b.center); gun = v.x.ToString("F3") + "," + v.y.ToString("F3") + " d" + v.z.ToString("F2") + " size" + b.size.magnitude.ToString("F2"); }
        }
        string Path(Transform t) { if (t == null) return "-"; string p = t.name; for (var x = t.parent; x != null && x != root; x = x.parent) p = x.name + "/" + p; return p; }
        if (tag == "hip") gun += " rearPath=" + Path(rear);
        return " gun=" + gun + " rear=" + (rear != null ? rear.name : "none") + "@" + V(rear) + " front=" + (front != null ? front.name : "none") + "@" + V(front) +
               " obstruct=" + Field(player, "obstruct") + " root=" + (root != null ? root.localPosition.ToString("F3") : "-") + " hipT=" + P(Field(player, "hipPosition")) + " adsT=" + P(Field(player, "adsPosition"));
    }

    // Sight line from the held weapon's geometry: the bore axis comes from the muzzle marker, "up" points from the
    // gripping hand to the weapon, and the line runs just over the highest point near the centreline (where sights,
    // rails and ribs are), parallel to the bore. Stored in the muzzle's local space (operator independent).
    static string Calibrate(HDRPSlicePlayer player, string weaponId)
    {
        var root = player.ActiveWeaponRoot;
        Transform holder = null;
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == "OperatorWeapon_" + weaponId) { holder = t; break; }
        if (holder == null) return "no holder";
        Transform muzzle = null;
        foreach (var t in holder.GetComponentsInChildren<Transform>(true)) if (t.name.ToLowerInvariant().Contains("muzzle")) { muzzle = t; break; }
        if (muzzle == null) return "no muzzle";
        string axisNote = "";
        var pts = new List<Vector3>();
        foreach (var mf in holder.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = mf.sharedMesh; if (mesh == null) continue;
            var verts = new List<Vector3>();
            try { using (var data = Mesh.AcquireReadOnlyMeshData(mesh)) { var arr = new Unity.Collections.NativeArray<Vector3>(data[0].vertexCount, Unity.Collections.Allocator.Temp); data[0].GetVertices(arr); verts.AddRange(arr); arr.Dispose(); } }
            catch { try { verts.AddRange(mesh.vertices); } catch { } }
            int stepV = Mathf.Max(1, verts.Count / 20000);
            for (int i = 0; i < verts.Count; i += stepV) pts.Add(mf.transform.TransformPoint(verts[i]));
        }
        if (pts.Count < 10) return "no vertices";
        // Bore axis: the muzzle axis with the largest extent, pointing out of the muzzle.
        Vector3 o = muzzle.position, axis = muzzle.forward; float best = -1;
        foreach (var cand in new[] { muzzle.forward, muzzle.up, muzzle.right })
        {
            float mn = float.MaxValue, mx = float.MinValue, mean = 0;
            foreach (var p in pts) { float d = Vector3.Dot(p - o, cand); mn = Mathf.Min(mn, d); mx = Mathf.Max(mx, d); mean += d; }
            if (mx - mn > best) { best = mx - mn; axis = mean > 0 ? -cand : cand; }
        }
        // Refine: principal axis of the front 60% (barrel, handguard, slide), which is straight, unlike stock and grip.
        {
            float sm = 0; foreach (var p in pts) sm = Mathf.Min(sm, Vector3.Dot(p - o, axis));
            var front = new List<Vector3>();
            foreach (var p in pts) if (Vector3.Dot(p - o, axis) > sm * 0.6f) front.Add(p);
            if (front.Count > 20)
            {
                Vector3 c = Vector3.zero; foreach (var p in front) c += p; c /= front.Count;
                float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
                foreach (var p in front) { var d = p - c; xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z; yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z; }
                Vector3 v = axis;
                for (int it = 0; it < 50; it++)
                {
                    v = new Vector3(xx * v.x + xy * v.y + xz * v.z, xy * v.x + yy * v.y + yz * v.z, xz * v.x + yz * v.y + zz * v.z).normalized;
                }
                if (Vector3.Dot(v, axis) < 0) v = -v;
                axisNote = " pcaAngle=" + Vector3.Angle(v, axis).ToString("F1");
                axis = v;
            }
        }
        Vector3 center = Vector3.zero; foreach (var p in pts) center += p; center /= pts.Count;
        // "Up": the weapon's tallest direction across the bore (magazine and grip hang below), principal axis of all
        // points projected off the bore; its sign from the gripping hand, which is below the weapon.
        Vector3 hint = Vector3.ProjectOnPlane(center - holder.parent.position, axis);
        Vector3 up = hint;
        {
            float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (var p in pts) { var d = Vector3.ProjectOnPlane(p - center, axis); xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z; yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z; }
            Vector3 v = hint.sqrMagnitude > 1e-8f ? hint.normalized : Vector3.ProjectOnPlane(muzzle.up, axis).normalized;
            for (int it = 0; it < 50; it++)
                v = Vector3.ProjectOnPlane(new Vector3(xx * v.x + xy * v.y + xz * v.z, xy * v.x + yy * v.y + yz * v.z, xz * v.x + yz * v.y + zz * v.z), axis).normalized;
            if (v.sqrMagnitude > 0.5f) { if (Vector3.Dot(v, hint) < 0) v = -v; axisNote += " roll=" + Vector3.Angle(v, hint).ToString("F1"); up = v; }
        }
        if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(muzzle.up, axis);
        up.Normalize();
        Vector3 lat = Vector3.Cross(up, axis).normalized;
        float sMin = 0; foreach (var p in pts) sMin = Mathf.Min(sMin, Vector3.Dot(p - o, axis));
        // Centre the line on the weapon: median sideways offset of the front 60% (the muzzle marker may sit off-axis).
        {
            var side = new List<float>();
            foreach (var p in pts) { var d = p - o; if (Vector3.Dot(d, axis) > sMin * 0.6f) side.Add(Vector3.Dot(d, lat)); }
            if (side.Count > 0) { side.Sort(); float off = side[side.Count / 2]; o += lat * off; axisNote += " side=" + off.ToString("F3"); }
        }
        float top = float.MinValue;
        foreach (float width in new[] { 0.012f, 0.025f, 0.05f })
        {
            foreach (var p in pts)
            {
                var d = p - o; float sv = Vector3.Dot(d, axis);
                if (sv < sMin * 0.9f || sv > sMin * 0.03f || Mathf.Abs(Vector3.Dot(d, lat)) > width) continue;
                top = Mathf.Max(top, Vector3.Dot(d, up));
            }
            if (top > float.MinValue) break;
        }
        if (top == float.MinValue) return "no top";
        float height = top + 0.003f;
        Vector3 rearW = o + up * height + axis * (sMin * 0.8f), frontW = o + up * height - axis * 0.02f;
        calibration.Add(new ElderboomAuthoredGrip.SightLine { id = weaponId, rear = muzzle.InverseTransformPoint(rearW), front = muzzle.InverseTransformPoint(frontW), up = muzzle.InverseTransformPoint(rearW + up * 0.05f) });
        return "calibrated length=" + (-sMin).ToString("F3") + " height=" + height.ToString("F3") + " pts=" + pts.Count + " muzzle=" + muzzle.name + axisNote;
    }

    // Turn the player towards open space (nothing within 6 m), so the weapon is never pulled in as "obstructed".
    static void FaceOpenSpace(HDRPSlicePlayer player)
    {
        var cam = player.ViewCamera; if (cam == null) return;
        for (int i = 0; i < 16; i++)
        {
            if (!Physics.Raycast(cam.transform.position, cam.transform.forward, 6f, ElderboomCombat.WorldMask, QueryTriggerInteraction.Ignore)) return;
            player.transform.Rotate(0f, 22.5f, 0f, Space.World);
        }
    }

    static void Tick()
    {
        var game = ElderboomGame.Active;
        var player = game != null ? game.Player : null;
        if (game == null || player == null) { nextAt = EditorApplication.timeSinceStartup + 1; return; }
        HDRPSlicePlayer.DevInvulnerable = true;
        foreach (var z in game.Zombies) if (z != null) Object.Destroy(z.gameObject);

        if (jobs == null)
        {
            // Clean pictures: no motion blur or depth of field (on runtime copies of the profiles, assets untouched).
            // Same as ElderboomHitFeedbackValidation: a top-priority volume turning both off, no TAA ghosting.
            var still = new GameObject("Weapon audit stills").AddComponent<UnityEngine.Rendering.Volume>();
            still.isGlobal = true; still.priority = 1000;
            still.sharedProfile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
            still.sharedProfile.Add<UnityEngine.Rendering.HighDefinition.MotionBlur>(true).intensity.Override(0f);
            still.sharedProfile.Add<UnityEngine.Rendering.HighDefinition.DepthOfField>(true).focusMode.Override(UnityEngine.Rendering.HighDefinition.DepthOfFieldMode.Off);
            var hd = player.ViewCamera != null ? player.ViewCamera.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>() : null;
            if (hd != null) hd.antialiasing = UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            Directory.CreateDirectory(Out);
            foreach (var f in Directory.GetFiles(Out, "*.png")) File.Delete(f);
            report = new StringBuilder();
            string filter = SessionState.GetString(Filter, "");
            calibrating = filter.Contains("calibrate");
            abilityMode = filter.Contains("ability");
            calibration.Clear();
            var words = new HashSet<string>(filter.Split(new[] { ' ', '\n', ',' }, System.StringSplitOptions.RemoveEmptyEntries));
            bool opFilter = false, weaponFilter = false;
            foreach (var op in ElderboomOperators.All) if (words.Contains(op.id.ToLowerInvariant())) opFilter = true;
            foreach (var w in ElderboomWeapons.All) if (words.Contains(w.id.ToLowerInvariant())) weaponFilter = true;
            jobs = new List<(string, string)>();
            foreach (var op in ElderboomOperators.All)
            {
                if (opFilter && !words.Contains(op.id.ToLowerInvariant())) continue;
                foreach (var w in ElderboomWeapons.All)
                    if (!weaponFilter || words.Contains(w.id.ToLowerInvariant())) jobs.Add((op.id, w.id));
            }
            Log("jobs " + jobs.Count);
            step = 0;
        }
        int per = abilityMode ? 6 : 3;
        if (step >= jobs.Count * per)
        {
            if (calibrating)
            {
                var sb = new StringBuilder("{\n  \"sights\": [\n");
                for (int i = 0; i < calibration.Count; i++)
                {
                    var c = calibration[i];
                    string V(Vector3 v) => "{\"x\":" + v.x.ToString("F5", System.Globalization.CultureInfo.InvariantCulture) + ",\"y\":" + v.y.ToString("F5", System.Globalization.CultureInfo.InvariantCulture) + ",\"z\":" + v.z.ToString("F5", System.Globalization.CultureInfo.InvariantCulture) + "}";
                    sb.Append("    {\"id\":\"" + c.id + "\",\"rear\":" + V(c.rear) + ",\"front\":" + V(c.front) + ",\"up\":" + V(c.up) + "}" + (i < calibration.Count - 1 ? "," : "") + "\n");
                }
                sb.Append("  ]\n}\n");
                File.WriteAllText("Assets/Resources/Elderboom/GripSights.json", sb.ToString());
                Log("wrote GripSights.json with " + calibration.Count + " weapons");
            }
            Log("DONE");
            Stop();
            EditorApplication.isPlaying = false;
            return;
        }
        var (opId, weaponId) = jobs[step / per];
        int phase = step % per;
        string name = opId.ToLowerInvariant() + "_" + weaponId;
        if (phase == 0)
        {
            ElderboomInput.HeldOverride = null;
            if (currentOp != opId)
            {
                currentOp = opId;
                game.BeginSession(opId, "NORMAL");
                nextAt = EditorApplication.timeSinceStartup + 3.5;
                Log("operator " + opId);
                return; // re-enter phase 0 once the loadout exists
            }
            FaceOpenSpace(player);
            var arsenal = player.GetComponent<ElderboomArsenal>();
            var def = ElderboomWeapons.Find(weaponId);
            bool ok = arsenal != null && def != null && (arsenal.Equip(weaponId) || arsenal.Add(def, true));
            var root = player.ActiveWeaponRoot;
            Log(name + ": equip=" + ok + " root=" + (root != null ? root.name : "-") + " weapon=" + player.WeaponDisplayName);
            step++; nextAt = EditorApplication.timeSinceStartup + 3.5; // equip/draw settles
            return;
        }
        if (abilityMode && phase >= 1)
        {
            // Ability over the weapon in hand: left arm only (ElderboomLeftArmAbility), pictures through the clip.
            if (phase == 1)
            {
                var tac = player.GetComponent<ElderboomTacticalArms>();
                string f = SessionState.GetString(Filter, "");
                float tacLength = 1.4f;
                if (opId == ElderboomIds.Tactical && tac != null)
                {
                    if (f.Contains("lift")) { tac.BeginLift(player.ActiveWeaponRoot); tacLength = 2.65f; }
                    else if (f.Contains("keep")) { tac.BeginKeep(player.ActiveWeaponRoot); tacLength = 4f; }
                    else tac.BeginShockwave(player.ActiveWeaponRoot);
                }
                else player.TriggerAbilityAnimation(-1f);
                var left = player.ActiveWeaponRoot != null ? player.ActiveWeaponRoot.GetComponent<ElderboomLeftArmAbility>() : null;
                abilityLength = opId == ElderboomIds.Soph ? 6.87f : opId == ElderboomIds.Tactical ? tacLength : 3.1f;
                Log(name + ": ability left-arm=" + (left != null && left.Active) + " tactical=" + (tac != null ? tac.Mode : "-") + " root=" + (player.ActiveWeaponRoot != null ? player.ActiveWeaponRoot.name : "-"));
                step++; nextAt = EditorApplication.timeSinceStartup + abilityLength * .15f;
                return;
            }
            Shot(name + "_ability_" + phase);
            step++;
            nextAt = EditorApplication.timeSinceStartup + (phase < 5 ? abilityLength * .22f : abilityLength * .5f + .8f);
            return;
        }
        if (phase == 1 && calibrating)
        {
            Log(name + ": " + Calibrate(player, weaponId));
            step += 2; nextAt = EditorApplication.timeSinceStartup + 0.3;
            return;
        }
        if (phase == 1)
        {
            Log(name + ": hip" + Sights(player, "hip"));
            Shot(name + "_hip");
            ElderboomInput.HeldOverride = a => a == "aim";
            step++; nextAt = EditorApplication.timeSinceStartup + 3.0; // aim transition settles
            return;
        }
        var hasAds = Field(player, "hasAdsReference");
        Log(name + ": hasAds=" + hasAds + " aiming=" + Field(player, "aiming") + Sights(player, "ads"));
        Shot(name + "_ads");
        ElderboomInput.HeldOverride = null;
        step++; nextAt = EditorApplication.timeSinceStartup + 0.6;
    }
}
