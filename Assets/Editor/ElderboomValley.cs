using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

// [InitializeOnLoad] removed 31 Oct 2026: the saved scene is the source of truth.
// Flag files no longer trigger rebuilds, builds or Editor exits when Unity opens.
public static class ElderboomValley
{
    const string Flag = "Logs/valley_now.txt";
    const string SatelliteFlag = "Logs/satellite_now.txt";
    const string ScenePath = "Assets/Scenes/Elderboom_Unity_Port.unity";
    const string DataPath = "Assets/Terrain/Elderboom_Valley.asset";
    const string NavPath = "Assets/Resources/Elderboom/Navigation.asset";
    const string LayerRoot = "Assets/TerrainDemoScene_HDRP/Terrain/Layers/";
    const string Pack = "Assets/TerrainDemoScene_HDRP/";
    const float Size = 700f;
    const float MaxHeight = 64f;
    const float ShelfY = 1f;
    static readonly Vector2 Village = new Vector2(5.7f, -0.7f);

    static ElderboomValley()
    {
        if (File.Exists(SatelliteFlag)) EditorApplication.update += SatelliteKick;
        else if (File.Exists(Flag)) EditorApplication.update += Kick;
    }

    static void Kick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            return;
        }
        EditorApplication.update -= Kick;
        File.Delete(Flag);
        Ensure();
        BuildPreview();
        EditorSettings.asyncShaderCompilation = false;
        string dir = "Logs/valley_preview";
        Directory.CreateDirectory(dir);
        if (File.Exists(dir + "/play_done.txt")) File.Delete(dir + "/play_done.txt");
        File.WriteAllText(dir + "/phase.txt", "entering playmode");
        double deadline = EditorApplication.timeSinceStartup + 360;
        EditorApplication.update += () =>
        {
            if (File.Exists(dir + "/play_done.txt")) return;
            if (EditorApplication.timeSinceStartup < deadline) return;
            File.AppendAllText(dir + "/report.txt", "\nPLAY TIMEOUT\n");
            EditorApplication.Exit(2);
        };
        EditorApplication.delayCall += () => EditorApplication.EnterPlaymode();
    }

    static void SatelliteKick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            return;
        }
        EditorApplication.update -= SatelliteKick;
        File.Delete(SatelliteFlag);
        Ensure();
        BuildPreview(true);
        EditorSettings.asyncShaderCompilation = false;
        string dir = "Logs/valley_preview";
        Directory.CreateDirectory(dir);
        if (File.Exists(dir + "/play_done.txt")) File.Delete(dir + "/play_done.txt");
        File.WriteAllText(dir + "/phase.txt", "satellite");
        double deadline = EditorApplication.timeSinceStartup + 480;
        EditorApplication.update += () =>
        {
            if (File.Exists(dir + "/play_done.txt")) return;
            if (EditorApplication.timeSinceStartup < deadline) return;
            File.AppendAllText(dir + "/satellite_report.txt", "\nPLAY TIMEOUT\n");
            EditorApplication.Exit(2);
        };
        EditorApplication.delayCall += () => EditorApplication.EnterPlaymode();
    }

    static void BuildPreview(bool satellite = false)
    {
        var game = SceneManager.GetActiveScene();
        var valley = GameObject.Find("Elderboom_Valley");
        var lamps = new List<GameObject>();
        var houses = new List<MeshRenderer>();
        foreach (var transform in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
            if (IsLampRoot(transform.gameObject)) lamps.Add(transform.gameObject);
        foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
        {
            string rootName = renderer.transform.root.name;
            if (rootName.Contains("Player") || rootName.Contains("Weapon") || rootName.Contains("View")) continue;
            if (renderer.transform.root.name == "Elderboom_Valley") continue;
            Vector3 point = renderer.transform.position;
            float dx = point.x - Village.x;
            float dz = point.z - Village.y;
            float reach = satellite ? 130f : 80f;
            if (dx * dx + dz * dz > reach * reach) continue;
            houses.Add(renderer);
        }
        var preview = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var valleyCopy = Object.Instantiate(valley);
        valleyCopy.name = "Elderboom_Valley";
        SceneManager.MoveGameObjectToScene(valleyCopy, preview);
        if (satellite)
        {
            var terrain = valleyCopy.GetComponentInChildren<Terrain>();
            terrain.basemapDistance = 2500f;
            terrain.treeDistance = 8000f;
            terrain.treeBillboardDistance = 90f;
            terrain.treeMaximumFullLODCount = 8000;
            terrain.detailObjectDistance = 0f;
        }
        foreach (var lamp in lamps)
        {
            var copy = Object.Instantiate(lamp);
            copy.name = lamp.name;
            SceneManager.MoveGameObjectToScene(copy, preview);
        }
        int copied = 0;
        foreach (var renderer in houses)
        {
            if (copied >= (satellite ? 1600 : 700)) break;
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            var copy = new GameObject(renderer.name);
            SceneManager.MoveGameObjectToScene(copy, preview);
            copy.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            copy.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
            copy.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
            copy.transform.localScale = renderer.transform.lossyScale;
            copied++;
        }
        var volume = new GameObject("PreviewVolume").AddComponent<Volume>();
        SceneManager.MoveGameObjectToScene(volume.gameObject, preview);
        volume.isGlobal = true;
        volume.priority = 20f;
        if (satellite)
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, "Assets/Settings/Elderboom_Satellite_Profile.asset");
            var exposure = profile.Add<Exposure>();
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(12.2f);
            var fog = profile.Add<Fog>();
            fog.enabled.Override(false);
            EditorUtility.SetDirty(profile);
            volume.sharedProfile = profile;
        }
        else volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/Elderboom_Night_Profile.asset");
        var moonObject = new GameObject("Moonlight");
        SceneManager.MoveGameObjectToScene(moonObject, preview);
        var moon = moonObject.AddComponent<Light>();
        moon.type = LightType.Directional;
        moon.shadows = LightShadows.Soft;
        moonObject.AddComponent<HDAdditionalLightData>();
        if (satellite)
        {
            moonObject.transform.rotation = Quaternion.Euler(58f, 25f, 0f);
            moon.color = new Color(1f, 0.96f, 0.9f);
            moon.intensity = 80000f;
        }
        else
        {
            moonObject.transform.rotation = new Quaternion(0.25901439f, -0.32877086f, 0.09427353f, 0.90329052f);
            moon.color = new Color(0.16641437f, 0.2840521f, 0.41911763f);
            moon.intensity = 2200f;
        }
        var cameraObject = new GameObject("ValleyShot");
        SceneManager.MoveGameObjectToScene(cameraObject, preview);
        var view = cameraObject.AddComponent<Camera>();
        view.fieldOfView = 60f;
        view.nearClipPlane = satellite ? 1f : 0.08f;
        view.farClipPlane = satellite ? 900f : 800f;
        if (satellite)
        {
            view.orthographic = true;
            view.orthographicSize = 350f;
            view.aspect = 1f;
            var center = new Vector3(Village.x, 0f, Village.y);
            cameraObject.transform.position = center + Vector3.up * 520f;
            cameraObject.transform.LookAt(center, Vector3.forward);
        }
        cameraObject.AddComponent<HDAdditionalCameraData>().clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
        var shot = cameraObject.AddComponent<ElderboomValleyShot>();
        shot.captureOnPlay = true;
        shot.satellite = satellite;
        EditorSceneManager.SaveScene(preview, "Assets/Scenes/Elderboom_Valley_Shot.unity");
        EditorSceneManager.SetActiveScene(preview);
        EditorSceneManager.CloseScene(game, true);
        File.WriteAllText("Logs/valley_preview/phase.txt", "preview houses " + copied + " lamps " + lamps.Count);
    }

    public static void Ensure()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var previous = GameObject.Find("Elderboom_Valley");
        if (previous != null) Object.DestroyImmediate(previous);
        if (AssetDatabase.LoadAssetAtPath<TerrainData>(DataPath) != null) AssetDatabase.DeleteAsset(DataPath);
        if (!AssetDatabase.IsValidFolder("Assets/Terrain")) AssetDatabase.CreateFolder("Assets", "Terrain");

        var terrainData = new TerrainData();
        terrainData.heightmapResolution = 513;
        terrainData.size = new Vector3(Size, MaxHeight, Size);
        AssetDatabase.CreateAsset(terrainData, DataPath);
        Sculpt(terrainData);
        Paint(terrainData);
        Plant(terrainData);

        var root = new GameObject("Elderboom_Valley");
        var terrainObject = Terrain.CreateTerrainGameObject(terrainData);
        terrainObject.name = "Elderboom_Valley_Terrain";
        terrainObject.transform.SetParent(root.transform, false);
        terrainObject.transform.position = new Vector3(Village.x - Size * 0.5f, 0f, Village.y - Size * 0.5f);
        var terrain = terrainObject.GetComponent<Terrain>();
        terrain.materialTemplate = AssetDatabase.LoadAssetAtPath<Material>(Pack + "Terrain/Materials/TerrainLit.mat");
        terrain.drawInstanced = true;
        terrain.groupingID = 0;
        terrain.allowAutoConnect = false;
        terrain.heightmapPixelError = 5f;
        terrain.basemapDistance = 420f;
        terrain.treeDistance = 720f;
        terrain.treeBillboardDistance = 95f;
        terrain.treeCrossFadeLength = 18f;
        terrain.detailObjectDistance = 48f;
        terrain.detailObjectDensity = 0.45f;
        Dress(root.transform, terrain);
        SeatLamps(terrain);
        var ground = GameObject.Find("Elderboom_Playable_Ground");
        if (ground != null) ground.SetActive(false);
        Bake(terrain);
        Directory.CreateDirectory("Logs/valley_preview");
        File.WriteAllText("Logs/valley_preview/heights.txt",
            "center " + terrain.SampleHeight(new Vector3(Village.x, 0f, Village.y)) + "\n" +
            "north " + terrain.SampleHeight(new Vector3(Village.x, 0f, Village.y + 168f)) + "\n" +
            "northRange " + terrain.SampleHeight(new Vector3(Village.x + 40f, 0f, Village.y + 310f)) + "\n" +
            "westRange " + terrain.SampleHeight(new Vector3(Village.x - 300f, 0f, Village.y - 20f)) + "\n" +
            "eastRange " + terrain.SampleHeight(new Vector3(Village.x + 310f, 0f, Village.y - 40f)) + "\n" +
            "mud " + terrain.SampleHeight(new Vector3(Village.x + 235f, 0f, Village.y + 125f)) + "\n");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    static void Sculpt(TerrainData data)
    {
        int resolution = data.heightmapResolution;
        var heights = new float[resolution, resolution];
        float originX = Village.x - Size * 0.5f;
        float originZ = Village.y - Size * 0.5f;
        for (int z = 0; z < resolution; z++)
        {
            float wz = originZ + (z / (float)(resolution - 1)) * Size;
            for (int x = 0; x < resolution; x++)
            {
                float wx = originX + (x / (float)(resolution - 1)) * Size;
                heights[z, x] = Mathf.Clamp01(WorldHeight(wx, wz) / MaxHeight);
            }
        }
        data.SetHeights(0, 0, heights);
        Directory.CreateDirectory("Logs/valley_preview");
        float north = data.GetInterpolatedHeight(Size * 0.5f, Size * 0.5f + 168f);
        float center = data.GetInterpolatedHeight(Size * 0.5f, Size * 0.5f);
        File.WriteAllText("Logs/valley_preview/height_raw.txt", "center " + center + " north " + north + " res " + resolution + "\n");
    }

    static float WorldHeight(float x, float z)
    {
        float dx = x - Village.x;
        float dz = z - Village.y;
        float dist = Mathf.Sqrt(dx * dx + dz * dz);
        float shelf = Mathf.InverseLerp(128f, 78f, dist);
        shelf = shelf * shelf * (3f - 2f * shelf);
        float meadow = (Fbm(x * 0.0075f, z * 0.0075f) - 0.42f) * 8f;
        float north = Mathf.Exp(-Sqr((dz - 168f) / 30f)) * Mathf.Clamp01(1f - Mathf.Abs(dx) / 150f) * 36f;
        float west = Mathf.Exp(-Sqr((dx + 170f) / 28f)) * Mathf.Clamp01(1f - Mathf.Abs(dz - 8f) / 130f) * 28f;
        float hollow = -Mathf.Exp(-(Sqr(dx - 155f) + Sqr(dz - 18f)) / (16f * 16f)) * 2.6f;
        float outside = meadow + north + west + hollow + WestRange(dx, dz) + NorthRange(dx, dz) + EastRange(dx, dz);
        float height = Mathf.Lerp(ShelfY + outside, ShelfY, shelf);
        float mud = MudWeight(dx, dz);
        if (mud > 0f)
        {
            float creek = MudChannel(dx, dz);
            float flat = Mathf.Lerp(1.05f, 0.32f, creek);
            height = Mathf.Lerp(height, flat, mud);
        }
        return Mathf.Clamp(height, 0.2f, 60f);
    }

    static float Band(float edgeIn, float full, float value)
    {
        float weight = Mathf.InverseLerp(edgeIn, full, value);
        return weight * weight * (3f - 2f * weight);
    }

    static float Summit(float dx, float dz, float seed)
    {
        float peaks = Mathf.PerlinNoise(dx * 0.011f + seed, dz * 0.011f + seed * 0.37f);
        return Mathf.Lerp(0.74f, 1f, peaks);
    }

    static float WestRange(float dx, float dz)
    {
        float depth = Band(-150f, -275f, dx);
        float along = Mathf.Clamp01(Mathf.Min(dz + 260f, 165f - dz) / 55f);
        if (depth <= 0f || along <= 0f) return 0f;
        float peaks = Mathf.PerlinNoise(dx * 0.01f + 2.1f, dz * 0.013f + 4.4f);
        return depth * along * Mathf.Lerp(42f, 60f, peaks);
    }

    static float NorthRange(float dx, float dz)
    {
        float depth = Band(200f, 305f, dz);
        float along = Mathf.Clamp01(Mathf.Min(dx + 80f, 185f - dx) / 50f);
        if (depth <= 0f || along <= 0f) return 0f;
        float peaks = Mathf.PerlinNoise(dx * 0.012f + 6.2f, dz * 0.01f + 1.7f);
        return depth * along * Mathf.Lerp(44f, 62f, peaks);
    }

    static float EastRange(float dx, float dz)
    {
        float depth = Band(220f, 315f, dx);
        float along = Mathf.Clamp01(Mathf.Min(dz + 175f, 55f - dz) / 48f);
        if (depth <= 0f || along <= 0f) return 0f;
        float peaks = Mathf.PerlinNoise(dx * 0.011f + 8.4f, dz * 0.014f + 3.3f);
        return depth * along * Mathf.Lerp(42f, 60f, peaks);
    }

    static float MudWeight(float dx, float dz)
    {
        float insetX = Mathf.Clamp01(Mathf.Min(dx - 155f, 315f - dx) / 22f);
        float insetZ = Mathf.Clamp01(Mathf.Min(dz - 60f, 190f - dz) / 22f);
        return insetX * insetZ;
    }

    static float MudChannel(float dx, float dz)
    {
        float along = (dx - 175f) / 150f;
        float main = Mathf.Abs((dz - 95f) / 16f - 0.55f * Mathf.Sin(along * 5.5f));
        float side = Mathf.Abs((dz - 155f) / 14f - 0.4f * Mathf.Sin(along * 4f + 1.2f));
        return Mathf.Clamp01(1f - Mathf.Min(main, side) * 2.4f);
    }

    static bool InDenseForest(float dx, float dz)
    {
        if (MudWeight(dx, dz) > 0.2f) return false;
        if (dx < -85f && dz > 170f && dx > -345f && dz < 345f) return true;
        if (dx > 195f && dz > 210f && dx < 345f && dz < 345f) return true;
        if (dz < -100f && dx > -150f && dx < 250f && dz > -345f) return true;
        return false;
    }

    static void Paint(TerrainData data)
    {
        var layers = new[]
        {
            LoadLayer("Grass_Soil_A"),
            LoadLayer("Grass_Moss_A"),
            LoadLayer("Heather_A"),
            LoadLayer("Grass_B"),
            LoadLayer("Cliff_Mossy_E"),
            LoadLayer("Black_Sand_A"),
            LoadLayer("Tidal_Pools_B"),
            LoadLayer("Black_Sand_Rocks_B")
        };
        data.terrainLayers = layers;
        data.alphamapResolution = 512;
        int resolution = data.alphamapResolution;
        var maps = new float[resolution, resolution, layers.Length];
        float originX = Village.x - Size * 0.5f;
        float originZ = Village.y - Size * 0.5f;
        for (int z = 0; z < resolution; z++)
        {
            float wz = originZ + (z / (float)(resolution - 1)) * Size;
            for (int x = 0; x < resolution; x++)
            {
                float wx = originX + (x / (float)(resolution - 1)) * Size;
                float dx = wx - Village.x;
                float dz = wz - Village.y;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                float height = WorldHeight(wx, wz);
                float slope = Slope(wx, wz);
                float soil = 0f, moss = 0f, heather = 0f, grass = 0f, cliff = 0f, sand = 0f, tidal = 0f, rocks = 0f;
                bool track = dist < 75f && (Mathf.Abs(dx) < 3.2f || Mathf.Abs(dz) < 3.2f);
                float mud = MudWeight(dx, dz);
                if (mud > 0.45f)
                {
                    float creek = MudChannel(dx, dz);
                    sand = Mathf.Lerp(0.78f, 0.16f, creek);
                    tidal = Mathf.Lerp(0.05f, 0.78f, creek);
                    rocks = Mathf.Lerp(0.17f, 0.06f, creek);
                }
                else if (slope > 0.42f && dist > 140f) { cliff = 0.8f; heather = 0.2f; }
                else if (height > 10f) { heather = 0.62f; moss = 0.28f; grass = 0.1f; }
                else if (track) { soil = 0.82f; moss = 0.18f; }
                else if (dist < 70f) { soil = 0.55f; moss = 0.45f; }
                else { moss = 0.62f; grass = 0.28f; soil = 0.1f; }
                maps[z, x, 0] = soil;
                maps[z, x, 1] = moss;
                maps[z, x, 2] = heather;
                maps[z, x, 3] = grass;
                maps[z, x, 4] = cliff;
                maps[z, x, 5] = sand;
                maps[z, x, 6] = tidal;
                maps[z, x, 7] = rocks;
            }
        }
        data.SetAlphamaps(0, 0, maps);
    }

    static float Slope(float x, float z)
    {
        float step = 2.5f;
        float h = WorldHeight(x, z);
        float dx = (WorldHeight(x + step, z) - h) / step;
        float dz = (WorldHeight(x, z + step) - h) / step;
        return Mathf.Clamp01(Mathf.Sqrt(dx * dx + dz * dz) / 1.4f);
    }

    static void Plant(TerrainData data)
    {
        var trees = new[]
        {
            Tree(Pack + "Prefabs/Trees/Pines/Pine_A/Pine_A.prefab"),
            Tree(Pack + "Prefabs/Trees/Pines/Pine_B/Pine_B.prefab"),
            Tree(Pack + "Prefabs/Trees/Pines/Pine_C/Pine_C.prefab"),
            Tree(Pack + "Prefabs/Trees/Pines/Pine_D/Pine_D.prefab"),
            Tree(Pack + "Prefabs/Trees/Conifer/Conifer.prefab")
        };
        data.treePrototypes = trees;
        var random = new System.Random(1701);
        var instances = new List<TreeInstance>();
        for (int i = 0; i < 900 && instances.Count < 230; i++)
        {
            float dx = Lerp(random, -280f, 280f);
            float dz = Lerp(random, -250f, 280f);
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            if (dist < 102f || dist > 290f) continue;
            if (MudWeight(dx, dz) > 0.2f) continue;
            if (InDenseForest(dx, dz)) continue;
            float nx = (dx + Size * 0.5f) / Size;
            float nz = (dz + Size * 0.5f) / Size;
            if (nx <= 0.02f || nx >= 0.98f || nz <= 0.02f || nz >= 0.98f) continue;
            float slope = Slope(Village.x + dx, Village.y + dz);
            if (slope > 0.72f) continue;
            float clump = Mathf.PerlinNoise(nx * 9f + 2f, nz * 9f + 6f);
            if (clump < 0.46f) continue;
            int kind = clump > 0.78f ? 4 : random.Next(0, 4);
            float scale = Lerp(random, 0.85f, 1.35f);
            float localX = nx * Size;
            float localZ = nz * Size;
            float worldHeight = data.GetInterpolatedHeight(localX, localZ);
            instances.Add(new TreeInstance
            {
                position = new Vector3(nx, worldHeight / MaxHeight, nz),
                widthScale = scale,
                heightScale = scale * Lerp(random, 0.92f, 1.08f),
                rotation = Lerp(random, 0f, Mathf.PI * 2f),
                prototypeIndex = kind,
                color = Color.white,
                lightmapColor = Color.white
            });
        }
        FillForest(instances, random);
        data.SetTreeInstances(instances.ToArray(), true);

        data.SetDetailResolution(128, 16);
        data.detailPrototypes = new[]
        {
            Detail(Pack + "Prefabs/Details/Grass_A.prefab"),
            Detail(Pack + "Prefabs/Details/Fern_A.prefab"),
            Detail(Pack + "Prefabs/Details/Heather_A.prefab")
        };
        int detail = data.detailResolution;
        var grass = new int[detail, detail];
        var fern = new int[detail, detail];
        var heather = new int[detail, detail];
        var detailRandom = new System.Random(1702);
        float originX = Village.x - Size * 0.5f;
        float originZ = Village.y - Size * 0.5f;
        for (int z = 0; z < detail; z++)
        {
            float wz = originZ + (z / (float)(detail - 1)) * Size;
            for (int x = 0; x < detail; x++)
            {
                float wx = originX + (x / (float)(detail - 1)) * Size;
                float dx = wx - Village.x;
                float dz = wz - Village.y;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist < 36f || dist > 220f || Slope(wx, wz) > 0.55f) continue;
                if (detailRandom.NextDouble() > 0.28) continue;
                if (dist < 78f) grass[z, x] = 1;
                else if (WorldHeight(wx, wz) > 9f) heather[z, x] = 1;
                else if (detailRandom.NextDouble() > 0.75) fern[z, x] = 1;
                else grass[z, x] = 1;
            }
        }
        data.SetDetailLayer(0, 0, 0, grass);
        data.SetDetailLayer(0, 0, 1, fern);
        data.SetDetailLayer(0, 0, 2, heather);
        Sculpt(data);
        var planted = data.treeInstances;
        data.SetTreeInstances(planted, true);
    }

    static void FillForest(List<TreeInstance> instances, System.Random random)
    {
        int count = 0;
        count += ForestRows(instances, random, -112f, -7.2f, 5, -145f, 245f, true);
        count += ForestRows(instances, random, 176f, 7.2f, 4, -210f, -96f, true);
        count += ForestRows(instances, random, -96f, -7.2f, 4, 176f, 245f, false);
        count += ForestRows(instances, random, 212f, 7.2f, 4, 200f, 300f, true);
        count += ForestRows(instances, random, 202f, 7.2f, 4, 212f, 280f, false);
        File.WriteAllText("Logs/valley_preview/forest.txt", "front rows " + count + "\n");
    }

    static int ForestRows(List<TreeInstance> instances, System.Random random, float start, float step, int rows, float from, float to, bool alongX)
    {
        int added = 0;
        for (int row = 0; row < rows; row++)
        {
            float line = start + step * row;
            for (float cursor = from; cursor <= to; cursor += 6.4f)
            {
                float dx = alongX ? cursor + Lerp(random, -1.8f, 1.8f) : line + Lerp(random, -1.8f, 1.8f);
                float dz = alongX ? line + Lerp(random, -1.8f, 1.8f) : cursor + Lerp(random, -1.8f, 1.8f);
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist < 108f) continue;
                float nx = (dx + Size * 0.5f) / Size;
                float nz = (dz + Size * 0.5f) / Size;
                if (nx <= 0.02f || nx >= 0.98f || nz <= 0.02f || nz >= 0.98f) continue;
                instances.Add(new TreeInstance
                {
                    position = new Vector3(nx, 0f, nz),
                    widthScale = Lerp(random, 0.95f, 1.4f),
                    heightScale = Lerp(random, 0.9f, 1.25f),
                    rotation = Lerp(random, 0f, Mathf.PI * 2f),
                    prototypeIndex = random.Next(0, 5),
                    color = Color.white,
                    lightmapColor = Color.white
                });
                added++;
            }
        }
        return added;
    }

    static void Dress(Transform root, Terrain terrain)
    {
        var rocks = new[]
        {
            Pack + "Prefabs/Rocks/Rock_Overgrown_A.prefab",
            Pack + "Prefabs/Rocks/Rock_Overgrown_B.prefab",
            Pack + "Prefabs/Rocks/Rock_Overgrown_C.prefab",
            Pack + "Prefabs/Rocks/Rock_A_01.prefab"
        };
        var random = new System.Random(1703);
        int placed = 0;
        for (int i = 0; i < 80 && placed < 16; i++)
        {
            float angle = Lerp(random, 0f, Mathf.PI * 2f);
            float radius = Lerp(random, 88f, 140f);
            var point = new Vector3(Village.x + Mathf.Cos(angle) * radius, 0f, Village.y + Mathf.Sin(angle) * radius);
            if (Sqr(point.x - (Village.x + 235f)) + Sqr(point.z - (Village.y + 125f)) < 90f * 90f) continue;
            point.y = terrain.SampleHeight(point) + terrain.transform.position.y;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(rocks[random.Next(0, rocks.Length)]);
            if (prefab == null) continue;
            var rock = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            rock.name = "Elderboom_Rock";
            rock.transform.SetParent(root, true);
            rock.transform.position = point;
            rock.transform.rotation = Quaternion.Euler(0f, Lerp(random, 0f, 360f), 0f);
            float scale = Lerp(random, 0.7f, 1.45f);
            rock.transform.localScale = Vector3.one * scale;
            if (rock.GetComponentInChildren<Collider>() == null)
            {
                var filter = rock.GetComponentInChildren<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                    rock.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
            }
            placed++;
        }

        var waterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Pack + "Prefabs/Water/WaterPlane.prefab");
        if (waterPrefab == null) return;
        var hollow = new Vector3(Village.x + 155f, 0f, Village.y + 18f);
        float bed = terrain.SampleHeight(hollow) + terrain.transform.position.y;
        var pond = (GameObject)PrefabUtility.InstantiatePrefab(waterPrefab);
        pond.name = "Elderboom_Hollow_Water";
        pond.transform.SetParent(root, true);
        pond.transform.position = new Vector3(hollow.x, bed + 0.15f, hollow.z);
        pond.transform.localScale = new Vector3(18f, 1f, 12f);
        foreach (var collider in pond.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);
    }

    static void PlaceSheet(GameObject waterPrefab, Transform root, Terrain terrain, float dx, float dz, float length, float width, float yaw)
    {
        var water = (GameObject)PrefabUtility.InstantiatePrefab(waterPrefab);
        water.name = "Elderboom_Mud_Water";
        water.transform.SetParent(root, true);
        var point = new Vector3(Village.x + dx, 0f, Village.y + dz);
        float bed = terrain.SampleHeight(point) + terrain.transform.position.y;
        water.transform.position = new Vector3(point.x, bed + 0.08f, point.z);
        water.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        water.transform.localScale = new Vector3(length, 1f, width);
        foreach (var collider in water.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);
    }

    static void SeatLamps(Terrain terrain)
    {
        foreach (var transform in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
        {
            if (!IsLampRoot(transform.gameObject)) continue;
            var position = transform.position;
            position.y = terrain.SampleHeight(position) + terrain.transform.position.y;
            transform.position = position;
        }
    }

    static bool IsLampRoot(GameObject gameObject)
    {
        if (!PrefabUtility.IsAnyPrefabInstanceRoot(gameObject)) return false;
        string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
        return !string.IsNullOrEmpty(path) && path.Replace('\\', '/').EndsWith("StreetLamp.gltf");
    }

    static void Bake(Terrain terrain)
    {
        var sources = new List<NavMeshBuildSource>();
        var terrainSource = new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Terrain,
            sourceObject = terrain.terrainData,
            transform = terrain.transform.localToWorldMatrix,
            area = 0
        };
        sources.Add(terrainSource);
        foreach (var collider in Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Exclude))
        {
            if (collider.sharedMesh == null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
            if (collider.GetComponent<Terrain>() != null) continue;
            sources.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Mesh,
                sourceObject = collider.sharedMesh,
                transform = collider.transform.localToWorldMatrix,
                area = 0
            });
        }
        foreach (var box in Object.FindObjectsByType<BoxCollider>(FindObjectsInactive.Exclude))
        {
            if (!box.enabled || !box.gameObject.activeInHierarchy || box.GetComponent<ElderboomGate>() != null) continue;
            sources.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                transform = box.transform.localToWorldMatrix * Matrix4x4.Translate(box.center),
                size = box.size,
                area = 0
            });
        }
        var settings = NavMesh.GetSettingsByIndex(0);
        settings.agentRadius = 0.4f;
        settings.agentHeight = 1.8f;
        settings.agentClimb = 0.35f;
        settings.agentSlope = 30f;
        var nav = NavMeshBuilder.BuildNavMeshData(settings, sources, new Bounds(new Vector3(Village.x, 4f, Village.y), new Vector3(200f, 16f, 200f)), Vector3.zero, Quaternion.identity);
        if (nav == null) throw new System.InvalidOperationException("Elderboom valley navigation bake failed.");
        AssetDatabase.DeleteAsset(NavPath);
        AssetDatabase.CreateAsset(nav, NavPath);
        var holder = Object.FindAnyObjectByType<ElderboomNavigation>();
        if (holder == null) holder = new GameObject("Elderboom_Navigation").AddComponent<ElderboomNavigation>();
        holder.data = nav;
        EditorUtility.SetDirty(holder);
    }

    static TerrainLayer LoadLayer(string name)
    {
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayerRoot + name + ".terrainlayer");
        if (layer == null) throw new System.InvalidOperationException("Missing terrain layer " + name);
        return layer;
    }

    static TreePrototype Tree(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) throw new System.InvalidOperationException("Missing tree " + path);
        return new TreePrototype { prefab = prefab };
    }

    static DetailPrototype Detail(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) throw new System.InvalidOperationException("Missing detail " + path);
        return new DetailPrototype
        {
            prototype = prefab,
            usePrototypeMesh = true,
            renderMode = DetailRenderMode.Grass,
            useInstancing = true,
            healthyColor = Color.white,
            dryColor = new Color(0.72f, 0.7f, 0.58f),
            minWidth = 0.7f,
            maxWidth = 1.15f,
            minHeight = 0.65f,
            maxHeight = 1.1f,
            noiseSpread = 0.45f,
            alignToGround = 0.65f
        };
    }

    static float Fbm(float x, float z)
    {
        float sum = 0f, amplitude = 0.55f, frequency = 1f;
        for (int i = 0; i < 4; i++)
        {
            sum += amplitude * Mathf.PerlinNoise(x * frequency + 13.2f, z * frequency + 4.7f);
            frequency *= 2.05f;
            amplitude *= 0.5f;
        }
        return sum;
    }

    static float Sqr(float value) => value * value;
    static float Lerp(System.Random random, float a, float b) => a + (float)random.NextDouble() * (b - a);
}
