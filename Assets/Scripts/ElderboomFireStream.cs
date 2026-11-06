using UnityEngine;

public sealed class ElderboomFireStream : MonoBehaviour
{
    HDRPSlicePlayer owner;
    Transform muzzle;
    ParticleSystem particles;
    float until;
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
        }
        stream.muzzle=source; stream.until=Time.time+Mathf.Max(.1f,interval*1.5f);
        if(!stream.particles.isPlaying)stream.particles.Play();
    }
    void LateUpdate()
    {
        if(owner == null || owner.IsDead || muzzle == null || !muzzle.gameObject.activeInHierarchy || owner.Reloading)
        { Destroy(gameObject); return; }
        transform.position=muzzle.position;
        transform.rotation=Quaternion.LookRotation(owner.ViewCamera.transform.forward);
        if(Time.time>until)
        {
            particles.Stop(true,ParticleSystemStopBehavior.StopEmitting);
            if(!particles.IsAlive())Destroy(gameObject);
        }
    }
}
