using System.Linq;
using UnityEngine;

public sealed class ElderboomSpecialPickup : MonoBehaviour
{
    ElderboomWeaponDefinition definition;
    HDRPSlicePlayer player;
    float expires;
    Vector3 ground;
    Transform visual;
    bool collected;
    static Material glow;
    public const float DropChance=.03f;
    // Special weapons only zombies drop (never sold at the stations).
    public static readonly string[] DropIds={"grenade","minigun","flame"};
    public static ElderboomSpecialPickup TrySpawn(Vector3 point)
    {
        if(Random.value>=DropChance)return null;
        return Spawn(point,ElderboomWeapons.Find(DropIds[Random.Range(0,DropIds.Length)]));
    }
    public static ElderboomSpecialPickup Spawn(Vector3 point,ElderboomWeaponDefinition weapon)
    {
        if(weapon==null || Resources.Load<GameObject>(weapon.resource)==null)return null;
        var go=new GameObject("SpecialDrop_"+weapon.id);go.transform.position=point;
        var pickup=go.AddComponent<ElderboomSpecialPickup>();pickup.definition=weapon;
        pickup.ground=point;pickup.expires=Time.time+30f;pickup.BuildVisual();return pickup;
    }
    // LAN: see ElderboomPowerUp (host lists drops in its snapshots, clients ask to collect).
    public static readonly System.Collections.Generic.List<ElderboomSpecialPickup> All=new System.Collections.Generic.List<ElderboomSpecialPickup>();
    public int NetId;
    public bool NetProxy;
    public ElderboomWeaponDefinition Definition=>definition;
    public Vector3 Ground=>ground;
    void OnEnable(){All.Add(this);}
    void OnDisable(){All.Remove(this);}
    void Start(){player=FindAnyObjectByType<HDRPSlicePlayer>();}
    void Update()
    {
        if(Time.time>=expires && !NetProxy){Destroy(gameObject);return;}
        transform.position=ground+Vector3.up*(.5f+Mathf.Sin(Time.time*3)*.1f);
        if(visual!=null)visual.Rotate(Vector3.up,Time.deltaTime*46,Space.World);
        if(player!=null && (player.transform.position-ground).sqrMagnitude<1.5f*1.5f)TryCollect(player);
    }
    public bool TryCollect(HDRPSlicePlayer target)
    {
        if(collected || target==null || target.IsDead)return false;
        if(NetProxy){collected=true;ElderboomLan.Collect(NetId);gameObject.SetActive(false);Destroy(gameObject);return true;}
        var arsenal=target.GetComponent<ElderboomArsenal>();
        if(arsenal==null || !arsenal.ReceiveTemporary(definition,30))return false;
        collected=true;gameObject.SetActive(false);Destroy(gameObject);return true;
    }
    void BuildVisual()
    {
        if(glow==null)
        {
            glow=new Material(Shader.Find("HDRP/Lit"));glow.SetColor("_BaseColor",new Color(.05f,.65f,.12f));
            glow.SetColor("_EmissiveColor",new Color(.08f,2f,.16f));
        }
        var prefab=Resources.Load<GameObject>(definition.resource);
        var model=Instantiate(prefab,transform,false);model.SetActive(true);visual=model.transform;
        foreach(var sampler in model.GetComponentsInChildren<ElderboomViewmodel>(true))
        {sampler.ResetToIdle();sampler.enabled=false;}
        foreach(var animator in model.GetComponentsInChildren<Animator>(true))animator.enabled=false;
        foreach(var light in model.GetComponentsInChildren<Light>(true))light.enabled=false;
        foreach(var camera in model.GetComponentsInChildren<Camera>(true))camera.enabled=false;
        var renderers=model.GetComponentsInChildren<Renderer>(true);
        foreach(var renderer in renderers)
        {
            string name=renderer.name.ToLowerInvariant();
            renderer.enabled=!name.Contains("arm")&&!name.Contains("hand")&&!name.Contains("body")&&!name.Contains("feet")&&!name.Contains("head");
            renderer.sharedMaterials=Enumerable.Repeat(glow,renderer.sharedMaterials.Length).ToArray();
            renderer.gameObject.layer=0;
        }
        var visible=renderers.Where(r=>r.enabled).ToArray();
        if(visible.Length>0)
        {
            var bounds=visible[0].bounds;foreach(var renderer in visible)bounds.Encapsulate(renderer.bounds);
            float scale=.75f/Mathf.Max(.01f,Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z)));
            model.transform.localScale*=scale;
            model.transform.localPosition-=(bounds.center-transform.position)*scale;
        }
        else
        {
            model.SetActive(false);Destroy(model);
            var beacon=GameObject.CreatePrimitive(PrimitiveType.Cube);beacon.transform.SetParent(transform,false);
            beacon.transform.localScale=new Vector3(.35f,.16f,.24f);beacon.GetComponent<Renderer>().sharedMaterial=glow;
            Destroy(beacon.GetComponent<Collider>());visual=beacon.transform;
        }
    }
    void OnGUI()
    {
        if(player==null || player.SettingsOpen || player.IsDead || (player.transform.position-ground).sqrMagnitude>16)return;
        var camera=player.ViewCamera;var screen=camera.WorldToScreenPoint(transform.position);
        if(screen.z<=0)return;
        GUI.Label(new Rect(screen.x-170,Screen.height-screen.y-38,340,30),definition.name+" · 30s",GUI.skin.box);
    }
}
