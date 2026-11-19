using UnityEngine;

public static class ElderboomAnimationSampling
{
    public static void Sample(AnimationClip clip,GameObject model,float time)
    {
        if(clip==null || model==null)return;
        // The Player enforces this on the exact binding root. An Animator on a
        // child rig does not satisfy SampleAnimation, although the Editor does.
        if(!clip.legacy && !model.TryGetComponent<Animator>(out _))
        {
            var animator=model.AddComponent<Animator>();
            animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            animator.enabled=false;
        }
        clip.SampleAnimation(model,time);
    }
}
