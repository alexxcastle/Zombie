using UnityEngine;

// The flamethrower's flame: particles plus its own sound (ignition, continuous roar while the trigger is held,
// a sputter when released) instead of a gunshot per pulse.
public sealed class ElderboomFireStream : MonoBehaviour
{
    const string AudioRoot = "weapons/flamethrower/";
    HDRPSlicePlayer owner;
    Transform muzzle;
    ParticleSystem particles;
    AudioSource roar;
    float until;
    bool emitting;

    public static void Pulse(HDRPSlicePlayer player,Transform source,float range,float interval)
    {
        var stream=player.GetComponentInChildren<ElderboomFireStream>(true);
        if(stream == null)
        {
            var go=new GameObject("FlamethrowerStream"); go.transform.SetParent(player.transform,false);
            stream=go.AddComponent<ElderboomFireStream>(); stream.owner=player;
            stream.particles=ElderboomCombatVfx.Particles(go,new Color(1,.65f,.1f),range/18f,18f,.48f);
            var main=stream.particles.main; main.loop=true; main.maxParticles=100;
            var emission=stream.particles.emission; emission.rateOverTime=65;
            var shape=stream.particles.shape; shape.shapeType=ParticleSystemShapeType.Cone; shape.angle=7; shape.radius=.025f;
            var clip=ElderboomAudio.Clip(AudioRoot+"flamethrower_loop");
            if(clip!=null)
            {
                stream.roar=go.AddComponent<AudioSource>();
                stream.roar.clip=clip; stream.roar.loop=true; stream.roar.playOnAwake=false; stream.roar.spatialBlend=0f; stream.roar.dopplerLevel=0f;
            }
        }
        stream.muzzle=source; stream.until=Time.time+Mathf.Max(.1f,interval*1.5f);
        if(!stream.emitting) stream.Ignite();
    }

    void Ignite()
    {
        emitting=true;
        if(!particles.isPlaying)particles.Play();
        ElderboomAudio.Play(AudioRoot+"flamethrower_start",transform.position,.75f,false,Random.Range(.96f,1.04f));
        if(roar!=null)
        {
            roar.volume=0f; roar.pitch=Random.Range(.97f,1.03f);
            roar.time=Random.Range(0f,roar.clip.length*.9f);
            roar.PlayDelayed(.06f);
        }
    }

    void Extinguish()
    {
        if(!emitting)return;
        emitting=false;
        particles.Stop(true,ParticleSystemStopBehavior.StopEmitting);
        ElderboomAudio.Play(AudioRoot+"flamethrower_end",transform.position,.6f,false,Random.Range(.96f,1.04f));
    }

    void LateUpdate()
    {
        if(owner == null || owner.IsDead || muzzle == null || !muzzle.gameObject.activeInHierarchy || owner.Reloading)
        { Extinguish(); Destroy(gameObject); return; }
        transform.position=muzzle.position;
        transform.rotation=Quaternion.LookRotation(owner.ViewCamera.transform.forward);
        if(roar!=null)
        {
            float target=emitting?.8f*ElderboomAudio.Effects:0f;
            roar.volume=Mathf.MoveTowards(roar.volume,target,Time.deltaTime*(emitting?9f:6f));
            if(!emitting && roar.volume<=0f && roar.isPlaying)roar.Stop();
        }
        if(Time.time>until)
        {
            Extinguish();
            if(!particles.IsAlive() && (roar==null || !roar.isPlaying))Destroy(gameObject);
        }
    }
}
