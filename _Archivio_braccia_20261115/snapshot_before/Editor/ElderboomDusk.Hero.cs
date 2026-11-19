using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

// The start area brought to the reference: forge with a lit hearth by the creek, muddy track with puddles, snow patches,
// flowing water, log piles, lanterns, chimney smoke. Batch: -executeMethod ElderboomDusk.HeroBatch
static partial class ElderboomDusk
{
    const string D = "Assets/GameAssets/Dusk";
    static Transform cur;
    static System.Random rnd = new System.Random(1207);
    static float Rn(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

    public static void HeroBatch() { Open(); Hero(); Save("hero"); }

    // ------------------------------------------------------------------ assets
    static Texture2D Tex(string path, bool normal = false)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter; if (ti == null) return null;
        if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    static Material Mat(string name, string shader)
    {
        Directory.CreateDirectory(D); string path = D + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
        return m;
    }
    static Material Lit(string name, Color col, float smooth, string diff = null, string nor = null, float tile = 1, float metal = 0, float normalScale = 1)
    {
        var m = Mat(name, "HDRP/Lit");
        m.SetColor("_BaseColor", col); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
        if (diff != null) m.SetTexture("_BaseColorMap", Tex(diff));
        m.SetTextureScale("_BaseColorMap", new Vector2(tile, tile));
        if (nor != null) { m.SetTexture("_NormalMap", Tex(nor, true)); m.SetFloat("_NormalScale", normalScale); }
        HDMaterial.ValidateMaterial(m); EditorUtility.SetDirty(m); return m;
    }
    static Material Glow(string name, Color col, float intensity)
    {
        var m = Mat(name, "HDRP/Lit");
        m.SetColor("_BaseColor", Color.black); m.SetColor("_EmissiveColor", col * intensity); m.SetColor("_EmissiveColorLDR", col); m.SetFloat("_EmissiveIntensity", intensity);
        m.SetFloat("_UseEmissiveIntensity", 1); m.SetFloat("_EmissiveExposureWeight", 0f); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        HDMaterial.ValidateMaterial(m); EditorUtility.SetDirty(m); return m;
    }
    static Material Particle(string name, Texture2D tex, Color col, bool additive)
    {
        var m = Mat(name, "HDRP/Unlit");
        m.SetFloat("_SurfaceType", 1); m.SetFloat("_BlendMode", additive ? 1 : 0); m.SetFloat("_ZWrite", 0); m.SetFloat("_TransparentZWrite", 0);
        m.SetTexture("_UnlitColorMap", tex); m.SetColor("_UnlitColor", col); m.SetFloat("_EnableFogOnTransparent", 1);
        HDMaterial.ValidateMaterial(m); EditorUtility.SetDirty(m); return m;
    }
    static Material Decal(string name, Texture2D albedo, Texture2D normal, float smooth, float opacity)
    {
        var m = Mat(name, "HDRP/Decal");
        m.SetTexture("_BaseColorMap", albedo); m.SetColor("_BaseColor", Color.white);
        if (normal) m.SetTexture("_NormalMap", normal);
        m.SetFloat("_AffectAlbedo", 1); m.SetFloat("_AffectNormal", 1); m.SetFloat("_AffectSmoothness", 1); m.SetFloat("_AffectMetal", 0); m.SetFloat("_AffectAO", 0);
        m.SetFloat("_Smoothness", smooth); m.SetFloat("_DecalBlend", opacity); m.SetFloat("_NormalBlendSrc", 0); m.SetFloat("_MaskBlendSrc", 0);
        m.SetFloat("_DecalMaskMapBlueScale", 1); m.enableInstancing = true;
        HDMaterial.ValidateMaterial(m); EditorUtility.SetDirty(m); return m;
    }

    // Soft irregular blob (alpha) over a colour or a photo; "holes" breaks the inside up (patchy snow, drying mud).
    static Texture2D Blob(string name, int seed, Color col, float holes, Texture2D photo = null, float edge = .3f)
    {
        string path = D + "/" + name + ".png";
        const int N = 256; var px = new Color[N * N]; float sx = seed * 13.37f, sy = seed * 7.91f;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = x / (N - 1f) * 2 - 1, v = y / (N - 1f) * 2 - 1, d = Mathf.Sqrt(u * u + v * v), a = Mathf.Atan2(v, u);
                float rad = .62f + .2f * Mathf.PerlinNoise(sx + Mathf.Cos(a) * 1.3f + 2, sy + Mathf.Sin(a) * 1.3f + 2) + .12f * Mathf.PerlinNoise(sx + u * 4 + 9, sy + v * 4 + 9);
                float al = Mathf.SmoothStep(0, 1, Mathf.Clamp01((rad - d) / edge));
                if (holes > 0) al *= Mathf.SmoothStep(0, 1, Mathf.InverseLerp(holes - .18f, holes + .12f, Mathf.PerlinNoise(sx + u * 3.1f + 40, sy + v * 3.1f + 40) * .65f + Mathf.PerlinNoise(sx + u * 9 + 5, sy + v * 9 + 5) * .35f));
                Color c = col;
                if (photo) { var p = photo.GetPixelBilinear(x / (float)N, y / (float)N); c = new Color(p.r * col.r, p.g * col.g, p.b * col.b); }
                else { float n = .92f + .16f * Mathf.PerlinNoise(sx + u * 11, sy + v * 11); c = new Color(col.r * n, col.g * n, col.b * n); }
                c.a = al; px[y * N + x] = c;
            }
        var t = new Texture2D(N, N, TextureFormat.RGBA32, false); t.SetPixels(px); t.Apply(); File.WriteAllBytes(path, t.EncodeToPNG()); Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path); ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    static Texture2D Photo(string file)
    {
        var t = new Texture2D(2, 2); t.LoadImage(File.ReadAllBytes(file)); return t;
    }

    // ------------------------------------------------------------------ geometry
    static readonly Dictionary<string, Mesh> boxCache = new Dictionary<string, Mesh>();
    static Mesh BoxMesh(Vector3 s)
    {
        string key = s.ToString("F3"); if (boxCache.TryGetValue(key, out var hit) && hit) return hit;
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>(); var e = s * .5f;
        void Face(Vector3 nrm, Vector3 right, Vector3 up, float w, float h, float dist)
        {
            int b = v.Count; var c = nrm * dist;
            v.Add(c - right * w - up * h); v.Add(c + right * w - up * h); v.Add(c + right * w + up * h); v.Add(c - right * w + up * h);
            for (int i = 0; i < 4; i++) n.Add(nrm);
            float o = dist * .37f;
            uv.Add(new Vector2(-w + o, -h + o)); uv.Add(new Vector2(w + o, -h + o)); uv.Add(new Vector2(w + o, h + o)); uv.Add(new Vector2(-w + o, h + o));
            tri.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
        }
        Face(Vector3.forward, Vector3.left, Vector3.up, e.x, e.y, e.z); Face(Vector3.back, Vector3.right, Vector3.up, e.x, e.y, e.z);
        Face(Vector3.right, Vector3.forward, Vector3.up, e.z, e.y, e.x); Face(Vector3.left, Vector3.back, Vector3.up, e.z, e.y, e.x);
        Face(Vector3.up, Vector3.right, Vector3.forward, e.x, e.z, e.y); Face(Vector3.down, Vector3.right, Vector3.back, e.x, e.z, e.y);
        var m = new Mesh { name = "DuskBox" + key }; m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateTangents(); m.RecalculateBounds();
        boxCache[key] = m; return m;
    }
    // Local placement inside "cur" (cur carries the building's position and yaw).
    static GameObject Box(string name, Vector3 c, Vector3 size, Material m, bool collide = true, Vector3 euler = default)
    {
        var go = new GameObject(name); go.transform.SetParent(cur, false); go.transform.localPosition = c; go.transform.localRotation = Quaternion.Euler(euler);
        go.AddComponent<MeshFilter>().sharedMesh = BoxMesh(size); go.AddComponent<MeshRenderer>().sharedMaterial = m;
        if (collide) go.AddComponent<BoxCollider>().size = size;
        go.isStatic = true; return go;
    }
    // Triangular gable: base "w" along local X, height h, thickness t along Z.
    static GameObject Gable(string name, Vector3 c, float w, float h, float t, Material m)
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        foreach (float z in new[] { t * .5f, -t * .5f })
        {
            int b = v.Count; v.Add(new Vector3(-w / 2, 0, z)); v.Add(new Vector3(w / 2, 0, z)); v.Add(new Vector3(0, h, z));
            uv.Add(new Vector2(-w / 2, 0)); uv.Add(new Vector2(w / 2, 0)); uv.Add(new Vector2(0, h));
            if (z > 0) tri.AddRange(new[] { b, b + 1, b + 2 }); else tri.AddRange(new[] { b, b + 2, b + 1 });
        }
        var mesh = new Mesh { name = "DuskGable" }; mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0); mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
        var go = new GameObject(name); go.transform.SetParent(cur, false); go.transform.localPosition = c;
        go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = m; go.isStatic = true; return go;
    }
    static GameObject Log(Vector3 c, float radius, float length, Material m, Vector3 euler)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Object.DestroyImmediate(go.GetComponent<Collider>());
        go.name = "Log"; go.transform.SetParent(cur, false); go.transform.localPosition = c; go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = new Vector3(radius * 2, length * .5f, radius * 2); go.GetComponent<MeshRenderer>().sharedMaterial = m; go.isStatic = true; return go;
    }
    static Transform Group(string name, Vector3 world, float yaw, Transform parent)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.position = world; go.transform.rotation = Quaternion.Euler(0, yaw, 0); return go.transform;
    }
    static Bounds BoundsOf(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer)).ToArray();
        var b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.zero); foreach (var r in rs) b.Encapsulate(r.bounds); return b;
    }
    // Copy of something already in the scene (first object whose name contains the key), standing on the ground at x,z.
    static GameObject Clone(string key, float x, float z, float yaw, float scale = 1, float lift = 0, Transform parent = null)
    {
        var src = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude).FirstOrDefault(t => t.name.Contains(key) && !t.IsChildOf(heroRoot) && t.GetComponentInChildren<MeshRenderer>() != null && BoundsOf(t.gameObject).size.magnitude < 9f);
        if (src == null) { log.AppendLine("  clone source missing: " + key); return null; }
        var go = Object.Instantiate(src.gameObject, parent ? parent : cur); go.name = "Dusk_" + key;
        go.transform.rotation = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(src.eulerAngles.x, 0, src.eulerAngles.z);
        go.transform.localScale = src.lossyScale * scale; go.transform.position = new Vector3(x, 0, z);
        var b = BoundsOf(go);
        go.transform.position += new Vector3(x - b.center.x, GroundY(x, z) + lift - b.min.y, z - b.center.z);
        return go;
    }
    static void DecalAt(Material m, float x, float z, float w, float l, float yaw, Transform parent, float y = float.NaN, float depth = .7f)
    {
        var go = new GameObject("Decal_" + m.name); go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(x, (float.IsNaN(y) ? GroundY(x, z) : y) + .12f, z); go.transform.rotation = Quaternion.Euler(90, yaw, 0);
        var d = go.AddComponent<DecalProjector>(); d.material = m; d.size = new Vector3(w, l, depth); d.pivot = new Vector3(0, 0, depth * .5f); d.drawDistance = 90; d.fadeScale = .8f;
    }

    static Transform heroRoot;

    // A Hyper3D model (Assets/GameAssets/Dusk/Rodin/<name>.glb), scaled to a height in metres, bottom on the ground (or on p.y).
    static GameObject Rodin(string name, Vector3 p, float yaw, float height, bool ground = true)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(D + "/Rodin/" + name + ".glb");
        if (!asset) { log.AppendLine("  rodin model not there yet: " + name); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, cur); go.name = "Dusk_" + name;
        go.transform.rotation = Quaternion.Euler(0, yaw, 0); go.transform.position = p;
        var b = BoundsOf(go); go.transform.localScale *= height / Mathf.Max(.01f, b.size.y);
        b = BoundsOf(go); float y = ground ? GroundY(p.x, p.z) - .05f : p.y;
        go.transform.position += new Vector3(p.x - b.center.x, y - b.min.y, p.z - b.center.z);
        foreach (var r in go.GetComponentsInChildren<Renderer>()) r.gameObject.isStatic = true;
        var lo = go.GetComponentsInChildren<Renderer>().Where(r => r.name.EndsWith("_LOD1")).ToArray(); var hi = go.GetComponentsInChildren<Renderer>().Where(r => !r.name.EndsWith("_LOD1")).ToArray();
        if (lo.Length > 0 && hi.Length > 0)
        {
            foreach (var r in lo) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            var lg = go.AddComponent<LODGroup>(); lg.SetLODs(new[] { new LOD(.22f, hi), new LOD(.012f, lo) }); lg.RecalculateBounds();
        }
        log.AppendLine("  rodin " + name + " size " + BoundsOf(go).size.ToString("F2"));
        return go;
    }
    static void Sound(string clip, Vector3 world, float volume, float min, float max, bool spatial = true)
    {
        var c = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Resources/Elderboom/Audio/ambience/" + clip + ".mp3");
        if (!c) { log.AppendLine("  sound missing: " + clip); return; }
        var go = new GameObject("Dusk_Sound_" + clip); go.transform.SetParent(heroRoot, false); go.transform.position = world;
        var a = go.AddComponent<AudioSource>(); a.clip = c; a.loop = true; a.playOnAwake = true; a.volume = volume; a.spatialBlend = spatial ? 1 : 0;
        a.rolloffMode = AudioRolloffMode.Linear; a.minDistance = min; a.maxDistance = max; a.dopplerLevel = 0;
    }

    // A downloaded Poly Haven model (real-world scale), bottom on "y" (or on the ground when y is NaN).
    static GameObject Place(string id, Vector3 p, float yaw, float scale = 1, bool ground = true, Vector3 tilt = default)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(D + "/Downloaded/Models/" + id + "/" + id + "_2k.gltf");
        if (!asset) { log.AppendLine("  model missing: " + id); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, cur); go.name = "Dusk_" + id;
        go.transform.rotation = Quaternion.Euler(tilt.x, yaw, tilt.z); go.transform.localScale = Vector3.one * scale; go.transform.position = p;
        var b = BoundsOf(go); float y = ground ? GroundY(p.x, p.z) : p.y;
        go.transform.position += new Vector3(p.x - b.center.x, y - b.min.y, p.z - b.center.z);
        log.AppendLine("  placed " + id + " size " + b.size.ToString("F2"));
        return go;
    }

    static void Hero()
    {
        var old = GameObject.Find("Dusk_Hero_Forge"); if (old) Object.DestroyImmediate(old);
        heroRoot = new GameObject("Dusk_Hero_Forge").transform; cur = heroRoot;
        const string PH = "Assets/ThirdParty/PolyHaven/Environment/", MS = "Assets/GameAssets/Ported/ElderboomVillage/ElderBoomVillage_Block_MI_";
        var stone = Lit("Dusk_Stone", new Color(.92f, .9f, .88f), .16f, D + "/Downloaded/Textures/old_stone_wall/old_stone_wall_diff_2k.jpg", D + "/Downloaded/Textures/old_stone_wall/old_stone_wall_nor_gl_2k.jpg", .36f, 0, 1.6f);
        var tiles = Lit("Dusk_StoneTiles", new Color(.7f, .68f, .66f), .3f, PH + "stone_tiles_diff_2k.jpg", PH + "stone_tiles_nor_gl_2k.jpg", .5f);
        var wood = Lit("Dusk_Wood", new Color(.62f, .52f, .44f), .22f, PH + "weathered_brown_planks_diff_2k.jpg", PH + "weathered_brown_planks_nor_gl_2k.jpg", .6f);
        var slate = Lit("Dusk_Slate", new Color(.6f, .58f, .56f), .2f, D + "/Downloaded/Textures/roof_slates_02/roof_slates_02_diff_2k.jpg", D + "/Downloaded/Textures/roof_slates_02/roof_slates_02_nor_gl_2k.jpg", .4f, 0, 1.3f);
        var snowM = Lit("Dusk_Snow", new Color(.95f, .96f, 1f), .35f, D + "/Downloaded/Textures/snow_02/snow_02_diff_2k.jpg", D + "/Downloaded/Textures/snow_02/snow_02_nor_gl_2k.jpg", .4f, 0, 1f);
        var canvas = Lit("Dusk_Canvas", new Color(.62f, .56f, .46f), .12f);
        var iron = Lit("Dusk_Iron", new Color(.12f, .12f, .13f), .55f, null, null, 1, .9f);
        var ember = Glow("Dusk_Embers", new Color(1f, .38f, .08f), 26f);
        var soot = Lit("Dusk_Soot", new Color(.05f, .045f, .04f), .1f);

        // ---------------------------------------------------------- forge (front = local +X, towards the start point)
        float fx = 61.5f, fz = -39.5f; float gy = GroundY(fx, fz);
        cur = Group("Forge", new Vector3(fx, gy - .05f, fz), -18f, heroRoot);
        const float L = 5.6f, W = 6.6f, H = 3.0f, T = .55f, P = 40f;
        Box("Floor", new Vector3(0, .06f, 0), new Vector3(L + .4f, .2f, W + .4f), tiles);
        Box("Wall_Back", new Vector3(-L / 2 + T / 2, H / 2, 0), new Vector3(T, H, W), stone);
        Box("Wall_North", new Vector3(0, H / 2, W / 2 - T / 2), new Vector3(L, H, T), stone);
        Box("Wall_South_Low", new Vector3(-.9f, .6f, -W / 2 + T / 2), new Vector3(L - 1.8f, 1.2f, T), stone);
        Box("Wall_South_Pier", new Vector3(-L / 2 + .9f, H / 2, -W / 2 + T / 2), new Vector3(1.8f, H, T), stone);
        foreach (float z in new[] { -W / 2 + .2f, W / 2 - .2f }) Box("Post", new Vector3(L / 2 - .18f, H / 2, z), new Vector3(.3f, H, .3f), wood);
        Box("Beam_Front", new Vector3(L / 2 - .18f, H + .16f, 0), new Vector3(.34f, .34f, W + .5f), wood, false);
        Box("Beam_Back", new Vector3(-L / 2 + .2f, H + .16f, 0), new Vector3(.34f, .34f, W + .5f), wood, false);
        float rise = (L / 2) * Mathf.Tan(P * Mathf.Deg2Rad), slab = (L / 2 + .7f) / Mathf.Cos(P * Mathf.Deg2Rad);
        for (int s = -1; s <= 1; s += 2)
        {
            var mid = new Vector3(s * (L / 4 + .35f * .5f), H + .32f + rise * .5f - .22f * .5f, 0);
            Box("Roof", mid, new Vector3(slab, .16f, W + 1.1f), slate, false, new Vector3(0, 0, -s * P));
            for (int r = 0; r < 5; r++) Box("Rafter", mid + new Vector3(0, -.2f, -W / 2 + .5f + r * (W - 1f) / 4f), new Vector3(slab - .2f, .16f, .14f), wood, false, new Vector3(0, 0, -s * P));
        }
        Box("Ridge", new Vector3(0, H + .32f + rise + .02f, 0), new Vector3(.3f, .2f, W + 1.2f), wood, false);
        var g1 = Gable("Gable_N", new Vector3(0, H, W / 2 - T / 2), L, rise + .3f, T, stone);
        var g2 = Gable("Gable_S", new Vector3(0, H, -W / 2 + T / 2), L, rise + .3f, .12f, wood);
        // chimney and hearth in the back corner
        var ch = new Vector3(-L / 2 + 1.0f, 0, W / 2 - 1.5f);
        Box("Chimney", ch + new Vector3(0, 3.9f, 0), new Vector3(1.5f, 7.8f, 1.5f), stone);
        Box("Chimney_Cap", ch + new Vector3(0, 7.9f, 0), new Vector3(1.8f, .2f, 1.8f), stone, false);
        Box("Chimney_Snow", ch + new Vector3(0, 8.06f, 0), new Vector3(1.7f, .14f, 1.7f), snowM, false);
        Box("Chimney_Top", ch + new Vector3(0, 8.3f, 0), new Vector3(1.0f, .45f, 1.0f), stone, false);
        Box("Hearth", ch + new Vector3(1.35f, .5f, 0), new Vector3(1.3f, 1.0f, 2.0f), stone);
        Box("Hearth_Hood", ch + new Vector3(1.25f, 2.55f, 0), new Vector3(1.2f, 1.1f, 2.0f), stone, false, new Vector3(0, 0, 14));
        Box("Hearth_Back", ch + new Vector3(.76f, 1.5f, 0), new Vector3(.06f, 1.1f, 1.7f), soot, false);
        Box("Embers", ch + new Vector3(1.35f, 1.03f, 0), new Vector3(.9f, .08f, 1.4f), ember, false);
        var fire = new GameObject("Forge_Fire"); fire.transform.SetParent(cur, false); fire.transform.localPosition = ch + new Vector3(1.4f, 1.15f, 0);
        FireFx(fire);
        var fl = new GameObject("Forge_Fire_Light"); fl.transform.SetParent(cur, false); fl.transform.localPosition = ch + new Vector3(1.9f, 1.7f, 0);
        PointLight(fl, new Color(1f, .5f, .18f), 5200f, 12f, true);
        var smoke = new GameObject("Chimney_Smoke"); smoke.transform.SetParent(cur, false); smoke.transform.localPosition = ch + new Vector3(0, 8.6f, 0);
        SmokeFx(smoke, 1f);
        // anvil, bench, tools
        Box("Anvil_Block", new Vector3(.9f, .35f, 1.0f), new Vector3(.55f, .7f, .55f), wood);
        Box("Anvil", new Vector3(.9f, .82f, 1.0f), new Vector3(.24f, .24f, .62f), iron, false);
        Box("Anvil_Horn", new Vector3(.9f, .86f, 1.42f), new Vector3(.12f, .12f, .3f), iron, false);
        Box("Bench_Top", new Vector3(-.2f, .92f, -W / 2 + 1.0f), new Vector3(2.2f, .09f, .75f), wood);
        foreach (var lp in new[] { new Vector2(-1.2f, -.3f), new Vector2(.8f, -.3f), new Vector2(-1.2f, .3f), new Vector2(.8f, .3f) }) Box("Bench_Leg", new Vector3(-.2f + lp.x + .2f, .45f, -W / 2 + 1.0f + lp.y), new Vector3(.1f, .9f, .1f), wood, false);
        Box("Quench_Trough", new Vector3(1.9f, .3f, -1.2f), new Vector3(.6f, .6f, 1.3f), wood);
        // side awning (south), cloth on poles
        Box("Awning", new Vector3(.3f, 2.55f, -W / 2 - 1.5f), new Vector3(L - .6f, .035f, 3.3f), canvas, false, new Vector3(-13, 0, 0));
        foreach (float x in new[] { -L / 2 + .7f, L / 2 - .5f }) Box("Awning_Pole", new Vector3(x, 1.1f, -W / 2 - 3.0f), new Vector3(.12f, 2.25f, .12f), wood);
        var forgeT = cur;
        // The generated forge replaces the block-built one when it is there; fire light and chimney smoke stay.
        bool genForge = Has("blacksmith_forge");
        if (genForge)
        {
            var fronts = ReadTable("Logs/dusk/model_front.txt"); float ff = fronts.TryGetValue("blacksmith_forge", out var fv) ? fv : 0;
            foreach (var t in forgeT.Cast<Transform>().ToArray()) if (t.name != "Forge_Lantern_Light" && t.name != "Chimney_Smoke") Object.DestroyImmediate(t.gameObject);
            var keep = cur; cur = heroRoot; var gf = Rodin("blacksmith_forge", new Vector3(fx, 0, fz), 72 - ff, 8.2f); cur = keep; Solid(gf, .8f);
            var fd = Quaternion.Euler(0, 72, 0) * Vector3.forward; var gb = BoundsOf(gf);
            var li = forgeT.Find("Forge_Lantern_Light"); if (li) li.position = new Vector3(fx, gy + 1.5f, fz) + fd * 1.2f;
            // smoke from the highest point of the model (the chimney)
            Vector3 top = gb.center; float best = -99;
            foreach (var mf in gf.GetComponentsInChildren<MeshFilter>()) { if (!mf.sharedMesh || mf.name.EndsWith("_LOD1")) continue; foreach (var v in mf.sharedMesh.vertices) { var w = mf.transform.TransformPoint(v); if (w.y > best) { best = w.y; top = w; } } }
            var sm = forgeT.Find("Chimney_Smoke"); if (sm) sm.position = top + Vector3.up * .1f;
        }
        // ---------------------------------------------------------- props (clones of what the village already has)
        cur = Group("Props", Vector3.zero, 0, heroRoot);
        var shed = GameObject.Find("Perimeter_Building_9");
        // The small shed by the start gives way to the alpine stone house, when its model is there.
        float houseYaw = File.Exists("Logs/dusk/house_yaw.txt") ? float.Parse(File.ReadAllText("Logs/dusk/house_yaw.txt").Trim(), System.Globalization.CultureInfo.InvariantCulture) : 0f;
        var house = Rodin("alpine_stone_house", new Vector3(64.5f, 0, -24.0f), houseYaw, 10.5f);
        if (house)
        {
            if (shed) { shed.SetActive(false); EditorUtility.SetDirty(shed); }
            var hb = BoundsOf(house); var col = new GameObject("House_Collider"); col.transform.SetParent(house.transform.parent, false); col.transform.position = hb.center;
            col.AddComponent<BoxCollider>().size = new Vector3(hb.size.x * .86f, hb.size.y, hb.size.z * .86f); col.isStatic = true;
            shed = house;
        }
        bool newHouse = house != null;
        var sb = shed ? BoundsOf(shed) : new Bounds(new Vector3(65, 4, -25.4f), new Vector3(6, 7, 14.4f));
        float ex = sb.max.x, sz = sb.min.z;
        Clone("Forged_Lantern", ex + .25f, sb.center.z - 3.2f, 90);
        Clone("Abandoned_Farm_Cart", ex + 2.6f, sb.center.z + 4.5f, 100);
        Clone("CC0_wine_barrel_01", ex + .7f, sz + 4.4f, 20); Clone("CC0_wine_barrel_01", ex + 1.5f, sz + 5.0f, 110);
        Clone("CC0_wooden_crate_01", ex + .8f, sz + 6.3f, 12); Clone("CC0_wooden_crate_01", ex + .9f, sz + 6.2f, 40, 1, .48f); Clone("CC0_wooden_crate_01", fx + 2.2f, fz - 4.6f, 70);
        Clone("wooden_bucket_01", fx + 3.3f, fz + .8f, 0); Clone("wooden_ladder", ex + .5f, sz + 1.0f, 80);
        Clone("tree_stump_02", 72.5f, -36.5f, 40, .8f); Clone("dead_tree_trunk_02", 74.5f, -39.2f, 100);
        Clone("Chopped_Wood_Pile", 70.8f, -37.4f, 15); Clone("Round_Hay_Bale", ex + 1.2f, sb.center.z + 8.5f, 0);
        Clone("Old_Wooden_Trough", ex + .9f, sb.center.z + 3.5f, 90);
        var lamp = Clone("Forged_Lantern", fx + (genForge ? 6.2f : 3.4f), fz + (genForge ? 4.8f : 3.6f), 200);
        var ap = forgeT.TransformPoint(genForge ? new Vector3(5.2f, 0, -1.6f) : new Vector3(.9f, .1f, 1.0f)); var anvil = Rodin("blacksmith_anvil", ap, forgeT.eulerAngles.y + 90, 1.05f, !genForge ? false : true);
        if (anvil) foreach (var n in new[] { "Anvil_Block", "Anvil", "Anvil_Horn" }) { var o = forgeT.Find(n); if (o) Object.DestroyImmediate(o.gameObject); }
        var wpos = forgeT.TransformPoint(genForge ? new Vector3(4.6f, 0, 2.6f) : new Vector3(2.3f, 0, W / 2 + .3f)); var wheel = Rodin("wagon_wheel", wpos, 0, 1.35f);
        if (wheel) { var wb = BoundsOf(wheel); wheel.transform.rotation = Quaternion.Euler(0, forgeT.eulerAngles.y + (wb.size.x < wb.size.z ? 90 : 0), 0); wb = BoundsOf(wheel); wheel.transform.position += new Vector3(wpos.x - wb.center.x, 0, wpos.z - wb.center.z); }
        Sound("forge_fire", forgeT.TransformPoint(new Vector3(-L / 2 + 2.3f, 1.2f, W / 2 - 1.5f)), .8f, 2.5f, 24f);
        Sound("creek_flow", new Vector3(64, -1, -49), .7f, 5f, 32f); Sound("creek_flow", new Vector3(30, -1, -47), .7f, 5f, 32f); Sound("creek_flow", new Vector3(0, -1, -48), .7f, 5f, 32f);
        Sound("winter_wind", Vector3.zero, .22f, 1, 500, false);
        var hp = forgeT.TransformPoint(new Vector3(.9f, .94f, .95f)); if (!genForge) Place("cross_pein_hammer", hp, 40, 1, false);
                hp = forgeT.TransformPoint(new Vector3(.4f, .965f, -W / 2 + 1.05f)); if (!genForge) Place("vintage_oil_lamp", hp, 0, 1, false);
        hp = forgeT.TransformPoint(new Vector3(L / 2 - .18f, 2.2f, W / 2 - .65f)); if (!genForge) Place("Lantern_01", hp, 0, 1, false);
        Place("tree_stump_01", new Vector3(70.0f, 0, -39.0f), 30); Place("hatchet", new Vector3(70.5f, 0, -38.2f), 10);
        Place("boulder_01", new Vector3(66.5f, 0, -45.2f), 60); Place("boulder_01", new Vector3(59.8f, 0, -45.4f), 200, .8f);
        Place("pine_roots", new Vector3(84.5f, 0, -36.5f), 200);
        // a small fire by the track, close to where the match starts
        var pit = Place("stone_fire_pit", new Vector3(74.4f, 0, -36.2f), 0);
        if (pit) { var pb = BoundsOf(pit); var pf = new GameObject("Pit_Fire"); pf.transform.SetParent(cur, false); pf.transform.position = new Vector3(pb.center.x, pb.min.y + .25f, pb.center.z); FireFx(pf); pf.transform.localScale = Vector3.one * .6f;
            var pl2 = new GameObject("Pit_Light"); pl2.transform.SetParent(cur, false); pl2.transform.position = pb.center + Vector3.up * .7f; PointLight(pl2, new Color(1f, .52f, .2f), 3000f, 9f, true); }
        Clone("Chopped_Wood_Pile", sb.center.x - .6f, sz - 1.1f, 80, .85f); Clone("Chopped_Wood_Pile", sb.center.x + 1.6f, sz - .9f, 200, .6f);
        var wp = forgeT.TransformPoint(new Vector3(-.6f, 0, W / 2 + 1.0f)); if (!genForge) Clone("Chopped_Wood_Pile", wp.x, wp.z, 140, .8f);
        wp = forgeT.TransformPoint(new Vector3(1.6f, 0, W / 2 + .9f)); if (!genForge) Clone("Chopped_Wood_Pile", wp.x, wp.z, 20, .55f);

        // pines framing the view (the sun comes through them)
        cur = Group("Pines", Vector3.zero, 0, heroRoot);
        string[] pines = { "Pine_A", "Pine_B", "Pine_C", "Pine_D" }; int pi = 0;
        foreach (var p in new[] { new Vector3(55.4f, 1.15f, -33.2f), new Vector3(58.5f, 1.3f, -45.0f), new Vector3(69.5f, 1.05f, -44.2f), new Vector3(83.5f, .95f, -40.5f), new Vector3(86f, 1.2f, -25f), new Vector3(72.5f, 1.0f, -22.5f), new Vector3(55.8f, 1.25f, -27.5f) })
        {
            string n = pines[pi++ % 4]; var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TerrainDemoScene_HDRP/Prefabs/Trees/Pines/" + n + "/" + n + ".prefab"); if (!prefab) continue;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, cur); go.transform.position = new Vector3(p.x, GroundY(p.x, p.z) - .15f, p.z);
            go.transform.rotation = Quaternion.Euler(0, Rn(0, 360), 0); go.transform.localScale = Vector3.one * p.y;
        }

        // ---------------------------------------------------------- ground: mud track, puddles, snow
        var mudPhoto = Photo(D + "/Downloaded/Textures/brown_mud_02/brown_mud_02_diff_2k.jpg"); var snowPhoto = Photo(D + "/Downloaded/Textures/snow_02/snow_02_diff_2k.jpg");
        var mudN = Tex(D + "/Downloaded/Textures/brown_mud_02/brown_mud_02_nor_gl_2k.jpg", true); var snowN = Tex(D + "/Downloaded/Textures/snow_02/snow_02_nor_gl_2k.jpg", true);
        string flatPath = D + "/flat_normal.png";
        { var ft = new Texture2D(4, 4, TextureFormat.RGBA32, false); var fc = new Color[16]; for (int i = 0; i < 16; i++) fc[i] = new Color(.5f, .5f, 1f, 1f); ft.SetPixels(fc); ft.Apply(); File.WriteAllBytes(flatPath, ft.EncodeToPNG()); Object.DestroyImmediate(ft); AssetDatabase.ImportAsset(flatPath, ImportAssetOptions.ForceUpdate); }
        var flatN = Tex(flatPath, true);
        var mud = new[] { Decal("Dusk_Decal_Mud0", Blob("mud0", 3, new Color(.72f, .6f, .5f), 0, mudPhoto, .6f), mudN, .3f, 1f), Decal("Dusk_Decal_Mud1", Blob("mud1", 8, new Color(.58f, .48f, .4f), .25f, mudPhoto, .6f), mudN, .36f, 1f) };
        var pud = new[] { Decal("Dusk_Decal_Puddle0", Blob("puddle0", 21, new Color(.05f, .045f, .04f), 0, null, .3f), flatN, .97f, .9f), Decal("Dusk_Decal_Puddle1", Blob("puddle1", 34, new Color(.06f, .05f, .045f), 0, null, .3f), flatN, .97f, .9f), Decal("Dusk_Decal_Puddle2", Blob("puddle2", 55, new Color(.045f, .04f, .04f), 0, null, .3f), flatN, .97f, .9f) };
        var sno = new[] { Decal("Dusk_Decal_Snow0", Blob("snow0", 71, new Color(.86f, .87f, .9f), .34f, snowPhoto, .55f), snowN, .34f, 1f), Decal("Dusk_Decal_Snow1", Blob("snow1", 77, new Color(.84f, .85f, .89f), .42f, snowPhoto, .6f), snowN, .34f, 1f), Decal("Dusk_Decal_Snow2", Blob("snow2", 90, new Color(.88f, .89f, .92f), .08f, snowPhoto, .6f), snowN, .34f, 1f) };
        Object.DestroyImmediate(mudPhoto); Object.DestroyImmediate(snowPhoto);
        var decals = Group("Ground_Decals", Vector3.zero, 0, heroRoot);
        var track = new List<Vector2>();
        void Seg(Vector2 a, Vector2 b) { float n = Vector2.Distance(a, b) / 1.6f; for (int i = 0; i <= n; i++) track.Add(Vector2.Lerp(a, b, i / n)); }
        Seg(new Vector2(92, -30), new Vector2(80, -32)); Seg(new Vector2(80, -32), new Vector2(71.5f, -34.5f)); Seg(new Vector2(71.5f, -34.5f), new Vector2(65.5f, -38.5f));
        Seg(new Vector2(71.5f, -34.5f), new Vector2(ex + 1.2f, sb.center.z - 3f)); Seg(new Vector2(80, -32), new Vector2(78, -20));
        foreach (var t in track) { var o = new Vector2(Rn(-.8f, .8f), Rn(-.8f, .8f)); DecalAt(mud[rnd.Next(2)], t.x + o.x, t.y + o.y, Rn(4.2f, 6.2f), Rn(4.2f, 6.2f), Rn(0, 360), decals); }
        for (int i = 0; i < track.Count; i += 2) { var t = track[i]; var o = new Vector2(Rn(-1.2f, 1.2f), Rn(-1.2f, 1.2f)); DecalAt(pud[rnd.Next(3)], t.x + o.x, t.y + o.y, Rn(.9f, 1.9f), Rn(1.6f, 3.4f), Rn(0, 360), decals); }
        int snowCount = 0;
        for (int i = 0; i < 400 && snowCount < 70; i++)
        {
            float x = Rn(54, 96), z = Rn(-47, -14); var p = new Vector2(x, z);
            if (track.Any(t => Vector2.Distance(t, p) < 3.4f)) continue;
            if (GroundY(x, z) < .6f) continue;                                   // creek
            if (Vector2.Distance(p, new Vector2(fx, fz)) < 6.5f) continue;        // inside the forge
            float size = Rn(2.2f, 6.5f); DecalAt(sno[rnd.Next(3)], x, z, size, size * Rn(.7f, 1.2f), Rn(0, 360), decals); snowCount++;
        }
        // drifts against the north side of walls and the forge
        foreach (var p in new[] { new Vector2(fx - 1, fz + 4.6f), new Vector2(fx + 1.5f, fz + 4.4f), new Vector2(sb.center.x, sb.max.z + 1.2f), new Vector2(sb.min.x - 1f, sb.center.z), new Vector2(sb.min.x - 1f, sb.center.z + 4), new Vector2(55.5f, -31f), new Vector2(55.8f, -38f) })
            DecalAt(sno[2], p.x, p.y, Rn(3.5f, 5f), Rn(3f, 4.5f), Rn(0, 360), decals);

        // snow lying on the roofs (projected from above, so it follows the slates)
        float topF = gy + H + rise + 1.0f;
        for (int i = 0; i < (genForge ? 0 : 16); i++) { var q = forgeT.TransformPoint(new Vector3(Rn(-L / 2 - .2f, L / 2 + .2f), 0, Rn(-W / 2, W / 2))); DecalAt(sno[2], q.x, q.z, Rn(2.6f, 4.2f), Rn(2.6f, 4.2f), Rn(0, 360), decals, topF, rise + 1.6f); }
        var cq = forgeT.TransformPoint(new Vector3(-L / 2 + 1.0f, 0, W / 2 - 1.5f)); if (!genForge) DecalAt(sno[2], cq.x, cq.z, 2.3f, 2.3f, 0, decals, gy + 8.9f, 1.2f);
        if (shed && !newHouse) for (int i = 0; i < 26; i++) DecalAt(sno[2], Rn(sb.min.x, sb.max.x), Rn(sb.min.z, sb.max.z), Rn(3f, 4.6f), Rn(3f, 4.6f), Rn(0, 360), decals, sb.max.y + .3f, sb.size.y * .5f + .5f);

        Water();

        // open the first view: the cemetery shrubs between the start point and the forge go
        int hidden = 0;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
        {
            if (!(t.name.StartsWith("Cypress") || t.name.StartsWith("Red_Bush")) || t.IsChildOf(heroRoot)) continue;
            var q = t.position; if (q.x > 66 && q.x < 78.5f && q.z > -40 && q.z < -28.5f) { t.gameObject.SetActive(false); hidden++; }
        }
        log.AppendLine("hidden shrubs in the first view: " + hidden);

        // the player looks towards the forge and the low sun when the match starts
        var pl = Object.FindAnyObjectByType<HDRPSlicePlayer>(FindObjectsInactive.Include);
        if (pl) { pl.transform.rotation = Quaternion.Euler(0, 248f, 0); EditorUtility.SetDirty(pl.transform); }
        log.AppendLine("hero: forge at " + fx + "," + fz + ", " + track.Count + " mud, " + snowCount + " snow decals");
        try { log.AppendLine(ElderboomClaudeBlocks.BakeAccessNavigation()); } catch (System.Exception e) { log.AppendLine("nav bake failed " + e.Message); }
    }

    static void PointLight(GameObject go, Color c, float intensity, float range, bool shadows)
    {
        var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.range = range; l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        var hd = go.AddComponent<HDAdditionalLightData>(); l.intensity = intensity; hd.affectsVolumetric = true; hd.volumetricDimmer = 2f;
        go.name = "Forge_Lantern_Light";   // flickers with the lanterns (ElderboomAtmosphere)
    }

    static Texture2D SoftDot()
    {
        string path = D + "/soft_dot.png";
        var t = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) { float d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 31.5f; float a = Mathf.Pow(Mathf.Clamp01(1 - d), 1.8f); t.SetPixel(x, y, new Color(1, 1, 1, a)); }
        t.Apply(); File.WriteAllBytes(path, t.EncodeToPNG()); Object.DestroyImmediate(t); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path); ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void FireFx(GameObject go)
    {
        var ps = go.AddComponent<ParticleSystem>(); var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.45f, .95f); main.startSpeed = new ParticleSystem.MinMaxCurve(.5f, 1.3f); main.startSize = new ParticleSystem.MinMaxCurve(.3f, .7f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0, 6.28f); main.maxParticles = 80; main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = 34;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(.7f, .05f, 1.1f); go.transform.localRotation = Quaternion.Euler(-90, 0, 0);
        var col = ps.colorOverLifetime; col.enabled = true; var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(new Color(1f, .85f, .45f), 0), new GradientColorKey(new Color(1f, .45f, .1f), .5f), new GradientColorKey(new Color(.6f, .12f, .02f), 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .15f), new GradientAlphaKey(0, 1) });
        col.color = g;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, .6f), new Keyframe(.3f, 1f), new Keyframe(1, .25f)));
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = Particle("Dusk_Fire", SoftDot(), new Color(9f, 4.2f, 1.2f, 1f), true); r.shadowCastingMode = ShadowCastingMode.Off;
    }

    static void SmokeFx(GameObject go, float scale)
    {
        var ps = go.AddComponent<ParticleSystem>(); var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 11f); main.startSpeed = new ParticleSystem.MinMaxCurve(.7f, 1.2f); main.startSize = new ParticleSystem.MinMaxCurve(.9f * scale, 1.5f * scale);
        main.startRotation = new ParticleSystem.MinMaxCurve(0, 6.28f); main.maxParticles = 90; main.simulationSpace = ParticleSystemSimulationSpace.World; main.prewarm = true;
        main.startColor = new Color(1, 1, 1, .5f);
        var em = ps.emission; em.rateOverTime = 7;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 6; sh.radius = .25f; go.transform.localRotation = Quaternion.Euler(-90, 0, 0);
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(.25f, .6f); vel.y = new ParticleSystem.MinMaxCurve(.1f, .3f); vel.z = new ParticleSystem.MinMaxCurve(-.1f, .15f);
        var col = ps.colorOverLifetime; col.enabled = true; var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(new Color(.5f, .47f, .44f), 0), new GradientColorKey(new Color(.85f, .8f, .74f), 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.55f, .12f), new GradientAlphaKey(.25f, .6f), new GradientAlphaKey(0, 1) });
        col.color = g;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, .5f), new Keyframe(1, 4.5f)));
        var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-.3f, .3f);
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = Particle("Dusk_Smoke", SoftDot(), new Color(.8f, .76f, .7f, .55f), false); r.shadowCastingMode = ShadowCastingMode.Off;
    }

    // ------------------------------------------------------------------ creek water: a ribbon at water level along the bed
    static void Water()
    {
        string np = D + "/water_normal.png";
        if (!File.Exists(np))
        {
            const int N = 256; var t = new Texture2D(N, N, TextureFormat.RGBA32, false);
            float Hh(int x, int y) { float u = x / (float)N * 6.2832f, v = y / (float)N * 6.2832f; return .5f * Mathf.Sin(u * 3 + Mathf.Sin(v * 2) * 1.7f) + .3f * Mathf.Sin(v * 5 + Mathf.Cos(u * 4) * 1.3f) + .2f * Mathf.Sin((u + v) * 9 + Mathf.Sin(u * 7)); }
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                { float dx = Hh((x + 1) % N, y) - Hh((x + N - 1) % N, y), dy = Hh(x, (y + 1) % N) - Hh(x, (y + N - 1) % N); var n = new Vector3(-dx * 2.2f, -dy * 2.2f, 1).normalized; t.SetPixel(x, y, new Color(n.x * .5f + .5f, n.y * .5f + .5f, n.z * .5f + .5f, 1)); }
            t.Apply(); File.WriteAllBytes(np, t.EncodeToPNG()); Object.DestroyImmediate(t); AssetDatabase.ImportAsset(np, ImportAssetOptions.ForceUpdate);
        }
        var water = Lit("Dusk_Water", new Color(.035f, .05f, .055f), .95f, null, np, .22f, 0, .55f);
        const float level = -1.22f; var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>(); var nr = new List<Vector3>();
        float prevZ = float.NaN; int rows = 0;
        for (float x = -150; x <= 150; x += 1.5f)
        {
            float best = 99, bz = 0, z0 = float.IsNaN(prevZ) ? -90 : prevZ - 4, z1 = float.IsNaN(prevZ) ? -30 : prevZ + 4;
            for (float z = z0; z <= z1; z += .25f) { float h = GroundY(x, z); if (h < best) { best = h; bz = z; } }
            if (best > level - .08f) { if (rows > 1) { } prevZ = float.NaN; rows = 0; continue; }
            float a = bz, b = bz; while (a > bz - 9 && GroundY(x, a) < level + .1f) a -= .2f; while (b < bz + 9 && GroundY(x, b) < level + .1f) b += .2f;
            int i = v.Count; v.Add(new Vector3(x, level, a - .3f)); v.Add(new Vector3(x, level, b + .3f)); uv.Add(new Vector2(x, a)); uv.Add(new Vector2(x, b)); nr.Add(Vector3.up); nr.Add(Vector3.up);
            if (rows > 0) tri.AddRange(new[] { i - 2, i - 1, i, i, i - 1, i + 1 });
            rows++; prevZ = bz;
        }
        var mesh = new Mesh { name = "Dusk_Creek_Water", indexFormat = IndexFormat.UInt32 }; mesh.SetVertices(v); mesh.SetNormals(nr); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0); mesh.RecalculateTangents(); mesh.RecalculateBounds();
        Directory.CreateDirectory(D + "/Meshes"); AssetDatabase.DeleteAsset(D + "/Meshes/Dusk_Creek_Water.asset"); AssetDatabase.CreateAsset(mesh, D + "/Meshes/Dusk_Creek_Water.asset");
        var go = new GameObject("Creek_Water"); go.transform.SetParent(heroRoot, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = water; r.shadowCastingMode = ShadowCastingMode.Off;
        go.AddComponent<ElderboomWaterFlow>();
        // ULTRA: the same ribbon drawn by the HDRP water system (ElderboomUltraWater switches between the two)
        var ug = new GameObject("Creek_Water_Ultra"); ug.transform.SetParent(heroRoot, false); ug.transform.position = new Vector3(0, level, 0);
        var shape = new GameObject("Creek_Water_Ultra_Shape"); shape.transform.SetParent(ug.transform, false); shape.transform.position = Vector3.zero;
        shape.AddComponent<MeshFilter>().sharedMesh = mesh; var sr = shape.AddComponent<MeshRenderer>(); sr.enabled = false;
        var ws = ug.AddComponent<WaterSurface>();
        ws.surfaceType = WaterSurfaceType.River; ws.geometryType = WaterGeometryType.Custom; ws.meshRenderers = new List<MeshRenderer> { sr };
        ws.timeMultiplier = 1f; ws.largeWindSpeed = 4.5f; ws.largeChaos = .85f; ws.largeCurrentSpeedValue = 2.2f;
        ws.ripples = true; ws.ripplesWindSpeed = 7f; ws.ripplesChaos = .9f; ws.ripplesCurrentSpeedValue = 1.6f;
        ws.startSmoothness = .97f; ws.endSmoothness = .9f;
        ws.refractionColor = new Color(.42f, .5f, .44f); ws.maxRefractionDistance = .6f; ws.absorptionDistance = 2.2f; ws.scatteringColor = new Color(.05f, .11f, .09f);
        ws.caustics = true; ws.causticsIntensity = .7f; ws.virtualPlaneDistance = 1f;
        ws.foam = true; ws.simulationFoamAmount = .5f; ws.tessellation = true;
        ug.SetActive(false);
        var sw = heroRoot.gameObject.AddComponent<ElderboomUltraWater>(); sw.simple = go; sw.ultra = ug;
        log.AppendLine("water: " + v.Count / 2 + " rows at y " + level);
    }
}
