using UnityEngine;

// One transaction owns the apparatus until the 6 s mechanical cycle returns its
// weapon. The deposit stays in the original inventory slot; aborts restore it.
public sealed class ElderboomAltarUpgrader : MonoBehaviour
{
    public static ElderboomAltarUpgrader Instance { get; private set; }
    public Transform model, weaponSocket, interactionPoint;
    public AnimationClip cycle;
    public int cost = 3000;
    public float interactionRange = 3.3f;
    public Light processLight;
    HDRPSlicePlayer customer;
    ElderboomArsenal inventory;
    ElderboomGame transactionGame;
    GameObject display;
    int session, paid;
    float elapsed, pendingUntil, remoteSince;
    bool tinted;
    public int NetworkOwner { get; private set; } = -1;
    public float NetworkElapsed => customer != null ? elapsed : Mathf.Max(0, Time.time - remoteSince);
    public bool Busy => NetworkOwner >= 0 || customer != null;
    public float Progress => Busy ? Mathf.Clamp01(NetworkElapsed / Duration) : 0;
    public float Duration => cycle != null ? cycle.length : 6f;
    int LocalId => ElderboomLan.Instance != null ? ElderboomLan.Instance.LocalId : 0;
    void Awake() { Instance = this; SetPose(0); }
    void OnDestroy() { if (Instance == this) Instance = null; }
    public bool InRange(Vector3 position)
    {
        Vector3 target = interactionPoint != null ? interactionPoint.position : transform.position;
        return Vector2.Distance(new Vector2(position.x, position.z), new Vector2(target.x, target.z)) <= interactionRange
            && Mathf.Abs(position.y - target.y) < 3f;
    }
    bool Focused(HDRPSlicePlayer player)
    {
        if (player == null || player.ViewCamera == null || !InRange(player.transform.position)) return false;
        var point = interactionPoint != null ? interactionPoint.position : transform.position;
        var eye = player.ViewCamera.transform.position; var delta = point - eye;
        if (Vector3.Dot(player.ViewCamera.transform.forward, delta.normalized) < .55f) return false;
        // The altar's own surface can be the last hit before the interaction target.
        if (ElderboomCombat.Ray(eye, delta.normalized, delta.magnitude, player.transform, out var hit)
            && hit.distance < delta.magnitude - .8f && !hit.transform.IsChildOf(transform)) return false;
        return true;
    }
    public bool TryBegin(HDRPSlicePlayer player)
    {
        var game = ElderboomGame.Active;
        if (Busy || pendingUntil > Time.unscaledTime || player == null || !Focused(player) || game == null || !game.CanInteract || player.Seated) return false;
        var arsenal = player.GetComponent<ElderboomArsenal>();
        if (arsenal == null || !arsenal.CanUpgradeActive)
        { game.ShowNotice(arsenal != null && arsenal.ActiveUpgraded ? "WEAPON ALREADY UPGRADED" : "EQUIP A PERMANENT FIREARM"); return false; }
        if (game.Credits < cost) { game.ShowNotice("NEED " + cost + " CREDITS"); return false; }
        if (ElderboomLan.IsClient)
        {
            pendingUntil = Time.unscaledTime + 4f;
            ElderboomLan.RequestAltar(false); return true;
        }
        NetworkOwner = LocalId;
        return StartLocal(player);
    }
    bool StartLocal(HDRPSlicePlayer player)
    {
        var game = ElderboomGame.Active;
        var arsenal = player != null ? player.GetComponent<ElderboomArsenal>() : null;
        if (game == null || !game.CanInteract || !Focused(player) || arsenal == null || !arsenal.CanUpgradeActive || game.Credits < cost)
        { Release(); return false; }
        var visual = arsenal.BeginAltarDeposit(weaponSocket);
        if (visual == null) { game.ShowNotice("WEAPON DISPLAY UNAVAILABLE"); Release(); return false; }
        if (!game.TrySpend(cost)) { arsenal.CancelAltarDeposit(); Destroy(visual); Release(); return false; }
        customer = player; inventory = arsenal; display = visual; transactionGame = game;
        session = game.Session; paid = cost; elapsed = 0; tinted = false;
        game.ShowNotice("ALTAR // WEAPON DEPOSITED", 2f);
        ElderboomAudio.Play("kenney/impactPlate_light_000", transform.position, .4f);
        return true;
    }
    void Update()
    {
        if (customer != null)
        {
            if (inventory == null || customer.IsDead || !inventory.WeaponDeposited || transactionGame == null || !transactionGame.IsSessionActive(session)) { Abort(); return; }
            elapsed += Time.deltaTime; SetPose(elapsed);
            if (!tinted && elapsed >= Duration * .62f)
            {
                tinted = true;
                ElderboomUpgradedWeapon.Apply(display.transform, null, "", true);
                ElderboomAudio.Play("kenney/impactMetal_medium_000", transform.position, .35f);
            }
            if (elapsed >= Duration)
            {
                if (inventory.CompleteAltarUpgrade())
                {
                    paid = 0; transactionGame.ShowNotice("CRIMSON UPGRADE // DAMAGE x2 · AMMO +50%", 5f);
                    ClearVisual(); customer = null; inventory = null; Release();
                }
                else Abort();
            }
            return;
        }
        if (NetworkOwner >= 0)
        {
            SetPose(NetworkElapsed);
            if (!ElderboomLan.IsClient && NetworkElapsed > Duration + 5f) Release();
            return;
        }
        var player = ElderboomGame.Active != null ? ElderboomGame.Active.Player : null;
        if (player != null && player.ActionDown("interact")) TryBegin(player);
    }
    void LateUpdate()
    {
        if (processLight != null) processLight.enabled = Busy && Progress > .43f && Progress < .76f;
    }
    void SetPose(float seconds)
    {
        if (cycle != null && model != null) ElderboomAnimationSampling.Sample(cycle, model.gameObject, Mathf.Clamp(seconds, 0, Duration));
    }
    void ClearVisual() { if (display != null) Destroy(display); display = null; }
    void Release()
    {
        if (ElderboomLan.IsClient) ElderboomLan.RequestAltar(true);
        NetworkOwner = -1; pendingUntil = 0; elapsed = 0; SetPose(0);
    }
    public void Abort()
    {
        inventory?.CancelAltarDeposit();
        if (paid > 0 && transactionGame != null && transactionGame.Session == session) transactionGame.RefundAltar(paid);
        paid = 0; ClearVisual(); customer = null; inventory = null; transactionGame = null; Release();
    }
    void OnDisable() { if (customer != null) Abort(); if (processLight != null) processLight.enabled = false; }

    // The host arbitrates the one shared machine. Currency/inventory are local,
    // as with existing weapon shops; damage already travels through the hit RPC.
    public bool ReserveRemote(int id, Vector3 position)
    {
        if (Busy || !InRange(position)) return false;
        NetworkOwner = id; remoteSince = Time.time; return true;
    }
    public void ReleaseRemote(int id) { if (customer == null && NetworkOwner == id) Release(); }
    public void NetworkGrant(bool accepted)
    {
        if (pendingUntil <= Time.unscaledTime) { if (accepted) ElderboomLan.RequestAltar(true); return; }
        pendingUntil = 0;
        if (!accepted) { ElderboomGame.Active?.ShowNotice("ALTAR IN USE"); return; }
        NetworkOwner = LocalId;
        StartLocal(ElderboomGame.Active != null ? ElderboomGame.Active.Player : null);
    }
    public void ApplyNetworkState(int owner, float seconds)
    {
        if (customer != null) return;
        NetworkOwner = owner; remoteSince = Time.time - seconds;
        if (owner < 0) SetPose(0);
    }
    void OnGUI()
    {
        var game = ElderboomGame.Active; var player = game != null ? game.Player : null;
        if (player == null || !game.IsPlaying || player.SettingsOpen || player.IsDead || !Focused(player)) return;
        var arsenal = player.GetComponent<ElderboomArsenal>();
        string text = Busy ? "ALTAR // " + Mathf.RoundToInt(Progress * 100) + "%"
            : arsenal != null && arsenal.ActiveUpgraded ? "CRIMSON WEAPON // UPGRADED"
            : player.ActionLabel("interact") + " · UPGRADE WEAPON\n" + cost + " CREDITS";
        GUI.Label(new Rect(Screen.width / 2 - 210, Screen.height * .67f, 420, 60), text, GUI.skin.box);
    }
}
