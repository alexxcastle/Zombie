using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Logs/claude_armsdiag.txt -> compares each bone's rest transform in the arms prefabs with the clips (frame 0),
// and the skinned meshes' bind poses with the bones' rest. Report: Logs/claude_armsdiag_result.txt.
static class ElderboomBasicArmsDiag
{
    const string Flag = "Logs/claude_armsdiag.txt";
    static double next;
    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 1.5;
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        File.Delete(Flag);
        var sb = new StringBuilder();
        try { Run(sb); } catch (System.Exception e) { sb.AppendLine("EXCEPTION " + e); }
        File.WriteAllText("Logs/claude_armsdiag_result.txt", sb.ToString());
    }

    static void Run(StringBuilder sb)
    {
        var clip = AssetDatabase.LoadAllAssetsAtPath("Assets/GameAssets/BasicFPSAnimations/AR10/AR10_Idle.fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
        // the 24 axis-aligned rotations
        var cands = new List<Quaternion>();
        var axes = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        foreach (var f in axes) foreach (var u in axes) if (Mathf.Abs(Vector3.Dot(f, u)) < .5f) cands.Add(Quaternion.LookRotation(f, u));
        foreach (var op in new[] { "Tactical", "Eli", "Soph" })
        {
            var prefab = Resources.Load<GameObject>("Elderboom/Grips/" + op + "_Grips");
            var inst = Object.Instantiate(prefab);
            try
            {
                var arm = inst.GetComponentsInChildren<Transform>(true).First(t => t.name == "root").parent;
                var rest = arm.GetComponentsInChildren<Transform>(true).ToDictionary(t => t, t => t.localPosition);
                clip.SampleAnimation(arm.gameObject, 0f);
                var pairs = new List<(Transform t, Vector3 g, Vector3 f)>();
                foreach (var t in arm.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("DEF-") && t.parent != null && t.parent.name.StartsWith("DEF-") && rest[t].magnitude > .005f) pairs.Add((t, rest[t], t.localPosition));
                // child offsets: clip = D^-1 * rest  (D the same for every bone)
                float best = 1e9f; Quaternion bd = Quaternion.identity;
                foreach (var d in cands)
                {
                    float e = pairs.Sum(p => (Quaternion.Inverse(d) * p.g - p.f).magnitude);
                    if (e < best) { best = e; bd = d; }
                }
                float worst = pairs.Max(p => (Quaternion.Inverse(bd) * p.g - p.f).magnitude);
                sb.AppendLine(op + ": child offsets " + pairs.Count + ", best uniform D euler " + bd.eulerAngles.ToString("F0") + " q " + bd.ToString("F4") + " worst residual " + worst.ToString("F5") + " m");
                foreach (var p in pairs.OrderByDescending(p => (Quaternion.Inverse(bd) * p.g - p.f).magnitude).Take(4))
                    sb.AppendLine("   " + p.t.name + " rest " + p.g.ToString("F4") + " clip " + p.f.ToString("F4"));
                foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    sb.AppendLine("  mesh " + smr.name + " bones: " + string.Join(",", smr.bones.Where(b => b != null && !b.name.StartsWith("DEF-")).Select(b => b.name)) + " (non-DEF), rootBone " + (smr.rootBone ? smr.rootBone.name : "-"));
            }
            finally { Object.DestroyImmediate(inst); }
        }
    }
}
