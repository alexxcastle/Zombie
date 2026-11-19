using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

// Logs/claude_weaponicons.txt -> white side silhouettes of every weapon in the catalog, for the HUD
// (Assets/Resources/Elderboom/UI/WeaponIcons/<id>.png). The meshes are projected and rasterised on the CPU,
// so the result does not depend on lighting, exposure or the render pipeline. Arms and optional mods are left out.
// Result in Logs/claude_weaponicons_result.txt.
public static class ElderboomClaudeWeaponIcons
{
    const string Flag = "Logs/claude_weaponicons.txt", Result = "Logs/claude_weaponicons_result.txt";
    const string OutDir = "Assets/Resources/Elderboom/UI/WeaponIcons";
    static double next;

    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 2;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (!File.Exists(Flag)) return;
        File.Delete(Flag);
        var log = new System.Text.StringBuilder();
        try { MakeAll(log); log.Insert(0, "OK\n"); }
        catch (System.Exception e) { log.Insert(0, "FAIL " + e + "\n"); }
        File.WriteAllText(Result, log.ToString());
    }

    [MenuItem("Elderboom/Make HUD Weapon Icons")]
    public static void MakeAllMenu() { var log = new System.Text.StringBuilder(); MakeAll(log); Debug.Log(log.ToString()); }

    static void MakeAll(System.Text.StringBuilder log)
    {
        Directory.CreateDirectory(OutDir);
        foreach (var def in ElderboomWeapons.All)
        {
            string path = "Assets/Resources/Elderboom/Weapons/" + def.id + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { log.AppendLine(def.id + " no prefab"); continue; }
            var inst = Object.Instantiate(prefab);
            inst.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var png = Render(inst, out string info);
                if (png == null) { log.AppendLine(def.id + " empty " + info); continue; }
                string file = OutDir + "/" + def.id + ".png";
                File.WriteAllBytes(file, png);
                AssetDatabase.ImportAsset(file, ImportAssetOptions.ForceUpdate);
                var imp = AssetImporter.GetAtPath(file) as TextureImporter;
                if (imp != null)
                {
                    imp.textureType = TextureImporterType.Default; imp.alphaIsTransparency = true; imp.mipmapEnabled = false;
                    imp.wrapMode = TextureWrapMode.Clamp; imp.textureCompression = TextureImporterCompression.Uncompressed; imp.sRGBTexture = true;
                    imp.SaveAndReimport();
                }
                log.AppendLine(def.id + " " + info);
            }
            finally { Object.DestroyImmediate(inst); }
        }
        AssetDatabase.Refresh();
    }

    static bool Skip(Renderer r)
    {
        string n = r.name.ToLowerInvariant();
        if (n.Contains("arm") || n.Contains("hand") || n.Contains("foregrip") || n.Contains("folded") || n.StartsWith("mod_")) return true;
        for (var t = r.transform; t != null; t = t.parent) if (t.name.StartsWith("Mod_") || t.name == "WeaponMods") return true;
        return !r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer;
    }

    static Transform Find(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }

    static byte[] Render(GameObject inst, out string info)
    {
        var tris = new List<Vector3>();
        foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
        {
            if (Skip(r)) continue;
            Mesh mesh = null; bool temp = false; Matrix4x4 m = r.transform.localToWorldMatrix;
            if (r is SkinnedMeshRenderer smr) { mesh = new Mesh(); smr.BakeMesh(mesh, true); temp = true; m = Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one); }
            else if (r is MeshRenderer) { var mf = r.GetComponent<MeshFilter>(); if (mf != null) mesh = mf.sharedMesh; }
            if (mesh == null) continue;
            using (var data = Mesh.AcquireReadOnlyMeshData(mesh))
            {
                var md = data[0];
                var verts = new NativeArray<Vector3>(md.vertexCount, Allocator.Temp);
                md.GetVertices(verts);
                for (int s = 0; s < md.subMeshCount; s++)
                {
                    var sm = md.GetSubMesh(s);
                    if (sm.topology != MeshTopology.Triangles) continue;
                    var idx = new NativeArray<int>(sm.indexCount, Allocator.Temp);
                    md.GetIndices(idx, s);
                    for (int i = 0; i + 2 < idx.Length; i += 3)
                        for (int k = 0; k < 3; k++) tris.Add(m.MultiplyPoint3x4(verts[idx[i + k]]));
                    idx.Dispose();
                }
                verts.Dispose();
            }
            if (temp) Object.DestroyImmediate(mesh);
        }
        if (tris.Count == 0) { info = "no triangles"; return null; }
        // Axis: muzzle direction to the right, root up as up.
        Vector3 up = inst.transform.up;
        var muzzle = Find(inst.transform, "MainWeapon_Muzzle"); var rear = Find(inst.transform, "MainWeapon_ADS_Rear");
        Vector3 f;
        var b = new Bounds(tris[0], Vector3.zero); foreach (var p in tris) b.Encapsulate(p);
        if (muzzle != null && rear != null && (muzzle.position - rear.position).sqrMagnitude > 1e-6f) f = muzzle.position - rear.position;
        else if (muzzle != null) f = muzzle.position - b.center;
        else f = b.size.x > b.size.z ? Vector3.right : Vector3.forward;
        f = Vector3.ProjectOnPlane(f, up).normalized;
        if (f.sqrMagnitude < .5f) f = Vector3.forward;
        // 2D projection
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        var pts = new Vector2[tris.Count];
        for (int i = 0; i < tris.Count; i++)
        {
            var p = new Vector2(Vector3.Dot(tris[i], f), Vector3.Dot(tris[i], up)); pts[i] = p;
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
        }
        float wv = maxX - minX, hv = maxY - minY;
        if (wv <= 0 || hv <= 0) { info = "flat"; return null; }
        const int SS = 4, MaxW = 480, MaxH = 150, Pad = 3;
        float scale = Mathf.Min(MaxW / wv, MaxH / hv);
        int W = Mathf.CeilToInt(wv * scale) + Pad * 2, H = Mathf.CeilToInt(hv * scale) + Pad * 2;
        int BW = W * SS, BH = H * SS;
        var cov = new byte[BW * BH];
        float ss = scale * SS;
        for (int i = 0; i + 2 < pts.Length; i += 3)
        {
            Vector2 a = (pts[i] - new Vector2(minX, minY)) * ss + Vector2.one * Pad * SS;
            Vector2 c = (pts[i + 1] - new Vector2(minX, minY)) * ss + Vector2.one * Pad * SS;
            Vector2 d = (pts[i + 2] - new Vector2(minX, minY)) * ss + Vector2.one * Pad * SS;
            float area = (c.x - a.x) * (d.y - a.y) - (d.x - a.x) * (c.y - a.y);
            if (Mathf.Abs(area) < 1e-6f) continue;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(c.x, d.x)))), x1 = Mathf.Min(BW - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(c.x, d.x))));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(c.y, d.y)))), y1 = Mathf.Min(BH - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(c.y, d.y))));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + .5f, py = y + .5f;
                    float w0 = (c.x - px) * (d.y - py) - (d.x - px) * (c.y - py);
                    float w1 = (d.x - px) * (a.y - py) - (a.x - px) * (d.y - py);
                    float w2 = (a.x - px) * (c.y - py) - (c.x - px) * (a.y - py);
                    if ((w0 >= 0 && w1 >= 0 && w2 >= 0) || (w0 <= 0 && w1 <= 0 && w2 <= 0)) cov[y * BW + x] = 1;
                }
        }
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var px32 = new Color32[W * H];
        int filled = 0;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int sum = 0;
                for (int j = 0; j < SS; j++) for (int i = 0; i < SS; i++) sum += cov[(y * SS + j) * BW + x * SS + i];
                byte al = (byte)(sum * 255 / (SS * SS)); if (al > 0) filled++;
                px32[y * W + x] = new Color32(255, 255, 255, al);
            }
        tex.SetPixels32(px32); tex.Apply();
        var png = tex.EncodeToPNG(); Object.DestroyImmediate(tex);
        info = W + "x" + H + " tris " + (tris.Count / 3) + " filled " + filled + " axis " + f.ToString("F2");
        return png;
    }
}
