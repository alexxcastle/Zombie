using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Builds the two Elderboom zombies from their own meshes.
/// Locomotion and combat clips come only from files whose names start with "Zombie",
/// plus the female folder's "Female Zombie ..." takes. Generic mesh clips
/// (walking_man, FreeRunning) and male-zombie-1Walking-2 are never assigned.
/// </summary>
public static class ElderboomZombieRig
{
    const string MaleRoot = "Assets/GameAssets/Ported/ZombieMale/";
    const string FemaleRoot = "Assets/GameAssets/Ported/ZombieFemale/source/female-zombie-1/";
    const string MaleMesh = MaleRoot + "Meshy_AI_male_zombie_rigged_biped/Meshy_AI_male_zombie_rigged_biped_Meshy_Merged_Animations.fbx";
    const string FemaleMesh = FemaleRoot + "female zombie mesh/Meshy_AI_zombie_hoodie_rigged__biped_Animation_Walking_frame_rate_60.fbx";
    const string MaleAlbedo = MaleRoot + "Meshy_AI_male_zombie_rigged_biped/Meshy_AI_male_zombie_rigged_biped_texture_0.png";
    const string FemaleAlbedo = FemaleRoot + "female zombie mesh/Meshy_AI_zombie_hoodie_rigged__biped_texture_0.png";

    static readonly (string state, string file)[] MaleClips =
    {
        ("WalkA", "Zombie Walk.fbx"),
        ("AttackA", "Zombie Attack.fbx"),
        ("AttackPunch", "Zombie Punching attack.fbx"),
        ("AttackBite", "Zombie Neck Bite.fbx"),
        ("DeathA", "Zombie Dying 1.fbx"),
        ("DeathB", "Zombie Dying 2.fbx"),
        ("DeathMelee", "Zombie Dying by Melee.fbx"),
        ("ShotHit", "Zombie Shot Reaction Hit.fbx"),
        ("ShockwaveHit", "Zombie Shockwave Reaction Hit.fbx"),
        ("SpawnA", "Zombie Spawn animation 2.fbx"),
        ("SpawnB", "zombie spawn animation.fbx"),
        ("SpawnC", "Zombie Spawn animation 3.fbx"),
        ("Turn", "Zombie Turn.fbx"),
        ("GetUp", "zombie gettin up in not died by lift&smahs animation.fbx"),
        ("LiftFloat", "Zombie-Floating-during-lift&smash.fbx"),
        ("LiftSlam", "Zombie-Smashed-bylift&smash-animation.fbx")
    };

    static readonly (string state, string file)[] FemaleClips =
    {
        ("WalkA", "Female Zombie Walking 1.fbx"),
        ("WalkB", "Female Zombie Walk 2.fbx"),
        ("Crawl", "Female Zombie Crawling.fbx"),
        ("AttackA", "Zombie Attack.fbx"),
        ("AttackPunch", "Zombie Attack 2.fbx"),
        ("AttackBite", "Zombie Attack 3.fbx"),
        ("DeathA", "Zombie Dying.fbx"),
        ("DeathB", "Female Zombie Death by Shockwave.fbx"),
        ("ShotHit", "female Zombie shot  Reaction Hit.fbx"),
        ("ShockwaveHit", "Zombie Shockwave Reaction Hit.fbx"),
        ("SpawnA", "Zombie Spwan 1.fbx"),
        ("SpawnB", "female zombie spawn 2.fbx"),
        ("Turn", "Zombie Turn.fbx"),
        ("GetUp", "Zombie getting up if not died from lift&smash.fbx"),
        ("LiftFloat", "Female zombie floating during lift & smash.fbx"),
        ("LiftSlam", "female zombie smashed to ground druign lift & smash .fbx")
    };

    public static void RebuildAndShip()
    {
        Rebuild();
        HDRPSliceStandaloneBuild.BuildPort();
        Debug.Log("ELDERBOOM_ZOMBIES_SHIPPED");
    }

    public static void Rebuild()
    {
        var report = new List<string>();
        var maleAvatar = ConfigureMesh(MaleMesh, report);
        var femaleAvatar = ConfigureMesh(FemaleMesh, report);
        var maleMotions = ConfigureClips(MaleRoot, MaleClips, maleAvatar, report);
        var femaleMotions = ConfigureClips(FemaleRoot, FemaleClips, femaleAvatar, report);
        BuildPrefab(false, MaleMesh, maleAvatar, maleMotions, MaleAlbedo, "Assets/Resources/Elderboom/Zombie.prefab", "Assets/Animations/Port_Zombie.controller");
        BuildPrefab(true, FemaleMesh, femaleAvatar, femaleMotions, FemaleAlbedo, "Assets/Resources/Elderboom/ZombieFemale.prefab", "Assets/Animations/Port_ZombieFemale.controller");
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/zombie_clips.txt", string.Join("\n", report));
        AssetDatabase.SaveAssets();
        Debug.Log("ELDERBOOM_ZOMBIES_REBUILT\n" + string.Join("\n", report));
    }

    static bool Allowed(string fileName)
    {
        string name = Path.GetFileName(fileName).Trim();
        if (name.StartsWith("zombie", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.StartsWith("female zombie", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    static Avatar ConfigureMesh(string path, List<string> report)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        if (importer == null) throw new InvalidOperationException("Missing zombie mesh " + path);
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.SaveAndReimport();
        var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault(a => a != null && a.isHuman && a.isValid);
        if (avatar == null) throw new InvalidOperationException("Humanoid avatar failed for " + path);
        report.Add("MESH " + path + " avatar=" + avatar.name);
        return avatar;
    }

    static Dictionary<string, AnimationClip> ConfigureClips(string root, (string state, string file)[] files, Avatar avatar, List<string> report)
    {
        var motions = new Dictionary<string, AnimationClip>();
        foreach (var entry in files)
        {
            if (!Allowed(entry.file)) throw new InvalidOperationException("Refusing non-zombie clip " + entry.file);
            string path = root + entry.file;
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                report.Add("MISSING " + path);
                continue;
            }
            importer.importAnimation = true;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = null;
            importer.SaveAndReimport();
            bool loop = entry.state == "WalkA" || entry.state == "WalkB" || entry.state == "Crawl";
            var clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0) clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
            {
                report.Add("NO CLIP " + path);
                continue;
            }
            for (int i = 0; i < clips.Length; i++)
            {
                clips[i].name = entry.state;
                clips[i].loopTime = loop;
                clips[i].lockRootRotation = true;
                clips[i].lockRootHeightY = true;
                clips[i].lockRootPositionXZ = true;
                clips[i].keepOriginalOrientation = true;
                clips[i].keepOriginalPositionY = true;
                clips[i].keepOriginalPositionXZ = false;
                clips[i].maskType = ClipAnimationMaskType.None;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => c != null && !c.name.StartsWith("__preview"));
            if (clip == null)
            {
                report.Add("NO CLIP " + path);
                continue;
            }
            motions[entry.state] = clip;
            report.Add(entry.state + " <- " + entry.file);
        }
        return motions;
    }

    static void BuildPrefab(bool female, string meshPath, Avatar avatar, Dictionary<string, AnimationClip> motions, string albedoPath, string prefabPath, string controllerPath)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(meshPath);
        if (source == null) throw new InvalidOperationException("Missing " + meshPath);
        var root = new GameObject(female ? "ZombieFemale" : "Zombie");
        var visual = UnityEngine.Object.Instantiate(source, root.transform);
        visual.name = female ? "Female_Zombie_Visual" : "Male_Zombie_Visual";
        var renderers = visual.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) throw new InvalidOperationException("Zombie mesh has no renderers: " + meshPath);
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        visual.transform.localScale *= 1.82f / Mathf.Max(0.01f, bounds.size.y);
        bounds = renderers[0].bounds;
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        visual.transform.localPosition -= Vector3.up * bounds.min.y;

        AssetDatabase.DeleteAsset(controllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        AnimatorState defaultState = null;
        foreach (var pair in motions)
        {
            var state = controller.layers[0].stateMachine.AddState(pair.Key);
            state.motion = pair.Value;
            if (pair.Key == "WalkA") defaultState = state;
        }
        if (defaultState != null) controller.layers[0].stateMachine.defaultState = defaultState;

        var animator = visual.GetComponentInChildren<Animator>() ?? visual.AddComponent<Animator>();
        animator.avatar = avatar;
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;

        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
        var material = Lit(female ? "Port_Zombie_Female" : "Port_Zombie_Male", albedo);
        foreach (var renderer in renderers)
        {
            var slots = renderer.sharedMaterials;
            for (int i = 0; i < slots.Length; i++) slots[i] = material;
            renderer.sharedMaterials = slots;
        }

        var hitbox = root.AddComponent<CapsuleCollider>();
        hitbox.center = new Vector3(0f, 0.72f, 0f);
        hitbox.height = 1.35f;
        hitbox.radius = 0.32f;
        var head = new GameObject("Head");
        head.transform.SetParent(root.transform, false);
        head.transform.localPosition = new Vector3(0f, 1.58f, 0.04f);
        var headHit = head.AddComponent<SphereCollider>();
        headHit.radius = 0.18f;
        root.AddComponent<ElderboomZombie>();
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        if (prefab == null) throw new InvalidOperationException("Failed to save " + prefabPath);
    }

    static Material Lit(string name, Texture2D albedo)
    {
        string path = "Assets/Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("HDRP/Lit")) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", Color.white);
        material.SetTexture("_BaseColorMap", albedo);
        material.SetFloat("_Smoothness", 0.22f);
        material.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    public static void AdoptStoreZombie(IEnumerable<string> imported)
    {
        var paths = (imported ?? Array.Empty<string>()).Where(p => p.StartsWith("Assets/", StringComparison.Ordinal)).ToList();
        if (paths.Count == 0)
            paths = AssetDatabase.GetAllAssetPaths().Where(p => p.IndexOf("Zombie Male AAB", StringComparison.OrdinalIgnoreCase) >= 0 || p.IndexOf("ZombieMaleAAB", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        string modelPath = paths
            .Where(p => p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => ScoreStoreModel(p))
            .FirstOrDefault();
        if (string.IsNullOrEmpty(modelPath))
            throw new InvalidOperationException("ELDERBOOM_AAB_MODEL_MISSING");
        if (modelPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
        {
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer != null && importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.SaveAndReimport();
            }
        }
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        var avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().FirstOrDefault(a => a != null && a.isHuman && a.isValid);
        if (source == null) throw new InvalidOperationException("ELDERBOOM_AAB_SOURCE_MISSING " + modelPath);
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/Port_Zombie.controller");
        var root = new GameObject("ZombieMaleAAB");
        var visual = UnityEngine.Object.Instantiate(source, root.transform);
        visual.name = "AAB_Zombie_Visual";
        var renderers = visual.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) throw new InvalidOperationException("ELDERBOOM_AAB_NO_RENDERERS");
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        visual.transform.localScale *= 1.82f / Mathf.Max(0.01f, bounds.size.y);
        bounds = renderers[0].bounds;
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        visual.transform.localPosition -= Vector3.up * bounds.min.y;
        var animator = visual.GetComponentInChildren<Animator>() ?? visual.AddComponent<Animator>();
        if (avatar != null) animator.avatar = avatar;
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        var hitbox = root.AddComponent<CapsuleCollider>();
        hitbox.center = new Vector3(0f, 0.72f, 0f);
        hitbox.height = 1.35f;
        hitbox.radius = 0.32f;
        var head = new GameObject("Head");
        head.transform.SetParent(root.transform, false);
        head.transform.localPosition = new Vector3(0f, 1.58f, 0.04f);
        head.AddComponent<SphereCollider>().radius = 0.18f;
        root.AddComponent<ElderboomZombie>();
        Directory.CreateDirectory("Assets/Resources/Elderboom");
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, "Assets/Resources/Elderboom/ZombieMaleAAB.prefab");
        UnityEngine.Object.DestroyImmediate(root);
        if (prefab == null) throw new InvalidOperationException("ELDERBOOM_AAB_PREFAB_FAILED");
        Debug.Log("ELDERBOOM_AAB_READY " + modelPath);
    }

    static int ScoreStoreModel(string path)
    {
        string lower = path.ToLowerInvariant();
        int score = 0;
        if (lower.Contains("dismember") || lower.Contains("bodypart") || lower.Contains("parts")) score += 50;
        if (lower.Contains("shirtless")) score += 10;
        if (lower.Contains("hdrp")) score -= 5;
        if (lower.EndsWith(".prefab")) score -= 3;
        if (lower.Contains("demo") || lower.Contains("showcase")) score += 8;
        return score;
    }
}
