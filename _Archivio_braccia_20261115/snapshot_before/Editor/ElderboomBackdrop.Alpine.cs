using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Alpine mountains around the valley for the winter golden hour: the karst towers are gone, the ridges are higher and
// broader, snow covers the high ground. Reshapes Elderboom_Mountains in place and regrows its forest.
// Batch: -executeMethod ElderboomBackdrop.AlpineBatch
static partial class ElderboomBackdrop
{
    public static void AlpineBatch()
    {
        if (EditorSceneManager.GetActiveScene().path != Scene) EditorSceneManager.OpenScene(Scene);
        Directory.CreateDirectory(Out);
        string r = Alpine();
        Directory.CreateDirectory("Logs/dusk"); File.WriteAllText("Logs/dusk/alpine.txt", r);
        Debug.Log("DUSK_DONE alpine");
    }

    static Texture2D NoiseTex(string path, bool normal)
    {
        const int N = 512; var t = new Texture2D(N, N, TextureFormat.RGBA32, false);
        float H(int x, int y)
        {
            float u = x / (float)N, v = y / (float)N, s = 0, a = .5f, f = 4;
            for (int o = 0; o < 5; o++)
            {   // tileable: blend four shifted samples
                float n = Mathf.PerlinNoise(u * f, v * f) * (1 - u) * (1 - v) + Mathf.PerlinNoise((u - 1) * f, v * f) * u * (1 - v)
                        + Mathf.PerlinNoise(u * f, (v - 1) * f) * (1 - u) * v + Mathf.PerlinNoise((u - 1) * f, (v - 1) * f) * u * v;
                s += n * a; a *= .5f; f *= 2;
            }
            return s;
        }
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float h = H(x, y);
                if (!normal) { float c = .80f + .16f * h; px[y * N + x] = new Color(c * .965f, c * .985f, c * 1.02f, 1); }
                else
                {
                    float dx = (H((x + 1) % N, y) - H((x + N - 1) % N, y)) * 6f, dy = (H(x, (y + 1) % N) - H(x, (y + N - 1) % N)) * 6f;
                    var n = new Vector3(-dx, -dy, 1).normalized; px[y * N + x] = new Color(n.x * .5f + .5f, n.y * .5f + .5f, n.z * .5f + .5f, 1);
                }
            }
        t.SetPixels(px); t.Apply(); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, t.EncodeToPNG()); Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default; ti.wrapMode = TextureWrapMode.Repeat; ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    public static TerrainLayer SnowLayer(string name, float tile)
    {
        const string D = "Assets/GameAssets/Dusk";
        var alb = AssetDatabase.LoadAssetAtPath<Texture2D>(D + "/snow_albedo.png") ?? NoiseTex(D + "/snow_albedo.png", false);
        var nor = AssetDatabase.LoadAssetAtPath<Texture2D>(D + "/snow_normal.png") ?? NoiseTex(D + "/snow_normal.png", true);
        string path = D + "/" + name + ".terrainlayer";
        var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (l == null) { l = new TerrainLayer(); AssetDatabase.CreateAsset(l, path); }
        l.diffuseTexture = alb; l.normalMapTexture = nor; l.normalScale = .6f; l.tileSize = new Vector2(tile, tile); l.metallic = 0; l.smoothness = .32f;
        EditorUtility.SetDirty(l); return l;
    }

    static string Alpine()
    {
        var sb = new StringBuilder();
        var mount = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include).First(t => t.name == "Elderboom_Mountains");
        var main = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude).Where(t => t.name != "Elderboom_Mountains").OrderByDescending(x => x.terrainData.size.x).First();
        terrainMain = main;
        var data = mount.terrainData; int Res = data.heightmapResolution; const float Size = 3600f, MaxH = 900f;
        data.size = new Vector3(Size, MaxH, Size);
        var origin = mount.transform.position; var mo = main.transform.position; var ms = main.terrainData.size;
        // Massifs: (azimuth from north, distance, height, radius)
        var massifs = new (float az, float d, float h, float r)[]
        {
            (35, 1250, 430, 520), (62, 1420, 330, 430), (5, 1500, 300, 480), (318, 1300, 400, 500), (285, 1480, 300, 460),
            (200, 1350, 360, 520), (150, 1450, 300, 450), (105, 1380, 280, 430), (240, 1500, 330, 480),
        };
        var heights = new float[Res, Res]; float step = Size / (Res - 1);
        for (int z = 0; z < Res; z++)
            for (int x = 0; x < Res; x++)
            {
                var p = new Vector2(origin.x + x * step, origin.z + z * step);
                float cx = Mathf.Clamp(p.x, mo.x, mo.x + ms.x), cz = Mathf.Clamp(p.y, mo.z, mo.z + ms.z);
                float outside = Vector2.Distance(p, new Vector2(cx, cz));
                float edgeH = main.SampleHeight(new Vector3(cx, 0, cz)) + mo.y;
                float dist = Vector2.Distance(p, new Vector2(C.x, C.z));
                float hills = (outside > 0 ? 1 : 0) * (Mathf.SmoothStep(0, 1, outside / 260f) * (30 + 45 * Mathf.PerlinNoise(p.x * .004f, p.y * .004f)) + 8 * Mathf.PerlinNoise(p.x * .02f, p.y * .02f));
                float ground = edgeH - 1.5f + hills;
                float sum = 0, w = 1, f = 1f / 700f, ampl = 1, norm = 0;
                for (int o = 0; o < 7; o++)
                {
                    float nn = 1 - Mathf.Abs(Mathf.PerlinNoise(p.x * f + 31.7f * o, p.y * f + 17.3f * o) * 2 - 1);
                    nn *= nn; nn *= w; w = Mathf.Clamp01(nn * 1.7f);
                    sum += nn * ampl; norm += ampl; ampl *= .5f; f *= 2.05f;
                }
                float m = sum / norm;
                float amp = Mathf.Lerp(0, 210, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(430, 950, dist))) + Mathf.Lerp(0, 260, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1000, 1750, dist)));
                float h = ground + m * amp;
                foreach (var ms2 in massifs)
                {
                    float a = ms2.az * Mathf.Deg2Rad; var c = new Vector2(C.x + Mathf.Sin(a) * ms2.d, C.z + Mathf.Cos(a) * ms2.d);
                    float dd = Vector2.Distance(p, c) / ms2.r; if (dd > 2.2f) continue;
                    h += ms2.h * Mathf.Exp(-dd * dd * 1.5f) * (.45f + 1.0f * m);
                }
                float top = MaxH - 70f + origin.y;
                if (h > top - 90f) h = top - 90f + 90f * (1 - Mathf.Exp(-(h - (top - 90f)) / 90f));
                heights[z, x] = Mathf.Clamp01((h - origin.y) / MaxH);
            }
        data.SetHeights(0, 0, heights);

        var layers = data.terrainLayers.ToList();
        void Remap(string key, Vector4 max) { var l = layers.FirstOrDefault(t => t && t.name.Contains(key)); if (l) { l.diffuseRemapMax = max; l.smoothness = .08f; EditorUtility.SetDirty(l); } }
        Remap("Moss", new Vector4(.52f, .54f, .40f, 1)); Remap("Heather", new Vector4(.56f, .50f, .44f, 1)); Remap("Rock", new Vector4(.66f, .66f, .70f, 1));
        var snow = SnowLayer("Mountain_Snow", 60f);
        layers = layers.Where(l => l && l != snow).Take(3).ToList(); layers.Add(snow);
        data.terrainLayers = layers.ToArray();
        int A = data.alphamapResolution; var alpha = new float[A, A, 4];
        for (int z = 0; z < A; z++)
            for (int x = 0; x < A; x++)
            {
                float u = x / (A - 1f), v = z / (A - 1f); float steep = data.GetSteepness(u, v), hh = data.GetInterpolatedHeight(u, v) + origin.y;
                float wx = origin.x + u * Size, wz = origin.z + v * Size;
                float line = 255f + 90f * (Mathf.PerlinNoise(wx * .0021f + 7, wz * .0021f + 3) - .5f) + 40f * (Mathf.PerlinNoise(wx * .011f, wz * .011f) - .5f);
                float s = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(line - 45, line + 45, hh)) * (1 - .8f * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(52, 66, steep)));
                // light dusting lower down on gentle ground
                s = Mathf.Max(s, .55f * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(line - 130, line - 30, hh)) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(18, 34, steep))));
                float r = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(30, 46, steep)) * (1 - s);
                float he = Mathf.InverseLerp(60, 260, hh) * (1 - r - s) * .6f;
                alpha[z, x, 3] = s; alpha[z, x, 2] = r; alpha[z, x, 1] = he; alpha[z, x, 0] = Mathf.Max(0, 1 - s - r - he);
            }
        data.SetAlphamaps(0, 0, alpha);
        EditorUtility.SetDirty(data); AssetDatabase.SaveAssets();
        sb.AppendLine("alpine terrain: max height " + MaxH + ", " + massifs.Length + " massifs, layers " + string.Join(",", data.terrainLayers.Select(l => l.name)));
        EditorSceneManager.MarkSceneDirty(mount.gameObject.scene); EditorSceneManager.SaveScene(mount.gameObject.scene);
        sb.AppendLine(MountainVegetation());
        return sb.ToString();
    }
}
