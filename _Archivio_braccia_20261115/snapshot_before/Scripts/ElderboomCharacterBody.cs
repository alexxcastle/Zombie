using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Full-body operator model for third-person views (mount, menu preview, later the multiplayer proxy).
// Assets (exported from Blender, see Editor/ElderboomCharacterImport.cs):
//   Resources/Elderboom/Characters/<Op>/<Op>_Rigged.fbx            Humanoid model
//   Resources/Elderboom/Characters/<Op>/<Op>_Body_BaseColor.png    albedo
//   Resources/Elderboom/Characters/<Op>/Anims/<Op>_<Clip>.fbx      one clip per file
// Clips are played by short name ("Ride", "Bar_Hang_Idle", ...) through a two-input crossfade mixer.
public sealed class ElderboomCharacterBody : MonoBehaviour
{
    const string Root = "Elderboom/Characters/";

    public string OperatorId { get; private set; }
    public Animator Animator { get; private set; }
    public string CurrentClip { get; private set; }

    PlayableGraph graph;
    AnimationMixerPlayable mixer;
    AnimationClipPlayable current, previous;
    float fade = 1f, fadeTime = .25f;
    bool unscaled;
    string folder, prefix;
    readonly Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();
    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

    static string Cap(string op) => string.IsNullOrEmpty(op) ? "" : op.Substring(0, 1).ToUpperInvariant() + op.Substring(1).ToLowerInvariant();
    static string Folder(string op) => Root + Cap(op) + "/";
    static string ModelPath(string op) => Folder(op) + Cap(op) + "_Rigged";

    public static bool Has(string op) => !string.IsNullOrEmpty(op) && Resources.Load<GameObject>(ModelPath(op)) != null;

    public static ElderboomCharacterBody Create(string op, Transform parent, bool unscaledTime = false)
    {
        var prefab = string.IsNullOrEmpty(op) ? null : Resources.Load<GameObject>(ModelPath(op));
        if (prefab == null) return null;
        var go = Instantiate(prefab, parent, false);
        go.name = Cap(op) + "_Body";
        go.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        var body = go.AddComponent<ElderboomCharacterBody>();
        body.Init(op, unscaledTime);
        return body;
    }

    void Init(string op, bool unscaledTime)
    {
        OperatorId = op; unscaled = unscaledTime;
        folder = Folder(op); prefix = Cap(op) + "_";
        Animator = GetComponent<Animator>();
        if (Animator == null) Animator = gameObject.AddComponent<Animator>();
        Animator.applyRootMotion = false;
        Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        Animator.updateMode = unscaled ? AnimatorUpdateMode.UnscaledTime : AnimatorUpdateMode.Normal;
        ApplyMaterial(op);
        foreach (var skin in GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.updateWhenOffscreen = true;

        graph = PlayableGraph.Create(name);
        graph.SetTimeUpdateMode(unscaled ? DirectorUpdateMode.UnscaledGameTime : DirectorUpdateMode.GameTime);
        mixer = AnimationMixerPlayable.Create(graph, 2);
        var output = AnimationPlayableOutput.Create(graph, "Body", Animator);
        output.SetSourcePlayable(mixer);
        graph.Play();
    }

    // Two material slots from Blender: "Eli_Body" (body + tattoos) and "Eli_Head" (scanned head).
    // Each gets an HDRP/Lit copy with its own albedo from Resources (<Op>_Body_BaseColor / <Op>_Head_BaseColor).
    void ApplyMaterial(string op)
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            var shared = r.sharedMaterials;
            for (int i = 0; i < shared.Length; i++)
            {
                string part = shared[i] != null && shared[i].name.IndexOf("Head", System.StringComparison.OrdinalIgnoreCase) >= 0 ? "Head" : "Body";
                shared[i] = PartMaterial(op, part, shared[i]);
            }
            r.sharedMaterials = shared;
        }
    }

    Material PartMaterial(string op, string part, Material source)
    {
        string key = op + "/" + part;
        if (materials.TryGetValue(key, out var material) && material != null) return material;
        var shader = Shader.Find("HDRP/Lit");
        material = source != null && source.shader == shader ? new Material(source) : new Material(shader != null ? shader : Shader.Find("Hidden/InternalErrorShader"));
        material.name = Cap(op) + "_" + part;
        var albedo = Resources.Load<Texture2D>(folder + Cap(op) + "_" + part + "_BaseColor");
        if (albedo != null) { material.SetTexture("_BaseColorMap", albedo); material.mainTexture = albedo; }
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", part == "Head" ? .38f : .3f);
        return materials[key] = material;
    }

    // Props authored in Blender parented to a hand bone (Resources/.../<Op>/Props/<Op>_<prop>.fbx): the meshes are moved
    // under this body's matching bone with the same local offset, so the weapon sits in the hand as posed in Blender.
    public GameObject AttachProp(string prop)
    {
        var prefab = Resources.Load<GameObject>(folder + "Props/" + prefix + prop);
        if (prefab == null) { Debug.LogWarning("[ElderboomCharacterBody] Missing prop " + folder + "Props/" + prefix + prop); return null; }
        var temp = Instantiate(prefab);
        GameObject attached = null;
        foreach (var r in temp.GetComponentsInChildren<Renderer>(true))
        {
            var bone = r.transform.parent;
            string boneName = bone != null ? bone.name : "";
            int colon = boneName.LastIndexOf(':');
            if (colon >= 0) boneName = boneName.Substring(colon + 1);
            var target = FindBone(transform, boneName);
            if (target == null) continue;
            var t = r.transform;
            Vector3 lp = t.localPosition; Quaternion lr = t.localRotation; Vector3 ls = t.localScale;
            t.SetParent(target, false);
            t.SetLocalPositionAndRotation(lp, lr); t.localScale = ls;
            t.gameObject.name = prefix + prop;
            attached = t.gameObject;
        }
        Destroy(temp);
        return attached;
    }

    static Transform FindBone(Transform root, string boneName)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name; int colon = n.LastIndexOf(':'); if (colon >= 0) n = n.Substring(colon + 1);
            if (n == boneName) return t;
        }
        return null;
    }

    public AnimationClip Clip(string clipName)
    {
        if (clips.TryGetValue(clipName, out var clip)) return clip;
        foreach (var c in Resources.LoadAll<AnimationClip>(folder + "Anims/" + prefix + clipName))
            if (c != null && !c.name.StartsWith("__preview")) { clip = c; break; }
        if (clip == null) Debug.LogWarning("[ElderboomCharacterBody] Missing clip " + folder + "Anims/" + prefix + clipName);
        clips[clipName] = clip;
        return clip;
    }

    public bool HasClip(string clipName) => Clip(clipName) != null;

    // Crossfades to the clip; returns its length in seconds (0 when missing).
    public float Play(string clipName, float fadeSeconds = .25f, float speed = 1f, bool restart = false)
    {
        var clip = Clip(clipName);
        if (clip == null || !graph.IsValid()) return 0f;
        if (!restart && clipName == CurrentClip && current.IsValid()) { current.SetSpeed(speed); return clip.length; }
        var next = AnimationClipPlayable.Create(graph, clip);
        next.SetApplyFootIK(Animator != null && Animator.isHuman);
        next.SetSpeed(speed);
        if (previous.IsValid()) { graph.Disconnect(mixer, 1); previous.Destroy(); }
        if (current.IsValid())
        {
            graph.Disconnect(mixer, 0);
            previous = current;
            graph.Connect(previous, 0, mixer, 1);
        }
        current = next;
        graph.Connect(current, 0, mixer, 0);
        CurrentClip = clipName;
        fadeTime = Mathf.Max(.0001f, fadeSeconds);
        fade = previous.IsValid() && fadeSeconds > 0f ? 0f : 1f;
        ApplyWeights();
        return clip.length;
    }

    public void SetSpeed(float speed) { if (current.IsValid()) current.SetSpeed(speed); }

    // Normalized progress of the current clip (one-shots stop at 1).
    public float Progress
    {
        get
        {
            if (!current.IsValid()) return 1f;
            var clip = current.GetAnimationClip();
            return clip != null && clip.length > 0f ? Mathf.Clamp01((float)current.GetTime() / clip.length) : 1f;
        }
    }

    // Humanoid lookup when available, otherwise the Mixamo bone name (Generic rigs from Meshy).
    public Transform Bone(HumanBodyBones bone)
    {
        if (Animator != null && Animator.isHuman) return Animator.GetBoneTransform(bone);
        if (!boneCache.TryGetValue(bone, out var t) || t == null)
        {
            string n = MixamoName(bone);
            t = null;
            if (n != null)
                foreach (var x in GetComponentsInChildren<Transform>(true))
                    if (x.name == "mixamorig:" + n || x.name == n || x.name.EndsWith(":" + n)) { t = x; break; }
            boneCache[bone] = t;
        }
        return t;
    }

    readonly Dictionary<HumanBodyBones, Transform> boneCache = new Dictionary<HumanBodyBones, Transform>();

    static string MixamoName(HumanBodyBones b)
    {
        switch (b)
        {
            case HumanBodyBones.Hips: return "Hips";
            case HumanBodyBones.Spine: return "Spine";
            case HumanBodyBones.Chest: return "Spine1";
            case HumanBodyBones.UpperChest: return "Spine2";
            case HumanBodyBones.Neck: return "Neck";
            case HumanBodyBones.Head: return "Head";
            case HumanBodyBones.LeftShoulder: return "LeftShoulder";
            case HumanBodyBones.RightShoulder: return "RightShoulder";
            case HumanBodyBones.LeftUpperArm: return "LeftArm";
            case HumanBodyBones.RightUpperArm: return "RightArm";
            case HumanBodyBones.LeftLowerArm: return "LeftForeArm";
            case HumanBodyBones.RightLowerArm: return "RightForeArm";
            case HumanBodyBones.LeftHand: return "LeftHand";
            case HumanBodyBones.RightHand: return "RightHand";
            case HumanBodyBones.LeftUpperLeg: return "LeftUpLeg";
            case HumanBodyBones.RightUpperLeg: return "RightUpLeg";
            case HumanBodyBones.LeftLowerLeg: return "LeftLeg";
            case HumanBodyBones.RightLowerLeg: return "RightLeg";
            case HumanBodyBones.LeftFoot: return "LeftFoot";
            case HumanBodyBones.RightFoot: return "RightFoot";
            case HumanBodyBones.LeftToes: return "LeftToeBase";
            case HumanBodyBones.RightToes: return "RightToeBase";
            default: return null;
        }
    }

    public void SetLayer(int layer)
    {
        foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
    }

    void Update()
    {
        if (fade >= 1f) return;
        fade = Mathf.Min(1f, fade + (unscaled ? Time.unscaledDeltaTime : Time.deltaTime) / fadeTime);
        ApplyWeights();
        if (fade >= 1f && previous.IsValid()) { graph.Disconnect(mixer, 1); previous.Destroy(); }
    }

    void ApplyWeights()
    {
        mixer.SetInputWeight(0, fade);
        mixer.SetInputWeight(1, previous.IsValid() ? 1f - fade : 0f);
    }

    void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
    }
}
