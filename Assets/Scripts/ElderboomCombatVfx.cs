using System.Collections.Generic;
using UnityEngine;

public static class ElderboomCombatVfx
{
    static Material material;
    public static Material ParticleMaterial
    {
        get
        {
            if(material != null) return material;
            var texture = new Texture2D(64,64,TextureFormat.RGBA32,false,true);
            var pixels = new Color[64*64];
            for(int y=0;y<64;y++) for(int x=0;x<64;x++)
            {
                float radius = new Vector2((x+.5f)/32-1,(y+.5f)/32-1).magnitude;
                float alpha = Mathf.Pow(Mathf.Clamp01(1-radius),2);
                pixels[y*64+x] = new Color(1,1,1,alpha);
            }
            texture.SetPixels(pixels); texture.Apply(false,true); texture.wrapMode=TextureWrapMode.Clamp;
            material = new Material(Resources.Load<Shader>("Elderboom/Shaders/BloodParticles")) { name="CombatParticleShared", mainTexture=texture };
            return material;
        }
    }
    public static ParticleSystem Particles(GameObject go, Color color, float lifetime, float speed, float size)
    {
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.playOnAwake=false; main.startColor=color; main.startLifetime=lifetime;
        main.startSpeed=speed; main.startSize=new ParticleSystem.MinMaxCurve(size*.4f,size); main.simulationSpace=ParticleSystemSimulationSpace.World;
        var fade=ps.colorOverLifetime; fade.enabled=true;
        var gradient=new Gradient(); gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(.6f,.1f,.01f),1)},
            new[]{new GradientAlphaKey(.9f,0),new GradientAlphaKey(.65f,.35f),new GradientAlphaKey(0,1)}); fade.color=gradient;
        var renderer=ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial=ParticleMaterial;
        renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Color,ParticleSystemVertexStream.UV});
        return ps;
    }
    public static GameObject Burst(Vector3 point, Color color, float size)
    {
        var go=new GameObject("CombatBurst"); go.transform.position=point;
        var ps=Particles(go,color,.45f,size*3f,size*.7f);
        var main=ps.main; main.loop=false; main.maxParticles=32;
        var emission=ps.emission; emission.rateOverTime=0; emission.SetBursts(new[]{new ParticleSystem.Burst(0,24)});
        var shape=ps.shape; shape.shapeType=ParticleSystemShapeType.Sphere; shape.radius=.05f;
        ps.Play(); Object.Destroy(go,1f); return go;
    }
}
