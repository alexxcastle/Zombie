using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Logs/claude_armstest.txt ("m4 mp7 rhino ..." or "all") -> plays Elderboom_TestRange with the Tactical, and for each
// weapon: hip (grip metrics), aim (sight-line + non-tautological camera-distance/coverage metrics), aimed fire (both
// HeldOverride and pulsed DownOverride for semi-auto weapons), reload (ammo before / mid / after).
// Pictures in Logs/armstest/<id>_{hip,ads,fire,reload}.jpg, report in Logs/claude_armstest_result.txt.
[InitializeOnLoad]
static class ElderboomClaudeArmsTest
{
    const string Flag = "Logs/claude_armstest.txt", Result = "Logs/claude_armstest_result.txt", Dir = "Logs/armstest";
    const string Stage = "claude_armstest_stage", Jobs = "claude_armstest_jobs", Step = "claude_armstest_step", Log = "claude_armstest_log", Prev = "claude_armstest_prev";
    const string PlayStartKey = "claude_armstest_t0", MidKey = "claude_armstest_mid", Pulse2Key = "claude_armstest_p2", Pulse3Key = "claude_armstest_p3";
    const float Block = 17f;
    static double next;

    // Preallocated delegates: zero per-frame closure allocations while driving ElderboomInput.
    static readonly System.Func<string, bool> HoldNone = a => false;
    static readonly System.Func<string, bool> HoldAim = a => a == "aim";
    static readonly System.Func<string, bool> HoldAimFire = a => a == "aim" || a == "fire";
    static readonly System.Func<string, bool> DownFire = a => a == "fire";

    static ElderboomClaudeArmsTest()
    {
        EditorApplication.update += Poll;
        if (!EditorApplication.isPlayingOrWillChangePlaymode && SessionState.GetInt(Stage, 0) != 0 && SessionState.GetInt(Stage, 0) != 3) { SessionState.SetInt(Stage, 0); SessionState.SetInt(Step, 0); }
        Application.logMessageReceived += (msg, st, type) =>
        {
            if (SessionState.GetInt(Stage, 0) == 0) return;
            if (type == LogType.Error || type == LogType.Exception) Append(type + ": " + msg + "\n   " + (st.Length > 300 ? st.Substring(0, 300) : st));
        };
    }
    static void Append(string line) => SessionState.SetString(Log, SessionState.GetString(Log, "") + line + "\n");

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + .05;
        int stage = SessionState.GetInt(Stage, 0);
        if (stage == 0)
        {
            if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            string spec = File.ReadAllText(Flag).Trim(); File.Delete(Flag);
            if (spec == "" || spec == "all") spec = "m4 mp7 glock19 rhino shotgun mp133 grenade minigun flame";
            SessionState.SetString(Jobs, spec); SessionState.SetString(Log, ""); SessionState.SetInt(Step, 0);
            SessionState.SetFloat(MidKey, -1f); SessionState.SetFloat(Pulse2Key, -1f); SessionState.SetFloat(Pulse3Key, -1f);
            SessionState.SetString(Prev, EditorSceneManager.GetActiveScene().path);
            Directory.CreateDirectory(Dir);
            EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_TestRange.unity", OpenSceneMode.Single);
            SessionState.SetInt(Stage, 1); EditorApplication.isPlaying = true; return;
        }
        if (stage == 1 && EditorApplication.isPlaying) { SessionState.SetInt(Stage, 2); SessionState.SetFloat(PlayStartKey, Time.time); return; }
        if (stage == 2 && EditorApplication.isPlaying)
        {
            // Use game Time.time so first-frame HDRP shader warm-up never steals weapon #0's settle window.
            double t = Time.time - SessionState.GetFloat(PlayStartKey, 0f);
            if (ElderboomInput.HeldOverride == null) ElderboomInput.HeldOverride = HoldNone;
            var weapons = SessionState.GetString(Jobs, "").Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            int k = SessionState.GetInt(Step, 0);
            int w = k / 10, sub = k % 10;
            if (w >= weapons.Length)
            {
                File.WriteAllText(Result, SessionState.GetString(Log, "") + "DONE\n");
                ElderboomInput.HeldOverride = null;
                ElderboomInput.DownOverride = null;
                SessionState.SetInt(Stage, 3); EditorApplication.isPlaying = false; return;
            }
            string id = weapons[w]; double t0 = 2 + w * Block;
            var p = Object.FindFirstObjectByType<HDRPSlicePlayer>();
            float[] at = { 0f, 5f, 5.1f, 8f, 8.05f, 8.55f, 9f, 9.6f, 0f, 16.5f };

            float p2 = SessionState.GetFloat(Pulse2Key, -1f);
            if (p2 > 0f && t >= p2) { SessionState.SetFloat(Pulse2Key, -1f); ElderboomInput.DownOverride = DownFire; }
            float p3 = SessionState.GetFloat(Pulse3Key, -1f);
            if (p3 > 0f && t >= p3) { SessionState.SetFloat(Pulse3Key, -1f); ElderboomInput.DownOverride = DownFire; }

            if (sub == 8) { k++; } // set below once the reload length is known
            else if (t > t0 + at[sub])
            {
                switch (sub)
                {
                    case 0:
                        ElderboomArsenal.StartingWeapon = id;
                        ElderboomGame.Active?.BeginSession(ElderboomIds.Tactical, "NORMAL");
                        if (p != null) typeof(HDRPSlicePlayer).GetField("pitch", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(p, 0f);
                        break;
                    case 1: Shot(id + "_hip"); Grip(id, p); break;
                    case 2: ElderboomInput.HeldOverride = HoldAim; break;
                    case 3: Shot(id + "_ads"); Sights(id, p); break;
                    case 4:
                        ElderboomInput.HeldOverride = HoldAimFire;
                        ElderboomInput.DownOverride = DownFire;
                        SessionState.SetFloat(Pulse2Key, (float)t + 0.18f);
                        SessionState.SetFloat(Pulse3Key, (float)t + 0.36f);
                        break;
                    case 5: Shot(id + "_fire"); Append(id + ": after aimed fire ammo " + p.Ammo + "/" + p.ReserveAmmo); break;
                    case 6: ElderboomInput.HeldOverride = HoldNone; ElderboomInput.DownOverride = null; break;
                    case 7:
                        {
                            int before = p.Ammo;
                            var m = typeof(HDRPSlicePlayer).GetMethod("Reload", BindingFlags.Instance | BindingFlags.NonPublic);
                            var it = m != null ? m.Invoke(p, null) as System.Collections.IEnumerator : null;
                            if (it != null && !p.Reloading) p.StartCoroutine(it);
                            Append(id + ": reload started at ammo " + before + "/" + p.ReserveAmmo + " reloading " + p.Reloading);
                            SessionState.SetFloat(MidKey, (float)t + 1.2f);
                            break;
                        }
                    case 9: Append(id + ": after reload ammo " + p.Ammo + "/" + p.ReserveAmmo + " reloading " + p.Reloading); break;
                }
                k++;
            }
            float mid = SessionState.GetFloat(MidKey, -1f);
            if (mid > 0f && t > mid) { SessionState.SetFloat(MidKey, -1f); Shot(id + "_reload"); Append(id + ": mid reload ammo " + p.Ammo + "/" + p.ReserveAmmo); }
            SessionState.SetInt(Step, k);
            return;
        }
        if (stage == 3 && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            SessionState.SetInt(Stage, 0); SessionState.SetInt(Step, 0);
            var prev = SessionState.GetString(Prev, ""); if (prev.Length > 0) EditorSceneManager.OpenScene(prev, OpenSceneMode.Single);
        }
    }

    static Transform Active(HDRPSlicePlayer p)
    {
        var ars = p != null ? p.GetComponent<ElderboomArsenal>() : null; if (ars == null) return null;
        var af = typeof(ElderboomArsenal).GetField("active", BindingFlags.NonPublic | BindingFlags.Instance);
        var slot = af?.GetValue(ars);
        return slot?.GetType().GetField("root")?.GetValue(slot) as Transform;
    }
    static Transform Deep(Transform r, string n) { foreach (var t in r.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t; return null; }

    // How far each hand's closed fist sits from the weapon's grip socket (after the IK).
    static void Grip(string id, HDRPSlicePlayer p)
    {
        var root = Active(p); if (root == null) { Append(id + ": no weapon root"); return; }
        var drv = root.GetComponent<ElderboomArmsDriver>();
        if (drv == null) { Append(id + ": not armed (" + root.name + ")"); return; }
        var wn = drv.WeaponNode;
        var sb = new StringBuilder(id + ": " + drv.family);
        for (int idx = 0; idx < 2; idx++)
        {
            string s = idx == 0 ? "R" : "L";
            Transform sock = null;
            foreach (var n in idx == 0 ? new[] { "Grip_R", "RightHand_Grip", "R_palm", "palm_2", "R_wrist" } : new[] { "Grip_L", "LeftHand_Support", "L_palm", "palm", "L_wrist" }) { sock = Deep(wn, n); if (sock != null) break; }
            Vector3 fist = drv.FistPosition(idx);
            sb.Append("  " + s + " socket " + (sock != null ? sock.name + " " + (Vector3.Distance(fist, sock.position) * 100f).ToString("F1") + " cm" : "none"));
        }
        Append(sb.ToString());
        var cam = Camera.main;
        var muzzle = ElderboomWeaponOnly.Muzzle(wn);
        if (cam != null && muzzle != null)
        {
            float boreAng = Vector3.Angle(muzzle.forward, cam.transform.forward);
            float boreLimit = drv.LocksSights ? 25f : 10f; // 10 deg for heavy weapons, 25 deg for normal hip cant
            string boreVerdict = boreAng <= boreLimit ? "PASS" : "FAIL";
            Append($"{id}: HIP boreAngle {boreAng:F1} deg (limit {boreLimit:F1} deg -> {boreVerdict}), fov {cam.fieldOfView:F1} deg");
        }
    }

    // Sight line against the view axis + non-tautological viewmodel distance & screen coverage checks.
    static void Sights(string id, HDRPSlicePlayer p)
    {
        var root = Active(p); var cam = Camera.main; if (root == null || cam == null) return;
        var drv = root.GetComponent<ElderboomArmsDriver>();
        bool locks = drv == null || drv.LocksSights;
        var rear = Deep(root, "MainWeapon_ADS_Rear"); var front = Deep(root, "MainWeapon_ADS_Front");
        if (rear != null && front != null)
        {
            Vector3 r = cam.transform.InverseTransformPoint(rear.position), f = cam.transform.InverseTransformPoint(front.position);
            float ang = Vector3.Angle(f - r, Vector3.forward);
            Vector3 sr = cam.WorldToViewportPoint(rear.position), sf = cam.WorldToViewportPoint(front.position);
            Append(id + ": ADS rear " + r.ToString("F3") + " angle " + ang.ToString("F2") + " deg, rear off-centre " + ((sr.x - .5f) * 1600).ToString("F0") + "," + ((sr.y - .5f) * 900).ToString("F0") + " px, front " + ((sf.x - .5f) * 1600).ToString("F0") + "," + ((sf.y - .5f) * 900).ToString("F0") + " px, aiming " + p.IsAiming + (locks ? "" : " (lockSights False)"));
        }
        else Append(id + ": no sight markers, aiming " + p.IsAiming);

        MeasureViewmodelBounds(root, cam, out float minDist, out float covPct);
        float distLimit = cam.nearClipPlane + 0.04f;
        string distVerdict = minDist >= distLimit ? "PASS" : "FAIL";
        string covVerdict = covPct <= 35f ? "PASS" : "FAIL";
        Append($"{id}: ADS clearance minDist {minDist:F3} m (limit {distLimit:F3} m -> {distVerdict}), viewmodelCov {covPct:F1}% (limit 35.0% -> {covVerdict}), fov {cam.fieldOfView:F1} deg");
    }

    // Rasterizes all active viewmodel renderers (weapon + arms) onto a 160x90 viewport grid to measure:
    //  1) minimum camera-space distance of any visible viewmodel geometry (catches camera-inside-stock/arm clipping);
    //  2) percentage of screen pixels covered by the viewmodel in ADS.
    static void MeasureViewmodelBounds(Transform root, Camera cam, out float minDist, out float covPct)
    {
        const int W = 160, H = 90;
        var covered = new bool[W * H];
        minDist = float.PositiveInfinity;
        float near = cam.nearClipPlane;
        var baked = new Mesh();
        var vList = new List<Vector3>();
        var tList = new List<int>();
        var camSpace = new List<Vector3>();
        var vpSpace = new List<Vector3>();
        Matrix4x4 w2c = cam.transform.worldToLocalMatrix;

        foreach (var rend in root.GetComponentsInChildren<Renderer>(false))
        {
            if (rend == null || !rend.enabled || rend.forceRenderingOff || rend is ParticleSystemRenderer || rend is LineRenderer) continue;
            Mesh mesh = null;
            Matrix4x4 l2w;
            if (rend is SkinnedMeshRenderer smr)
            {
                if (smr.sharedMesh == null) continue;
                smr.BakeMesh(baked, true);
                mesh = baked;
                l2w = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            }
            else
            {
                var mf = rend.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                mesh = mf.sharedMesh;
                l2w = rend.transform.localToWorldMatrix;
            }

            mesh.GetVertices(vList);
            int vc = vList.Count;
            if (vc == 0) continue;
            camSpace.Clear(); vpSpace.Clear();
            if (camSpace.Capacity < vc) camSpace.Capacity = vc;
            if (vpSpace.Capacity < vc) vpSpace.Capacity = vc;
            for (int i = 0; i < vc; i++)
            {
                Vector3 wp = l2w.MultiplyPoint3x4(vList[i]);
                camSpace.Add(w2c.MultiplyPoint3x4(wp));
                vpSpace.Add(cam.WorldToViewportPoint(wp));
            }

            int subCount = mesh.subMeshCount;
            for (int sm = 0; sm < subCount; sm++)
            {
                mesh.GetTriangles(tList, sm);
                for (int i = 0; i + 2 < tList.Count; i += 3)
                {
                    int i0 = tList[i], i1 = tList[i + 1], i2 = tList[i + 2];
                    Vector3 c0 = camSpace[i0], c1 = camSpace[i1], c2 = camSpace[i2];
                    if (c0.z <= 0f && c1.z <= 0f && c2.z <= 0f) continue;

                    // Straddling the near plane: flag near-plane clipping if the visible part intersects the screen
                    if (c0.z <= near || c1.z <= near || c2.z <= near)
                    {
                        Vector3 p = c0.z > near ? vpSpace[i0] : c1.z > near ? vpSpace[i1] : vpSpace[i2];
                        if (p.x >= -0.2f && p.x <= 1.2f && p.y >= -0.2f && p.y <= 1.2f && p.z > near)
                            minDist = Mathf.Min(minDist, near);
                        continue;
                    }

                    Vector3 a = vpSpace[i0], b = vpSpace[i1], c = vpSpace[i2];
                    float minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x));
                    float maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
                    float minY = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
                    float maxY = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
                    if (maxX < 0f || minX > 1f || maxY < 0f || minY > 1f) continue;

                    int x0 = Mathf.Clamp(Mathf.FloorToInt(minX * W), 0, W - 1);
                    int x1 = Mathf.Clamp(Mathf.CeilToInt(maxX * W), 0, W - 1);
                    int y0 = Mathf.Clamp(Mathf.FloorToInt(minY * H), 0, H - 1);
                    int y1 = Mathf.Clamp(Mathf.CeilToInt(maxY * H), 0, H - 1);

                    float ax = a.x * W, ay = a.y * H, bx = b.x * W, by = b.y * H, cx = c.x * W, cy = c.y * H;
                    float area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
                    if (Mathf.Abs(area) < 1e-4f) continue;
                    float invArea = 1f / area;

                    for (int py = y0; py <= y1; py++)
                    {
                        float fy = py + 0.5f;
                        for (int px = x0; px <= x1; px++)
                        {
                            float fx = px + 0.5f;
                            float w0 = ((bx - fx) * (cy - fy) - (by - fy) * (cx - fx)) * invArea;
                            float w1 = ((cx - fx) * (ay - fy) - (cy - fy) * (ax - fx)) * invArea;
                            float w2 = 1f - w0 - w1;
                            if (w0 >= 0f && w1 >= 0f && w2 >= 0f)
                            {
                                covered[py * W + px] = true;
                                float z = w0 * c0.z + w1 * c1.z + w2 * c2.z;
                                if (z < minDist) minDist = z;
                            }
                        }
                    }
                }
            }
        }
        Object.DestroyImmediate(baked);

        int count = 0;
        for (int i = 0; i < covered.Length; i++) if (covered[i]) count++;
        covPct = 100f * count / covered.Length;
        if (float.IsPositiveInfinity(minDist)) minDist = 0f;
    }

    static void Shot(string name)
    {
        var cam = Camera.main; if (cam == null) return;
        var rt = new RenderTexture(1600, 900, 24); var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        var old = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = old;
        RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply(); RenderTexture.active = null;
        File.WriteAllBytes(Dir + "/" + name + ".jpg", tex.EncodeToJPG(88)); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
    }
}
