using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

// Logs/claude_animpreview.txt -> renders preview frames of the Basic arms clips in the editor (no Play Mode).
// Each line of the flag: "<operator> <weapon> <Role>[+<Role>...] [maxSeconds]". Empty flag = default set.
// Frames: Logs/anim_preview/<op>_<weapon>_<roles>/f000.png (30 fps). Report: Logs/claude_animpreview_result.txt.
static class ElderboomBasicArmsPreview
{
    const string Flag = "Logs/claude_animpreview.txt";
    const string AuditFlag = "Logs/codex_animpreview.txt";
    const string OutDir = "Logs/anim_preview";
    const int W = 640, H = 360, Fps = 30;
    static readonly string[] Default =
    {
        "tactical m4 Idle 3", "tactical m4 Fire", "tactical m4 Reload", "tactical m4 Sprint 2", "tactical m4 Draw",
        "tactical glock19 Reload", "tactical mp133 ReloadStart+ReloadLoop+ReloadLoop+ReloadEnd", "tactical mp7 Reload",
        "eli m4 Reload", "soph glock19 Fire",
    };

    static double next;
    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 1.5;
        string request = File.Exists(AuditFlag) ? AuditFlag : Flag;
        if (!File.Exists(request) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        bool audit = request == AuditFlag;
        var lines = File.ReadAllLines(request).Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        File.Delete(request);
        var sb = new StringBuilder();
        try { Run(lines.Length > 0 ? lines : Default, sb, audit ? "Logs/codex_anim_preview" : OutDir); } catch (System.Exception e) { sb.AppendLine("EXCEPTION " + e); }
        File.WriteAllText(audit ? "Logs/codex_animpreview_result.txt" : "Logs/claude_animpreview_result.txt", sb.ToString());
    }

    static void Run(string[] specs, StringBuilder sb, string outDir)
    {
        var stage = new GameObject("__AnimPreview");
        stage.transform.position = new Vector3(0f, 2000f, 0f);
        try
        {
            var camGo = new GameObject("Cam"); camGo.transform.SetParent(stage.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = .01f; cam.farClipPlane = 50f;
            cam.fieldOfView = Camera.HorizontalToVerticalFieldOfView(ElderboomPauseMenu.DefaultHorizontalFov, (float)W / H);
            var hd = camGo.AddComponent<HDAdditionalCameraData>();
            hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            hd.backgroundColorHDR = new Color(.16f, .17f, .19f);
            // Fixed exposure (no eye adaptation between frames) and two plain directional lights.
            var vol = stage.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 10000f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>(); vol.sharedProfile = profile;
            var exposure = profile.Add<Exposure>(true); exposure.mode.Override(ExposureMode.Fixed); exposure.fixedExposure.Override(10.5f);
            var key = new GameObject("Key").AddComponent<Light>(); key.transform.SetParent(stage.transform, false);
            key.type = LightType.Directional; key.transform.localRotation = Quaternion.Euler(40f, -35f, 0f);
            key.gameObject.AddComponent<HDAdditionalLightData>(); key.intensity = 7000f;
            var fill = new GameObject("Fill").AddComponent<Light>(); fill.transform.SetParent(stage.transform, false);
            fill.type = LightType.Directional; fill.transform.localRotation = Quaternion.Euler(10f, 150f, 0f);
            fill.gameObject.AddComponent<HDAdditionalLightData>(); fill.intensity = 2500f;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            // Side view: from the right of the shooter, looking at the hands.
            var sideGo = new GameObject("Side"); sideGo.transform.SetParent(camGo.transform, false);
            sideGo.transform.localPosition = new Vector3(.75f, -.05f, .32f);
            sideGo.transform.LookAt(camGo.transform.TransformPoint(new Vector3(0f, -.17f, .32f)));
            var side = sideGo.AddComponent<Camera>(); side.nearClipPlane = .01f; side.farClipPlane = 50f; side.fieldOfView = 45f;
            var sideHd = sideGo.AddComponent<HDAdditionalCameraData>();
            sideHd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color; sideHd.backgroundColorHDR = new Color(.20f, .21f, .23f);
            var rt2 = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            side.targetTexture = rt2;
            var img = new Texture2D(W * 2, H, TextureFormat.RGB24, false);
            Directory.CreateDirectory(outDir);

            foreach (var spec in specs)
            {
                var parts = spec.Split(' ');
                if (parts.Length < 3) { sb.AppendLine("bad spec " + spec); continue; }
                var op = ElderboomOperators.Find(parts[0]);
                var weapon = ElderboomWeapons.Find(parts[1]);
                var roles = parts[2].Split('+');
                var maxTok = parts.Skip(3).FirstOrDefault(x => !x.Contains("="));
                float max = maxTok != null ? float.Parse(maxTok, System.Globalization.CultureInfo.InvariantCulture) : 99f;
                if (op == null || weapon == null) { sb.AppendLine("unknown " + spec); continue; }
                var root = ElderboomBasicArms.Build(null, op, weapon, cam.transform);
                if (root == null) { sb.AppendLine("build failed " + spec); continue; }
                var vm = root.GetComponent<ElderboomViewmodel>();
                var hipTok = parts.FirstOrDefault(x => x.StartsWith("hip="));
                if (hipTok != null)
                {
                    var v = hipTok.Substring(4).Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                    vm.hipPosition = new Vector3(v[0], v[1], v[2]); vm.hipRotation = Quaternion.Euler(v.Length > 4 ? v[4] : 0f, v.Length > 3 ? v[3] : 0f, v.Length > 5 ? v[5] : 0f);
                }
                root.localPosition = vm != null ? vm.hipPosition : Vector3.zero;
                root.localRotation = vm != null ? vm.hipRotation : Quaternion.identity;
                var depthTok = parts.FirstOrDefault(x => x.StartsWith("depth="));
                if (depthTok != null)
                {
                    var bv = root.Find("BasicView");
                    bv.localScale = new Vector3(bv.localScale.x, bv.localScale.y, bv.localScale.z * float.Parse(depthTok.Substring(6), System.Globalization.CultureInfo.InvariantCulture));
                }
                var sideTok = parts.FirstOrDefault(x => x.StartsWith("side="));
                if (sideTok != null)
                {
                    var v = sideTok.Substring(5).Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                    sideGo.transform.localPosition = new Vector3(v[0], v[1], v[2]);
                    sideGo.transform.LookAt(camGo.transform.TransformPoint(new Vector3(v[3], v[4], v[5])));
                }
                else
                {
                    sideGo.transform.localPosition = new Vector3(.75f, -.05f, .32f);
                    sideGo.transform.LookAt(camGo.transform.TransformPoint(new Vector3(0f, -.17f, .32f)));
                }
                root.gameObject.SetActive(true);
                // "grip=vx,vy,depth,yaw,pitch,roll": turn the rig by the angles, then move it so the right hand (idle,
                // first frame) sits at viewport (vx, vy), "depth" metres in front of the eye.
                var gripTok = parts.FirstOrDefault(x => x.StartsWith("grip="));
                if (gripTok != null)
                {
                    var v = gripTok.Substring(5).Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                    var anim0 = root.GetComponentInChildren<Animator>(true);
                    var idle0 = anim0.runtimeAnimatorController.animationClips.First(c => c.name == root.GetComponent<ElderboomBasicArms>().Family + "_Idle");
                    root.localPosition = Vector3.zero; root.localRotation = Quaternion.Euler(v[4], v[3], v[5]);
                    idle0.SampleAnimation(anim0.gameObject, 0f);
                    var hand0 = root.GetComponentsInChildren<Transform>(true).First(x => x.name == "DEF-hand.R");
                    Vector3 h = cam.transform.InverseTransformPoint(hand0.position);
                    float t = Mathf.Tan(cam.fieldOfView * .5f * Mathf.Deg2Rad);
                    Vector3 want = new Vector3((v[0] - .5f) * 2f * t * cam.aspect * v[2], (v[1] - .5f) * 2f * t * v[2], v[2]);
                    root.localPosition = want - h;
                    sb.AppendLine(spec + ": hip position " + root.localPosition.ToString("F3") + " rotation " + root.localEulerAngles.ToString("F1"));
                }
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = camGo.layer;
                var arms = root.GetComponent<ElderboomBasicArms>();
                var animator = root.GetComponentInChildren<Animator>(true);
                var controller = animator.runtimeAnimatorController;
                string name = op.id + "_" + weapon.id + "_" + string.Join("-", roles) + "_" + string.Join("_", parts.Skip(3).Where(x => x.Contains("=")).Select(x => x.Replace("=", "").Replace(",", "_")));
                string dir = Path.Combine(outDir, name);
                if (Directory.Exists(dir)) foreach (var f in Directory.GetFiles(dir)) File.Delete(f);
                Directory.CreateDirectory(dir);
                // Where the rig's own eye camera (P_eyes, from the reference idle pose) sits relative to the game camera.
                var meta = ElderboomBasicArms.LoadMeta(arms.Family);
                var idleClip = controller.animationClips.FirstOrDefault(c => c.name == arms.Family + "_Idle");
                var weaponNode = animator.transform.Find("Weapon");
                if (idleClip != null && weaponNode != null && meta != null && meta.idle != null)
                {
                    idleClip.SampleAnimation(animator.gameObject, 0f);
                    Vector3 e = cam.transform.InverseTransformPoint(weaponNode.TransformPoint(meta.idle.eye));
                    Vector3 f = cam.transform.InverseTransformDirection(weaponNode.TransformDirection(meta.idle.forward)).normalized;
                    Vector3 u = cam.transform.InverseTransformDirection(weaponNode.TransformDirection(meta.idle.up)).normalized;
                    sb.AppendLine(name + ": rig eye in camera space " + e.ToString("F3") + " forward " + f.ToString("F3") + " up " + u.ToString("F3"));
                    var handR = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "DEF-hand.R");
                    var muz = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name.ToLowerInvariant().Contains("muzzle"));
                    if (handR != null) sb.AppendLine("   hand.R cam " + cam.transform.InverseTransformPoint(handR.position).ToString("F3") + " viewport " + cam.WorldToViewportPoint(handR.position).ToString("F2"));
                    if (muz != null) sb.AppendLine("   muzzle cam " + cam.transform.InverseTransformPoint(muz.position).ToString("F3") + " viewport " + cam.WorldToViewportPoint(muz.position).ToString("F2"));
                }
                int frame = 0; float total = 0f;
                foreach (var role in roles)
                {
                    var clip = controller.animationClips.FirstOrDefault(c => c.name == arms.Family + "_" + role);
                    if (clip == null) { sb.AppendLine(name + ": no clip " + arms.Family + "_" + role); continue; }
                    float len = Mathf.Min(clip.length, max);
                    var sequencePosition = root.localPosition;
                    var sequenceRotation = root.localRotation;
                    if (role.StartsWith("ADS") && vm.hasAds)
                    {
                        root.localPosition = vm.adsPosition;
                        root.localRotation = vm.adsRotation;
                    }
                    for (float t = 0f; t <= len + 1e-4f; t += 1f / Fps)
                    {
                        clip.SampleAnimation(animator.gameObject, t);
                        arms.ApplyContacts(role, t / Mathf.Max(clip.length, .001f));
                        cam.Render(); side.Render();
                        RenderTexture.active = rt;
                        img.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                        RenderTexture.active = rt2;
                        img.ReadPixels(new Rect(0, 0, W, H), W, 0); img.Apply();
                        RenderTexture.active = null;
                        File.WriteAllBytes(Path.Combine(dir, "f" + frame.ToString("000") + ".png"), img.EncodeToPNG());
                        frame++;
                    }
                    root.localPosition = sequencePosition;
                    root.localRotation = sequenceRotation;
                    total += len;
                }
                sb.AppendLine(name + " family " + arms.Family + " frames " + frame + " " + total.ToString("F2") + "s");
                Object.DestroyImmediate(root.gameObject);
            }
            cam.targetTexture = null; side.targetTexture = null;
            rt.Release(); rt2.Release();
        }
        finally { Object.DestroyImmediate(stage); }
    }
}
