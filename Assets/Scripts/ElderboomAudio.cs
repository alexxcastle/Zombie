using System.Collections.Generic;
using UnityEngine;

public static class ElderboomAudio
{
    const string Root="Elderboom/Audio/";
    static readonly Dictionary<string,AudioClip> clips=new Dictionary<string,AudioClip>();
    static readonly AudioSource[] voices=new AudioSource[40];
    static readonly AudioSource[] zombieVoices=new AudioSource[10];
    static int next, nextZombie;
    static readonly string[] Ambient={"zombies/zombie_groan_exhausted","zombies/zombie_gas_groans","zombies/zombie_trying_to_talk"};
    static readonly string[] Attack={"zombies/zombie_shout_01","zombies/zombie_shout_02","zombies/zombie_attack_strain"};
    static readonly string[] Spawn={"zombies/zombie_spawn_terrain_close","zombies/zombie_spawn_hearable_close"};
    public static AudioClip Clip(string path)
    {
        if(!clips.TryGetValue(path,out var clip)){clip=Resources.Load<AudioClip>(Root+path);clips[path]=clip;}
        return clip;
    }
    public static float Effects = 1f;
    public static void StopGameplay()
    {
        foreach(var source in voices)if(source!=null)source.Stop();
        foreach(var source in zombieVoices)if(source!=null)source.Stop();
        foreach(var source in tails)if(source!=null)source.Stop();
        System.Array.Clear(tailFades,0,tailFades.Length);
    }
    public static void Play(string path,Vector3 position,float volume=1f,bool spatial=false,float pitch=1f,float time=0f)
        =>PlayClip(Clip(path),position,volume,spatial,pitch,time);
    public static void PlayClip(AudioClip clip,Vector3 position,float volume=1f,bool spatial=false,float pitch=1f,float time=0f)
    {
        if(clip==null)return;
        int index=next++%voices.Length;
        var source=voices[index];
        if(source==null){var go=new GameObject("Elderboom_Audio_Voice_"+index);source=go.AddComponent<AudioSource>();voices[index]=source;}
        source.transform.position=position;source.Stop();source.clip=clip;source.volume=volume*Effects;source.pitch=pitch;source.spatialBlend=spatial?1:0;
        source.rolloffMode=AudioRolloffMode.Linear;source.minDistance=2f;source.maxDistance=48f;source.dopplerLevel=0;
        source.time=Mathf.Clamp(time,0f,Mathf.Max(0f,clip.length-0.02f));
        source.Play();
    }
    public static void Zombie(string kind, ref int last, Vector3 position)
    {
        string[] set; float db, minPitch, maxPitch, maxDistance, unit;
        switch (kind)
        {
            case "attack": set=Attack; db=-6f; minPitch=.94f; maxPitch=1.04f; maxDistance=25f; unit=3.6f; break;
            case "spawn": set=Spawn; db=-12f; minPitch=.96f; maxPitch=1.04f; maxDistance=9f; unit=2.6f; break;
            case "contact": set=new[]{"zombies/zombie-claw-swipe"}; db=-12f; minPitch=.96f; maxPitch=1.04f; maxDistance=13f; unit=2.6f; break;
            case "hit": set=new[]{"zombies/infected-cough"}; db=-15f; minPitch=.97f; maxPitch=1.04f; maxDistance=12f; unit=2.7f; break;
            case "death": set=new[]{"zombies/zombie_death_fall_impact"}; db=-10f; minPitch=.97f; maxPitch=1.03f; maxDistance=20f; unit=3.6f; break;
            default: set=Ambient; db=-8f; minPitch=.94f; maxPitch=1.06f; maxDistance=27f; unit=4.2f; break;
        }
        int index=Random.Range(0,set.Length);
        if(set.Length>1 && index==last) index=(index+Random.Range(1,set.Length))%set.Length;
        last=index;
        var clip=Clip(set[index]); if(clip==null) return;
        int slot=nextZombie++%zombieVoices.Length;
        var source=zombieVoices[slot];
        if(source==null){var go=new GameObject("Elderboom_Zombie_Voice_"+slot); Object.DontDestroyOnLoad(go); source=go.AddComponent<AudioSource>(); zombieVoices[slot]=source;}
        source.transform.position=position; source.Stop(); source.clip=clip;
        source.volume=Mathf.Pow(10f, db/20f)*Effects; source.pitch=Random.Range(minPitch,maxPitch); source.spatialBlend=1f;
        source.rolloffMode=AudioRolloffMode.Logarithmic; source.minDistance=unit; source.maxDistance=maxDistance; source.dopplerLevel=0f;
        source.Play();
    }

    static int aimVariant = 2, equipVariant = 2;
    static float lastAim = -1f, lastEquip = -1f, lastDry = -1f;
    static readonly AudioSource[] tails = new AudioSource[4];
    static readonly float[] tailFades = new float[4];
    static int nextTail;
    static Host host;

    public static void Weapon(string category,string stage,Vector3 position)
    {
        Play(ClipPath(category, stage), position, Db(-3f), false, Random.Range(.985f, 1.015f));
    }

    public static void FireWeapon(string category, Vector3 position)
    {
        float pitch = Random.Range(0.985f, 1.015f);
        Play(ClipPath(category, "gunshot_01"), position, Db(-1.5f), false, pitch);
        var clip = Clip(ClipPath(category, "tail_01"));
        if (clip == null) return;
        int slot = NextTail();
        for (int i = 0; i < tails.Length; i++)
            if (i != slot && tails[i] != null && tails[i].isPlaying) tailFades[i] = 0.075f;
        var voice = tails[slot];
        voice.Stop();
        voice.clip = clip;
        voice.pitch = pitch;
        voice.volume = Db(-3f) * Effects;
        voice.spatialBlend = 0f;
        voice.Play();
        tailFades[slot] = 0f;
        EnsureHost();
    }

    public static void DryFire(string category, Vector3 position)
    {
        if (Time.unscaledTime - lastDry < 0.12f) return;
        lastDry = Time.unscaledTime;
        string folder = Folder(category);
        Play("operator_weapons/" + folder + "/dry_click", position, Db(-5f));
    }

    public static void AimFoley(string category, Vector3 position)
    {
        if (Time.unscaledTime - lastAim < 0.11f || Time.unscaledTime - lastEquip < 0.18f) return;
        lastAim = Time.unscaledTime;
        Foley(category, "aim", ref aimVariant, position);
    }

    public static void EquipFoley(string category, Vector3 position)
    {
        if (Time.unscaledTime - lastEquip < 0.18f) return;
        lastEquip = Time.unscaledTime;
        Foley(category, "equip", ref equipVariant, position);
    }

    public static void ReloadCue(string category, string stage, Vector3 position)
    {
        Play(ClipPath(category, ReloadFile(category, stage)), position, Db(-3f), false, Random.Range(0.99f, 1.01f));
    }

    static void Foley(string category, string kind, ref int variant, Vector3 position)
    {
        variant = variant == 1 ? 2 : 1;
        Play(ClipPath(category, "foley_" + kind + "_0" + variant), position, Db(-5f), false, Random.Range(0.99f, 1.01f));
    }

    static string ReloadFile(string category, string stage)
    {
        if (category == ElderboomIds.Category.Shotgun)
        {
            switch (stage)
            {
                case "bolt": return "reload_01_charge";
                case "open": return "reload_02_pumpBack";
                case "shell_draw": return "reload_03_shellDraw";
                case "shell_in": return "reload_04_chambering";
                default: return "reload_05_pumpForward";
            }
        }
        if (category == ElderboomIds.Category.Handgun)
        {
            switch (stage)
            {
                case "open": return "reload_01_slideLock";
                case "mag_out": return "reload_02_magOut";
                case "mag_draw": return "reload_03_magDraw";
                case "mag_in": return "reload_04_magIn";
                default: return "reload_05_charge";
            }
        }
        switch (stage)
        {
            case "mag_out": return "reload_01_magOut";
            case "mag_draw": return "reload_02_magDraw";
            case "mag_in": return "reload_03_magIn";
            default: return "reload_04_charge";
        }
    }

    static string Folder(string category) => category == ElderboomIds.Category.Shotgun ? "shotgun" : category == ElderboomIds.Category.Handgun ? "handgun" : "assaultrifle";
    static string Prefix(string category) => category == ElderboomIds.Category.Shotgun ? "sgn" : category == ElderboomIds.Category.Handgun ? "hgn" : "ar";
    static string ClipPath(string category, string stage) => "operator_weapons/" + Folder(category) + "/sfx_wpn_" + Prefix(category) + "_" + stage;
    static float Db(float db) => Mathf.Pow(10f, db / 20f);

    static int NextTail()
    {
        EnsureHost();
        int slot = nextTail++ % tails.Length;
        if (tails[slot] != null) return slot;
        var go = new GameObject("Elderboom_Weapon_Tail_" + slot);
        Object.DontDestroyOnLoad(go);
        tails[slot] = go.AddComponent<AudioSource>();
        tails[slot].playOnAwake = false;
        tails[slot].dopplerLevel = 0f;
        return slot;
    }

    static void EnsureHost()
    {
        if (host != null) return;
        var go = new GameObject("Elderboom_Weapon_Audio");
        Object.DontDestroyOnLoad(go);
        host = go.AddComponent<Host>();
    }

    sealed class Host : MonoBehaviour
    {
        void Update()
        {
            for (int i = 0; i < tails.Length; i++)
            {
                if (tailFades[i] <= 0f || tails[i] == null || !tails[i].isPlaying) continue;
                tailFades[i] -= Time.unscaledDeltaTime;
                tails[i].volume = Mathf.Max(0f, Db(-3f) * Effects * (tailFades[i] / 0.075f));
                if (tailFades[i] <= 0f) tails[i].Stop();
            }
        }
    }
}
