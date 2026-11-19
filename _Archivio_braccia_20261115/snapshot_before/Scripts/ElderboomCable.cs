using System.Collections.Generic;
using UnityEngine;

// A ground power cable for the tech kit: a smooth tube through the given points (local space), resting on whatever is
// below it, with a plug at each end. Edit the points in the Inspector; the mesh rebuilds in the editor and at start.
[ExecuteAlways, RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class ElderboomCable : MonoBehaviour
{
    public List<Vector3> points = new List<Vector3> { Vector3.zero, new Vector3(1f, 0f, .25f), new Vector3(2f, 0f, 0f) };
    [Range(.01f, .06f)] public float radius = .022f;
    public bool followGround = true;
    public float groundOffset = 0f;
    public LayerMask groundMask = ~0;
    [Range(6, 16)] public int sides = 8;
    public float samplesPerMeter = 10f;
    public float plugLength = .14f, plugRadius = .036f;

    Mesh mesh;
    void OnEnable() => Rebuild();
    void OnValidate() { if (isActiveAndEnabled) Rebuild(); }

    Vector3 Spline(int i, float t)
    {
        Vector3 p0 = points[Mathf.Max(i - 1, 0)], p1 = points[i], p2 = points[Mathf.Min(i + 1, points.Count - 1)], p3 = points[Mathf.Min(i + 2, points.Count - 1)];
        float t2 = t * t, t3 = t2 * t;
        return .5f * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
    }

    Vector3 Ground(Vector3 local)
    {
        if (!followGround) return local;
        var w = transform.TransformPoint(local);
        var hits = Physics.RaycastAll(w + Vector3.up * 1.5f, Vector3.down, 6f, groundMask, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        foreach (var h in hits) if (!h.transform.IsChildOf(transform) && h.point.y > best) best = h.point.y;
        if (float.IsNegativeInfinity(best)) return local;
        w.y = best + radius + groundOffset;
        return transform.InverseTransformPoint(w);
    }

    public void Rebuild()
    {
        if (points == null || points.Count < 2) return;
        // Path samples
        var path = new List<Vector3>();
        for (int i = 0; i < points.Count - 1; i++)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(points[i], points[i + 1]) * samplesPerMeter));
            for (int s = 0; s < n; s++) path.Add(Ground(Spline(i, s / (float)n)));
        }
        path.Add(Ground(points[points.Count - 1]));
        // Leave room for the plugs at both ends.
        var cable = new List<Vector3>(path);
        var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>();
        var rubber = new List<int>(); var plug = new List<int>(); var collar = new List<int>();
        Vector3 up = Vector3.up; float along = 0;
        for (int i = 0; i < cable.Count; i++)
        {
            Vector3 fwd = (cable[Mathf.Min(i + 1, cable.Count - 1)] - cable[Mathf.Max(i - 1, 0)]).normalized;
            if (fwd == Vector3.zero) fwd = Vector3.right;
            Vector3 side = Vector3.Cross(up, fwd); if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(Vector3.forward, fwd); side.Normalize();
            Vector3 nup = Vector3.Cross(fwd, side);
            if (i > 0) along += Vector3.Distance(cable[i], cable[i - 1]);
            for (int k = 0; k <= sides; k++)
            {
                float a = k / (float)sides * Mathf.PI * 2; Vector3 dir = side * Mathf.Cos(a) + nup * Mathf.Sin(a);
                verts.Add(cable[i] + dir * radius); norms.Add(dir); uvs.Add(new Vector2(k / (float)sides, along * 4f));
            }
            if (i > 0)
            {
                int b0 = (i - 1) * (sides + 1), b1 = i * (sides + 1);
                for (int k = 0; k < sides; k++) { rubber.Add(b0 + k); rubber.Add(b1 + k); rubber.Add(b0 + k + 1); rubber.Add(b0 + k + 1); rubber.Add(b1 + k); rubber.Add(b1 + k + 1); }
            }
        }
        // Plugs: a dark body and a bright collar, pointing outwards from each end.
        void Cylinder(List<int> tris, Vector3 a, Vector3 b, float r, bool capB)
        {
            Vector3 f = (b - a).normalized; Vector3 s = Vector3.Cross(Vector3.up, f); if (s.sqrMagnitude < 1e-6f) s = Vector3.Cross(Vector3.forward, f); s.Normalize(); Vector3 u = Vector3.Cross(f, s);
            int start = verts.Count;
            for (int ring = 0; ring < 2; ring++)
                for (int k = 0; k <= sides; k++) { float an = k / (float)sides * Mathf.PI * 2; Vector3 d = s * Mathf.Cos(an) + u * Mathf.Sin(an); verts.Add((ring == 0 ? a : b) + d * r); norms.Add(d); uvs.Add(new Vector2(k / (float)sides, ring)); }
            for (int k = 0; k < sides; k++) { int a0 = start + k, b0 = start + sides + 1 + k; tris.Add(a0); tris.Add(b0); tris.Add(a0 + 1); tris.Add(a0 + 1); tris.Add(b0); tris.Add(b0 + 1); }
            if (capB)
            {
                int c = verts.Count; verts.Add(b); norms.Add(f); uvs.Add(new Vector2(.5f, .5f));
                int rim = verts.Count;
                for (int k = 0; k <= sides; k++) { float an = k / (float)sides * Mathf.PI * 2; verts.Add(b + (s * Mathf.Cos(an) + u * Mathf.Sin(an)) * r); norms.Add(f); uvs.Add(new Vector2(.5f + .5f * Mathf.Cos(an), .5f + .5f * Mathf.Sin(an))); }
                for (int k = 0; k < sides; k++) { tris.Add(c); tris.Add(rim + k + 1); tris.Add(rim + k); }
            }
        }
        foreach (bool start in new[] { true, false })
        {
            Vector3 end = start ? cable[0] : cable[cable.Count - 1];
            Vector3 inward = start ? (cable[Mathf.Min(1, cable.Count - 1)] - cable[0]).normalized : (cable[cable.Count - 2] - cable[cable.Count - 1]).normalized;
            Vector3 outward = -inward;
            Cylinder(plug, end - outward * .02f, end + outward * plugLength * .65f, plugRadius, false);
            Cylinder(collar, end + outward * plugLength * .65f, end + outward * plugLength, plugRadius * 1.08f, true);
        }
        if (mesh == null) { mesh = new Mesh { name = "ElderboomCable" }; mesh.hideFlags = HideFlags.DontSave; }
        mesh.Clear();
        mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 3; mesh.SetTriangles(rubber, 0); mesh.SetTriangles(plug, 1); mesh.SetTriangles(collar, 2);
        mesh.RecalculateBounds(); mesh.RecalculateTangents();
        GetComponent<MeshFilter>().sharedMesh = mesh;
    }
}
