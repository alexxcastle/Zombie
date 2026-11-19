using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Weapon customization audit (Editor automation):
//   Logs/claude_moddump.txt   (weapon ids, one per line; empty = all)  -> Logs/claude_moddump_result.txt
//     every weapon prefab measured in its muzzle marker's space (+Z toward the muzzle, +Y up): parts and a profile.
//   Logs/claude_modshot.txt   (lines "weaponId slot=MOD slot=MOD ...") -> Logs/mod_shots/<weaponId>[_n].png
//     the loadout stage rendered to a picture.
static class ElderboomModsAudit
{
    static double next;
    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 2;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        Run("Logs/claude_moddump.txt", "Logs/claude_moddump_result.txt", Dump);
        Run("Logs/claude_modshot.txt", "Logs/claude_modshot_result.txt", Shots);
    }

    static void Run(string flag, string result, Func<string[], string> job)
    {
        if (!File.Exists(flag)) return;
        var lines = File.ReadAllLines(flag);
        File.Delete(flag);
        try { File.WriteAllText(result, job(lines)); }
        catch (Exception e) { File.WriteAllText(result, "FAIL " + e); }
    }

    static readonly string[] AllIds = { "rhino", "m1911", "glock19", "mp5", "mp7", "hk53", "shotgun", "mp133", "a5", "k40", "ak74", "mosin", "mp5vk", "p45", "m4" };

    static bool IsArm(string n)
    {
        n = n.ToLowerInvariant();
        return n.Contains("arm") || n.Contains("hand") || n.Contains("sleeve") || n.Contains("glove") || n.Contains("finger") || n.Contains("body_") || n.StartsWith("p_");
    }

    static string F(float v) => v.ToString("0.000", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => "(" + F(v.x) + "," + F(v.y) + "," + F(v.z) + ")";

    static string Dump(string[] lines)
    {
        var ids = new List<string>();
        foreach (var l in lines) if (!string.IsNullOrWhiteSpace(l)) ids.Add(l.Trim());
        if (ids.Count == 0) ids.AddRange(AllIds);
        var sb = new StringBuilder();
        foreach (var id in ids)
        {
            var def = ElderboomWeapons.Find(id);
            var prefab = def != null ? Resources.Load<GameObject>(def.resource) : null;
            sb.AppendLine("=== " + id + (prefab == null ? " NO PREFAB" : ""));
            if (prefab == null) continue;
            var go = (GameObject)Object.Instantiate(prefab);
            try
            {
                go.transform.position = new Vector3(0, -3000, 0);
                var muzzle = ElderboomWeaponMods.FindMuzzle(go.transform);
                if (muzzle == null) { sb.AppendLine("no muzzle"); continue; }
                sb.AppendLine("muzzle=" + muzzle.name + " scale=" + F(muzzle.lossyScale.x) + " fwdInRoot=" + V(go.transform.InverseTransformDirection(muzzle.forward)));
                var pts = new List<Vector3>();
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (IsArm(r.name) || r is ParticleSystemRenderer) continue;
                    Mesh mesh = null; bool baked = false;
                    if (r is SkinnedMeshRenderer sk && sk.sharedMesh != null) { mesh = new Mesh(); sk.BakeMesh(mesh, true); baked = true; }
                    else { var mf = r.GetComponent<MeshFilter>(); if (mf != null) mesh = mf.sharedMesh; }
                    if (mesh == null) continue;
                    var lo = new Vector3(1e9f, 1e9f, 1e9f); var hi = -lo; int n = 0;
                    var m = muzzle.worldToLocalMatrix * r.transform.localToWorldMatrix;
                    if (mesh.isReadable)
                    {
                        var verts = mesh.vertices;
                        int step = Mathf.Max(1, verts.Length / 4000);
                        for (int i = 0; i < verts.Length; i += step) { var p = m.MultiplyPoint3x4(verts[i]); lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); pts.Add(p); n++; }
                    }
                    else
                    {
                        var b = mesh.bounds;
                        for (int i = 0; i < 8; i++)
                        {
                            var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                            var p = m.MultiplyPoint3x4(c); lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p);
                        }
                    }
                    sb.AppendLine("  part " + r.name + (baked ? " [skinned]" : "") + (mesh.isReadable ? " pts=" + n : " [bounds]") + " lo=" + V(lo) + " hi=" + V(hi));
                    if (baked) Object.DestroyImmediate(mesh);
                }
                // Profile along the bore: top/bottom on the centre line, sides.
                if (pts.Count > 0)
                {
                    float zmin = 1e9f, zmax = -1e9f;
                    foreach (var p in pts) { zmin = Mathf.Min(zmin, p.z); zmax = Mathf.Max(zmax, p.z); }
                    sb.AppendLine("  profile z " + F(zmin) + ".." + F(zmax) + "  (slice: top@centre bottom@centre left right)");
                    for (float z = Mathf.Floor(zmin * 50f) / 50f; z <= zmax; z += .02f)
                    {
                        float top = -1e9f, bot = 1e9f, left = 1e9f, right = -1e9f; bool any = false;
                        foreach (var p in pts)
                        {
                            if (p.z < z || p.z >= z + .02f) continue;
                            left = Mathf.Min(left, p.x); right = Mathf.Max(right, p.x);
                            if (Mathf.Abs(p.x) < .012f) { top = Mathf.Max(top, p.y); bot = Mathf.Min(bot, p.y); any = true; }
                        }
                        if (left > 1e8f) continue;
                        sb.AppendLine("    z=" + F(z) + (any ? " top=" + F(top) + " bot=" + F(bot) : " (no centre)") + " x=" + F(left) + ".." + F(right));
                    }
                }
            }
            finally { Object.DestroyImmediate(go); }
        }
        return sb.ToString();
    }

    static string Shots(string[] lines)
    {
        Directory.CreateDirectory("Logs/mod_shots");
        var sb = new StringBuilder("OK\n");
        int k = 0;
        foreach (var raw in lines)
        {
            var parts = raw.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            string id = parts[0];
            var loadout = new Dictionary<string, string>();
            float yaw = -14f;
            for (int i = 1; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq <= 0) continue;
                string key = parts[i].Substring(0, eq), val = parts[i].Substring(eq + 1);
                if (key == "yaw") yaw = float.Parse(val, CultureInfo.InvariantCulture);
                else loadout[key] = val;
            }
            var stage = ElderboomLoadoutStage.Create();
            try
            {
                stage.Show(id, loadout);
                stage.SetYaw(yaw);
                var cam = stage.Camera;
                var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.aspect = 16f / 9f;
                stage.Show(id, loadout);
                stage.SetYaw(yaw);
                cam.Render(); cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply();
                RenderTexture.active = prev;
                string file = "Logs/mod_shots/" + (k++).ToString("00") + "_" + id + ".png";
                File.WriteAllBytes(file, tex.EncodeToPNG());
                cam.targetTexture = null;
                Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
                sb.AppendLine(file + " sockets=" + stage.Sockets.Count);
            }
            finally { Object.DestroyImmediate(stage.gameObject); }
        }
        return sb.ToString();
    }
}
