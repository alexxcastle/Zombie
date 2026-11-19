using UnityEditor;
using UnityEngine;

// Import rules for the full-body operator models exported from Blender (Meshy rig, Mixamo bone names):
//   Assets/Resources/Elderboom/Characters/<Op>/<Op>_Rigged.fbx       mesh + skeleton (Humanoid avatar)
//   Assets/Resources/Elderboom/Characters/<Op>/Anims/<Op>_<Clip>.fbx  skeleton + one clip each
// Generic rig, hierarchy preserved: every clip drives the same bone paths as the model.
// Loops, root handling and clip names are set here; nothing has to be configured by hand in the Inspector.
public sealed class ElderboomCharacterImport : AssetPostprocessor
{
    const string Root = "Assets/Resources/Elderboom/Characters/";

    // One-shot clips (everything else loops).
    static readonly string[] OneShot = { "Fall_from_Bar", "Throw_Grenade", "Downed_ReachHelp" };   // Rifle_Idle loops
    // Clips that travel forward/back/sideways: their XZ goes to root motion, which the body discards (stays in place).
    static readonly string[] Travelling = { "Walking", "Running", "Rifle_WalkForward", "Walk_Backward_with_Gun", "Walk_Left_with_Gun" };

    public override uint GetVersion() => 3;   // bump to reimport everything under Characters/

    bool Ours => assetPath.StartsWith(Root) && assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);
    bool IsAnim => assetPath.Contains("/Anims/");
    bool IsProp => assetPath.Contains("/Props/");   // weapon parented to a hand bone (plain hierarchy, no avatar)

    void OnPreprocessModel()
    {
        if (!Ours) return;
        var mi = (ModelImporter)assetImporter;
        if (IsProp)
        {
            mi.animationType = ModelImporterAnimationType.None;
            mi.importAnimation = false;
            mi.importCameras = mi.importLights = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            // Blender material "akmn_<part>" -> the game's existing HDRP material "PBR_akmn_<part>_*".
            foreach (var guid in AssetDatabase.FindAssets("PBR_akmn_ t:Material"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string file = System.IO.Path.GetFileNameWithoutExtension(path);          // PBR_akmn_receiver_002_...
                string[] parts = file.Split('_');
                if (parts.Length < 3) continue;
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null) mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "akmn_" + parts[2]), mat);
            }
            return;
        }
        // Generic, not Humanoid: the clips are authored on this exact skeleton in Blender, and Humanoid retargeting
        // (muscle limits) bent poses with the arms across the body (rifle grips). Generic plays the bone rotations as-is.
        mi.animationType = ModelImporterAnimationType.Generic;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.preserveHierarchy = true;   // keep the "SmartRigArmature" node so clip paths match the model
        mi.importAnimation = IsAnim;
        mi.importBlendShapes = false;
        mi.importCameras = mi.importLights = false;
        mi.materialImportMode = IsAnim ? ModelImporterMaterialImportMode.None : ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        mi.isReadable = false;
        mi.meshCompression = ModelImporterMeshCompression.Off;
    }

    void OnPreprocessAnimation()
    {
        if (!Ours || !IsAnim || IsProp) return;
        var mi = (ModelImporter)assetImporter;
        var clips = mi.defaultClipAnimations;
        if (clips == null || clips.Length == 0) return;
        string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        int sep = file.IndexOf('_');
        string clipName = sep > 0 ? file.Substring(sep + 1) : file;   // "Eli_Bar_Hang_Idle" -> "Bar_Hang_Idle"
        bool loop = System.Array.IndexOf(OneShot, clipName) < 0;
        bool travelling = !clipName.Contains("inplace") && System.Array.Exists(Travelling, t => clipName == t || clipName.StartsWith(t + "_"));
        for (int i = 0; i < clips.Length; i++)
        {
            clips[i].name = file;
            clips[i].loopTime = loop;
            clips[i].loopPose = false;
            clips[i].lockRootRotation = true;
            clips[i].keepOriginalOrientation = true;
            clips[i].lockRootHeightY = true;
            clips[i].keepOriginalPositionY = true;
            clips[i].lockRootPositionXZ = !travelling;
            clips[i].keepOriginalPositionXZ = true;
            clips[i].maskType = ClipAnimationMaskType.None;
        }
        mi.clipAnimations = clips;
    }
}
