using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Melee reactions and deaths for the zombies (Mixamo clips, one FBX per state in GameAssets/Animations/ZombieMelee;
// the file name is the Animator state name). On editor load (idempotent): imports each file as Humanoid, then adds
// one state per clip to every zombie Animator controller. ElderboomZombie.TakeMeleeDamage plays them.
// Result: Logs/claude_zombie_melee.txt
static class ElderboomZombieMeleeAnims
{
    const string Folder = "Assets/GameAssets/Animations/ZombieMelee";
    const string Result = "Logs/claude_zombie_melee.txt";

    [InitializeOnLoadMethod]
    static void Watch() { EditorApplication.delayCall += Setup; }

    public static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || !Directory.Exists(Folder)) return;
        var log = new StringBuilder();
        try
        {
            var clips = new Dictionary<string, AnimationClip>();
            foreach (string file in Directory.GetFiles(Folder, "*.fbx").OrderBy(f => f))
            {
                string path = file.Replace('\\', '/'), state = Path.GetFileNameWithoutExtension(path);
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) { log.AppendLine(state + " not imported yet"); continue; }
                bool dirty = false;
                if (importer.animationType != ModelImporterAnimationType.Human) { importer.animationType = ModelImporterAnimationType.Human; importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; dirty = true; }
                if (importer.materialImportMode != ModelImporterMaterialImportMode.None) { importer.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
                var current = importer.clipAnimations;
                if (current == null || current.Length != 1 || current[0].name != state)
                {
                    var d = importer.defaultClipAnimations.FirstOrDefault();
                    if (d == null) { log.AppendLine(state + " has no take"); continue; }
                    importer.clipAnimations = new[] { new ModelImporterClipAnimation
                    {
                        name = state, takeName = d.takeName, firstFrame = d.firstFrame, lastFrame = d.lastFrame, loopTime = false,
                        lockRootRotation = true, lockRootHeightY = true, lockRootPositionXZ = true,
                        keepOriginalOrientation = true, keepOriginalPositionY = true, keepOriginalPositionXZ = true
                    } };
                    dirty = true;
                }
                if (dirty) importer.SaveAndReimport();
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
                if (clip == null) { log.AppendLine(state + " NO CLIP"); continue; }
                log.AppendLine(state + " " + clip.length.ToString("0.00") + "s " + (clip.isHumanMotion ? "human" : "GENERIC (humanoid mapping failed)"));
                if (clip.isHumanMotion) clips[state] = clip;
            }

            var controllers = new HashSet<AnimatorController>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Resources/Elderboom/Zombies" }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab == null) continue;
                foreach (var animator in prefab.GetComponentsInChildren<Animator>(true))
                {
                    var rc = animator.runtimeAnimatorController;
                    if (rc is AnimatorOverrideController over) rc = over.runtimeAnimatorController;
                    if (rc is AnimatorController ac) controllers.Add(ac);
                }
            }
            foreach (string path in new[] { "Assets/Animations/Port_Zombie.controller", "Assets/Animations/Port_ZombieFemale.controller" })
            {
                var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (ac != null) controllers.Add(ac);
            }
            int added = 0;
            foreach (var ac in controllers)
            {
                if (ac.layers.Length == 0) continue;
                var machine = ac.layers[0].stateMachine;
                bool changed = false;
                foreach (var pair in clips)
                {
                    var existing = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == pair.Key);
                    if (existing == null) { machine.AddState(pair.Key).motion = pair.Value; added++; changed = true; }
                    else if (existing.motion != pair.Value) { existing.motion = pair.Value; changed = true; }
                }
                if (changed) EditorUtility.SetDirty(ac);
                log.AppendLine("controller " + AssetDatabase.GetAssetPath(ac));
            }
            if (added > 0) AssetDatabase.SaveAssets();
            log.Insert(0, "OK clips=" + clips.Count + " controllers=" + controllers.Count + " statesAdded=" + added + "\n");
        }
        catch (System.Exception e) { log.Insert(0, "FAIL " + e + "\n"); }
        finally { try { File.WriteAllText(Result, log.ToString()); } catch { } }
    }
}
