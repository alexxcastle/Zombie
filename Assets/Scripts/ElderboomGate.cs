using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public sealed class ElderboomGate : MonoBehaviour
{
    public string areaName;
    // Secondary destination of a shortcut, so it also works when purchased from the far side.
    public string otherAreaName;
    public int cost=500;
    // Shown when the passage opens (e.g. the repaired bridge deck); kept outside the gate, which sinks away.
    public GameObject reveal;
    bool opened;
    public bool Opened => opened;
    // LAN: opened by another player (ElderboomLan); no cost here.
    public void NetOpen(){if(opened)return;opened=true;if(isActiveAndEnabled)StartCoroutine(Open());}
    HDRPSlicePlayer player;
    void Start()
    {
        player=FindAnyObjectByType<HDRPSlicePlayer>();
        var obstacle=GetComponent<NavMeshObstacle>();if(obstacle==null)obstacle=gameObject.AddComponent<NavMeshObstacle>();obstacle.shape=NavMeshObstacleShape.Box;obstacle.carving=true;
        // A gate built as a parent with its own box collider (block map passages) blocks exactly that box.
        var box=GetComponent<BoxCollider>();obstacle.size=box!=null?box.size:Vector3.one;obstacle.center=box!=null?box.center:Vector3.zero;
    }
    void Update()
    {
        if(opened || player==null || ElderboomGame.Active==null || !ElderboomGame.Active.CanInteract)return;
        if(Vector3.Distance(player.transform.position,transform.position)<4f && player.ActionDown("interact"))
            if(ElderboomGame.Active.TrySpend(cost)){opened=true;StartCoroutine(Open());ElderboomLan.GateOpened(this);}
    }
    IEnumerator Open()
    {
        var blocker=GetComponent<Collider>();var obstacle=GetComponent<NavMeshObstacle>();
        var leaves=GetComponent<ElderboomGateLeaves>();
        if(leaves!=null)yield return leaves.Open();
        if(blocker!=null)blocker.enabled=false;if(obstacle!=null)obstacle.enabled=false;
        ElderboomArea.Unlock(areaName);
        ElderboomArea.Unlock(otherAreaName);
        // Both sides of the passage open, whichever side it was bought from.
        foreach(var d in new[]{transform.forward,-transform.forward,transform.right,-transform.right})
        { var side=ElderboomArea.At(transform.position+d*6f); if(side!=null)ElderboomArea.Unlock(side.areaName); }
        if(reveal!=null)reveal.SetActive(true);
        if(leaves!=null)yield break;
        Vector3 start=transform.position;
        for(float t=0;t<1;t+=Time.deltaTime){transform.position=start+Vector3.down*3f*Mathf.SmoothStep(0,1,t);yield return null;}
        gameObject.SetActive(false);
    }
    void LateUpdate() => PromptTick();

    void PromptTick()
    {
        if(!opened && player!=null && Vector3.Distance(player.transform.position,transform.position)<4f)
        {
            string destination=!string.IsNullOrEmpty(otherAreaName) && ElderboomArea.At(player.transform.position)?.areaName==areaName?otherAreaName:areaName;
            ElderboomPrompt.Offer(player.transform.position, transform.position, player.ActionLabel("interact")+" · UNLOCK "+destination.ToUpperInvariant()+"\n"+cost+" CREDITS");
        }
    }
}
