using System.Collections.Generic;
using UnityEngine;

// The support hand built for the weapon's own handguard (unified arms rig). The donor holds come from other weapons
// (the M4 borrows the MP5's), whose slimmer handguards leave the left palm open under a thick AR-15 rail.
//  1. PlaceMarker (while the hold is built): the handguard's cross-section is measured on the mesh where the donor hold
//     brings the hand, and the support marker the donor motion solves the left arm to (shoulder, elbow and forearm
//     twist included) is moved there: palm under the handguard turned a little to the right, index toward the muzzle.
//  2. The component (every frame): the fingers, straightened along the palm and curled until their pads touch the
//     handguard, the thumb along its left side. Measured once, a few frames after the weapon is shown; while a reload
//     takes the hand away from the gun, the authored fingers play as they are.
[DefaultExecutionOrder(290)] // after the donor motion and grip offsets, before the finger contact pass
public sealed class ElderboomSupportGrip : MonoBehaviour
{
    // support region per weapon, in the muzzle marker's space (+Z toward the muzzle, +Y up, +X right)
    static readonly Dictionary<string, Vector3> Points = new Dictionary<string, Vector3>
    {
        { "m4", new Vector3(0f, -.0169f, -.2952f) }, // M4_v7 LeftHand_Support marker, under the handguard
    };
    static readonly float[] Share = { .9f, 1.1f, .8f };

    Transform weapon, muzzle, hand;
    Transform[][] chains; Transform[] palms;
    Quaternion[][] fingerLocal; Quaternion[] palmLocal;
    Vector3 wristW; bool measured; int frames;

    public static bool Has(Transform root) => root != null && root.GetComponent<ElderboomSupportGrip>() != null;

    // ---- 1. where the hand goes -------------------------------------------------------------------------------------
    public static bool PlaceMarker(Transform marker, Transform weapon, string weaponId, Transform handL, Transform middleKnuckle)
    {
        if (marker == null || weapon == null || handL == null || middleKnuckle == null || !Points.TryGetValue(weaponId, out var support)) return false;
        var muzzle = ElderboomWeaponMods.FindMuzzle(weapon);
        if (muzzle == null) return false;
        var tris = new List<Vector3>(); var normals = new List<Vector3>();
        Collect(weapon, muzzle.TransformPoint(support), .2f, tris, normals);
        if (tris.Count == 0) return false;
        float zs = support.z; // the donor motion moves the shoulder when the hold is out of reach
        float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
        foreach (var t in tris)
        {
            var p = muzzle.InverseTransformPoint(t);
            if (Mathf.Abs(p.z - zs) > .015f || Mathf.Abs(p.x) > .06f || p.y < support.y - .05f || p.y > .06f) continue;
            x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); y0 = Mathf.Min(y0, p.y); y1 = Mathf.Max(y1, p.y);
        }
        if (x0 > x1) return false;
        Vector3 right = muzzle.TransformDirection(Vector3.right), up = muzzle.TransformDirection(Vector3.up), fwd = muzzle.TransformDirection(Vector3.forward);
        Vector3 palmN = (up + right * .45f).normalized, along = (right - up * .45f).normalized;
        float palmLen = Vector3.Distance(middleKnuckle.position, handL.position);
        Vector3 contact = muzzle.TransformPoint(new Vector3((x0 + x1) * .5f - (x1 - x0) * .22f, y0, zs));
        Vector3 wrist = contact - palmN * .016f - along * palmLen * .55f;
        // the marker is a hand frame: X across the knuckles (index side), Y along the palm, Z = X × Y
        Vector3 x = fwd, y = Vector3.ProjectOnPlane(along, x).normalized, z = Vector3.Cross(x, y);
        marker.SetPositionAndRotation(wrist, Quaternion.LookRotation(z, y));
        Debug.Log("[SupportGrip] " + weapon.name + " handguard x " + (x0 * 1000f).ToString("F0") + ".." + (x1 * 1000f).ToString("F0") + " y " + (y0 * 1000f).ToString("F0") + ".." + (y1 * 1000f).ToString("F0") + " mm at z " + (zs * 1000f).ToString("F0") + ": marker moved " + (Vector3.Distance(wrist, handL.position) * 1000f).ToString("F0") + " mm");
        return true;
    }

    // ---- 2. the fingers ---------------------------------------------------------------------------------------------
    public static void Attach(Transform root, Transform model, Transform weapon, Transform handL)
    {
        if (root == null || model == null || weapon == null || handL == null) return;
        var muzzle = ElderboomWeaponMods.FindMuzzle(weapon);
        if (muzzle == null) return;
        string[] names = { "f_index", "f_middle", "f_ring", "f_pinky", "thumb" };
        var chains = new Transform[5][];
        for (int i = 0; i < 5; i++)
        {
            chains[i] = new Transform[3];
            for (int k = 0; k < 3; k++) { chains[i][k] = Find(model, "DEF-" + names[i] + ".0" + (k + 1) + ".L"); if (chains[i][k] == null) return; }
        }
        var palms = new Transform[4];
        for (int i = 0; i < 4; i++) { palms[i] = Find(model, "DEF-palm.0" + (i + 1) + ".L"); if (palms[i] == null) return; }
        var c = root.gameObject.AddComponent<ElderboomSupportGrip>();
        c.weapon = weapon; c.muzzle = muzzle; c.hand = handL; c.chains = chains; c.palms = palms;
    }

    static Transform Find(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }

    void OnDisable() { frames = 0; }

    void LateUpdate()
    {
        if (weapon == null || hand == null) return;
        if (!measured)
        {
            if (++frames < 5) return;
            measured = true;
            if (!Measure()) { enabled = false; return; }
        }
        float away = Vector3.Distance(hand.position, weapon.TransformPoint(wristW));
        float w = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.02f, .07f, away));
        if (w <= 0f) return;
        for (int i = 0; i < 4; i++) palms[i].localRotation = Quaternion.Slerp(palms[i].localRotation, palmLocal[i], w);
        for (int i = 0; i < 5; i++)
            for (int k = 0; k < 3; k++) chains[i][k].localRotation = Quaternion.Slerp(chains[i][k].localRotation, fingerLocal[i][k], w);
    }

    bool Measure()
    {
        var tris = new List<Vector3>(); var normals = new List<Vector3>();
        Collect(weapon, hand.position, .16f, tris, normals);
        if (tris.Count == 0) return false;
        wristW = weapon.InverseTransformPoint(hand.position);
        Vector3 right = muzzle.TransformDirection(Vector3.right), up = muzzle.TransformDirection(Vector3.up), fwd = muzzle.TransformDirection(Vector3.forward);
        // the hand as the donor motion placed it
        Vector3 along = (chains[1][0].position - hand.position).normalized;
        Vector3 across = (chains[0][0].position - chains[3][0].position).normalized;
        Vector3 palmN = Vector3.Cross(across, along).normalized;
        Vector3 palmC = (hand.position + chains[1][0].position) * .5f;
        if (Vector3.Dot(palmN, Nearest(palmC, tris) - palmC) < 0f) palmN = -palmN;
        var all = new List<int>(); for (int i = 0; i < tris.Count; i += 3) all.Add(i);
        palmLocal = new Quaternion[4];
        for (int i = 0; i < 4; i++)
        {
            var p = palms[i]; var dir = chains[i][0].position - p.position;
            p.rotation = Quaternion.FromToRotation(dir, along * Vector3.Dot(dir, along) + across * Vector3.Dot(dir, across)) * p.rotation;
            palmLocal[i] = p.localRotation;
        }
        fingerLocal = new Quaternion[5][];
        var report = new System.Text.StringBuilder("[SupportGrip] " + weapon.name + " fingers:");
        for (int i = 0; i < 5; i++)
        {
            bool thumb = i == 4; var ch = chains[i];
            Vector3 aim = thumb ? (fwd * .8f + up * .35f - right * .2f).normalized : along;
            for (int k = 0; k < 3; k++)
            {
                var next = k < 2 ? ch[k + 1].position : ch[k].position + (ch[k].position - ch[k - 1].position);
                ch[k].rotation = Quaternion.FromToRotation(next - ch[k].position, aim) * ch[k].rotation;
            }
            Vector3 axis = Vector3.Cross(aim, palmN).normalized;
            float best = 0f;
            for (float a = 0f; a <= 100f; a += 2.5f) { best = a; if (Clearance(ch, axis, a, all, tris, normals) <= 0f) break; }
            if (thumb) best = Mathf.Min(best, 35f);
            for (int k = 0; k < 3; k++) ch[k].rotation = Quaternion.AngleAxis(best * Share[k], axis) * ch[k].rotation;
            fingerLocal[i] = new Quaternion[3];
            for (int k = 0; k < 3; k++) fingerLocal[i][k] = ch[k].localRotation;
            report.Append(' ').Append(ch[0].name).Append('=').Append(best.ToString("F0"));
        }
        Debug.Log(report.ToString());
        return true;
    }

    static float Clearance(Transform[] ch, Vector3 axis, float curl, List<int> all, List<Vector3> tris, List<Vector3> normals)
    {
        Vector3 a = ch[0].position, b = ch[1].position, c = ch[2].position;
        Vector3 d = c + (c - b).normalized * (c - b).magnitude * .95f;
        var q0 = Quaternion.AngleAxis(curl * Share[0], axis);
        b = a + q0 * (b - a); c = a + q0 * (c - a); d = a + q0 * (d - a);
        var q1 = Quaternion.AngleAxis(curl * Share[1], axis);
        c = b + q1 * (c - b); d = b + q1 * (d - b);
        var q2 = Quaternion.AngleAxis(curl * Share[2], axis);
        d = c + q2 * (d - c);
        float m = float.MaxValue;
        for (int s = 1; s <= 3; s++)
        {
            float t = s / 3f;
            m = Mathf.Min(m, Signed(Vector3.Lerp(a, b, t), all, tris, normals));
            m = Mathf.Min(m, Signed(Vector3.Lerp(b, c, t), all, tris, normals));
            m = Mathf.Min(m, Signed(Vector3.Lerp(c, d, t), all, tris, normals));
        }
        return m - .0085f;
    }

    static float Signed(Vector3 p, List<int> all, List<Vector3> tris, List<Vector3> normals)
    {
        float bd = float.MaxValue, sign = 1f;
        foreach (int i in all)
        {
            var q = Closest(p, tris[i], tris[i + 1], tris[i + 2]);
            float d = (q - p).sqrMagnitude;
            if (d < bd) { bd = d; sign = Vector3.Dot(p - q, normals[i / 3]) < 0f ? -1f : 1f; }
        }
        return Mathf.Sqrt(bd) * sign;
    }

    static Vector3 Nearest(Vector3 p, List<Vector3> tris)
    {
        Vector3 best = p; float bd = float.MaxValue;
        for (int i = 0; i < tris.Count; i += 3) { var q = Closest(p, tris[i], tris[i + 1], tris[i + 2]); float d = (q - p).sqrMagnitude; if (d < bd) { bd = d; best = q; } }
        return best;
    }

    static void Collect(Transform weapon, Vector3 centre, float radius, List<Vector3> tris, List<Vector3> normals)
    {
        float r2 = radius * radius;
        foreach (var r in weapon.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy || r.name.StartsWith("Mod_")) continue;
            var mf = r.GetComponent<MeshFilter>(); var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null || !mesh.isReadable) continue;
            var m = r.localToWorldMatrix; var v = mesh.vertices; var vn = mesh.normals; bool hasN = vn != null && vn.Length == v.Length;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                var t = mesh.GetTriangles(s);
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 a = m.MultiplyPoint3x4(v[t[i]]), b = m.MultiplyPoint3x4(v[t[i + 1]]), c = m.MultiplyPoint3x4(v[t[i + 2]]);
                    if ((Closest(centre, a, b, c) - centre).sqrMagnitude > r2) continue;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    normals.Add(hasN ? m.MultiplyVector(vn[t[i]] + vn[t[i + 1]] + vn[t[i + 2]]).normalized : Vector3.Cross(b - a, c - a).normalized);
                }
            }
        }
    }

    static Vector3 Closest(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
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
