using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.AI;

// LAN co-op on the same Wi-Fi over plain UDP (no packages, no relay).
//   Host:   plays normally (waves, zombie AI, damage are authoritative here) and announces the game by broadcast
//           every second on DiscoveryPort, so anyone on the network sees it in MULTIPLAYER and can join at any time.
//   Client: runs no waves and no zombie AI; it shows zombie proxies from the host's snapshots (20 Hz),
//           sends its own position and its hits on zombies; the host sends back melee damage and kill rewards.
// Every player is shown to the others as a full-body avatar (ElderboomLanAvatar).
// ONLINE: the same messages travel through Unity Relay (Unity Transport) instead of the LAN socket: the host gets a
// short join code, friends type it and connect from any network (no VPN, no open ports, no Unity account for them).
public sealed class ElderboomLan : MonoBehaviour
{
    public enum Mode { Offline, Host, Client }
    public const int MaxRelayPlayers = 8;
    public const int GamePort = 47777, DiscoveryPort = 47778;
    const float SendInterval = .05f, AnnounceInterval = 1f, PeerTimeout = 10f, GameListTimeout = 4f;
    // Bump Version whenever messages change: players on different builds get a clear "update" message.
    const byte Magic = 0xEB, Version = 4;
    enum Msg : byte { Announce = 1, Join = 2, Accept = 3, State = 4, World = 5, Hit = 6, Damage = 7, Kill = 8, Leave = 9, Effect = 10, Ignore = 11, Collect = 12, PowerUp = 13, Grant = 14, GateOpen = 15, Reject = 16, HorsePart = 17, Altar = 18 }
    static readonly string[] Variants = { "walker", "runner", "brute" };

    public static ElderboomLan Instance { get; private set; }
    public static Mode Current => Instance != null ? Instance.mode : Mode.Offline;
    public static bool IsHost => Current == Mode.Host;
    public static bool IsClient => Current == Mode.Client;
    // Host: id of the client whose hit is being applied right now (0 = the host itself), so the reward goes to them.
    public static int RemoteShooter;
    public static bool AnyRemoteAlive
    {
        get { foreach (var a in ElderboomLanAvatar.All) if (a != null && !a.IsDead) return true; return false; }
    }

    public sealed class Found { public string name, difficulty; public IPEndPoint endPoint; public int players; public float seen; }
    public readonly List<Found> Games = new List<Found>();
    public string Status { get; private set; } = "";
    public bool Accepted { get; private set; }
    public bool Failed { get; private set; }
    public int LocalId { get; private set; }
    public string HostDifficulty { get; private set; } = "NORMAL";
    public int Players => mode == Mode.Host ? peers.Count + 1 : avatars.Count + 1;
    public static string LocalAddress => FindLocalAddress();

    Mode mode;
    UdpClient socket, discovery;
    Remote host;

    // Where a message came from / goes to: a LAN address or a Relay connection.
    sealed class Remote
    {
        public IPEndPoint ep; public NetworkConnection conn; public bool relay;
        public string Key => relay ? "relay:" + conn : ep.ToString();
        public string Label => relay ? "the host" : ep.Address.ToString();
        public static Remote Udp(IPEndPoint ep) => new Remote { ep = ep };
    }

    // Relay transport.
    NetworkDriver driver;
    NetworkPipeline fastPipe, reliablePipe;
    readonly List<NetworkConnection> connections = new List<NetworkConnection>();
    readonly Dictionary<NetworkConnection, Remote> relayRemotes = new Dictionary<NetworkConnection, Remote>();
    bool relayActive, relayConnected;
    public bool Online => relayActive;
    public string JoinCode { get; private set; } = "";
    public bool HostReady { get; private set; }
    string joinOperator, hostDifficulty = "NORMAL";
    float worldErrorAt;
    float sendAt, announceAt, joinAt, joinUntil, hostSeen, lostNoticeAt;
    int placedSession = -1;
    List<IPAddress> broadcasts;

    sealed class Peer
    {
        public int id; public Remote ep; public float seen; public string op = "";
        public bool hasState; public Vector3 pos; public float feet, yaw; public bool dead; public int shots;
        public int seat; public Vector3 horsePos; public float horseYaw, horseSpeed; // 1 driver, 2 passenger
    }
    readonly Dictionary<string, Peer> peers = new Dictionary<string, Peer>();
    int nextPeerId = 1;
    readonly Dictionary<int, ElderboomLanAvatar> avatars = new Dictionary<int, ElderboomLanAvatar>();

    // Host: network ids for live zombies.
    readonly Dictionary<ElderboomZombie, ushort> zombieIds = new Dictionary<ElderboomZombie, ushort>();
    readonly Dictionary<ushort, ElderboomZombie> zombiesById = new Dictionary<ushort, ElderboomZombie>();
    readonly Dictionary<ElderboomZombie, Animator> animators = new Dictionary<ElderboomZombie, Animator>();
    ushort nextZombieId = 1;

    // Client: proxies of the host's zombies.
    sealed class Proxy { public ElderboomZombie zombie; public Animator animator; public Vector3 pos; public float yaw; public int anim; public bool dead; public float seen; }
    readonly Dictionary<ushort, Proxy> proxies = new Dictionary<ushort, Proxy>();
    readonly HashSet<ushort> deadIds = new HashSet<ushort>();
    readonly HashSet<ushort> seenIds = new HashSet<ushort>();
    readonly HashSet<int> seenPlayers = new HashSet<int>();
    readonly List<ushort> dropIds = new List<ushort>();
    readonly List<int> dropPlayers = new List<int>();

    readonly MemoryStream outStream = new MemoryStream(1400);
    BinaryWriter writer;

    // ---- Public API -----------------------------------------------------------------------

    static ElderboomLan Ensure()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("ElderboomLan");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<ElderboomLan>();
        return Instance;
    }

    // Menu: listen for games announced on the network.
    public static void StartDiscovery() => Ensure().OpenDiscovery();

    public static bool StartHost(string difficulty)
    {
        var lan = Ensure();
        lan.CloseSockets();
        lan.mode = Mode.Host; lan.hostDifficulty = difficulty; lan.LocalId = 0;
        try
        {
            lan.socket = new UdpClient(AddressFamily.InterNetwork);
            lan.socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            lan.socket.Client.Bind(new IPEndPoint(IPAddress.Any, GamePort));
            lan.socket.EnableBroadcast = true;
            NoConnReset(lan.socket);
            lan.Status = "HOSTING ON " + LocalAddress;
            Debug.Log("[ElderboomLan] Hosting on " + LocalAddress + ":" + GamePort);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[ElderboomLan] Host failed: " + e.Message);
            lan.Status = "COULD NOT HOST: " + e.Message;
            lan.mode = Mode.Offline; lan.CloseSockets();
            return false;
        }
    }

    public static void Join(IPEndPoint host, string op)
    {
        var lan = Ensure();
        lan.CloseGameSocket(); lan.CloseRelay();
        lan.mode = Mode.Client; lan.host = Remote.Udp(host); lan.joinOperator = op;
        lan.Accepted = false; lan.Failed = false; lan.joinAt = 0f; lan.joinUntil = Time.unscaledTime + 4f;
        try
        {
            lan.socket = new UdpClient(0, AddressFamily.InterNetwork);
            NoConnReset(lan.socket);
            lan.Status = "CONNECTING TO " + host.Address + "...";
        }
        catch (Exception e) { lan.Status = "COULD NOT CONNECT: " + e.Message; lan.Failed = true; lan.mode = Mode.Offline; }
    }

    public static void Shutdown()
    {
        if (Instance == null) return;
        Instance.SendLeave();
        Destroy(Instance.gameObject);
        Instance = null;
    }

    public static void StopDiscovery() { if (Instance != null) Instance.CloseDiscovery(); }

    // Client: a shot on a zombie proxy goes to the host, which applies it to the real zombie.
    public static void SendHit(ElderboomZombie proxy, float amount, Vector3 point, Vector3 normal)
    {
        var lan = Instance;
        if (lan == null || lan.mode != Mode.Client || !lan.Accepted || proxy == null) return;
        var w = lan.Begin(Msg.Hit);
        w.Write(proxy.NetId); w.Write(amount); Write(w, point); Write(w, normal);
        lan.Send(lan.host, true);
    }

    // Client: an ability effect on a zombie proxy (freeze, push, lift, ...) is applied by the host to the real zombie.
    public static void SendEffect(ElderboomZombie proxy, byte kind, float[] values)
    {
        var lan = Instance;
        if (lan == null || lan.mode != Mode.Client || !lan.Accepted || proxy == null) return;
        var w = lan.Begin(Msg.Effect);
        w.Write(proxy.NetId); w.Write(kind); w.Write((byte)values.Length);
        foreach (var v in values) w.Write(v);
        lan.Send(lan.host, kind != 7 && kind != 1); // keep-away (every frame) and noises are best effort
    }

    // Client: Eli's Ignore Signal; the host's zombies stop chasing this player for a while.
    public static void SendIgnore(float seconds)
    {
        var lan = Instance;
        if (lan == null || lan.mode != Mode.Client || !lan.Accepted) return;
        var w = lan.Begin(Msg.Ignore); w.Write(seconds);
        lan.Send(lan.host, true);
    }

    // ---- Mechanical horse ---------------------------------------------------------------------
    static int LocalSeat()
    {
        var horse = ElderboomMount.Instance;
        return horse == null ? 0 : horse.Mounted ? 1 : horse.Passenger ? 2 : 0;
    }

    // Host: who sits where. A seat stays with its player until they leave it; free seats go to whoever claims them.
    void ResolveSeats()
    {
        var horse = ElderboomMount.Instance;
        if (horse == null) return;
        if (!horse.Assembled) { horse.NetDriver = horse.NetPassenger = -1; horse.ForceLeave(); return; }
        int driver = horse.NetDriver, passenger = horse.NetPassenger;
        bool Claims(int id, int seat)
        {
            if (id == 0) return LocalSeat() == seat;
            foreach (var pr in peers.Values) if (pr.id == id) return pr.seat == seat && !pr.dead;
            return false;
        }
        if (driver >= 0 && !Claims(driver, 1)) driver = -1;
        if (passenger >= 0 && !Claims(passenger, 2)) passenger = -1;
        if (driver < 0 && LocalSeat() == 1) driver = 0;
        if (passenger < 0 && LocalSeat() == 2 && driver != 0) passenger = 0;
        foreach (var pr in peers.Values)
        {
            if (driver < 0 && pr.seat == 1 && passenger != pr.id) driver = pr.id;
            else if (passenger < 0 && pr.seat == 2 && driver != pr.id) passenger = pr.id;
        }
        horse.NetDriver = driver; horse.NetPassenger = passenger;
        if (driver > 0) foreach (var pr in peers.Values) if (pr.id == driver) horse.SetNetPose(pr.horsePos, pr.horseYaw, pr.horseSpeed);
        // The host lost a seat race (a client got there first): leave it.
        if ((LocalSeat() == 1 && driver != 0) || (LocalSeat() == 2 && passenger != 0)) horse.ForceLeave();
    }

    void WriteHorse(BinaryWriter w)
    {
        var horse = ElderboomMount.Instance;
        w.Write(horse != null);
        if (horse == null) return;
        Write(w, horse.transform.position); w.Write(horse.transform.eulerAngles.y); w.Write(horse.Speed);
        w.Write((sbyte)horse.NetDriver); w.Write((sbyte)horse.NetPassenger);
        var assembly = ElderboomHorseAssembly.Instance;
        w.Write((byte)(assembly != null ? assembly.CollectedMask : 0));
        w.Write(assembly != null && assembly.Built);
        w.Write(assembly != null ? assembly.Revision : 0u);
    }

    void ReadHorse(BinaryReader r)
    {
        if (!r.ReadBoolean()) return;
        var pos = ReadV(r); float yaw = r.ReadSingle(), speed = r.ReadSingle();
        int driver = r.ReadSByte(), passenger = r.ReadSByte();
        int mask = r.ReadByte(); bool built = r.ReadBoolean(); uint revision = r.ReadUInt32();
        ElderboomHorseAssembly.Instance?.ApplySnapshot(mask, built, revision);
        var horse = ElderboomMount.Instance;
        if (horse == null || !horse.Assembled) return;
        horse.NetDriver = driver; horse.NetPassenger = passenger;
        if (driver != LocalId) horse.SetNetPose(pos, yaw, speed);
        // Seat given to someone else (both claimed at once): leave it. A short grace while our claim travels.
        int mine = LocalSeat();
        if (mine == 0) seatSince = Time.unscaledTime;
        else if (Time.unscaledTime - seatSince > .6f && ((mine == 1 && driver != LocalId) || (mine == 2 && passenger != LocalId))) horse.ForceLeave();
    }
    float seatSince;

    public static void RequestHorsePart(byte item, uint revision)
    {
        var lan = Instance;
        if (lan == null || lan.mode != Mode.Client || !lan.Accepted) return;
        var w = lan.Begin(Msg.HorsePart); w.Write(item); w.Write(revision);
        lan.Send(lan.host, true);
    }

    public static void RequestAltar(bool release)
    {
        var lan = Instance;
        if (lan == null || lan.mode != Mode.Client || !lan.Accepted) return;
        var w = lan.Begin(Msg.Altar); w.Write((byte)(release ? 1 : 0));
        lan.Send(lan.host, true);
    }

    // ---- Pickups and gates ----------------------------------------------------------------

    // Client: walked into a power-up / weapon drop proxy; the host decides and answers.
    public static void Collect(int netId)
    {
        var lan = Instance;
        if (lan == null || lan.mode != Mode.Client || !lan.Accepted) return;
        var w = lan.Begin(Msg.Collect); w.Write(netId);
        lan.Send(lan.host, true);
    }

    // Host: a power-up was activated (by anyone): every client gets the same effect.
    public static void BroadcastPowerUp(string kind)
    {
        var lan = Instance;
        if (lan == null || lan.mode != Mode.Host) return;
        var w = lan.Begin(Msg.PowerUp); w.Write(kind ?? "");
        foreach (var p in lan.peers.Values) lan.Send(p.ep, true);
    }

    // A gate opened here: clients tell the host, the host lists open gates in every snapshot.
    public static void GateOpened(ElderboomGate gate)
    {
        var lan = Instance;
        if (lan == null || lan.mode != Mode.Client || !lan.Accepted) return;
        int index = System.Array.IndexOf(Gates(), gate);
        if (index < 0) return;
        var w = lan.Begin(Msg.GateOpen); w.Write((byte)index);
        lan.Send(lan.host, true);
    }

    static ElderboomGate[] gates;
    // Same order on every machine: sorted by position.
    static ElderboomGate[] Gates()
    {
        if (gates == null || System.Array.Exists(gates, g => g == null))
        {
            gates = UnityEngine.Object.FindObjectsByType<ElderboomGate>(FindObjectsInactive.Include);
            System.Array.Sort(gates, (a, b) => { var pa = a.transform.position; var pb = b.transform.position; int c = pa.x.CompareTo(pb.x); return c != 0 ? c : pa.z.CompareTo(pb.z); });
        }
        return gates;
    }

    readonly Dictionary<int, Component> pickupProxies = new Dictionary<int, Component>();
    readonly HashSet<int> seenPickups = new HashSet<int>();

    void WritePickupsAndGates(BinaryWriter w)
    {
        int mask = 0; var g = Gates();
        for (int i = 0; i < g.Length && i < 31; i++) if (g[i] != null && g[i].Opened) mask |= 1 << i;
        w.Write(mask);
        int count = 0;
        foreach (var p in ElderboomPowerUp.All) if (p != null && !p.NetProxy) count++;
        foreach (var p in ElderboomSpecialPickup.All) if (p != null && !p.NetProxy && p.Definition != null) count++;
        count = Mathf.Min(count, 16);
        w.Write((byte)count);
        int n = 0;
        foreach (var p in ElderboomPowerUp.All)
        {
            if (p == null || p.NetProxy || n >= count) continue;
            if (p.NetId == 0) p.NetId = ++pickupCounter; n++;
            w.Write(p.NetId); w.Write((byte)0); w.Write(p.Kind ?? ""); Write(w, p.Ground);
        }
        foreach (var p in ElderboomSpecialPickup.All)
        {
            if (p == null || p.NetProxy || p.Definition == null || n >= count) continue;
            if (p.NetId == 0) p.NetId = ++pickupCounter; n++;
            w.Write(p.NetId); w.Write((byte)1); w.Write(p.Definition.id); Write(w, p.Ground);
        }
    }

    void ReadPickupsAndGates(BinaryReader r)
    {
        int mask = r.ReadInt32();
        var g = Gates();
        for (int i = 0; i < g.Length && i < 31; i++) if ((mask & (1 << i)) != 0 && g[i] != null && !g[i].Opened) g[i].NetOpen();
        int count = r.ReadByte();
        seenPickups.Clear();
        for (int i = 0; i < count; i++)
        {
            int id = r.ReadInt32(); int type = r.ReadByte(); string kind = r.ReadString(); var pos = ReadV(r);
            seenPickups.Add(id);
            if (pickupProxies.TryGetValue(id, out var existing) && existing != null) continue;
            if (pickupProxies.ContainsKey(id)) continue; // collected here, waiting for the host to drop it
            Component proxy = null;
            if (type == 0) { var p = ElderboomPowerUp.Spawn(pos, kind); if (p != null) { p.NetProxy = true; p.NetId = id; proxy = p; } }
            else { var p = ElderboomSpecialPickup.Spawn(pos, ElderboomWeapons.Find(kind)); if (p != null) { p.NetProxy = true; p.NetId = id; proxy = p; } }
            pickupProxies[id] = proxy;
        }
        dropInts.Clear();
        foreach (var kv in pickupProxies) if (!seenPickups.Contains(kv.Key)) dropInts.Add(kv.Key);
        foreach (var id in dropInts) { var c = pickupProxies[id]; if (c != null) Destroy(c.gameObject); pickupProxies.Remove(id); }
    }
    readonly List<int> dropInts = new List<int>();
    static int pickupCounter;

    // Host: a zombie struck a remote player.
    public static void DamageAvatar(ElderboomLanAvatar avatar, float amount, Vector3 from)
    {
        var lan = Instance;
        if (lan == null || lan.mode != Mode.Host || avatar == null) return;
        foreach (var peer in lan.peers.Values)
            if (peer.id == avatar.Id)
            {
                var w = lan.Begin(Msg.Damage); w.Write(amount); Write(w, from);
                lan.Send(peer.ep, true);
                return;
            }
    }

    // ---- Online (Unity Relay) -------------------------------------------------------------

    // Host anywhere: allocates a Relay slot and gets a join code to share (shown in the menu and in game).
    public static void StartHostOnline(string difficulty)
    {
        var lan = Ensure();
        lan.CloseSockets();
        lan.mode = Mode.Host; lan.hostDifficulty = difficulty; lan.LocalId = 0;
        lan.HostReady = false; lan.Failed = false; lan.JoinCode = "";
        lan.Status = "CREATING ONLINE GAME...";
        _ = lan.HostOnlineAsync();
    }

    // Join from anywhere with the host's code.
    public static void JoinOnline(string code, string op)
    {
        var lan = Ensure();
        lan.CloseSockets();
        lan.mode = Mode.Client; lan.joinOperator = op; lan.host = null;
        lan.Accepted = false; lan.Failed = false; lan.joinAt = 0f; lan.joinUntil = float.MaxValue;
        lan.Status = "CONNECTING ONLINE...";
        _ = lan.JoinOnlineAsync((code ?? "").Trim().ToUpperInvariant());
    }

    void Fail(string why)
    {
        Failed = true; Status = why; Log("FAIL " + why);
        Debug.LogWarning("[ElderboomLan] " + why);
    }

    // Unity Gaming Services: anonymous sign-in, so players need no account.
    async Task<bool> SignIn()
    {
        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
            return true;
        }
        catch (Exception e)
        {
            if (this != null) Fail("ONLINE SERVICES UNAVAILABLE (" + e.Message + ")");
            return false;
        }
    }

    async Task HostOnlineAsync()
    {
        if (!await SignIn() || this == null || mode != Mode.Host) return;
        try
        {
            var allocation = await RelayService.Instance.CreateAllocationAsync(MaxRelayPlayers - 1);
            string code = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            if (this == null || mode != Mode.Host) return;
            var data = allocation.ToRelayServerData("dtls");
            if (!CreateDriver(ref data)) return;
            if (driver.Listen() != 0) { Fail("COULD NOT START THE ONLINE GAME"); return; }
            JoinCode = code; HostReady = true; Status = "ONLINE  ·  CODE " + code; Log("hosting online, code " + code);
            Debug.Log("[ElderboomLan] Online game, join code " + code);
        }
        catch (Exception e) { if (this != null) Fail("COULD NOT CREATE THE ONLINE GAME (" + e.Message + ")"); }
    }

    async Task JoinOnlineAsync(string code)
    {
        if (code.Length < 4) { Fail("TYPE THE HOST'S CODE"); return; }
        if (!await SignIn() || this == null || mode != Mode.Client) return;
        try
        {
            var allocation = await RelayService.Instance.JoinAllocationAsync(code);
            if (this == null || mode != Mode.Client) return;
            var data = allocation.ToRelayServerData("dtls");
            if (!CreateDriver(ref data)) return;
            var conn = driver.Connect(data.Endpoint);
            host = new Remote { conn = conn, relay = true };
            connections.Add(conn); relayRemotes[conn] = host;
            joinUntil = Time.unscaledTime + 12f;
        }
        catch (Exception e) { if (this != null) Fail("CODE NOT FOUND OR GAME CLOSED (" + e.Message + ")"); }
    }

    bool CreateDriver(ref RelayServerData data)
    {
        CloseRelay();
        var settings = new NetworkSettings(Allocator.Temp);
        settings.WithRelayParameters(ref data);
        driver = NetworkDriver.Create(settings);
        // Snapshots: unreliable (a newer one follows); events (join, hits, damage, kills): reliable and ordered.
        fastPipe = driver.CreatePipeline(typeof(FragmentationPipelineStage));
        reliablePipe = driver.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage));
        relayActive = true;
        if (driver.Bind(NetworkEndpoint.AnyIpv4) != 0) { Fail("COULD NOT REACH THE ONLINE SERVICE"); return false; }
        return true;
    }

    void PumpRelay()
    {
        if (!relayActive || !driver.IsCreated) return;
        driver.ScheduleUpdate().Complete();
        if (mode == Mode.Host)
        {
            NetworkConnection c;
            while ((c = driver.Accept()) != default(NetworkConnection))
            {
                connections.Add(c);
                relayRemotes[c] = new Remote { conn = c, relay = true };
            }
        }
        for (int i = connections.Count - 1; i >= 0; i--)
        {
            if (i >= connections.Count || !driver.IsCreated) break;
            var c = connections[i];
            if (!relayRemotes.TryGetValue(c, out var remote)) continue;
            NetworkEvent.Type ev;
            while ((ev = driver.PopEventForConnection(c, out var reader)) != NetworkEvent.Type.Empty)
            {
                if (ev == NetworkEvent.Type.Connect)
                {
                    relayConnected = true;
                    if (!Accepted) Status = "CONNECTED, JOINING...";
                }
                else if (ev == NetworkEvent.Type.Data)
                {
                    var data = new byte[reader.Length];
                    reader.ReadBytes(new Span<byte>(data));
                    Dispatch(data, remote);
                    if (!driver.IsCreated) return;
                }
                else if (ev == NetworkEvent.Type.Disconnect)
                {
                    connections.Remove(c); relayRemotes.Remove(c);
                    Log("relay disconnect " + remote.Key + " reason " + (reader.Length > 0 ? ((Unity.Networking.Transport.Error.DisconnectReason)reader.ReadByte()).ToString() : "?"));
                    if (mode == Mode.Host) RemovePeer(remote.Key, "DISCONNECTED");
                    else
                    {
                        relayConnected = false;
                        if (!Accepted) { if (!Failed) Fail("THE HOST DID NOT ACCEPT THE CONNECTION"); }
                        else { ElderboomGame.Active?.ShowNotice("CONNECTION TO HOST LOST", 5f); hostSeen = -100f; }
                    }
                    break;
                }
            }
        }
    }

    void CloseRelay()
    {
        if (driver.IsCreated)
        {
            foreach (var c in connections) driver.Disconnect(c);
            driver.ScheduleUpdate().Complete();
            driver.Dispose();
        }
        driver = default;
        connections.Clear(); relayRemotes.Clear();
        relayActive = relayConnected = false; HostReady = false;
    }

    // Host: join code (online) or address (LAN) and player count in the top-right corner while playing.
    static GUIStyle cornerStyle;
    string cachedAddress;
    void OnGUI()
    {
        if (mode != Mode.Host) return;
        var game = ElderboomGame.Active;
        if (game == null || game.Player == null || !game.IsPlaying) return;
        cachedAddress ??= LocalAddress;
        string text = relayActive ? (HostReady ? "ONLINE CODE  " + JoinCode : Failed ? Status : "CREATING ONLINE GAME...") : "LAN  " + cachedAddress;
        text += "   ·   " + Players + (Players == 1 ? " PLAYER" : " PLAYERS");
        if (cornerStyle == null) cornerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperRight, fontStyle = FontStyle.Bold, fontSize = 16 };
        var rect = new Rect(Screen.width - 620f, 14f, 600f, 28f);
        cornerStyle.normal.textColor = new Color(0, 0, 0, .8f);
        GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), text, cornerStyle);
        cornerStyle.normal.textColor = new Color(.92f, .96f, 1f, .92f);
        GUI.Label(rect, text, cornerStyle);
    }

    // ---- Diagnostics: lan_log.txt next to the game (Mac: inside the .app's Contents, Windows: beside the .exe) ----
    static string logPath;
    public static void Log(string line)
    {
        try
        {
            logPath ??= Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "lan_log.txt");
            File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + "\n");
        }
        catch { }
        Debug.Log("[ElderboomLan] " + line);
    }

    // ---- Sockets --------------------------------------------------------------------------

    void Awake() { writer = new BinaryWriter(outStream); }

    void OpenDiscovery()
    {
        if (discovery != null) return;
        try
        {
            discovery = new UdpClient(AddressFamily.InterNetwork);
            discovery.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            discovery.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            NoConnReset(discovery);
        }
        catch (Exception e) { Debug.LogWarning("[ElderboomLan] Discovery failed: " + e.Message); discovery = null; }
    }

    void CloseDiscovery() { try { discovery?.Close(); } catch { } discovery = null; Games.Clear(); }
    void CloseGameSocket() { try { socket?.Close(); } catch { } socket = null; }
    void CloseSockets() { CloseGameSocket(); CloseDiscovery(); CloseRelay(); }

    void OnDestroy()
    {
        CloseSockets();
        foreach (var a in avatars.Values) if (a != null) Destroy(a.gameObject);
        avatars.Clear();
        if (Instance == this) Instance = null;
    }

    void OnApplicationQuit() { SendLeave(); }

    void SendLeave()
    {
        if (socket == null) return;
        try
        {
            Begin(Msg.Leave);
            if (mode == Mode.Host) foreach (var p in peers.Values) Send(p.ep, true);
            else if (mode == Mode.Client && host != null) Send(host, true);
            if (relayActive && driver.IsCreated) driver.ScheduleUpdate().Complete();
        }
        catch { }
    }

    static void NoConnReset(UdpClient c)
    {
        // Windows reports ICMP "port unreachable" as an error on the next Receive; turn that off.
        if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor) return;
        try { c.Client.IOControl(-1744830452, new byte[] { 0, 0, 0, 0 }, null); } catch { }
    }

    BinaryWriter Begin(Msg msg)
    {
        outStream.SetLength(0);
        writer.Write(Magic); writer.Write(Version); writer.Write((byte)msg);
        return writer;
    }

    void Send(Remote to, bool reliable = false)
    {
        if (to == null) return;
        writer.Flush();
        if (!to.relay) { SendUdp(to.ep); return; }
        if (!relayActive || !driver.IsCreated) return;
        if (driver.BeginSend(reliable ? reliablePipe : fastPipe, to.conn, out var w) != 0) return;
        w.WriteBytes(new Span<byte>(outStream.GetBuffer(), 0, (int)outStream.Length));
        driver.EndSend(w);
    }

    void SendUdp(IPEndPoint to)
    {
        if (socket == null || to == null) return;
        writer.Flush();
        try { socket.Send(outStream.GetBuffer(), (int)outStream.Length, to); } catch (Exception) { }
    }

    void Dispatch(byte[] data, Remote from)
    {
        if (data.Length < 3 || data[0] != Magic) return;
        if (data[1] != Version)
        {
            // Another build: tell a joining player (host side), or say so here (client side).
            if (mode == Mode.Host && data[2] == (byte)Msg.Join) { Begin(Msg.Reject); writer.Write("DIFFERENT GAME VERSION: UPDATE THE GAME"); Send(from, true); }
            else if (mode == Mode.Client && !Failed && !Accepted) Fail("THE HOST HAS A DIFFERENT GAME VERSION: BOTH OF YOU UPDATE");
            Log("version mismatch: got " + data[1] + " expected " + Version + " msg " + data[2]);
            return;
        }
        try
        {
            using (var r = new BinaryReader(new MemoryStream(data, 3, data.Length - 3)))
                Handle((Msg)data[2], r, from);
        }
        catch (EndOfStreamException) { }
        catch (Exception e) { Log("bad packet " + (Msg)data[2] + " " + e.GetType().Name + ": " + e.Message); }
    }

    static void Write(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
    static Vector3 ReadV(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

    void Pump(UdpClient c)
    {
        if (c == null) return;
        try
        {
            for (int guard = 0; guard < 512 && c.Available > 0; guard++)
            {
                IPEndPoint from = null;
                var data = c.Receive(ref from);
                Dispatch(data, Remote.Udp(from));
            }
        }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
    }

    // ---- Loop -----------------------------------------------------------------------------

    void Update()
    {
        Pump(discovery);
        Pump(socket);
        PumpRelay();
        float now = Time.unscaledTime;
        Games.RemoveAll(g => now - g.seen > GameListTimeout);

        var game = ElderboomGame.Active;
        bool inGame = game != null && game.Player != null;
        if (mode == Mode.Host)
        {
            if (now >= announceAt) { announceAt = now + AnnounceInterval; Announce(); }
            var lost = new List<string>();
            foreach (var kv in peers) if (now - kv.Value.seen > PeerTimeout) lost.Add(kv.Key);
            foreach (var key in lost) RemovePeer(key, "LEFT");
            if (inGame && game.IsPlaying && game.Session != placedSession) { placedSession = game.Session; game.ShowNotice(relayActive ? "ONLINE GAME  //  CODE " + JoinCode : "LAN HOST  //  JOIN ON " + LocalAddress, 8f); }
            if (inGame)
            {
                foreach (var peer in peers.Values)
                    if (peer.hasState)
                    {
                        var avatar = AvatarFor(peer.id, peer.op);
                        avatar.Apply(peer.pos, peer.feet, peer.yaw, peer.dead, peer.shots, peer.seat);
                    }
                if (now >= sendAt)
                {
                    sendAt = now + SendInterval;
                    try { SendWorld(game); }
                    catch (Exception e) { if (now >= worldErrorAt) { worldErrorAt = now + 5f; Log("snapshot error " + e); } }
                }
            }
        }
        else if (mode == Mode.Client)
        {
            if (!Accepted)
            {
                bool canSend = host != null && (!host.relay || relayConnected);
                if (canSend && now >= joinAt) { joinAt = now + .4f; var w = Begin(Msg.Join); w.Write(joinOperator ?? ""); Send(host, true); }
                if (host != null && now > joinUntil && !Failed) { Failed = true; Status = "NO ANSWER FROM " + host.Label.ToUpperInvariant(); }
            }
            else if (inGame)
            {
                PlaceLocalPlayer(game);
                if (now >= sendAt) { sendAt = now + SendInterval; try { SendState(game); } catch (Exception e) { if (now >= worldErrorAt) { worldErrorAt = now + 5f; Log("state error " + e); } } }
                UpdateProxies();
                if (now - hostSeen > PeerTimeout && now >= lostNoticeAt) { lostNoticeAt = now + 5f; game.ShowNotice("CONNECTION TO HOST LOST", 4f); Log("no snapshot from host for " + (now - hostSeen).ToString("F1") + " s, relayConnected=" + relayConnected); }
            }
        }
    }

    void Announce()
    {
        broadcasts ??= BroadcastAddresses();
        var w = Begin(Msg.Announce);
        w.Write(HostName()); w.Write((byte)Mathf.Min(255, peers.Count + 1)); w.Write(hostDifficulty ?? "NORMAL");
        if (socket == null) return;
        foreach (var address in broadcasts) SendUdp(new IPEndPoint(address, DiscoveryPort));
    }

    static string HostName()
    {
        string n = SystemInfo.deviceName;
        if (string.IsNullOrEmpty(n) || n == SystemInfo.unsupportedIdentifier) n = Environment.UserName;
        return string.IsNullOrEmpty(n) ? "ELDERBOOM" : n;
    }

    void Handle(Msg msg, BinaryReader r, Remote remote)
    {
        float now = Time.unscaledTime;
        switch (msg)
        {
            case Msg.Announce:
            {
                if (mode == Mode.Host || remote.relay) return;
                var from = remote.ep;
                string name = r.ReadString(); int players = r.ReadByte(); string difficulty = r.ReadString();
                var found = Games.Find(g => g.endPoint.Address.Equals(from.Address));
                if (found == null) { found = new Found { endPoint = new IPEndPoint(from.Address, from.Port) }; Games.Add(found); }
                found.name = name; found.players = players; found.difficulty = difficulty; found.seen = now;
                found.endPoint = new IPEndPoint(from.Address, from.Port);
                return;
            }
            case Msg.Join:
            {
                if (mode != Mode.Host) return;
                string op = r.ReadString();
                string key = remote.Key;
                if (!peers.TryGetValue(key, out var peer))
                {
                    peer = new Peer { id = nextPeerId++, ep = remote };
                    peers[key] = peer;
                    ElderboomGame.Active?.ShowNotice("PLAYER " + (peer.id + 1) + " JOINED", 3f);
                    Log("player " + peer.id + " joined from " + key);
                }
                peer.op = op; peer.seen = now;
                var w = Begin(Msg.Accept); w.Write((byte)peer.id); w.Write(hostDifficulty ?? "NORMAL");
                Send(remote, true);
                return;
            }
            case Msg.Accept:
            {
                if (mode != Mode.Client || !SameHost(remote)) return;
                LocalId = r.ReadByte(); HostDifficulty = r.ReadString();
                if (!Accepted) { Accepted = true; hostSeen = now; Status = "CONNECTED AS PLAYER " + (LocalId + 1); Log("accepted as player " + LocalId); }
                return;
            }
            case Msg.State:
            {
                if (mode != Mode.Host || !peers.TryGetValue(remote.Key, out var peer)) return;
                peer.seen = now;
                peer.op = r.ReadString(); peer.pos = ReadV(r); peer.feet = r.ReadSingle(); peer.yaw = r.ReadSingle();
                byte flags = r.ReadByte(); peer.dead = (flags & 1) != 0; peer.shots = r.ReadUInt16(); peer.hasState = true;
                peer.seat = (flags >> 1) & 3;
                if (peer.seat == 1) { peer.horsePos = ReadV(r); peer.horseYaw = r.ReadSingle(); peer.horseSpeed = r.ReadSingle(); }
                return;
            }
            case Msg.World:
                if (mode == Mode.Client && SameHost(remote)) { hostSeen = now; ApplyWorld(r); }
                return;
            case Msg.Hit:
            {
                if (mode != Mode.Host || !peers.TryGetValue(remote.Key, out var peer)) return;
                peer.seen = now;
                ushort id = r.ReadUInt16(); float amount = r.ReadSingle(); var point = ReadV(r); var normal = ReadV(r);
                if (!zombiesById.TryGetValue(id, out var zombie) || zombie == null || !zombie.IsAlive) return;
                var game = ElderboomGame.Active;
                bool doublePoints = game != null && game.DoublePoints;
                RemoteShooter = peer.id;
                bool killed;
                try { killed = zombie.TakeDamage(Mathf.Min(amount, 5000f), peer.hasState ? peer.pos : point, point, normal); }
                finally { RemoteShooter = 0; }
                if (killed) { var w = Begin(Msg.Kill); w.Write((short)(doublePoints ? 200 : 100)); Send(peer.ep, true); }
                return;
            }
            case Msg.Effect:
            {
                if (mode != Mode.Host || !peers.TryGetValue(remote.Key, out var peer)) return;
                peer.seen = now;
                ushort id = r.ReadUInt16(); byte kind = r.ReadByte(); int count = r.ReadByte();
                var values = new float[count];
                for (int i = 0; i < count; i++) values[i] = r.ReadSingle();
                if (!zombiesById.TryGetValue(id, out var zombie) || zombie == null || !zombie.IsAlive) return;
                var game = ElderboomGame.Active;
                bool doublePoints = game != null && game.DoublePoints;
                RemoteShooter = peer.id;
                try { zombie.ApplyNetEffect(kind, values); }
                catch (Exception e) { Debug.LogWarning("[ElderboomLan] Effect " + kind + ": " + e.Message); }
                finally { RemoteShooter = 0; }
                if (!zombie.IsAlive) { var w = Begin(Msg.Kill); w.Write((short)(doublePoints ? 200 : 100)); Send(peer.ep, true); }
                return;
            }
            case Msg.Ignore:
            {
                if (mode != Mode.Host || !peers.TryGetValue(remote.Key, out var peer)) return;
                float seconds = r.ReadSingle();
                if (avatars.TryGetValue(peer.id, out var avatar) && avatar != null) avatar.IgnoreFor(seconds);
                return;
            }
            case Msg.Reject:
            {
                if (mode != Mode.Client) return;
                Fail(r.ReadString());
                return;
            }
            case Msg.Collect:
            {
                if (mode != Mode.Host || !peers.TryGetValue(remote.Key, out var peer)) return;
                int id = r.ReadInt32();
                var game = ElderboomGame.Active;
                var power = ElderboomPowerUp.All.Find(p => p != null && p.NetId == id);
                if (power != null) { power.gameObject.SetActive(false); Destroy(power.gameObject); game?.ActivatePowerUp(power.Kind); return; }
                var drop = ElderboomSpecialPickup.All.Find(p => p != null && p.NetId == id);
                if (drop != null && drop.Definition != null)
                {
                    var w = Begin(Msg.Grant); w.Write(drop.Definition.id); Send(peer.ep, true);
                    drop.gameObject.SetActive(false); Destroy(drop.gameObject);
                }
                return;
            }
            case Msg.PowerUp:
            {
                if (mode != Mode.Client || !SameHost(remote)) return;
                string kind = r.ReadString();
                ElderboomGame.Active?.ActivatePowerUp(kind);
                return;
            }
            case Msg.Grant:
            {
                if (mode != Mode.Client || !SameHost(remote)) return;
                var def = ElderboomWeapons.Find(r.ReadString());
                var arsenal = ElderboomGame.Active?.Player?.GetComponent<ElderboomArsenal>();
                if (def != null && arsenal != null) arsenal.ReceiveTemporary(def, 30);
                return;
            }
            case Msg.GateOpen:
            {
                if (mode != Mode.Host || !peers.TryGetValue(remote.Key, out var peer)) return;
                int index = r.ReadByte();
                var g = Gates();
                if (index < g.Length && g[index] != null) g[index].NetOpen();
                return;
            }
            case Msg.HorsePart:
            {
                if (mode != Mode.Host || !peers.TryGetValue(remote.Key, out var peer) || !peer.hasState || peer.dead || peer.seat != 0) return;
                byte item = r.ReadByte(); uint revision = r.ReadUInt32();
                ElderboomHorseAssembly.Instance?.TryInteract(item, peer.pos, !peer.dead, revision);
                return;
            }
            case Msg.Altar:
            {
                byte action = r.ReadByte();
                var altar = ElderboomAltarUpgrader.Instance;
                if (mode == Mode.Host && peers.TryGetValue(remote.Key, out var peer))
                {
                    if (action == 1) { altar?.ReleaseRemote(peer.id); return; }
                    bool allowed = action == 0 && peer.hasState && !peer.dead && peer.seat == 0
                        && altar != null && altar.ReserveRemote(peer.id, peer.pos);
                    var w = Begin(Msg.Altar); w.Write((byte)2); w.Write(allowed); Send(remote, true);
                }
                else if (mode == Mode.Client && SameHost(remote) && action == 2) altar?.NetworkGrant(r.ReadBoolean());
                return;
            }
            case Msg.Damage:
            {
                if (mode != Mode.Client || !SameHost(remote)) return;
                float amount = r.ReadSingle(); var source = ReadV(r);
                var player = ElderboomGame.Active?.Player;
                if (player != null && !player.IsDead) player.TakeDamage(amount, source);
                return;
            }
            case Msg.Kill:
            {
                if (mode != Mode.Client || !SameHost(remote)) return;
                int points = r.ReadInt16();
                ElderboomGame.Active?.NetAddKill(points);
                ElderboomGame.Active?.Player?.ConfirmHit();
                return;
            }
            case Msg.Leave:
                if (mode == Mode.Host) RemovePeer(remote.Key, "LEFT");
                else if (mode == Mode.Client && SameHost(remote)) { ElderboomGame.Active?.ShowNotice("THE HOST CLOSED THE GAME", 5f); hostSeen = -100f; }
                return;
        }
    }

    bool SameHost(Remote from) => host != null && (from.relay ? host.relay : !host.relay && from.ep.Address.Equals(host.ep.Address));

    void RemovePeer(string key, string why)
    {
        if (!peers.TryGetValue(key, out var peer)) return;
        peers.Remove(key);
        if (avatars.TryGetValue(peer.id, out var a) && a != null) Destroy(a.gameObject);
        avatars.Remove(peer.id);
        ElderboomGame.Active?.ShowNotice("PLAYER " + (peer.id + 1) + " " + why, 3f);
        Log("player " + peer.id + " " + why + " (silent " + (Time.unscaledTime - peer.seen).ToString("F1") + " s)");
    }

    ElderboomLanAvatar AvatarFor(int id, string op)
    {
        if (!avatars.TryGetValue(id, out var avatar) || avatar == null)
        {
            avatar = ElderboomLanAvatar.Create(id, op);
            avatars[id] = avatar;
        }
        else avatar.SetOperator(op);
        return avatar;
    }

    // ---- Local player -------------------------------------------------------------------

    static float FeetOffset(HDRPSlicePlayer player)
    {
        var cc = player.GetComponent<CharacterController>();
        if (cc == null) return 0f;
        return player.transform.position.y - player.transform.TransformPoint(cc.center - Vector3.up * (cc.height * .5f)).y;
    }

    // Clients start beside the host's spawn instead of inside it.
    void PlaceLocalPlayer(ElderboomGame game)
    {
        if (!game.IsPlaying || game.Session == placedSession) return;
        placedSession = game.Session;
        var player = game.Player;
        var cc = player.GetComponent<CharacterController>();
        Vector3 offset = player.transform.right * (1.3f * LocalId);
        Vector3 target = player.transform.position + offset;
        if (NavMesh.SamplePosition(target, out var hit, 2f, NavMesh.AllAreas)) target = hit.position + Vector3.up * FeetOffset(player);
        if (cc != null) cc.enabled = false;
        player.transform.position = target;
        if (cc != null) cc.enabled = true;
    }

    void SendState(ElderboomGame game)
    {
        var p = game.Player;
        var w = Begin(Msg.State);
        w.Write(game.Operator != null ? game.Operator.id : ElderboomMenu.Operator);
        Write(w, p.transform.position); w.Write(FeetOffset(p)); w.Write(p.transform.eulerAngles.y);
        w.Write((byte)((p.IsDead ? 1 : 0) | (LocalSeat() << 1))); w.Write((ushort)(game.ShotsFired & 0xFFFF));
        var horse = ElderboomMount.Instance;
        if (LocalSeat() == 1 && horse != null) { Write(w, horse.transform.position); w.Write(horse.transform.eulerAngles.y); w.Write(horse.Speed); }
        Send(host);
    }

    // ---- Host snapshot ------------------------------------------------------------------

    void SendWorld(ElderboomGame game)
    {
        if (peers.Count == 0) return;
        var p = game.Player;
        var w = Begin(Msg.World);
        w.Write((short)game.Wave); w.Write((short)game.Remaining);
        int count = 1; foreach (var peer in peers.Values) if (peer.hasState) count++;
        w.Write((byte)count);
        ResolveSeats();
        w.Write((byte)0); w.Write(game.Operator != null ? game.Operator.id : ElderboomMenu.Operator);
        Write(w, p.transform.position); w.Write(FeetOffset(p)); w.Write(p.transform.eulerAngles.y);
        w.Write((byte)((p.IsDead ? 1 : 0) | (LocalSeat() << 1))); w.Write((ushort)(game.ShotsFired & 0xFFFF));
        foreach (var peer in peers.Values)
        {
            if (!peer.hasState) continue;
            w.Write((byte)peer.id); w.Write(peer.op ?? "");
            Write(w, peer.pos); w.Write(peer.feet); w.Write(peer.yaw);
            w.Write((byte)((peer.dead ? 1 : 0) | (peer.seat << 1))); w.Write((ushort)peer.shots);
        }

        // Zombies: id, position, yaw, animator state and speed, flags.
        zombiesById.Clear();
        var list = game.Zombies;
        int zombieCount = 0;
        foreach (var z in list) if (z != null && z.gameObject.activeInHierarchy) zombieCount++;
        zombieCount = Mathf.Min(zombieCount, 72);
        w.Write((byte)zombieCount);
        int written = 0;
        foreach (var z in list)
        {
            if (z == null || !z.gameObject.activeInHierarchy || written >= zombieCount) continue;
            if (!zombieIds.TryGetValue(z, out var id)) { id = nextZombieId++; if (nextZombieId == 0) nextZombieId = 1; zombieIds[z] = id; }
            zombiesById[id] = z;
            if (!animators.TryGetValue(z, out var animator)) animators[z] = animator = z.GetComponentInChildren<Animator>();
            int hash = 0; float speed = 1f;
            if (animator != null && animator.isActiveAndEnabled)
            {
                var info = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
                hash = info.shortNameHash; speed = animator.speed;
            }
            w.Write(id); Write(w, z.transform.position); w.Write((short)Mathf.RoundToInt(z.transform.eulerAngles.y * 10f));
            w.Write(hash); w.Write((byte)Mathf.Clamp(Mathf.RoundToInt(speed * 50f), 0, 255));
            int variant = Mathf.Max(0, Array.IndexOf(Variants, z.Variant));
            w.Write((byte)((z.IsAlive ? 0 : 1) | (z.NetFemale ? 2 : 0) | (variant << 2)));
            written++;
        }
        WritePickupsAndGates(w);
        WriteHorse(w);
        var altar = ElderboomAltarUpgrader.Instance;
        w.Write((sbyte)(altar != null ? altar.NetworkOwner : -1));
        w.Write(altar != null ? altar.NetworkElapsed : 0f);
        // Forget ids of zombies that are gone.
        if (zombieIds.Count > zombiesById.Count + 8)
        {
            var stale = new List<ElderboomZombie>();
            foreach (var kv in zombieIds) if (kv.Key == null || !zombiesById.ContainsKey(kv.Value)) stale.Add(kv.Key);
            foreach (var z in stale) { zombieIds.Remove(z); animators.Remove(z); }
        }
        foreach (var peer in peers.Values) Send(peer.ep);
    }

    // ---- Client snapshot ----------------------------------------------------------------

    void ApplyWorld(BinaryReader r)
    {
        var game = ElderboomGame.Active;
        int wave = r.ReadInt16(), remaining = r.ReadInt16();
        int players = r.ReadByte();
        seenPlayers.Clear();
        for (int i = 0; i < players; i++)
        {
            int id = r.ReadByte(); string op = r.ReadString();
            var pos = ReadV(r); float feet = r.ReadSingle(), yaw = r.ReadSingle();
            int flags = r.ReadByte(); bool dead = (flags & 1) != 0; int shots = r.ReadUInt16();
            if (id == LocalId || game == null) continue;
            seenPlayers.Add(id);
            AvatarFor(id, op).Apply(pos, feet, yaw, dead, shots, (flags >> 1) & 3);
        }
        dropPlayers.Clear();
        foreach (var id in avatars.Keys) if (!seenPlayers.Contains(id)) dropPlayers.Add(id);
        foreach (var id in dropPlayers) { if (avatars[id] != null) Destroy(avatars[id].gameObject); avatars.Remove(id); }

        if (game == null) return;
        game.NetSetWave(wave, remaining);
        int zombies = r.ReadByte();
        float now = Time.unscaledTime;
        seenIds.Clear();
        for (int i = 0; i < zombies; i++)
        {
            ushort id = r.ReadUInt16(); var pos = ReadV(r); float yaw = r.ReadInt16() / 10f;
            int hash = r.ReadInt32(); float speed = r.ReadByte() / 50f; int flags = r.ReadByte();
            bool dead = (flags & 1) != 0, female = (flags & 2) != 0; int variant = Mathf.Clamp(flags >> 2, 0, Variants.Length - 1);
            seenIds.Add(id);
            if (deadIds.Contains(id)) continue;
            if (!proxies.TryGetValue(id, out var proxy) || proxy.zombie == null)
            {
                if (dead) continue;
                proxy = SpawnProxy(id, pos, yaw, female, variant, game);
                if (proxy == null) continue;
                proxies[id] = proxy;
            }
            proxy.pos = pos; proxy.yaw = yaw; proxy.seen = now;
            if (proxy.animator != null)
            {
                if (hash != 0 && hash != proxy.anim && proxy.animator.HasState(0, hash)) proxy.animator.CrossFadeInFixedTime(hash, .1f, 0);
                if (hash != 0) proxy.anim = hash;
                proxy.animator.speed = speed;
            }
            if (dead && !proxy.dead)
            {
                proxy.dead = true; deadIds.Add(id);
                proxy.zombie.NetDie();
                proxies.Remove(id);
            }
        }
        // Zombies the host no longer has (retired, despawned) disappear.
        dropIds.Clear();
        foreach (var kv in proxies) if (!seenIds.Contains(kv.Key)) dropIds.Add(kv.Key);
        foreach (var id in dropIds) { var p = proxies[id]; if (p.zombie != null) Destroy(p.zombie.gameObject); proxies.Remove(id); }
        deadIds.RemoveWhere(id => !seenIds.Contains(id));
        ReadPickupsAndGates(r);
        ReadHorse(r);
        int altarOwner = r.ReadSByte(); float altarTime = r.ReadSingle();
        ElderboomAltarUpgrader.Instance?.ApplyNetworkState(altarOwner, altarTime);
    }

    Proxy SpawnProxy(ushort id, Vector3 pos, float yaw, bool female, int variant, ElderboomGame game)
    {
        var prefab = Resources.Load<GameObject>(female ? "Elderboom/ZombieFemale" : "Elderboom/Zombie");
        if (prefab == null) prefab = Resources.Load<GameObject>("Elderboom/Zombie");
        if (prefab == null) return null;
        var go = Instantiate(prefab, pos, Quaternion.Euler(0f, yaw, 0f));
        go.name = "LanZombie_" + id;
        foreach (var agent in go.GetComponentsInChildren<NavMeshAgent>(true)) agent.enabled = false;
        var zombie = go.GetComponent<ElderboomZombie>() ?? go.GetComponentInChildren<ElderboomZombie>();
        if (zombie == null) { Destroy(go); return null; }
        zombie.NetProxy = true; zombie.NetId = id;
        zombie.SetVariant(Variants[variant]);
        game.NetTrackZombie(zombie);
        var animator = go.GetComponentInChildren<Animator>();
        if (animator != null) { animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; }
        return new Proxy { zombie = zombie, animator = animator, pos = pos, yaw = yaw };
    }

    void UpdateProxies()
    {
        float k = 1f - Mathf.Exp(-14f * Time.deltaTime);
        foreach (var proxy in proxies.Values)
        {
            if (proxy.zombie == null) continue;
            var t = proxy.zombie.transform;
            if ((t.position - proxy.pos).sqrMagnitude > 9f) t.position = proxy.pos;
            else t.position = Vector3.Lerp(t.position, proxy.pos, k);
            t.rotation = Quaternion.Slerp(t.rotation, Quaternion.Euler(0f, proxy.yaw, 0f), k);
        }
    }

    // ---- Addresses ----------------------------------------------------------------------

    static List<IPAddress> BroadcastAddresses()
    {
        var list = new List<IPAddress> { IPAddress.Broadcast };
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ua.Address)) continue;
                    IPAddress mask = null;
                    try { mask = ua.IPv4Mask; } catch { }
                    var a = ua.Address.GetAddressBytes();
                    var m = mask != null && !mask.Equals(IPAddress.Any) ? mask.GetAddressBytes() : new byte[] { 255, 255, 255, 0 };
                    var b = new byte[4];
                    for (int i = 0; i < 4; i++) b[i] = (byte)(a[i] | ~m[i]);
                    var address = new IPAddress(b);
                    if (!list.Contains(address)) list.Add(address);
                }
            }
        }
        catch (Exception e) { Debug.LogWarning("[ElderboomLan] Interfaces: " + e.Message); }
        return list;
    }

    static string FindLocalAddress()
    {
        try
        {
            // The interface the OS would route to the LAN (no packet is sent by Connect on UDP).
            using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                s.Connect("192.168.1.1", 65530);
                if (s.LocalEndPoint is IPEndPoint ep && !IPAddress.IsLoopback(ep.Address)) return ep.Address.ToString();
            }
        }
        catch { }
        try
        {
            foreach (var address in Dns.GetHostAddresses(Dns.GetHostName()))
                if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address)) return address.ToString();
        }
        catch { }
        return "?";
    }
}
