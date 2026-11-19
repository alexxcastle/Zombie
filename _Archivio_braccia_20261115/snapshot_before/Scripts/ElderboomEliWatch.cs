using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Eli's wrist watch becomes a real screen. While an ability effect runs it counts the
// seconds down (cyan), then shows the cooldown (amber), then the time of day (green dot = ready).
// The screen is a small emissive disc parented to Eli's left forearm bone, placed on the
// watch dial of the Eli arm model (position measured on Resources/Elderboom/Grips/Eli_Grips).
public sealed class ElderboomEliWatch : MonoBehaviour
{
    // Dial centre / normal in the DEF-forearm.L.001 bone space of the unified arms rig (glTF frame, X mirrored for Unity).
    const string ForearmBone = "DEF-forearm.L.001";
    static readonly Vector3 DialCenter = new Vector3(-0.009578f, 0.117192f, 0.028201f);
    static readonly Vector3 DialNormal = new Vector3(-0.015618f, 0.032070f, 0.999364f);
    const float Radius = 0.0142f, Lift = 0.0012f, MatchTolerance = 0.014f;
    const int Size = 128;
    const string ScreenName = "Eli_WatchScreen";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<ElderboomEliWatch>() != null) return;
        var go = new GameObject("ElderboomEliWatch");
        DontDestroyOnLoad(go);
        go.AddComponent<ElderboomEliWatch>();
    }

    readonly List<Transform> screens = new List<Transform>();
    readonly HashSet<Transform> rejected = new HashSet<Transform>();
    Texture2D texture; Color32[] pixels; Material material; Mesh disc;
    float nextScan, effectStart = -1f, effectEnd = -1f, effectTotal = 1f, cooldownTotal = 1f, lastCooldown;
    string shown = "";

    void OnEnable() { ElderboomEvents.AbilityUsed -= OnAbility; ElderboomEvents.AbilityUsed += OnAbility; }
    void OnDisable() { ElderboomEvents.AbilityUsed -= OnAbility; }

    void OnAbility(string id)
    {
        var game = ElderboomGame.Active;
        if (game == null || game.Operator == null || game.Operator.abilities == null) return;
        foreach (var ability in game.Operator.abilities)
        {
            if (ability == null || ability.id != id) continue;
            float windup = Field(ability, "windup", 0f), duration = Field(ability, "duration", 0f);
            effectStart = Time.time + windup; effectEnd = duration > 0f ? effectStart + duration : -1f; effectTotal = Mathf.Max(.01f, duration);
            cooldownTotal = Mathf.Max(.01f, ability.cooldown);
        }
    }

    static float Field(object o, string name, float fallback)
    {
        var f = o.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
        return f != null && f.FieldType == typeof(float) ? (float)f.GetValue(o) : fallback;
    }

    void Update()
    {
        if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + .5f; ElderboomEvents.AbilityUsed -= OnAbility; ElderboomEvents.AbilityUsed += OnAbility; Scan(); }
        screens.RemoveAll(s => s == null);
        if (screens.Count == 0) return;
        Redraw();
    }

    float nextDiag;
    void Diag()
    {
        var cam = Camera.main;
        foreach (var t in screens)
        {
            var r = t.GetComponent<MeshRenderer>();
            string camInfo = cam == null ? "nocam" : ("dist " + Vector3.Distance(cam.transform.position, t.position).ToString("0.000") + " vp " + cam.WorldToViewportPoint(t.position).ToString("F2") + " mask " + ((cam.cullingMask >> t.gameObject.layer) & 1) + " facing " + Vector3.Dot(t.forward, (cam.transform.position - t.position).normalized).ToString("0.00") + " near " + cam.nearClipPlane);
            Debug.Log("[EliWatch] diag " + t.parent.name + " active " + t.gameObject.activeInHierarchy + " visible " + (r != null && r.isVisible) + " size " + (r != null ? r.bounds.size.ToString("F4") : "-") + " layer " + t.gameObject.layer + " shader " + (material != null ? material.shader.name + " ok " + material.shader.isSupported : "-") + " " + camInfo);
        }
    }

    // ---- Placement ------------------------------------------------------------------------
    void Scan()
    {
        var player = FindAnyObjectByType<HDRPSlicePlayer>();
        if (player == null) return;
        foreach (var smr in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.bones == null) continue;
            foreach (var bone in smr.bones)
            {
                if (bone == null || bone.name != ForearmBone || rejected.Contains(bone) || bone.Find(ScreenName) != null) continue;
                if (!IsEli(bone)) { rejected.Add(bone); continue; }
                // The Basic-rig arms carry their own digital watch with its own display.
                if (bone.GetComponentInParent<ElderboomBasicArms>(true) != null) { rejected.Add(bone); continue; }
                if (!smr.gameObject.activeInHierarchy) continue; // measure when visible (posed)
                if (!Place(smr, bone)) rejected.Add(bone);
            }
        }
    }

    static bool IsEli(Transform t)
    {
        for (var p = t; p != null; p = p.parent)
            if (p.name.IndexOf("Eli", StringComparison.Ordinal) >= 0) return true;
        return false;
    }

    bool Place(SkinnedMeshRenderer smr, Transform bone)
    {
        // Find the dial on the posed mesh: near the measured spot, the largest flat patch of triangles.
        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        var verts = baked.vertices; var tris = baked.triangles;
        var m = smr.transform.localToWorldMatrix;
        Vector3[] flips = { new Vector3(1, 1, 1), new Vector3(-1, 1, 1), new Vector3(1, 1, -1), new Vector3(-1, 1, -1) };
        var guesses = new Vector3[flips.Length];
        for (int i = 0; i < flips.Length; i++) guesses[i] = bone.TransformPoint(Vector3.Scale(DialCenter, flips[i]));
        var cn = new List<Vector3>(); var cc = new List<Vector3>(); var ca = new List<float>();
        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector3 a = m.MultiplyPoint3x4(verts[tris[i]]), b = m.MultiplyPoint3x4(verts[tris[i + 1]]), c = m.MultiplyPoint3x4(verts[tris[i + 2]]);
            Vector3 ctr = (a + b + c) / 3f;
            float near = float.MaxValue; foreach (var g in guesses) near = Mathf.Min(near, (ctr - g).sqrMagnitude);
            if (near > 0.04f * 0.04f) continue;
            Vector3 cr = Vector3.Cross(b - a, c - a); float area = cr.magnitude * .5f;
            if (area < 1e-9f) continue;
            cn.Add(cr.normalized); cc.Add(ctr); ca.Add(area);
        }
        Destroy(baked);
        // forearm axis: from this bone toward its child (the hand)
        Vector3 axis = bone.up;
        for (int ci = 0; ci < bone.childCount; ci++) { var ch = bone.GetChild(ci); if (ch.name.StartsWith("DEF-hand") || ch.name.StartsWith("DEF_hand") || ch.name.StartsWith("DEF")) { axis = (ch.position - bone.position).normalized; break; } }
        // greedy plane clustering
        float bestArea = 0f; Vector3 bestC = Vector3.zero, bestN = Vector3.zero; float bestR = 0f;
        var report = new System.Text.StringBuilder();
        var used = new bool[cn.Count];
        for (int i = 0; i < cn.Count; i++)
        {
            if (used[i]) continue;
            Vector3 n = cn[i]; float area = 0f; Vector3 sum = Vector3.zero; var members = new List<int>();
            for (int j = 0; j < cn.Count; j++)
            {
                if (used[j]) continue;
                if (Vector3.Dot(cn[j], n) < .985f) continue;
                if (Mathf.Abs(Vector3.Dot(cc[j] - cc[i], n)) > .0012f) continue;
                if ((cc[j] - cc[i]).sqrMagnitude > .022f * .022f) continue;
                members.Add(j); area += ca[j]; sum += cc[j] * ca[j];
            }
            if (area <= bestArea) continue;
            Vector3 ctr = sum / area; float r = 0f;
            foreach (int j in members) r = Mathf.Max(r, Vector3.ProjectOnPlane(cc[j] - ctr, n).magnitude);
            Vector3 axisPt = bone.position + Vector3.Project(ctr - bone.position, axis);
            float outward = Vector3.Dot(n, (ctr - axisPt).normalized);
            if (area > 1.5e-4f) report.Append(" [r" + (r * 1000f).ToString("0.0") + " a" + (area * 1e4f).ToString("0.00") + " out" + outward.ToString("0.00") + "]");
            if (r > .021f || r < .012f) continue; // the dial is a round face ~16 mm in radius
            if (outward < .5f) continue;           // must face away from the arm (not the case back)
            foreach (int j in members) used[j] = true;
            bestArea = area; bestC = ctr; bestN = n; bestR = r;
        }
        if (bestArea < 2e-4f) { Debug.Log("[EliWatch] no flat dial found on " + smr.name + " area " + bestArea); return false; }
        // triangle winding gives the outward normal (Unity front faces)
        Vector3 center = bone.InverseTransformPoint(bestC), normal = bone.InverseTransformDirection(bestN).normalized;
        float radius = Mathf.Clamp(bestR * 1.05f, .009f, .0165f);
        var go = new GameObject(ScreenName);
        go.layer = bone.gameObject.layer;
        var t = go.transform;
        t.SetParent(bone, false);
        t.localPosition = center + normal * (Lift / Mathf.Max(1e-4f, bone.lossyScale.x));
        // reading orientation like a real watch: 3 o'clock points toward the hand (bone +Y)
        Vector3 hand = (Vector3.up - Vector3.Dot(Vector3.up, normal) * normal).normalized;
        Vector3 screenUp = Vector3.Cross(normal, -hand);
        t.localRotation = Quaternion.LookRotation(normal, screenUp);
        Vector3 ls = bone.lossyScale; float k = radius / Radius;
        t.localScale = new Vector3(k / Mathf.Max(1e-4f, ls.x), k / Mathf.Max(1e-4f, ls.y), k / Mathf.Max(1e-4f, ls.z));
        go.AddComponent<MeshFilter>().sharedMesh = Disc();
        var r2 = go.AddComponent<MeshRenderer>();
        r2.sharedMaterial = Material();
        r2.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r2.receiveShadows = false;
        screens.Add(t);
        Debug.Log("[EliWatch] screen on " + smr.name + " dial area " + (bestArea * 1e4f).ToString("0.00") + " cm2 radius " + (bestR * 1000f).ToString("0.0") + " mm");
        return true;
    }

    Mesh Disc()
    {
        if (disc != null) return disc;
        const int seg = 40;
        var v = new Vector3[seg + 1]; var uv = new Vector2[seg + 1]; var tri = new int[seg * 3];
        v[0] = Vector3.zero; uv[0] = new Vector2(.5f, .5f);
        for (int i = 0; i < seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg; float x = Mathf.Cos(a), y = Mathf.Sin(a);
            v[i + 1] = new Vector3(x * Radius, y * Radius, 0f); uv[i + 1] = new Vector2(.5f - x * .5f, .5f + y * .5f);
            tri[i * 3] = 0; tri[i * 3 + 1] = (i + 1) % seg + 1; tri[i * 3 + 2] = i + 1;
        }
        disc = new Mesh { name = "EliWatchDisc", vertices = v, uv = uv, triangles = tri };
        disc.RecalculateNormals(); disc.RecalculateBounds();
        return disc;
    }

    Material Material()
    {
        if (material != null) return material;
        texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "EliWatchScreen", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        pixels = new Color32[Size * Size];
        var shader = Shader.Find("HDRP/Unlit");
        material = new Material(shader != null ? shader : Shader.Find("Unlit/Texture"));
        material.SetColor("_UnlitColor", Color.white);
        material.SetTexture("_UnlitColorMap", texture);
        material.SetTexture("_MainTex", texture);
        material.SetColor("_EmissiveColor", Color.white * 1.6f);
        material.SetTexture("_EmissiveColorMap", texture);
        material.SetFloat("_UseEmissiveIntensity", 0f);
        material.SetFloat("_EmissiveExposureWeight", 0f);
        material.EnableKeyword("_EMISSIVE_COLOR_MAP");
        material.SetFloat("_DoubleSidedEnable", 1f);
        material.SetFloat("_CullMode", 0f);
        material.SetFloat("_CullModeForward", 0f);
        try { UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(material); } catch { }
        shown = "";
        return material;
    }

    // ---- Drawing ----------------------------------------------------------------------------
    static readonly Color32 Background = new Color32(4, 6, 8, 255);
    static readonly Color32 Cyan = new Color32(70, 225, 255, 255), Amber = new Color32(255, 165, 40, 255), Green = new Color32(90, 255, 140, 255), Dim = new Color32(150, 190, 175, 255);

    void Redraw()
    {
        if (texture == null) return;
        var game = ElderboomGame.Active;
        float now = Time.time;
        string mode; int value; float fill; Color32 col;
        if (effectEnd > 0f && now < effectEnd)
        {
            bool winding = now < effectStart;
            mode = winding ? "W" : "E"; col = Cyan;
            value = Mathf.CeilToInt(winding ? effectTotal : effectEnd - now);
            fill = winding ? 1f : Mathf.Clamp01((effectEnd - now) / effectTotal);
            if (winding && Mathf.Repeat(now * 4f, 1f) < .5f) col = new Color32(30, 110, 130, 255);
        }
        else if (game != null && game.AbilityRemaining > .05f)
        {
            float cd = game.AbilityRemaining;
            if (cd > lastCooldown + .5f) cooldownTotal = Mathf.Max(cooldownTotal, cd);
            mode = "C"; col = Amber; value = Mathf.CeilToInt(cd); fill = Mathf.Clamp01(1f - cd / Mathf.Max(.01f, cooldownTotal));
            lastCooldown = cd;
        }
        else
        {
            lastCooldown = 0f;
            var t = DateTime.Now; mode = "T"; col = Dim; value = t.Hour * 100 + t.Minute; fill = 1f;
            if (t.Second % 2 == 0) mode = "t";
        }
        string key = mode + value + ":" + Mathf.RoundToInt(fill * 60f) + col.r;
        if (key == shown) return;
        shown = key;
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Background;
        Ring(fill, mode == "T" || mode == "t" ? Green : col, mode == "T" || mode == "t");
        if (mode == "T" || mode == "t")
        {
            int h = value / 100, m = value % 100;
            Digit(20, 46, 18, 34, 4, h / 10, Dim); Digit(42, 46, 18, 34, 4, h % 10, Dim);
            if (mode == "T") { Box(63, 54, 66, 57, Dim); Box(63, 68, 66, 71, Dim); }
            Digit(70, 46, 18, 34, 4, m / 10, Dim); Digit(92, 46, 18, 34, 4, m % 10, Dim);
            Disk(64, 98, 5, Green);
        }
        else
        {
            value = Mathf.Clamp(value, 0, 99);
            if (value >= 10) { Digit(30, 34, 30, 58, 7, value / 10, col); Digit(68, 34, 30, 58, 7, value % 10, col); }
            else Digit(49, 34, 30, 58, 7, value, col);
            // small caption bar: cyan = active, amber = recharging
            Box(50, 102, 78, 105, col);
        }
        texture.SetPixels32(pixels);
        texture.Apply(false);
    }

    void Ring(float fill, Color32 col, bool thin)
    {
        float r0 = thin ? 58f : 55f, r1 = 62f;
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float dx = x - 63.5f, dy = y - 63.5f; float r = Mathf.Sqrt(dx * dx + dy * dy);
                if (r < r0 || r > r1) continue;
                float a = Mathf.Repeat(Mathf.Atan2(dx, dy) / (Mathf.PI * 2f), 1f); // 0 at top, clockwise
                var c = a <= fill ? col : new Color32((byte)(col.r / 6), (byte)(col.g / 6), (byte)(col.b / 6), 255);
                pixels[(Size - 1 - y) * Size + x] = c;
            }
    }

    // 7-segment digit; (x,y) top-left in screen pixels (y down)
    static readonly int[] Segments = { 0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F };
    void Digit(int x, int y, int w, int h, int t, int d, Color32 c)
    {
        int s = Segments[Mathf.Clamp(d, 0, 9)]; int mid = y + h / 2;
        if ((s & 1) != 0) Box(x + t, y, x + w - t, y + t, c);
        if ((s & 2) != 0) Box(x + w - t, y + t, x + w, mid, c);
        if ((s & 4) != 0) Box(x + w - t, mid, x + w, y + h - t, c);
        if ((s & 8) != 0) Box(x + t, y + h - t, x + w - t, y + h, c);
        if ((s & 16) != 0) Box(x, mid, x + t, y + h - t, c);
        if ((s & 32) != 0) Box(x, y + t, x + t, mid, c);
        if ((s & 64) != 0) Box(x + t, mid - t / 2, x + w - t, mid + (t + 1) / 2, c);
    }

    void Box(int x0, int y0, int x1, int y1, Color32 c)
    {
        for (int y = Mathf.Max(0, y0); y < Mathf.Min(Size, y1); y++)
            for (int x = Mathf.Max(0, x0); x < Mathf.Min(Size, x1); x++)
                pixels[(Size - 1 - y) * Size + x] = c;
    }

    void Disk(int cx, int cy, int r, Color32 c)
    {
        for (int y = cy - r; y <= cy + r; y++)
            for (int x = cx - r; x <= cx + r; x++)
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r && x >= 0 && y >= 0 && x < Size && y < Size)
                    pixels[(Size - 1 - y) * Size + x] = c;
    }

    void OnDestroy()
    {
        if (texture != null) Destroy(texture);
        if (material != null) Destroy(material);
        if (disc != null) Destroy(disc);
    }
}
