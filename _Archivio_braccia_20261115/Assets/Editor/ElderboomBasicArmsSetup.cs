using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Basic FPS rig clips (Assets/GameAssets/BasicFPSAnimations/<Family>/<Family>_<Role>.fbx, animation-only, 60 fps):
// imported as Generic (no avatar, no compression, curves resampled), looped where the role loops.
// Logs/claude_basicarms.txt -> builds Resources/Elderboom/BasicArms/BasicArms.controller (one state per clip, named like
// the file) and checks every clip against the values expected from Blender (<Family>_expect.json) and every bound
// path against the three operators' arms. Report: Logs/claude_basicarms_result.txt.
[InitializeOnLoad]
sealed class ElderboomBasicArmsSetup : AssetPostprocessor
{
    public const string ClipRoot = "Assets/GameAssets/BasicFPSAnimations";
    const string ControllerPath = "Assets/Resources/Elderboom/BasicArms/BasicArms.controller";
    static readonly string[] Loops = { "Idle", "ADSIdle", "Walk", "Sprint", "ReloadLoop" };

    static bool Ours(string p) => p.Replace('\\', '/').StartsWith(ClipRoot + "/") && p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);
    static string Role(string path) { string n = Path.GetFileNameWithoutExtension(path); int u = n.IndexOf('_'); return u >= 0 ? n.Substring(u + 1) : n; }

    void OnPreprocessModel()
    {
        if (!Ours(assetPath)) return;
        var mi = (ModelImporter)assetImporter;
        mi.animationType = ModelImporterAnimationType.Generic;
        mi.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
        mi.importAnimation = true;
        mi.materialImportMode = ModelImporterMaterialImportMode.None;
        mi.importCameras = false; mi.importLights = false; mi.importBlendShapes = false; mi.importVisibility = false;
        mi.resampleCurves = true;
        mi.animationCompression = ModelImporterAnimationCompression.Off;
        mi.useFileScale = true; mi.globalScale = 1f;
        mi.bakeAxisConversion = false;
        mi.preserveHierarchy = true;
    }

    void OnPreprocessAnimation()
    {
        if (!Ours(assetPath)) return;
        var mi = (ModelImporter)assetImporter;
        var clips = mi.defaultClipAnimations;
        if (clips == null || clips.Length == 0) return;
        string name = Path.GetFileNameWithoutExtension(assetPath);
        bool loop = Loops.Contains(Role(assetPath));
        var c = clips[0];
        c.name = name; c.loopTime = loop; c.loopPose = false; c.lockRootRotation = false; c.lockRootHeightY = false; c.lockRootPositionXZ = false;
        c.keepOriginalOrientation = true; c.keepOriginalPositionY = true; c.keepOriginalPositionXZ = true;
        mi.clipAnimations = new[] { c };
    }

    // The FBX files carry Blender's unit scale on the two top nodes ("root" and "Weapon" at scale 100, everything below
    // them in centimetres). The arms' bind poses expect scale 1 and metres, so the scale is moved back into the curves:
    // top nodes to scale 1, local positions of everything below them multiplied by the same factor.
    void OnPostprocessAnimation(GameObject go, AnimationClip clip)
    {
        if (!Ours(assetPath)) return;
        var bindings = AnimationUtility.GetCurveBindings(clip);
        foreach (var top in new[] { "root", "Weapon" })
        {
            var sx = bindings.FirstOrDefault(b => b.path == top && b.propertyName == "m_LocalScale.x");
            var sc = sx.path == top ? AnimationUtility.GetEditorCurve(clip, sx) : null;
            if (sc == null) continue;
            float k = sc.Evaluate(0f);
            if (Mathf.Abs(k - 1f) < .001f || k <= 0f) continue;
            foreach (var b in bindings)
            {
                if (b.path == top && b.propertyName.StartsWith("m_LocalScale"))
                {
                    var c = AnimationUtility.GetEditorCurve(clip, b);
                    var keys = c.keys; for (int i = 0; i < keys.Length; i++) { keys[i].value /= k; keys[i].inTangent /= k; keys[i].outTangent /= k; }
                    c.keys = keys; AnimationUtility.SetEditorCurve(clip, b, c);
                }
                else if (b.path.StartsWith(top + "/") && b.propertyName.StartsWith("m_LocalPosition"))
                {
                    var c = AnimationUtility.GetEditorCurve(clip, b);
                    var keys = c.keys; for (int i = 0; i < keys.Length; i++) { keys[i].value *= k; keys[i].inTangent *= k; keys[i].outTangent *= k; }
                    c.keys = keys; AnimationUtility.SetEditorCurve(clip, b, c);
                }
            }
        }
    }

    static ElderboomBasicArmsSetup() { EditorApplication.update += Poll; }
    static double next;
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 1;
        const string Flag = "Logs/claude_basicarms.txt";
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string arg = File.ReadAllText(Flag).Trim();
        File.Delete(Flag);
        var sb = new StringBuilder();
        try { Run(arg, sb); } catch (System.Exception e) { sb.AppendLine("EXCEPTION " + e); }
        File.WriteAllText("Logs/claude_basicarms_result.txt", sb.ToString());
    }

    static AnimationClip ClipOf(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));

    static void Run(string arg, StringBuilder sb)
    {
        var files = Directory.GetFiles(ClipRoot, "*.fbx", SearchOption.AllDirectories).Select(p => p.Replace('\\', '/')).OrderBy(p => p).ToList();
        if (arg.Contains("reimport")) foreach (var f in files) AssetDatabase.ImportAsset(f, ImportAssetOptions.ForceUpdate);
        sb.AppendLine("clips " + files.Count + " " + System.DateTime.Now.ToString("HH:mm:ss"));
        // ---- controller
        Directory.CreateDirectory(Path.GetDirectoryName(ControllerPath));
        if (File.Exists(ControllerPath)) AssetDatabase.DeleteAsset(ControllerPath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var sm = ctrl.layers[0].stateMachine;
        AnimatorState def = null;
        var clips = new Dictionary<string, AnimationClip>();
        foreach (var f in files)
        {
            var clip = ClipOf(f);
            string name = Path.GetFileNameWithoutExtension(f);
            if (clip == null) { sb.AppendLine("NO CLIP " + f); continue; }
            clips[name] = clip;
            var st = sm.AddState(name);
            st.motion = clip; st.writeDefaultValues = true;
            if (name == "AR10_Idle") def = st;
        }
        if (def != null) sm.defaultState = def;
        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        sb.AppendLine("controller " + ControllerPath + " states " + sm.states.Length);
        // ---- expected values (Blender, converted to Unity space)
        float worstP = 0, worstR = 0; string worstClip = "";
        int compared = 0;
        foreach (var ef in Directory.GetFiles(ClipRoot, "*_expect.json"))
        {
            var root = MiniJson.Parse(File.ReadAllText(ef)) as Dictionary<string, object>;
            foreach (var kv in root)
            {
                if (!clips.TryGetValue(kv.Key, out var clip)) { sb.AppendLine("expect: no clip " + kv.Key); continue; }
                var bindings = AnimationUtility.GetCurveBindings(clip);
                foreach (var fr in (Dictionary<string, object>)kv.Value)
                {
                    float t = int.Parse(fr.Key) / 60f;
                    foreach (var nv in (Dictionary<string, object>)fr.Value)
                    {
                        string leaf = nv.Key.Split('/').Last();
                        string path = bindings.Select(b => b.path).FirstOrDefault(p => p == nv.Key || p.EndsWith("/" + nv.Key));
                        if (path == null) { sb.AppendLine("expect: " + kv.Key + " no path for " + nv.Key); continue; }
                        var d = (Dictionary<string, object>)nv.Value;
                        var pe = Vec((List<object>)d["p"]); var qe = (List<object>)d["q"];
                        var q = new Quaternion(F(qe[0]), F(qe[1]), F(qe[2]), F(qe[3]));
                        Vector3 p = new Vector3(Eval(clip, path, "m_LocalPosition.x", t), Eval(clip, path, "m_LocalPosition.y", t), Eval(clip, path, "m_LocalPosition.z", t));
                        var r = new Quaternion(Eval(clip, path, "m_LocalRotation.x", t), Eval(clip, path, "m_LocalRotation.y", t), Eval(clip, path, "m_LocalRotation.z", t), Eval(clip, path, "m_LocalRotation.w", t));
                        float ep = (p - pe).magnitude, er = Quaternion.Angle(Norm(r), q);
                        compared++;
                        if (ep > .001f) sb.AppendLine("  pos " + kv.Key + " " + nv.Key + " t" + fr.Key + " " + ep.ToString("F4") + " m got " + p.ToString("F4") + " want " + pe.ToString("F4"));
                        if (ep > worstP) { worstP = ep; worstClip = kv.Key + " " + nv.Key + " t" + fr.Key + " got " + p.ToString("F4") + " want " + pe.ToString("F4"); }
                        if (er > worstR) { worstR = er; sb.AppendLine("  rot " + kv.Key + " " + nv.Key + " t" + fr.Key + " " + er.ToString("F2") + " deg got " + Norm(r).ToString("F4") + " want " + q.ToString("F4")); }
                    }
                }
            }
        }
        sb.AppendLine("compared " + compared + " values, worst position error " + worstP.ToString("F5") + " m (" + worstClip + "), worst rotation error " + worstR.ToString("F3") + " deg");
        // ---- paths vs the three operators' arms (Animator on the armature node, parent of "root")
        var allPaths = new HashSet<string>();
        foreach (var c in clips.Values) foreach (var b in AnimationUtility.GetCurveBindings(c)) allPaths.Add(b.path);
        sb.AppendLine("bound paths " + allPaths.Count);
        if (clips.TryGetValue("AR10_Idle", out var probe))
            foreach (var bd in AnimationUtility.GetCurveBindings(probe))
            {
                if (!bd.propertyName.StartsWith("m_LocalScale")) continue;
                float v = AnimationUtility.GetEditorCurve(probe, bd).Evaluate(0f);
                if (Mathf.Abs(v - 1f) > .001f) sb.AppendLine("  scale " + bd.path + " " + bd.propertyName + " " + v.ToString("F4"));
            }
        foreach (var op in new[] { "Eli", "Soph", "Tactical" })
        {
            var prefab = Resources.Load<GameObject>("Elderboom/Grips/" + op + "_Grips");
            if (prefab == null) { sb.AppendLine(op + ": arms prefab missing"); continue; }
            var rootBone = prefab.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "root");
            if (rootBone == null || rootBone.parent == null) { sb.AppendLine(op + ": no root bone"); continue; }
            var arm = rootBone.parent;
            var missing = allPaths.Where(p => p.Length > 0 && !p.StartsWith("Weapon") && arm.Find(p) == null).OrderBy(p => p).ToList();
            var runtime = allPaths.Where(p => p.StartsWith("Weapon")).OrderBy(p => p).ToList();
            sb.AppendLine(op + ": animator node '" + arm.name + "' (path in prefab: " + AnimationUtility.CalculateTransformPath(arm, prefab.transform) + "), missing " + missing.Count + (missing.Count > 0 ? ": " + string.Join(", ", missing) : "") + "; created at runtime: " + string.Join(", ", runtime));
            var animsInPrefab = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(prefab)).OfType<AnimationClip>().Count(c => !c.name.StartsWith("__preview__"));
            sb.AppendLine(op + ": clips left inside the arms asset " + animsInPrefab);
        }
        // ---- per-clip summary
        foreach (var kv in clips.OrderBy(k => k.Key))
            sb.AppendLine("  " + kv.Key + " " + kv.Value.length.ToString("F3") + "s " + kv.Value.frameRate + "fps loop " + kv.Value.isLooping + " legacy " + kv.Value.legacy + " curves " + AnimationUtility.GetCurveBindings(kv.Value).Length);
    }

    static float F(object o) => System.Convert.ToSingle(o, System.Globalization.CultureInfo.InvariantCulture);
    static Vector3 Vec(List<object> l) => new Vector3(F(l[0]), F(l[1]), F(l[2]));
    static Quaternion Norm(Quaternion q) { float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w); return m > 1e-6f ? new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m) : Quaternion.identity; }
    static float Eval(AnimationClip clip, string path, string prop, float t)
    {
        var c = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), prop));
        return c != null ? c.Evaluate(t) : float.NaN;
    }

    // Tiny JSON reader (objects, arrays, numbers, strings, bools).
    static class MiniJson
    {
        public static object Parse(string s) { int i = 0; return Val(s, ref i); }
        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
        static object Val(string s, ref int i)
        {
            Ws(s, ref i);
            char c = s[i];
            if (c == '{') { i++; var d = new Dictionary<string, object>(); Ws(s, ref i); if (s[i] == '}') { i++; return d; } while (true) { Ws(s, ref i); var k = (string)Val(s, ref i); Ws(s, ref i); i++; d[k] = Val(s, ref i); Ws(s, ref i); if (s[i] == ',') { i++; continue; } i++; return d; } }
            if (c == '[') { i++; var l = new List<object>(); Ws(s, ref i); if (s[i] == ']') { i++; return l; } while (true) { l.Add(Val(s, ref i)); Ws(s, ref i); if (s[i] == ',') { i++; continue; } i++; return l; } }
            if (c == '"') { i++; var b = new StringBuilder(); while (s[i] != '"') { if (s[i] == '\\') { i++; } b.Append(s[i]); i++; } i++; return b.ToString(); }
            if (s.Substring(i).StartsWith("true")) { i += 4; return true; }
            if (s.Substring(i).StartsWith("false")) { i += 5; return false; }
            if (s.Substring(i).StartsWith("null")) { i += 4; return null; }
            int j = i; while (j < s.Length && "+-0123456789.eE".IndexOf(s[j]) >= 0) j++;
            var num = double.Parse(s.Substring(i, j - i), System.Globalization.CultureInfo.InvariantCulture); i = j; return num;
        }
    }
}
