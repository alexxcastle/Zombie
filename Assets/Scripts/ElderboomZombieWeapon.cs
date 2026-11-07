using UnityEngine;

// A melee weapon held in the zombie's right hand (the inn cook's cleaver). Attached when the zombie wakes,
// from the hand's own bones so it sits in the fist on any of the rural rigs; also on LAN proxies.
// The zombie swings it with the weapon clips (CleaverDown / CleaverSide): more reach and damage than claws.
public sealed class ElderboomZombieWeapon : MonoBehaviour
{
    public GameObject weaponPrefab;
    public float damageMultiplier = 1.8f, reach = 1.75f, swingSpeed = 1.45f;
    public string[] attackStates = { "CleaverDown", "CleaverSide" };
    // Hit moment as a fraction of each clip (matches attackStates).
    public float[] hitFraction = { .43f, .42f };
    // Grip tuning in the hand frame (metres / degrees): along the fingers, out of the palm, along the handle.
    public Vector3 gripOffset = new Vector3(.075f, .02f, 0f);
    public Vector3 gripEuler = Vector3.zero;
    public Transform Held { get; private set; }

    void Awake()
    {
        if (weaponPrefab == null || Held != null) return;
        var hand = Find(transform, "RightHand");
        var index = Find(transform, "RightHandIndex1"); var pinky = Find(transform, "RightHandPinky1"); var middle = Find(transform, "RightHandMiddle1");
        if (hand == null) return;
        var go = Instantiate(weaponPrefab); go.name = weaponPrefab.name; Held = go.transform;
        foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
        Vector3 fingers = middle != null ? (middle.position - hand.position).normalized : hand.right;
        Vector3 across = index != null && pinky != null ? (index.position - pinky.position) : hand.forward; // pinky -> index side
        across = Vector3.ProjectOnPlane(across, fingers).normalized;
        Vector3 palm = Vector3.Cross(fingers, across).normalized; // out of the palm (right hand)
        // Cleaver mesh: +X blade direction (out of the index side), -Y cutting edge, origin at the grip.
        // Cleaver mesh: +X blade (out of the index side), +Y spine / -Y edge (edge toward the fingertips), origin at the grip.
        Vector3 X = across, Y = -fingers, Z = Vector3.Cross(X, Y);
        var rot = Quaternion.LookRotation(Z, Y);
        Held.rotation = rot * Quaternion.Euler(gripEuler);
        Held.position = hand.position + fingers * gripOffset.x + palm * gripOffset.y + across * gripOffset.z;
        Held.SetParent(hand, true);
    }

    static Transform Find(Transform t, string name)
    {
        if (t.name == name || t.name.EndsWith(":" + name)) return t;
        foreach (Transform c in t) { var r = Find(c, name); if (r != null) return r; }
        return null;
    }
}
