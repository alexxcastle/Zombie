using UnityEngine;
using UnityEngine.Events;

// Designer-placed volume for scripted moments: entering it can show a message,
// play a sound, unlock an area, spawn extra zombies at chosen points and invoke
// any UnityEvent wired in the Inspector.
[RequireComponent(typeof(BoxCollider))]
public sealed class ElderboomTrigger : MonoBehaviour
{
    public bool once = true;
    public bool onlyWhilePlaying = true;
    [TextArea] public string notice = "";
    public float noticeSeconds = 4f;
    [Tooltip("Path under Resources/Elderboom/Audio, e.g. zombies/zombie-feral-snarl")]
    public string sound = "";
    public float soundVolume = .7f;
    public string unlockArea = "";
    public Transform[] spawnZombiesAt = new Transform[0];
    public UnityEvent onEnter = new UnityEvent();
    bool fired;

    void Reset() { GetComponent<BoxCollider>().isTrigger = true; }
    void Awake() { GetComponent<BoxCollider>().isTrigger = true; }

    void OnTriggerEnter(Collider other)
    {
        if (fired && once) return;
        var player = other.GetComponentInParent<HDRPSlicePlayer>();
        if (player == null || player.IsDead) return;
        var game = ElderboomGame.Active;
        if (onlyWhilePlaying && (game == null || !game.IsPlaying)) return;
        fired = true;
        if (!string.IsNullOrEmpty(notice) && game != null) game.ShowNotice(notice, noticeSeconds);
        if (!string.IsNullOrEmpty(sound)) ElderboomAudio.Play(sound, player.transform.position, soundVolume);
        if (!string.IsNullOrEmpty(unlockArea)) ElderboomArea.Unlock(unlockArea);
        if (game != null) foreach (var point in spawnZombiesAt) if (point != null) game.SpawnScripted(point);
        ElderboomEvents.RaiseTriggerFired(name);
        onEnter.Invoke();
    }

    public void ResetTrigger() { fired = false; }

    void OnDrawGizmos()
    {
        var box = GetComponent<BoxCollider>();
        if (box == null) return;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(.3f, .6f, 1f, .1f); Gizmos.DrawCube(box.center, box.size);
        Gizmos.color = new Color(.3f, .6f, 1f, .9f); Gizmos.DrawWireCube(box.center, box.size);
    }
}
