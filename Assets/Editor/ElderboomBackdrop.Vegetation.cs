using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Logs/claude_backdrop.txt = "vegetation": very cheap forest on the mountain terrain around the valley.
// Faceted low-poly conifers (10 tris) and broadleaf clumps (12 tris) merged into 300 m chunks, one material with a
// small colour palette (UV picks the colour), no shadows, no probes, no colliders, Backdrop layer (ElderboomFarView).
// Each chunk has a LODGroup: far chunks draw only one tree in three, slightly bigger, so the silhouette stays the same.
static partial class ElderboomBackdrop
{
    const string VegRoot = "Elderboom_Mountain_Vegetation";

    static string MountainVegetation()
    {
        var sb = new StringBuilder(); var rnd = new System.Random(4242);
        float Rv(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
        var mount = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).FirstOrDefault(t => t.name == "Elderboom_Mountains");
        if (!mount) return "FAIL Elderboom_Mountains terrain missing (run 'terrain' first)";
        var main = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Where(t => t.name != "Elderboom_Mountains").OrderByDescending(x => x.terrainData.size.x).First();
        var old = GameObject.Find(VegRoot); if (old) Object.DestroyImmediate(old);
        Directory.CreateDirectory(Dir + "/Meshes"); Directory.CreateDirectory(Dir + "/Vegetation");
        foreach (var f in Directory.GetFiles(Dir + "/Vegetation", "*.asset")) AssetDatabase.DeleteAsset(f.Replace('\\', '/'));

        // Palette: night conifers (deep green, blue-green, olive), dead/dry pines, broadleaf clumps. sRGB.
        var palette = new[]
        {
            new Color32(30, 44, 30, 255), new Color32(25, 38, 31, 255), new Color32(36, 48, 31, 255), new Color32(27, 35, 36, 255),
            new Color32(44, 46, 33, 255), new Color32(40, 36, 29, 255), new Color32(34, 50, 28, 255), new Color32(46, 54, 32, 255),
        };
        string texPath = Dir + "/Vegetation/Mountain_Veg_Palette.png";
        var pt = new Texture2D(palette.Length, 1, TextureFormat.RGBA32, false); pt.SetPixels32(palette); pt.Apply();
        File.WriteAllBytes(texPath, pt.EncodeToPNG()); Object.DestroyImmediate(pt);
        AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(texPath);
        ti.filterMode = FilterMode.Point; ti.mipmapEnabled = false; ti.wrapMode = TextureWrapMode.Clamp; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.sRGBTexture = true; ti.SaveAndReimport();
        var paletteTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        var mat = Mat("Mountain_Vegetation", m =>
        {
            m.SetTexture("_BaseColorMap", paletteTex); m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", .08f); m.SetFloat("_Metallic", 0); m.SetFloat("_DoubleSidedEnable", 0);
            m.enableInstancing = true;
        });

        var data = mount.terrainData; var o = mount.transform.position; var size = data.size;
        var mo = main.transform.position; var ms = main.terrainData.size;
        int holeRes = data.holesResolution;
        const float Chunk = 300f, Grid = 7.5f;
        int chunks = Mathf.CeilToInt(size.x / Chunk);
        var lists = new List<(Vector3 p, float h, float yaw, int kind, int colour)>[chunks, chunks];
        int total = 0;
        float seedA = Rv(0, 100), seedB = Rv(0, 100);
        for (float z = Grid * .5f; z < size.z; z += Grid)
            for (float x = Grid * .5f; x < size.x; x += Grid)
            {
                float px = x + Rv(-.45f, .45f) * Grid, pz = z + Rv(-.45f, .45f) * Grid;
                float u = px / size.x, v = pz / size.z; if (u < 0 || v < 0 || u > 1 || v > 1) continue;
                int hx = Mathf.Clamp((int)(u * holeRes), 0, holeRes - 1), hz = Mathf.Clamp((int)(v * holeRes), 0, holeRes - 1);
                if (data.IsHole(hx, hz)) continue;   // inside the valley: its own terrain and forest are there
                var wp = new Vector3(o.x + px, 0, o.z + pz);
                // Keep the closest band clear: low-poly trees must never be seen from up close.
                float cx = Mathf.Clamp(wp.x, mo.x, mo.x + ms.x), cz = Mathf.Clamp(wp.z, mo.z, mo.z + ms.z);
                float outside = Vector2.Distance(new Vector2(wp.x, wp.z), new Vector2(cx, cz));
                if (outside < 110f) continue;
                float steep = data.GetSteepness(u, v); if (steep > 42f) continue;   // bare rock on cliffs and tower sides
                float y = data.GetInterpolatedHeight(u, v) + o.y;
                // Forest patches with clearings, thinner with altitude (treeline ~300 m), sparse on steeper ground.
                float patch = Mathf.PerlinNoise(wp.x * .0035f + seedA, wp.z * .0035f + seedB) * .7f + Mathf.PerlinNoise(wp.x * .013f + seedB, wp.z * .013f + seedA) * .3f;
                float alt = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(190f, 340f, y));
                float slope = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(30f, 42f, steep));
                float edge = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(110f, 220f, outside));
                float density = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.26f, .50f, patch)) * alt * slope * edge;
                if ((float)rnd.NextDouble() > density * .95f) continue;
                bool broad = y < 110f && rnd.NextDouble() < .28f;
                bool dead = !broad && rnd.NextDouble() < .06f;
                int colour = broad ? (rnd.NextDouble() < .5 ? 6 : 7) : dead ? (rnd.NextDouble() < .5 ? 4 : 5) : rnd.Next(0, 4);
                float h = broad ? Rv(9f, 15f) : Rv(13f, 25f) * Mathf.Lerp(.75f, 1f, alt);
                int ix = Mathf.Clamp((int)(px / Chunk), 0, chunks - 1), iz = Mathf.Clamp((int)(pz / Chunk), 0, chunks - 1);
                (lists[ix, iz] ??= new List<(Vector3, float, float, int, int)>()).Add((new Vector3(wp.x, y - .8f, wp.z), h, Rv(0, 360), broad ? 1 : dead ? 2 : 0, colour));
                total++;
            }

        var root = new GameObject(VegRoot); root.layer = ElderboomFarView.Layer; root.isStatic = true;
        int made = 0; long tris0 = 0;
        for (int iz = 0; iz < chunks; iz++)
            for (int ix = 0; ix < chunks; ix++)
            {
                var items = lists[ix, iz]; if (items == null || items.Count == 0) continue;
                var centre = Vector3.zero; foreach (var it in items) centre += it.p; centre /= items.Count;
                var go = new GameObject("Veg_" + ix + "_" + iz); go.layer = ElderboomFarView.Layer; go.isStatic = true;
                go.transform.SetParent(root.transform, false); go.transform.position = centre;
                var lod0 = VegMesh(items, centre, 1f, 1); var lod1 = VegMesh(items, centre, 1.15f, 3);
                tris0 += lod0.triangles.Length / 3;
                var r0 = VegPart(go.transform, "LOD0", lod0, mat, ix + "_" + iz + "_0");
                var r1 = VegPart(go.transform, "LOD1", lod1, mat, ix + "_" + iz + "_1");
                var group = go.AddComponent<LODGroup>();
                group.SetLODs(new[] { new LOD(.09f, new Renderer[] { r0 }), new LOD(0f, new Renderer[] { r1 }) });
                group.RecalculateBounds();
                made++;
            }
        AssetDatabase.SaveAssets();
        sb.AppendLine("mountain vegetation: " + total + " plants in " + made + " chunks, LOD0 " + tris0 + " triangles in total (frustum + LOD culled per chunk)");
        EditorSceneManager.MarkSceneDirty(root.scene); EditorSceneManager.SaveScene(root.scene);
        Horizon("veg");
        sb.AppendLine("DONE");
        return sb.ToString();
    }

    static MeshRenderer VegPart(Transform parent, string name, Mesh mesh, Material mat, string id)
    {
        string path = Dir + "/Vegetation/Veg_" + id + ".asset"; mesh.name = "Veg_" + id; AssetDatabase.CreateAsset(mesh, path);
        var go = new GameObject(name); go.layer = ElderboomFarView.Layer; go.isStatic = true; go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off; r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        r.allowOcclusionWhenDynamic = true;
        return r;
    }

    // Faceted shapes, flat normals tilted up (soft sky light on the crowns, dark undersides).
    static Mesh VegMesh(List<(Vector3 p, float h, float yaw, int kind, int colour)> items, Vector3 centre, float scale, int every)
    {
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        void Face(Vector3 a, Vector3 b, Vector3 c, Vector2 col)
        {
            var nn = Vector3.Cross(b - a, c - a).normalized; nn = (nn + Vector3.up * .45f).normalized;
            int s = v.Count; v.Add(a); v.Add(b); v.Add(c); n.Add(nn); n.Add(nn); n.Add(nn); uv.Add(col); uv.Add(col); uv.Add(col);
            tri.Add(s); tri.Add(s + 1); tri.Add(s + 2);
        }
        void Cone(Vector3 basePos, float r, float y0, float y1, int sides, float yaw, Vector2 col)
        {
            var tip = basePos + Vector3.up * y1;
            for (int i = 0; i < sides; i++)
            {
                float a0 = yaw + i * Mathf.PI * 2 / sides, a1 = yaw + (i + 1) * Mathf.PI * 2 / sides;
                var p0 = basePos + new Vector3(Mathf.Cos(a0) * r, y0, Mathf.Sin(a0) * r); var p1 = basePos + new Vector3(Mathf.Cos(a1) * r, y0, Mathf.Sin(a1) * r);
                Face(p0, tip, p1, col);
            }
        }
        for (int k = 0; k < items.Count; k += every)
        {
            var it = items[k]; var p = it.p - centre; float h = it.h * scale, yaw = it.yaw * Mathf.Deg2Rad;
            var col = new Vector2((it.colour + .5f) / 8f, .5f);
            if (it.kind == 1)
            {
                // Broadleaf clump: squashed bipyramid, 6 sides (12 tris).
                float r = h * .42f, mid = h * .55f; var top = p + Vector3.up * h; var bot = p + Vector3.up * h * .18f;
                for (int i = 0; i < 6; i++)
                {
                    float a0 = yaw + i * Mathf.PI / 3, a1 = yaw + (i + 1) * Mathf.PI / 3;
                    var q0 = p + new Vector3(Mathf.Cos(a0) * r, mid, Mathf.Sin(a0) * r); var q1 = p + new Vector3(Mathf.Cos(a1) * r, mid, Mathf.Sin(a1) * r);
                    Face(q0, top, q1, col); Face(q1, bot, q0, col);
                }
            }
            else if (it.kind == 2) Cone(p, h * .14f, h * .1f, h, 5, yaw, col);    // dead pine: a thin spike (5 tris)
            else { Cone(p, h * .3f, h * .08f, h * .72f, 5, yaw, col); Cone(p, h * .21f, h * .42f, h, 5, yaw + .6f, col); }   // conifer: two tiers (10 tris)
        }
        var m = new Mesh { indexFormat = IndexFormat.UInt32 }; m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateBounds(); m.UploadMeshData(false);
        return m;
    }
}
