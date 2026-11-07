using System.Collections.Generic;
using UnityEngine;

// Eli's weapon holds (Eli_<weapon>_Idle in Eli_Grips) stretch the left forearm: the hand bone sits 6.7 cm past its
// bind length and the knuckles over-bend, so the left hand looks deformed in every grip. Each frame, after the hold
// is sampled: the hand keeps where (and how) the hold puts it, the forearm is restored to its true length with a
// two-bone solve from the shoulder, and the fingers take the clean support grip (HandGrip_Rifle / _Pistol).
[DefaultExecutionOrder(60)]
public sealed class ElderboomLimbLock : MonoBehaviour
{
    Transform upper, fore, hand;
    Vector3 handRest;
    readonly List<(Transform bone, Quaternion rot)> fingers = new List<(Transform, Quaternion)>();

    public static void Attach(Transform model, string side, AnimationClip grip, AnimationClip hold)
    {
        Transform Find(string n) { foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t; return null; }
        var h = Find("DEF_hand." + side); var f = Find("DEF_forearm." + side); var u = Find("DEF_upper_arm." + side);
        if (h == null || f == null || u == null || h.parent != f) return;
        var lockC = model.gameObject.AddComponent<ElderboomLimbLock>();
        lockC.upper = u; lockC.fore = f; lockC.hand = h;
        lockC.handRest = h.localPosition; // the model is still in its bind pose here
        if (grip != null)
        {
            ElderboomAnimationSampling.Sample(grip, model.gameObject, 0f);
            foreach (var t in h.GetComponentsInChildren<Transform>(true))
                if (t != h && t.name.StartsWith("DEF_")) lockC.fingers.Add((t, t.localRotation));
        }
        if (hold != null) ElderboomAnimationSampling.Sample(hold, model.gameObject, 0f);
    }

    void LateUpdate()
    {
        if (hand == null) return;
        Vector3 target = hand.position; Quaternion handRot = hand.rotation;
        hand.localPosition = handRest;
        ElderboomSupportHand.Solve(upper, fore, hand, target);
        hand.rotation = handRot;
        foreach (var (bone, rot) in fingers) if (bone != null) bone.localRotation = rot;
    }
}

// First-person arms whose shoulder comes into view (the Tactical operator's right shoulder in its idle, Soph's
// sleeves when aiming): the shoulder end of the upper arm is pulled toward the elbow, shortening the sleeve that
// reaches the camera while the elbow, forearm and hand stay exactly where the animation put them.
// Runs after the grips, the arm IK and the left-arm abilities.
[DefaultExecutionOrder(250)]
public sealed class ElderboomShoulderTrim : MonoBehaviour
{
    sealed class Side { public List<Transform> upper = new List<Transform>(); public Transform elbow; public float amount; }
    readonly List<Side> sides = new List<Side>();
    string boundTo;

    public static void Ensure(Transform root, string operatorId)
    {
        if (root == null) return;
        float right = 0f, left = 0f;
        if (operatorId == ElderboomIds.Tactical) { right = .38f; left = .12f; }
        else if (operatorId == ElderboomIds.Soph) { right = .3f; left = .3f; }
        else if (operatorId == ElderboomIds.Eli) { right = .3f; left = .15f; }
        var trim = root.GetComponent<ElderboomShoulderTrim>() ?? root.gameObject.AddComponent<ElderboomShoulderTrim>();
        trim.Bind(operatorId, right, left);
    }

    static bool IsSide(string name, bool right)
    {
        string n = name.ToLowerInvariant();
        return right ? n.EndsWith(".r") || n.Contains(".r.") || n.EndsWith("_r") || n.StartsWith("r_") || n.Contains("right")
                     : n.EndsWith(".l") || n.Contains(".l.") || n.EndsWith("_l") || n.StartsWith("l_") || n.Contains("left");
    }

    void Bind(string op, float right, float left)
    {
        if (boundTo == op && sides.Count > 0) return;
        boundTo = op; sides.Clear();
        var bones = new HashSet<Transform>();
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (smr.bones != null) foreach (var b in smr.bones) if (b != null) bones.Add(b);
        foreach (bool isRight in new[] { true, false })
        {
            float amount = isRight ? right : left; if (amount <= 0f) continue;
            var s = new Side { amount = amount };
            foreach (var b in bones)
            {
                if (!IsSide(b.name, isRight)) continue;
                string n = b.name.ToLowerInvariant();
                if (n.Contains("upper") || n.Contains("upperarm") || n == (isRight ? "arm.r" : "arm.l") || n.EndsWith(isRight ? "rightarm" : "leftarm")) s.upper.Add(b);
                else if (s.elbow == null && (n.Contains("forearm") || n.Contains("lowerarm") || n.Contains("fore"))) s.elbow = b;
            }
            if (s.upper.Count > 0 && s.elbow != null) sides.Add(s);
        }
    }

    void LateUpdate()
    {
        foreach (var s in sides)
        {
            if (s.elbow == null) continue;
            Transform shoulder = s.upper[0]; float far = -1f;
            foreach (var u in s.upper) { float d = (u.position - s.elbow.position).sqrMagnitude; if (d > far) { far = d; shoulder = u; } }
            Vector3 shift = (s.elbow.position - shoulder.position) * s.amount;
            Vector3 ep = s.elbow.position; Quaternion er = s.elbow.rotation;
            foreach (var u in s.upper) if (u != null && !s.upper.Contains(u.parent)) u.position += shift; // children ride along
            s.elbow.SetPositionAndRotation(ep, er); // hierarchical rigs: the forearm (and the hand under it) stays
        }
    }
}

// Left hand on the weapon: the authored holds leave the support hand off the gun (open, beside it). The pack's own
// left hand shows where it belongs on that weapon; a marker under the weapon keeps that place (and the hand's
// orientation) and a two-bone solve from the shoulder brings the operator's left hand there every frame.
[DefaultExecutionOrder(70)]
public sealed class ElderboomSupportHand : MonoBehaviour
{
    Transform upper, fore, hand, target;
    public static void Attach(Transform model, Transform handBone, Transform marker)
    {
        if (handBone == null || marker == null || handBone.parent == null) return;
        var fore = handBone.parent; var upper = fore.parent;
        while (upper != null && upper.parent != null && upper.parent.name.ToLowerInvariant().Contains("upper")) upper = upper.parent; // past twist bones
        if (upper == null) return;
        var c = model.gameObject.AddComponent<ElderboomSupportHand>();
        c.upper = upper; c.fore = fore; c.hand = handBone; c.target = marker;
    }

    public static void Solve(Transform upper, Transform fore, Transform hand, Vector3 target)
    {
        Vector3 a = upper.position, b = fore.position, c = hand.position;
        float l1 = (b - a).magnitude, l2 = (c - b).magnitude;
        if (l1 < 1e-4f || l2 < 1e-4f) return;
        Vector3 to = target - a; float d = Mathf.Clamp(to.magnitude, Mathf.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-3f);
        Vector3 dir = to.sqrMagnitude > 1e-8f ? to.normalized : (c - a).normalized;
        Vector3 bend = (b - a) - Vector3.Dot(b - a, dir) * dir; if (bend.sqrMagnitude < 1e-8f) bend = Vector3.Cross(dir, Vector3.up); bend.Normalize();
        float along = (l1 * l1 - l2 * l2 + d * d) / (2f * d), height = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - along * along));
        Vector3 elbow = a + dir * along + bend * height;
        upper.rotation = Quaternion.FromToRotation(fore.position - a, elbow - a) * upper.rotation;
        fore.rotation = Quaternion.FromToRotation(hand.position - fore.position, a + dir * d - fore.position) * fore.rotation;
    }

    void LateUpdate()
    {
        if (target == null || hand == null) return;
        var left = GetComponentInParent<ElderboomLeftArmAbility>();
        if (left != null && left.Active) return;
        Solve(upper, fore, hand, target.position);
        hand.rotation = target.rotation;
    }
}

// A weapon shown smaller or bigger than its model (the STRIKER-12 reads oversized): scaled about the hand holding it,
// every frame after the viewmodel animation (which may rewrite the node), without compounding when it does not.
[DefaultExecutionOrder(45)]
public sealed class ElderboomScaleAbout : MonoBehaviour
{
    public Transform target, pivot; public float factor = 1f;
    Vector3 setPos, setScale; bool applied;

    public static void Apply(Transform weaponRoot, Transform node, Transform hand, float f)
    {
        if (node == null || hand == null) return;
        foreach (var e in weaponRoot.GetComponents<ElderboomScaleAbout>()) if (e.target == node) return;
        var c = weaponRoot.gameObject.AddComponent<ElderboomScaleAbout>(); c.target = node; c.pivot = hand; c.factor = f; c.LateUpdate(); // now, so the sights are measured scaled
    }

    public void LateUpdate()
    {
        if (target == null || pivot == null) return;
        if (applied && target.localPosition == setPos && target.localScale == setScale) return; // nothing rewrote it
        Vector3 p = pivot.position;
        target.localScale *= factor;
        target.position = p + (target.position - p) * factor;
        setPos = target.localPosition; setScale = target.localScale; applied = true;
    }
}
