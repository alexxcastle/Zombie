using System.Collections.Generic;
using UnityEngine;

// Fingers closed on the weapon (unified arms rig). The holds come from other weapons' animations (donor packs) or
// authored clips, so the fingers float above a thicker handguard or sink into a slimmer one. Once per weapon, a few
// frames after it is shown in its idle hold, every finger of the support hand (and middle/ring/pinky of the firing
// hand: the index stays on the trigger) is curled or opened until its pads just touch the weapon's surface. The
// correction is added every frame on top of the animation, and fades out when the hand leaves the weapon (reloads).
[DefaultExecutionOrder(300)] // after the viewmodel, donor motion, support-hand solve, grip offsets and shoulder trim
public sealed class ElderboomFingerContact : MonoBehaviour
{
    const float PadRadius = 0.0085f, MinCurl = -35f, MaxCurl = 75f, Step = 2.5f;
    static readonly float[] Share = { 1f, 1.1f, .75f };

    sealed class Finger
    {
        public Transform[] joints = new Transform[3];
        public Vector3[] axis = new Vector3[3];       // in each joint's own space
        public Quaternion[] applied = new Quaternion[3];
        public Quaternion[] set = new Quaternion[3];
        public float curl, debug; public bool right;
    }

    Transform weapon, handR, handL, upperL, foreL;
    Vector3 palmShift;            // support palm pushed out of the weapon, in weapon space
    Vector3 lastHandL; bool shifted;
    readonly List<Finger> fingers = new List<Finger>();
    Vector3 restR, restL; // wrists in weapon space when measured
    int frames; bool measured;

    public static void Attach(Transform root, Transform model, Transform weapon, Transform handR, Transform handL)
    {
        Bind(root, model, weapon, handR, handL, false);
    }

    static ElderboomFingerContact Bind(Transform root, Transform model, Transform weapon, Transform handR, Transform handL, bool manual)
    {
        if (root == null || model == null || weapon == null || handR == null || handL == null) return null;
        if (Find(model, "DEF-f_index.01.L") == null) return null; // only the unified rig has the named finger chains
        var c = root.gameObject.AddComponent<ElderboomFingerContact>();
        c.weapon = weapon; c.handR = handR; c.handL = handL;
        var fore = handL.parent;
        if (fore != null && fore.name.EndsWith(".001") && fore.parent != null) fore = fore.parent;
        var upper = fore != null ? fore.parent : null;
        while (upper != null && upper.parent != null && upper.parent.name.ToLowerInvariant().Contains("upper")) upper = upper.parent;
        c.foreL = fore; c.upperL = upper;
        foreach (var side in new[] { "L", "R" })
            foreach (var f in new[] { "index", "middle", "ring", "pinky" })
            {
                if (side == "R" && f == "index") continue; // trigger finger
                if (side == "L" && !manual && ElderboomSupportGrip.Has(root)) continue; // the support grip owns that hand
                var finger = new Finger { right = side == "R" };
                for (int k = 0; k < 3; k++) finger.joints[k] = Find(model, "DEF-f_" + f + ".0" + (k + 1) + "." + side);
                if (System.Array.Exists(finger.joints, j => j == null)) continue;
                for (int k = 0; k < 3; k++) finger.applied[k] = Quaternion.identity;
                c.fingers.Add(finger);
            }
        return c;
    }

    public static ElderboomFingerContact FitContact(Transform root, Transform model, Transform weapon, Transform right, Transform left)
    {
        var contact = Bind(root, model, weapon, right, left, true);
        if (contact == null) return null;
        contact.enabled = false;
        contact.Measure(false);
        return contact;
    }

    public void ApplyPose(float rightWeight, float leftWeight)
    {
        foreach (var f in fingers)
        {
            float weight = f.right ? rightWeight : leftWeight;
            for (int k = 0; k < 3; k++)
            {
                var joint = f.joints[k];
                if (joint.localRotation == f.set[k])
                    joint.localRotation = f.set[k] * Quaternion.Inverse(f.applied[k]);
                if (weight <= 0f || Mathf.Abs(f.curl) < .001f)
                {
                    f.applied[k] = Quaternion.identity;
                    f.set[k] = joint.localRotation;
                    continue;
                }
                f.applied[k] = Quaternion.AngleAxis(f.curl * Share[k] * weight, f.axis[k]);
                joint.localRotation *= f.applied[k];
                f.set[k] = joint.localRotation;
            }
        }
    }

    static Transform Find(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }

    void OnDisable() { frames = 0; } // measure again in the idle hold after the next draw

    void LateUpdate()
    {
        if (weapon == null) return;
        // Undo last frame's correction where nothing rewrote the joint since (no compounding).
        foreach (var f in fingers)
            for (int k = 0; k < 3; k++)
                if (f.joints[k].localRotation == f.set[k]) f.joints[k].localRotation = f.set[k] * Quaternion.Inverse(f.applied[k]);
        if (!measured)
        {
            if (++frames < 6) return;
            Measure();
            measured = true;
        }
        float wl = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.025f, .07f, Vector3.Distance(weapon.InverseTransformPoint(handL.position), restL) * weapon.lossyScale.x));
        if (palmShift != Vector3.zero && upperL != null && foreL != null && !(shifted && handL.position == lastHandL))
        {
            var rot = handL.rotation;
            ElderboomSupportHand.Solve(upperL, foreL, handL, handL.position + weapon.TransformVector(palmShift) * wl);
            handL.rotation = rot;
            lastHandL = handL.position; shifted = true;
        }
        foreach (var f in fingers)
        {
            var hand = f.right ? handR : handL;
            float away = Vector3.Distance(weapon.InverseTransformPoint(hand.position), f.right ? restR : restL) * weapon.lossyScale.x;
            float w = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.025f, .07f, away));
            for (int k = 0; k < 3; k++)
            {
                var q = Quaternion.AngleAxis(f.curl * Share[k] * w, f.axis[k]);
                f.joints[k].localRotation = f.joints[k].localRotation * q;
                f.applied[k] = q; f.set[k] = f.joints[k].localRotation;
            }
        }
    }

    // ---- measuring ---------------------------------------------------------------------------------------------
    readonly List<Vector3> tris = new List<Vector3>();
    readonly List<Vector3> normals = new List<Vector3>(); // one per triangle: its vertex normals averaged (outward)

    void Measure(bool solvePalm = true)
    {
        restR = weapon.InverseTransformPoint(handR.position);
        restL = weapon.InverseTransformPoint(handL.position);
        tris.Clear(); normals.Clear();
        CollectTriangles(.2f);
        if (tris.Count == 0) return;
        palmShift = Vector3.zero;
        Vector3 shiftWorld = !solvePalm || ElderboomSupportGrip.Has(transform) ? Vector3.zero : PalmShift();
        palmShift = weapon.InverseTransformVector(shiftWorld);
        foreach (var f in fingers)
        {
            var hand = f.right ? handR : handL;
            Vector3 o = f.right ? Vector3.zero : shiftWorld;
            Vector3 j0 = f.joints[0].position + o, j1 = f.joints[1].position + o, j2 = f.joints[2].position + o;
            Vector3 tip = j2 + (j2 - j1).normalized * (j2 - j1).magnitude * .95f;
            Vector3 dir = (tip - j0).normalized;
            // Anatomical curl axis: across the knuckles; the palm faces the weapon.
            Vector3 across = Knuckle(f.right, "index") - Knuckle(f.right, "pinky");
            if (f.right) across = Knuckle(true, "middle") - Knuckle(true, "pinky");
            Vector3 palmN = Vector3.Cross(across, dir).normalized;
            Vector3 palmC = hand.position + o;
            if (Vector3.Dot(palmN, Nearest(palmC, out _) - palmC) < 0f) palmN = -palmN;
            Vector3 axisWorld = Vector3.Cross(dir, palmN).normalized;
            for (int k = 0; k < 3; k++) f.axis[k] = Quaternion.Inverse(f.joints[k].rotation) * axisWorld;
            var local = new List<int>();
            for (int i = 0; i < tris.Count; i += 3)
            {
                // by the triangle's nearest point (long rail faces have far centroids)
                if ((ClosestOnTriangle(j0, tris[i], tris[i + 1], tris[i + 2]) - j0).sqrMagnitude < .1f * .1f) local.Add(i);
            }
            if (local.Count == 0) { f.curl = 0f; continue; }
            // Signed clearance of the finger pads from the surface (negative: inside the weapon).
            float Clearance(float curl)
            {
                Vector3 a = j0, b = j1, c = j2, d = tip;
                var q0 = Quaternion.AngleAxis(curl * Share[0], axisWorld);
                b = a + q0 * (b - a); c = a + q0 * (c - a); d = a + q0 * (d - a);
                var q1 = Quaternion.AngleAxis(curl * Share[1], q0 * axisWorld);
                c = b + q1 * (c - b); d = b + q1 * (d - b);
                var q2 = Quaternion.AngleAxis(curl * Share[2], q1 * q0 * axisWorld);
                d = c + q2 * (d - c);
                float m = float.MaxValue;
                for (int s = 1; s <= 3; s++)
                {
                    float t = s / 3f;
                    m = Mathf.Min(m, Signed(Vector3.Lerp(a, b, t), local));
                    m = Mathf.Min(m, Signed(Vector3.Lerp(b, c, t), local));
                    m = Mathf.Min(m, Signed(Vector3.Lerp(c, d, t), local));
                }
                return m - PadRadius;
            }
            float best = 0f, c0 = Clearance(0f); f.debug = c0 * 1000f;
            if (c0 < -.004f)
            {
                // sunk into the weapon: open just until the pads rest on the surface
                // (the firing hand only a little: its fingers wrap the grip and the grip hides them)
                float min = f.right ? -12f : -12f; // opening the hold flattens the hand: only a little
                best = min;
                for (float a = -Step; a >= min; a -= Step) if (Clearance(a) >= -.0015f) { best = a; break; }
            }
            else if (c0 > .003f)
            {
                // floating above it: close until the pads touch (nothing within reach: keep the hold's own pose)
                for (float a = Step; a <= MaxCurl; a += Step) if (Clearance(a) <= 0f) { best = a; break; }
            }
            f.curl = best;
        }
        var report = new System.Text.StringBuilder("[FingerContact] " + weapon.name + " tris " + tris.Count / 3 + " palm shift " + (shiftWorld.magnitude * 1000f).ToString("F0") + "mm:");
        foreach (var f in fingers) report.Append(' ').Append(f.joints[0].name).Append('=').Append(f.curl.ToString("F0")).Append(" (").Append(f.debug.ToString("F1")).Append("mm)");
        Debug.Log(report.ToString());
    }

    Vector3 Knuckle(bool right, string finger)
    {
        foreach (var f in fingers) if (f.right == right && f.joints[0].name.Contains("_" + finger + ".")) return f.joints[0].position;
        return (right ? handR : handL).position;
    }

    // How far the support palm must move so that its knuckles and centre rest on the weapon instead of inside it.
    Vector3 PalmShift()
    {
        // palm points (12 mm of flesh) and every finger joint of the support hand (pads, 8.5 mm)
        var pts = new List<(Vector3 p, float r)> { (handL.position, .012f) };
        Vector3 sum = handL.position; int n = 1;
        foreach (var f in fingers)
        {
            if (f.right) continue;
            sum += f.joints[0].position; n++;
            foreach (var j in f.joints) pts.Add((j.position, PadRadius));
        }
        pts.Add((sum / n, .012f));
        var all = new List<int>(); for (int i = 0; i < tris.Count; i += 3) all.Add(i);
        Vector3 shift = Vector3.zero;
        for (int it = 0; it < 6; it++)
        {
            float worst = 0f; Vector3 dir = Vector3.zero;
            foreach (var (p0, r) in pts)
            {
                float sd = SignedWithNormal(p0 + shift, all, out var nrm);
                if (sd < r && r - sd > worst) { worst = r - sd; dir = nrm; }
            }
            if (worst <= .001f) break;
            shift += dir * (worst + .0005f);
        }
        return shift.magnitude > .045f ? Vector3.zero : shift; // more than 4.5 cm: not a contact problem, leave the hold alone
    }

    float SignedWithNormal(Vector3 p, List<int> local, out Vector3 normal)
    {
        float bd = float.MaxValue, sign = 1f; normal = Vector3.up;
        foreach (int i in local)
        {
            var q = ClosestOnTriangle(p, tris[i], tris[i + 1], tris[i + 2]);
            float d = (q - p).sqrMagnitude;
            if (d < bd) { bd = d; normal = normals[i / 3]; sign = Vector3.Dot(p - q, normal) < 0f ? -1f : 1f; }
        }
        return Mathf.Sqrt(bd) * sign;
    }

    // Distance to the nearest triangle, negative behind it (triangles face outward).
    float Signed(Vector3 p, List<int> local)
    {
        float bd = float.MaxValue, sign = 1f;
        foreach (int i in local)
        {
            Vector3 a = tris[i], b = tris[i + 1], c = tris[i + 2];
            var q = ClosestOnTriangle(p, a, b, c);
            float d = (q - p).sqrMagnitude;
            if (d < bd) { bd = d; sign = Vector3.Dot(p - q, normals[i / 3]) < 0f ? -1f : 1f; }
        }
        return Mathf.Sqrt(bd) * sign;
    }

    Vector3 Nearest(Vector3 p, out float distance)
    {
        Vector3 best = p; float bd = float.MaxValue;
        for (int i = 0; i < tris.Count; i += 3)
        {
            var q = ClosestOnTriangle(p, tris[i], tris[i + 1], tris[i + 2]);
            float d = (q - p).sqrMagnitude; if (d < bd) { bd = d; best = q; }
        }
        distance = Mathf.Sqrt(bd); return best;
    }

    void CollectTriangles(float radius)
    {
        Vector3 cR = handR.position, cL = handL.position; float r2 = radius * radius;
        foreach (var r in weapon.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
            if (r.name.StartsWith("SightUnused") || r.bounds.size.sqrMagnitude < 1e-8f) continue;
            Mesh mesh = null; bool temp = false; Matrix4x4 m = r.localToWorldMatrix;
            if (r is SkinnedMeshRenderer smr) { mesh = new Mesh(); smr.BakeMesh(mesh, true); temp = true; m = smr.transform.localToWorldMatrix; }
            else { var mf = r.GetComponent<MeshFilter>(); mesh = mf != null ? mf.sharedMesh : null; }
            if (mesh == null || !mesh.isReadable) { if (temp) Destroy(mesh); continue; }
            var v = mesh.vertices; var vn = mesh.normals; bool hasN = vn != null && vn.Length == v.Length;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                var t = mesh.GetTriangles(s);
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 a = m.MultiplyPoint3x4(v[t[i]]), b = m.MultiplyPoint3x4(v[t[i + 1]]), c = m.MultiplyPoint3x4(v[t[i + 2]]);
                    Vector3 ctr = (a + b + c) / 3f;
                    if ((ctr - cR).sqrMagnitude > r2 && (ctr - cL).sqrMagnitude > r2) continue;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    normals.Add(hasN ? m.MultiplyVector(vn[t[i]] + vn[t[i + 1]] + vn[t[i + 2]]).normalized : Vector3.Cross(b - a, c - a).normalized);
                }
            }
            if (temp) Destroy(mesh);
        }
    }

    // Ericson, Real-Time Collision Detection 5.1.5
    static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;
        Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
        Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
        float denom = 1f / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }
}
