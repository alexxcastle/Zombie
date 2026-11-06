using UnityEngine;

public sealed class ElderboomStation : MonoBehaviour
{
    public string label,category;
    public string[] weapons;
    public int cost=300;
    HDRPSlicePlayer player;
    string feedback;
    void Start()
    {
        player=FindAnyObjectByType<HDRPSlicePlayer>();
        // Weapon stations sell every weapon of their kind in the catalog except the zombie drops and the knife.
        if(category=="weapons")
        {
            string kind=label.Contains("PISTOL")?ElderboomIds.Category.Handgun:label.Contains("SHOTGUN")?ElderboomIds.Category.Shotgun:
                label.Contains("MELEE")?ElderboomIds.Category.Melee:label.Contains("RIFLE")?ElderboomIds.Category.AssaultRifle:null;
            if(kind!=null)
            {
                var list=new System.Collections.Generic.List<string>();
                foreach(var w in ElderboomWeapons.All)
                    if(w.category==kind && w.id!="knife" && System.Array.IndexOf(ElderboomSpecialPickup.DropIds,w.id)<0)list.Add(w.id);
                if(list.Count>0)weapons=list.ToArray();
            }
            // One ammo station next to the pistols, created once.
            if(label.Contains("PISTOL") && FindObjectsByType<ElderboomStation>().Length>0 && System.Array.Find(FindObjectsByType<ElderboomStation>(),s=>s.category==ElderboomIds.Station.Ammo)==null)
                SpawnAmmoStation();
        }
    }

    void SpawnAmmoStation()
    {
        var size=GetComponent<Renderer>()!=null?GetComponent<Renderer>().bounds.size:Vector3.one;
        foreach(var dir in new[]{transform.right,-transform.right,transform.forward,-transform.forward})
        {
            Vector3 at=transform.position+dir*(Mathf.Max(size.x,size.z)+1.2f);
            if(Physics.CheckBox(at+Vector3.up*(size.y*.5f+.05f),size*.45f,transform.rotation,ElderboomCombat.WorldMask,QueryTriggerInteraction.Ignore))continue;
            var copy=Instantiate(gameObject,at,transform.rotation,transform.parent);
            copy.name="Station_AMMO";
            var station=copy.GetComponent<ElderboomStation>();
            station.label="AMMO";station.category=ElderboomIds.Station.Ammo;station.weapons=new string[0];station.cost=250;
            return;
        }
    }
    void Update()
    {
        if(player==null || Vector3.Distance(player.transform.position,transform.position)>3f || !player.ActionDown("interact"))return;
        TryPurchase(player);
    }
    public bool TryPurchase(HDRPSlicePlayer target)
    {
        var game=ElderboomGame.Active;
        if(cost<0 || target==null || game==null || !game.CanInteract)return false;
        if(game.Credits<cost){game.ShowNotice("NEED "+cost+" CREDITS");return false;}
        var arsenal=target.GetComponent<ElderboomArsenal>();
        if(category==ElderboomIds.Station.Medical && target.Syringes>=3){game.ShowNotice("SYRINGES FULL");return false;}
        if(category==ElderboomIds.Station.Slot && (arsenal==null || arsenal.MaxSlots>=3)){game.ShowNotice("THIRD WEAPON SLOT ALREADY UNLOCKED");return false;}
        if(category==ElderboomIds.Station.Medical)target.AddSyringe();
        else if(category==ElderboomIds.Station.Ammo)
        {
            bool refilled=arsenal!=null?arsenal.RefillAmmo():target.RefillAmmo();
            if(!refilled){game.ShowNotice("AMMO ALREADY FULL");return false;}
        }
        else if(category==ElderboomIds.Station.Slot){arsenal.UnlockThirdSlot();target.AddSpeedPerk(1.5f);}
        else
        {
            if(weapons==null || weapons.Length==0 || arsenal==null)return false;
            var available=System.Array.FindAll(weapons,name=>!arsenal.Contains(name) && ElderboomWeapons.Find(name)!=null);
            if(available.Length==0){game.ShowNotice("YOU ALREADY OWN THESE WEAPONS");return false;}
            // Random pick; if a weapon cannot be built for this operator, try the others.
            for(int i=available.Length-1;i>0;i--){int j=Random.Range(0,i+1);(available[i],available[j])=(available[j],available[i]);}
            bool added=false;
            foreach(var name in available)if(arsenal.Add(ElderboomWeapons.Find(name))){added=true;break;}
            if(!added){game.ShowNotice("WEAPON UNAVAILABLE");return false;}
        }
        game.TrySpend(cost);
        game.ShowNotice(category==ElderboomIds.Station.Slot?"THIRD SLOT + MOVEMENT SPEED UNLOCKED":label+" PURCHASED");
        ElderboomAudio.Play("menu/single_mouse_click",target.transform.position,.6f);return true;
    }
    void OnGUI()
    {
        if(player==null || player.IsDead || player.SettingsOpen || ElderboomGame.Active==null || !ElderboomGame.Active.IsPlaying || Vector3.Distance(player.transform.position,transform.position)>=3f)return;
        GUI.Label(new Rect(Screen.width/2-190,Screen.height*.67f,380,60),player.ActionLabel("interact")+" · "+label+"\n"+cost+" CREDITS",GUI.skin.box);
    }
}
