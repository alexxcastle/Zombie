using UnityEngine;

// Zombie power-up drops (phase 6): Max Ammo, Insta-Kill, Double Points.
// A floating emissive orb with a letter; collected by walking through it, gone after 25 s.
public sealed class ElderboomPowerUp : MonoBehaviour
{
    public const float DropChance = .04f;
    static readonly string[] Kinds = { "max_ammo", "insta_kill", "double_points" };
    static Material orbMaterial;
    string kind;
    Vector3 ground;
    float expires;
    HDRPSlicePlayer player;
    Light glow;
    GUIStyle style;

    public static ElderboomPowerUp TrySpawn(Vector3 point)
    {
        if (Random.value >= DropChance) return null;
        return Spawn(point, Kinds[Random.Range(0, Kinds.Length)]);
    }

    public static ElderboomPowerUp Spawn(Vector3 point, string kind)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "PowerUp_" + kind;
        Destroy(go.GetComponent<Collider>());
        go.transform.position = point; go.transform.localScale = Vector3.one * .32f;
        var renderer = go.GetComponent<Renderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (orbMaterial == null)
        {
            var shader = Resources.Load<Shader>("Elderboom/Shaders/FeelParticles");
            orbMaterial = new Material(shader) { mainTexture = Texture2D.whiteTexture };
            orbMaterial.SetFloat("_SrcBlend", 5f); orbMaterial.SetFloat("_DstBlend", 1f); orbMaterial.SetFloat("_Intensity", 3f);
            orbMaterial.renderQueue = 3000;
        }
        renderer.sharedMaterial = orbMaterial;
        var block = new MaterialPropertyBlock();
        block.SetColor("_Color", Tint(kind) * .55f); renderer.SetPropertyBlock(block);
        var powerUp = go.AddComponent<ElderboomPowerUp>();
        powerUp.kind = kind; powerUp.ground = point; powerUp.expires = Time.time + 25f;
        var light = new GameObject("Glow").AddComponent<Light>();
        light.transform.SetParent(go.transform, false);
        light.type = LightType.Point; light.color = Tint(kind); light.range = 4f; light.intensity = 1800f; light.shadows = LightShadows.None;
        powerUp.glow = light;
        return powerUp;
    }

    static Color Tint(string kind) => kind == "max_ammo" ? new Color(.35f, .8f, 1f) : kind == "insta_kill" ? new Color(1f, .25f, .2f) : new Color(1f, .82f, .2f);
    static string Letter(string kind) => kind == "max_ammo" ? "MAX AMMO" : kind == "insta_kill" ? "INSTA-KILL" : "DOUBLE POINTS";

    // LAN: the host's power-ups are listed in its snapshots; clients show proxies and ask the host to collect.
    public static readonly System.Collections.Generic.List<ElderboomPowerUp> All = new System.Collections.Generic.List<ElderboomPowerUp>();
    public int NetId;
    public bool NetProxy;
    public string Kind => kind;
    public Vector3 Ground => ground;
    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Start() { player = FindAnyObjectByType<HDRPSlicePlayer>(); }

    void Update()
    {
        float left = expires - Time.time;
        if ((left <= 0f && !NetProxy) || ElderboomGame.Active == null || !ElderboomGame.Active.IsPlaying) { Destroy(gameObject); return; }
        transform.position = ground + Vector3.up * (1.0f + Mathf.Sin(Time.time * 2.6f) * .12f);
        // Blink during the last five seconds.
        bool visible = left > 5f || Mathf.Repeat(Time.time * 6f, 1f) > .35f;
        GetComponent<Renderer>().enabled = visible;
        if (glow != null) glow.enabled = visible;
        if (player != null && !player.IsDead && (player.transform.position - ground).sqrMagnitude < 1.6f * 1.6f)
        {
            if (NetProxy) ElderboomLan.Collect(NetId); else ElderboomGame.Active.ActivatePowerUp(kind);
            gameObject.SetActive(false); Destroy(gameObject);
        }
    }

    void OnGUI()
    {
        if (player == null || player.SettingsOpen || player.IsDead || player.ViewCamera == null) return;
        if ((player.transform.position - ground).sqrMagnitude > 30f * 30f) return;
        var screen = player.ViewCamera.WorldToScreenPoint(transform.position + Vector3.up * .45f);
        if (screen.z <= 0f) return;
        if (style == null) { style = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }; }
        style.normal.textColor = Tint(kind);
        GUI.Label(new Rect(screen.x - 120, Screen.height - screen.y - 12, 240, 24), Letter(kind), style);
    }
}
