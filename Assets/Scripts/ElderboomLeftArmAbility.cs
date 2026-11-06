using System.Collections.Generic;
using UnityEngine;

// Operator abilities on any weapon: the right hand keeps the current weapon (its own animation and grip IK) while
// the left arm plays the operator's ability. The ability runs on the operator's own viewmodel kept hidden, and every
// left-arm bone (shoulder, arm, forearm, hand, fingers) is copied onto the weapon's arms each frame, relative to
// each viewmodel's root, with a short blend in and out. Runs after ElderboomArmIK (order 100), so it replaces the
// left-hand support grip only while the ability plays.
[DefaultExecutionOrder(200)]
public sealed class ElderboomLeftArmAbility : MonoBehaviour
{
    const float Blend = .22f;
    readonly List<(Transform target, Transform source)> pairs = new List<(Transform, Transform)>();
    Transform sourceRoot, holder, wrist;
    Vector3 holderLocalPos, relPos; Quaternion holderLocalRot, relRot;
    float startAt, endAt;
    bool active, bothArms, worldSpace;
    static readonly string[] RightWrists = { "R_wrist", "DEF-hand.R", "hand.R", "RightHand" };

    public bool Active => active;

    // Left-arm bones of a viewmodel rig (same side rules as ElderboomArmIK).
    static bool IsRight(string name)
    {
        string n = name.ToLowerInvariant();
        return n.EndsWith(".r") || n.Contains(".r.") || n.EndsWith("_r") || n.StartsWith("r_") || n.Contains("right");
    }
    bool Wanted(string name) => IsLeft(name) || (bothArms && IsRight(name));

    static bool IsLeft(string name)
    {
        string n = name.ToLowerInvariant();
        return n.EndsWith(".l") || n.Contains(".l.") || n.EndsWith("_l") || n.StartsWith("l_") || n.Contains("left");
    }

    static string PathOf(Transform t, Transform root)
    {
        string p = t.name;
        for (var x = t.parent; x != null && x != root; x = x.parent) p = x.name + "/" + p;
        return p;
    }

    static int Depth(Transform t) { int d = 0; for (var x = t.parent; x != null; x = x.parent) d++; return d; }

    // Pairs this viewmodel's arm bones with the bones of the same name in the operator's own viewmodel (the grips
    // model and the operator's model are the same rig, in different hierarchies).
    // bothArms: abilities that use the right arm too (Eli's acoustic device sits on the right forearm). Then both
    // arms follow the ability and the weapon in hand follows the right hand, as the operator's own weapon does.
    // world: copy the bones where the source shows them on screen (Tactical's Lift Smash / Keep Away models, framed
    // on their own), instead of relative to each viewmodel's root.
    public int Bind(Transform operatorRoot, bool both = false, bool world = false)
    {
        if (sourceRoot == operatorRoot && bothArms == both && worldSpace == world && pairs.Count > 0) return pairs.Count;
        sourceRoot = operatorRoot; bothArms = both; worldSpace = world; pairs.Clear(); holder = null; wrist = null;
        if (operatorRoot == null) return 0;
        var sources = new Dictionary<string, Transform>();
        foreach (var smr in operatorRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (smr.bones != null) foreach (var b in smr.bones) if (b != null && Wanted(b.name)) sources[b.name] = b;
        var seen = new HashSet<Transform>();
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.bones == null) continue;
            foreach (var b in smr.bones)
                if (b != null && Wanted(b.name) && seen.Add(b) && sources.TryGetValue(b.name, out var s)) pairs.Add((b, s));
        }
        if (bothArms)
        {
            foreach (Transform c in transform) if (c.name.StartsWith("OperatorWeapon_")) { holder = c; break; }
            foreach (var p in pairs) if (System.Array.IndexOf(RightWrists, p.target.name) >= 0) { wrist = p.target; break; }
        }
        pairs.Sort((a, b) => Depth(a.target).CompareTo(Depth(b.target))); // parents before children
        return pairs.Count;
    }

    public void Begin(float duration)
    {
        startAt = Time.time; endAt = Time.time + Mathf.Max(.3f, duration); active = pairs.Count > 0;
        if (holder != null && wrist != null)
        {
            // Weapon held as now: its pose relative to the right hand, kept while the hand moves.
            holderLocalPos = holder.localPosition; holderLocalRot = holder.localRotation;
            relPos = wrist.InverseTransformPoint(holder.position); relRot = Quaternion.Inverse(wrist.rotation) * holder.rotation;
        }
    }
    public void Stop()
    {
        if (active && holder != null && wrist != null) { holder.localPosition = holderLocalPos; holder.localRotation = holderLocalRot; }
        active = false;
    }

    void LateUpdate()
    {
        if (!active || sourceRoot == null) return;
        float w = Mathf.Min(Mathf.Clamp01((Time.time - startAt) / Blend), Mathf.Clamp01((endAt - Time.time) / Blend));
        w = w * w * (3f - 2f * w);
        if (w <= 0f) { if (Time.time >= endAt) Stop(); return; }
        var inv = Quaternion.Inverse(sourceRoot.rotation);
        foreach (var (target, source) in pairs)
        {
            if (target == null || source == null) continue;
            Vector3 pos = worldSpace ? source.position : transform.TransformPoint(sourceRoot.InverseTransformPoint(source.position));
            Quaternion rot = worldSpace ? source.rotation : transform.rotation * (inv * source.rotation);
            target.SetPositionAndRotation(Vector3.Lerp(target.position, pos, w), Quaternion.Slerp(target.rotation, rot, w));
        }
        if (holder != null && wrist != null)
        {
            var restPos = transform.TransformPoint(holderLocalPos); var restRot = transform.rotation * holderLocalRot;
            holder.SetPositionAndRotation(Vector3.Lerp(restPos, wrist.TransformPoint(relPos), w), Quaternion.Slerp(restRot, wrist.rotation * relRot, w));
        }
        if (Time.time >= endAt) Stop();
    }
}
