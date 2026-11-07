using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class ElderboomArsenal : MonoBehaviour
{
    sealed class Slot
    {
        public ElderboomWeaponDefinition definition;
        public Transform root;
        public int ammo,reserve;
        public bool owned;
    }
    readonly List<Slot> slots=new List<Slot>();
    HDRPSlicePlayer player;
    Transform operatorRoot;
    Slot active,knife,temporary;
    int slotLimit=2, selectedRegular, temporaryReturn;
    bool showingAbility;
    float temporaryUntil;
    public int Count => slots.Count;
    public int MaxSlots => slotLimit;
    public bool HasTemporary => temporary!=null;
    public bool TemporarySelected => active!=null && active==temporary;
    public float TemporaryRemaining => HasTemporary?Mathf.Max(0,temporaryUntil-Time.time):0;
    public bool CanSwitch => !WeaponDeposited && player!=null && !player.SettingsOpen && !player.IsDead && !player.Reloading && !showingAbility
        && !(ElderboomGame.Active!=null && ElderboomGame.Active.AbilityBusy)
        && !(player.GetComponent<ElderboomTacticalArms>() is ElderboomTacticalArms arms && arms.Playing);

    public void Initialize(string operatorId)
    {
        CancelAltarDeposit();
        upgradedSlots.Clear();
        player=GetComponent<HDRPSlicePlayer>();
        Transform primary=player.ActiveWeaponRoot;
        CancelOperatorAbility();
        foreach(var slot in slots)Dispose(slot);
        Dispose(knife);Dispose(temporary);slots.Clear();knife=temporary=active=null;
        // Starting weapons and slot count come from the operator definition.
        var op=ElderboomOperators.Find(operatorId)??ElderboomOperators.All[0];
        operatorRoot=primary;selectedRegular=0;slotLimit=Mathf.Clamp(op.weaponSlots,2,3);
        operatorId=op.id;
        var main=ElderboomWeapons.Find(op.primaryWeapon);
        // Every match starts with the same pistol and nothing else; the rest comes from the weapon stations.
        var start=string.IsNullOrEmpty(StartingWeapon)?null:Create(ElderboomWeapons.Find(StartingWeapon));
        if(start!=null)
        {
            if(primary!=null)primary.gameObject.SetActive(false);
            slots.Add(start);
        }
        else
        {
            slots.Add(new Slot{definition=main,root=primary,ammo=main.capacity,reserve=main.reserve});
            slots.Add(Create(ElderboomWeapons.Find(op.secondaryWeapon)));
            if(!string.IsNullOrEmpty(op.extraWeapon))slots.Add(Create(ElderboomWeapons.Find(op.extraWeapon)));
        }
        knife=Create(ElderboomWeapons.Find("knife"));
        EquipUnchecked(slots[0]);
    }
    void Update()
    {
        if(temporary!=null && Time.time>=temporaryUntil && CanSwitch)ExpireTemporary();
        if(!CanSwitch)return;
        int total=slots.Count+(knife!=null?1:0)+(temporary!=null?1:0);
        if(total<2)return;
        float wheel=Input.mouseScrollDelta.y;
        if(wheel==0)return;
        int current=slots.IndexOf(active);
        if(current<0)current=active==knife?slots.Count:total-1;
        int next=(current+(wheel>0?1:total-1))%total;
        EquipUnchecked(next<slots.Count?slots[next]:next==slots.Count && knife!=null?knife:temporary);
    }
    // Starting loadout for every operator (empty = the operator's own weapons, as before).
    public static string StartingWeapon="rhino";
    string operatorId;
    Slot Create(ElderboomWeaponDefinition definition)
    {
        if(definition==null)return null;
        // The operator's own primary is its own viewmodel (as in the old starting loadout), bought at a station.
        var self=ElderboomOperators.Find(operatorId);
        if(self!=null && operatorRoot!=null && string.Equals(definition.id,self.primaryWeapon,StringComparison.OrdinalIgnoreCase))
            return new Slot{definition=definition,root=operatorRoot,ammo=definition.capacity,reserve=definition.reserve,owned=false};
        var prefab=string.IsNullOrEmpty(definition.resource)?null:Resources.Load<GameObject>(definition.resource);
        // Another operator's primary has no prefab: only an authored grip can show it.
        if(prefab==null && !definition.IsOperatorPrimary)return null;
        // Every weapon is held in the operator's own right hand; the pack's arms are
        // only a fallback when the operator rig cannot be resolved.
        var op=ElderboomOperators.Find(player.OperatorDisplayName);
        var root=operatorRoot!=null?ElderboomAuthoredGrip.Build(operatorRoot,op,definition,player.ViewmodelParent):null;
        if(root==null && UseOperatorGrip && operatorRoot!=null)root=ElderboomOperatorGrip.Build(operatorRoot,op,definition,player.ViewmodelParent);
        if(root==null && prefab==null)return null;
        if(root==null){root=Instantiate(prefab,player.ViewmodelParent,false).transform;root.gameObject.SetActive(false);}
        return new Slot{definition=definition,root=root,ammo=definition.capacity,reserve=definition.reserve,owned=true};
    }
    // Runtime IK grip (fallback when no authored grip exists) is parked: not good enough.
    public static bool UseOperatorGrip=false;
    void Dispose(Slot slot)
    {
        if (slot != null) upgradedSlots.Remove(slot);
        if(slot?.root==null)return;
        slot.root.gameObject.SetActive(false);
        if(slot.owned)Destroy(slot.root.gameObject);
    }
    void SaveActive()
    {
        if(active==null)return;
        active.ammo=player.Ammo;active.reserve=player.ReserveAmmo;
    }
    public bool Contains(string id) => slots.Exists(s=>s!=null && Match(s,id));
    static bool Match(Slot s,string id)=>string.Equals(s.definition.id,id,StringComparison.OrdinalIgnoreCase)||string.Equals(s.definition.name,id,StringComparison.OrdinalIgnoreCase);
    public bool Add(ElderboomWeaponDefinition definition,bool equip=true)
    {
        if(definition==null || player==null || !CanSwitch || Contains(definition.id) || definition.id=="knife")return false;
        var next=Create(definition);if(next==null)return false;
        SaveActive();
        bool replaceActive=false;
        if(slots.Count<slotLimit)slots.Add(next);
        else
        {
            // Holding the knife or a temporary weapon replaces the last selected
            // regular slot, not the operator primary in slot 0.
            int index=slots.IndexOf(active);if(index<0)index=Mathf.Clamp(selectedRegular,0,slots.Count-1);
            var old=slots[index];replaceActive=active==old;
            slots[index]=next;if(replaceActive)active=null;Dispose(old);
        }
        if(equip || replaceActive)EquipUnchecked(next);
        return true;
    }
    public bool Equip(string name)
    {
        if(!CanSwitch)return false;
        var next=slots.Find(s=>Match(s,name));
        if(next==null && knife!=null && Match(knife,name))next=knife;
        if(next==null && temporary!=null && Match(temporary,name))next=temporary;
        if(next==null)return false;
        EquipUnchecked(next);return true;
    }
    void EquipUnchecked(Slot next)
    {
        if(next?.root==null)return;
        SaveActive();
        foreach(var slot in slots)if(slot?.root!=null)slot.root.gameObject.SetActive(false);
        if(knife?.root!=null)knife.root.gameObject.SetActive(false);
        if(temporary?.root!=null)temporary.root.gameObject.SetActive(false);
        if(operatorRoot!=null)operatorRoot.gameObject.SetActive(false);
        active=next;int index=slots.IndexOf(next);if(index>=0)selectedRegular=index;
        next.root.gameObject.SetActive(true);next.root.GetComponent<ElderboomViewmodel>()?.ResetToIdle();
        player.BindWeaponRoot(next.root);player.ConfigureWeapon(next.definition,next.ammo,next.reserve);
        ElderboomUpgradedWeapon.Apply(next.root, next.definition, player.OperatorDisplayName, IsUpgraded(next));
        ElderboomAudio.EquipFoley(next.definition.category,player.transform.position);
    }
    // Refills every regular slot to its catalog reserve. Melee and the temporary
    // special weapon are excluded. Returns false when every reserve is already full.
    public bool RefillAmmo()
    {
        if(player==null)return false;
        SaveActive();
        bool changed=false;
        foreach(var slot in slots)changed|=Refill(slot);
        if(changed && active!=null && active!=temporary && active!=knife)player.SetReserveAmmo(active.reserve);
        return changed;
    }
    static bool Refill(Slot slot)
    {
        if(slot?.definition==null || slot.definition.FireMode==ElderboomFireMode.Melee || slot.reserve>=slot.definition.reserve)return false;
        slot.reserve=slot.definition.reserve;return true;
    }
    public bool UnlockThirdSlot()
    {
        if(slotLimit>=3)return false;
        slotLimit=3;ElderboomGame.Active?.ShowNotice("THIRD WEAPON SLOT UNLOCKED");return true;
    }
    public bool ReceiveTemporary(ElderboomWeaponDefinition definition,float duration=30f)
    {
        if(!CanSwitch || definition==null)return false;
        var next=Create(definition);if(next==null)return false;
        SaveActive();
        if(temporary==null)temporaryReturn=selectedRegular;
        bool wasTemporary=active==temporary && temporary!=null;
        Dispose(temporary);if(wasTemporary)active=null;
        temporary=next;temporaryUntil=Time.time+Mathf.Max(.01f,duration);
        EquipUnchecked(temporary);
        ElderboomGame.Active?.ShowNotice("TEMPORARY "+definition.name+" · "+Mathf.CeilToInt(duration)+" SECONDS");return true;
    }
    public void ExpireTemporary()
    {
        if(temporary==null)return;
        var old=temporary;
        if(active==old)
        {
            player.CancelGameplayActions();active=null;temporary=null;
            EquipUnchecked(slots[Mathf.Clamp(temporaryReturn,0,slots.Count-1)]);
        }
        else temporary=null;
        Dispose(old);ElderboomGame.Active?.ShowNotice("SPECIAL WEAPON EXPIRED");
    }
    // True when the ability plays on the left arm only over the weapon in hand (the caller then must not play the
    // whole-arms ability clip on the current viewmodel).
    public bool ShowOperatorAbility(float duration)
    {
        if(active?.root==null || operatorRoot==null || active.root==operatorRoot || showingAbility)return false;
        var view=operatorRoot.GetComponent<ElderboomViewmodel>();if(view==null || view.ability==null)return false;
        // Any weapon held by the operator's own arms: the ability plays on the arms, the weapon stays in hand.
        if(PlayAbilityOverWeapon(duration,view.operatorId==ElderboomIds.Eli))return true; // Eli: acoustic device on the right forearm, both arms
        showingAbility=true;active.root.gameObject.SetActive(false);operatorRoot.gameObject.SetActive(true);
        player.BindWeaponRoot(operatorRoot);StartCoroutine(RestoreWeapon(duration));
        return false;
    }
    // The weapon in hand is held by the operator's own arms (authored grips): play the operator's ability (its own
    // viewmodel's ability clip, hidden) on the left arm, or on both arms when the ability uses the right one too.
    public bool PlayAbilityOverWeapon(float duration,bool bothArms)
    {
        if(active?.root==null || operatorRoot==null || active.root==operatorRoot || showingAbility)return false;
        var view=operatorRoot.GetComponent<ElderboomViewmodel>();if(view==null || view.ability==null)return false;
        var current=active.root.GetComponent<ElderboomViewmodel>();
        if(current==null || current.model==null)return false; // any weapon whose model carries an arm rig (authored grips or a full FPS model); Bind checks the bones
        var left=active.root.GetComponent<ElderboomLeftArmAbility>();if(left==null)left=active.root.gameObject.AddComponent<ElderboomLeftArmAbility>();
        if(left.Bind(operatorRoot,bothArms)<8)return false;
        showingAbility=true;leftArmAbility=left;
        operatorRoot.localPosition=active.root.localPosition;operatorRoot.localRotation=active.root.localRotation;
        operatorRoot.gameObject.SetActive(true);HideOperatorRoot(true);
        view.PlayAbility();left.Begin(duration);
        StartCoroutine(RestoreWeapon(duration));
        return true;
    }
    ElderboomLeftArmAbility leftArmAbility;
    // The ability took the weapon out of the right hand (old whole-viewmodel path): no firing until it ends.
    public bool AbilityHidesWeapon=>showingAbility && leftArmAbility==null;
    readonly System.Collections.Generic.List<Renderer> hiddenRenderers=new System.Collections.Generic.List<Renderer>();
    readonly System.Collections.Generic.List<Light> hiddenLights=new System.Collections.Generic.List<Light>();
    // The operator's own viewmodel animates the ability unseen: it is only the source of the left arm's pose.
    void HideOperatorRoot(bool hide)
    {
        if(hide)
        {
            hiddenRenderers.Clear();hiddenLights.Clear();
            foreach(var r in operatorRoot.GetComponentsInChildren<Renderer>(true))if(!r.forceRenderingOff){r.forceRenderingOff=true;hiddenRenderers.Add(r);}
            foreach(var l in operatorRoot.GetComponentsInChildren<Light>(true))if(l.enabled){l.enabled=false;hiddenLights.Add(l);}
        }
        else
        {
            foreach(var r in hiddenRenderers)if(r!=null)r.forceRenderingOff=false;
            foreach(var l in hiddenLights)if(l!=null)l.enabled=true;
            hiddenRenderers.Clear();hiddenLights.Clear();
        }
    }
    public void CancelOperatorAbility()
    {
        StopAllCoroutines();
        if(!showingAbility)return;
        showingAbility=false;
        if(leftArmAbility!=null)
        {
            leftArmAbility.Stop();leftArmAbility=null;
            if(operatorRoot!=null){operatorRoot.GetComponent<ElderboomViewmodel>()?.ResetToIdle();HideOperatorRoot(false);operatorRoot.gameObject.SetActive(false);}
            return;
        }
        if(operatorRoot!=null){operatorRoot.GetComponent<ElderboomViewmodel>()?.ResetToIdle();operatorRoot.gameObject.SetActive(false);}
        if(active?.root!=null)
        {
            active.root.gameObject.SetActive(true);active.root.GetComponent<ElderboomViewmodel>()?.ResetToIdle();
            player.BindWeaponRoot(active.root);
        }
    }
    IEnumerator RestoreWeapon(float duration)
    {
        yield return new WaitForSeconds(duration);CancelOperatorAbility();
    }
}
