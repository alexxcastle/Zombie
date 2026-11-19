using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// A manual PlayableGraph that blends a fixed set of clips on one model.
// The caller sets each clip's time (keeping the calibrated time remaps) and the
// active input; weights cross-fade toward it, then the graph is evaluated once.
// Returns null from Create when a clip is legacy (Playables cannot play those):
// callers then fall back to AnimationClip.SampleAnimation.
public sealed class ElderboomClipMixer : IDisposable
{
    PlayableGraph graph;
    AnimationMixerPlayable mixer;
    AnimationClipPlayable[] inputs;
    float[] weights;
    int active = -1;

    public bool Valid => graph.IsValid();

    public static ElderboomClipMixer Create(GameObject model, string name, params AnimationClip[] clips)
    {
        if (model == null || clips == null || clips.Length == 0) return null;
        bool any = false;
        foreach (var clip in clips)
        {
            if (clip == null) continue;
            if (clip.legacy) return null;
            any = true;
        }
        if (!any) return null;

        if (!model.TryGetComponent<Animator>(out var animator)) animator = model.AddComponent<Animator>();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        // Clips were sampled directly before; an imported controller would fight the graph.
        animator.runtimeAnimatorController = null;
        animator.enabled = true; // a PlayableOutput only writes through an enabled Animator

        var result = new ElderboomClipMixer();
        result.graph = PlayableGraph.Create(name);
        result.graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var output = AnimationPlayableOutput.Create(result.graph, "Viewmodel", animator);
        result.mixer = AnimationMixerPlayable.Create(result.graph, clips.Length);
        result.inputs = new AnimationClipPlayable[clips.Length];
        result.weights = new float[clips.Length];
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] == null) continue;
            var playable = AnimationClipPlayable.Create(result.graph, clips[i]);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            result.graph.Connect(playable, 0, result.mixer, i);
            result.inputs[i] = playable;
        }
        output.SetSourcePlayable(result.mixer);
        result.graph.Play();
        return result;
    }

    public bool Has(int input) => input >= 0 && input < inputs.Length && inputs[input].IsValid();

    public void SetTime(int input, float time)
    {
        if (Has(input)) inputs[input].SetTime(time);
    }

    // Instantly shows one input, with no blend (equip, cancel, reset).
    public void Snap(int input)
    {
        for (int i = 0; i < weights.Length; i++) weights[i] = i == input ? 1f : 0f;
        active = input;
        Apply();
    }

    // Cross-fades toward the given input over fadeSeconds, then evaluates the pose.
    public void Evaluate(int input, float fadeSeconds, float deltaTime)
    {
        if (!Valid) return;
        if (active < 0) Snap(input);
        active = input;
        float step = fadeSeconds <= 0f ? 1f : deltaTime / fadeSeconds;
        float total = 0f;
        for (int i = 0; i < weights.Length; i++)
        {
            weights[i] = Has(i) ? Mathf.MoveTowards(weights[i], i == input ? 1f : 0f, step) : 0f;
            total += weights[i];
        }
        if (total <= 0.0001f) { Snap(input); return; }
        for (int i = 0; i < weights.Length; i++) weights[i] /= total;
        Apply();
    }

    void Apply()
    {
        for (int i = 0; i < weights.Length; i++) mixer.SetInputWeight(i, weights[i]);
        graph.Evaluate(0f);
    }

    public void Dispose()
    {
        if (graph.IsValid()) graph.Destroy();
    }
}
