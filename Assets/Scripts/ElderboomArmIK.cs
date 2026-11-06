using System.Collections.Generic;
using UnityEngine;

// Two-bone IK that brings an operator's own arms to a weapon taken from another pack:
// the right hand to the grip, the left hand to the support position the pack's arms used.
// Works on hierarchical rigs (Eli, Tactical) and flat ones whose deform bones are not
// parented (Soph): each arm segment (upper arm, forearm, hand + fingers) is moved as a
// rigid body from its animated pose, so both kinds of rig follow. Runs after the
// viewmodel animation; hand rotation keeps its animated (camera-space) orientation.
[DefaultExecutionOrder(100)]
public sealed class ElderboomArmIK : MonoBehaviour
{
    sealed class Arm
    {
        public Transform target;
        public List<Transform> upper = new List<Transform>(), fore = new List<Transform>(), hand = new List<Transform>();
        public Transform shoulder, elbow, wrist;
        public Vector3[] upperPos, forePos, handPos;
        public Quaternion[] upperRot, foreRot, handRot;
    }

    readonly List<Arm> arms = new List<Arm>();
    [Range(0f, 1f)] public float weight = 1f;

    // bones: the operator arm mesh bones; wrist names for each side.
    public void Setup(IList<Transform> bones, string rightWrist, Transform rightTarget, string leftWrist, Transform leftTarget)
    {
        arms.Clear();
        AddArm(bones, rightWrist, rightTarget, true);
        AddArm(bones, leftWrist, leftTarget, false);
        var cam = Camera.main != null ? Camera.main.transform : transform;
        var report = new System.Text.StringBuilder(name + ": " + arms.Count + " arm(s), " + bones.Count + " bones\n");
        foreach (var arm in arms)
            report.AppendLine("  " + arm.wrist.name + " shoulder=" + arm.shoulder.name + " elbow=" + arm.elbow.name
                + " upper[" + string.Join(",", arm.upper.ConvertAll(b => b.name)) + "] fore[" + string.Join(",", arm.fore.ConvertAll(b => b.name)) + "] hand=" + arm.hand.Count
                + " wristCam=" + cam.InverseTransformPoint(arm.wrist.position).ToString("F3") + " targetCam=" + cam.InverseTransformPoint(arm.target.position).ToString("F3")
                + " reach=" + ((arm.elbow.position - arm.shoulder.position).magnitude + (arm.wrist.position - arm.elbow.position).magnitude).ToString("F3")
                + " need=" + (arm.target.position - arm.shoulder.position).magnitude.ToString("F3"));
        System.IO.File.AppendAllText("Logs/arm_ik.txt", report.ToString());
    }

    void AddArm(IList<Transform> bones, string wristName, Transform target, bool right)
    {
        if (target == null || string.IsNullOrEmpty(wristName)) return;
        Transform wrist = null;
        foreach (var b in bones) if (b != null && b.name == wristName) { wrist = b; break; }
        if (wrist == null) return;
        var arm = new Arm { target = target, wrist = wrist };
        foreach (var b in bones)
        {
            if (b == null || !IsSide(b.name, right)) continue;
            string n = b.name.ToLowerInvariant();
            if (n.Contains("upper")) arm.upper.Add(b);
            else if (n.Contains("fore")) arm.fore.Add(b);
            else if (b == wrist || n.Contains("palm") || n.Contains("thumb") || n.Contains("index") || n.Contains("middle") || n.Contains("ring") || n.Contains("pinky") || n.Contains("f_") || n.Contains("finger")) arm.hand.Add(b);
        }
        if (arm.upper.Count == 0 || arm.fore.Count == 0) return;
        if (!arm.hand.Contains(wrist)) arm.hand.Add(wrist);
        arm.shoulder = Farthest(arm.upper, wrist.position);
        arm.elbow = Farthest(arm.fore, wrist.position);
        Allocate(arm);
        arms.Add(arm);
    }

    static bool IsSide(string name, bool right)
    {
        string n = name.ToLowerInvariant();
        return right
            ? n.EndsWith(".r") || n.Contains(".r.") || n.EndsWith("_r") || n.StartsWith("r_") || n.Contains("right")
            : n.EndsWith(".l") || n.Contains(".l.") || n.EndsWith("_l") || n.StartsWith("l_") || n.Contains("left");
    }

    static Transform Farthest(List<Transform> set, Vector3 from)
    {
        Transform best = set[0]; float d = -1f;
        foreach (var t in set) { float e = (t.position - from).sqrMagnitude; if (e > d) { d = e; best = t; } }
        return best;
    }

    static void Allocate(Arm a)
    {
        a.upperPos = new Vector3[a.upper.Count]; a.upperRot = new Quaternion[a.upper.Count];
        a.forePos = new Vector3[a.fore.Count]; a.foreRot = new Quaternion[a.fore.Count];
        a.handPos = new Vector3[a.hand.Count]; a.handRot = new Quaternion[a.hand.Count];
    }

    void LateUpdate()
    {
        if (weight <= 0f) return;
        foreach (var arm in arms) if (arm.target != null) Solve(arm);
    }

    void Solve(Arm arm)
    {
        // Cache the animated pose of every bone first (hierarchical rigs move children).
        Cache(arm.upper, arm.upperPos, arm.upperRot);
        Cache(arm.fore, arm.forePos, arm.foreRot);
        Cache(arm.hand, arm.handPos, arm.handRot);

        Vector3 a = arm.shoulder.position, b = arm.elbow.position, c = arm.wrist.position;
        Vector3 t = Vector3.Lerp(c, arm.target.position, weight);
        float upperLength = (b - a).magnitude, foreLength = (c - b).magnitude;
        if (upperLength < 1e-4f || foreLength < 1e-4f) return;

        Vector3 toTarget = t - a;
        float distance = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(upperLength - foreLength) + 1e-3f, upperLength + foreLength - 1e-3f);
        Vector3 direction = toTarget.sqrMagnitude > 1e-8f ? toTarget.normalized : (c - a).normalized;
        t = a + direction * distance;
        // Keep the elbow on the side it is animated on.
        Vector3 bend = (b - a) - Vector3.Dot(b - a, direction) * direction;
        if (bend.sqrMagnitude < 1e-8f) bend = Vector3.Cross(direction, Vector3.up);
        bend.Normalize();
        float along = (upperLength * upperLength - foreLength * foreLength + distance * distance) / (2f * distance);
        float height = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
        Vector3 newElbow = a + direction * along + bend * height;

        var upperDelta = Quaternion.FromToRotation(b - a, newElbow - a);
        var foreDelta = Quaternion.FromToRotation(upperDelta * (c - b), t - newElbow) * upperDelta;

        // Upper arm pivots about the shoulder; forearm is moved to the new elbow; the hand
        // is translated to the target and keeps its animated rotation.
        Apply(arm.upper, arm.upperPos, arm.upperRot, p => a + upperDelta * (p - a), upperDelta);
        Apply(arm.fore, arm.forePos, arm.foreRot, p => newElbow + foreDelta * (p - b), foreDelta);
        Vector3 shift = t - c;
        Apply(arm.hand, arm.handPos, arm.handRot, p => p + shift, Quaternion.identity);
    }

    static void Cache(List<Transform> bones, Vector3[] pos, Quaternion[] rot)
    {
        for (int i = 0; i < bones.Count; i++) { pos[i] = bones[i].position; rot[i] = bones[i].rotation; }
    }

    static void Apply(List<Transform> bones, Vector3[] pos, Quaternion[] rot, System.Func<Vector3, Vector3> place, Quaternion delta)
    {
        for (int i = 0; i < bones.Count; i++)
            bones[i].SetPositionAndRotation(place(pos[i]), delta * rot[i]);
    }
}
