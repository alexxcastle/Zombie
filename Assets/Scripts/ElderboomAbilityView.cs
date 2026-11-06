using UnityEngine;

public sealed class ElderboomAbilityView : MonoBehaviour
{
    public GameObject model;
    public AnimationClip clip;
    public Renderer[] leds;
    public Light glow;
    public float Length => clip != null ? Mathf.Max(0.2f, clip.length) : 1.32f;

    ElderboomClipMixer mixer;
    bool mixerTried;

    // Scrubbed by ElderboomTacticalArms; evaluated through a manual PlayableGraph,
    // or SampleAnimation for legacy clips.
    public void Sample(float time)
    {
        if (clip == null || model == null) return;
        time = Mathf.Clamp(time,0f,Mathf.Max(.01f,clip.length-.001f));
        if (!mixerTried) { mixerTried = true; mixer = ElderboomClipMixer.Create(model, name + "_Ability", clip); }
        if (mixer != null && mixer.Valid) { mixer.SetTime(0, time); mixer.Snap(0); }
        else ElderboomAnimationSampling.Sample(clip,model,time);
    }

    void OnDestroy() { mixer?.Dispose(); mixer = null; }

    public void SetCharge(float energy)
    {
        if (leds != null && leds.Length > 0)
        {
            var block = new MaterialPropertyBlock();
            Color color = new Color(0.04f, 0.72f, 1f) * Mathf.Max(0f, energy);
            foreach (var led in leds)
            {
                if (led == null) continue;
                led.GetPropertyBlock(block);
                block.SetColor("_EmissiveColor", color);
                block.SetColor("_EmissionColor", color);
                led.SetPropertyBlock(block);
            }
        }
        if (glow != null)
        {
            glow.intensity = Mathf.Max(0f, energy) * 800f;
            glow.enabled = energy > 0.02f;
        }
    }
}
