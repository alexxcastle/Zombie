using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Editor automation: Logs/claude_nettest.txt plays the game scene as an ONLINE HOST (the real ElderboomLan code),
// then a test client joins with the code through Unity Relay speaking the game's protocol (Join, State 20 Hz) and
// counts the host's snapshots for 15 s. Result in Logs/claude_nettest_result.txt.
[InitializeOnLoad]
static class ElderboomClaudeGameNetTest
{
    const string Flag = "Logs/claude_nettest.txt", Result = "Logs/claude_nettest_result.txt", Pending = "claude_nettest_pending";
    static double playSeen;
    static bool running;

    static ElderboomClaudeGameNetTest() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (SessionState.GetBool(Pending, false))
        {
            if (!EditorApplication.isPlaying) { if (!EditorApplication.isPlayingOrWillChangePlaymode) SessionState.SetBool(Pending, false); return; }
            if (running) return;
            if (playSeen <= 0) playSeen = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - playSeen < 6) return;
            SessionState.SetBool(Pending, false); playSeen = 0; running = true;
            _ = Run();
            return;
        }
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
        File.Delete(Flag);
        if (EditorSceneManager.GetActiveScene().isDirty) { File.WriteAllText(Result, "FAIL unsaved scene"); return; }
        File.WriteAllText(Result, "");
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        SessionState.SetBool(Pending, true);
        EditorApplication.isPlaying = true;
    }

    static void Log(string line) { File.AppendAllText(Result, DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + "\n"); }

    static byte[] Packet(byte msg, Action<BinaryWriter> body)
    {
        var ms = new MemoryStream(); var w = new BinaryWriter(ms);
        w.Write((byte)0xEB); w.Write((byte)2); w.Write(msg); body?.Invoke(w); w.Flush();
        return ms.ToArray();
    }

    static async Task Run()
    {
        NetworkDriver driver = default;
        try
        {
            HDRPSlicePlayer.DevInvulnerable = true;
            ElderboomLan.StartHostOnline("NORMAL");
            var lan = ElderboomLan.Instance;
            var until = DateTime.Now.AddSeconds(30);
            while (lan != null && !lan.HostReady && !lan.Failed && DateTime.Now < until) await Task.Delay(100);
            if (lan == null || !lan.HostReady) { Log("FAIL host not ready: " + (lan != null ? lan.Status : "no instance")); return; }
            Log("host ready, code " + lan.JoinCode);

            var join = await RelayService.Instance.JoinAllocationAsync(lan.JoinCode);
            var data = join.ToRelayServerData("dtls");
            var settings = new NetworkSettings(Allocator.Temp); settings.WithRelayParameters(ref data);
            driver = NetworkDriver.Create(settings);
            var fast = driver.CreatePipeline(typeof(FragmentationPipelineStage));
            var reliable = driver.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage));
            if (driver.Bind(NetworkEndpoint.AnyIpv4) != 0) { Log("FAIL bind"); return; }
            var conn = driver.Connect(data.Endpoint);

            void Send(NetworkPipeline pipe, byte[] bytes)
            {
                if (driver.BeginSend(pipe, conn, out var w) != 0) { Log("send failed"); return; }
                w.WriteBytes(new Span<byte>(bytes)); driver.EndSend(w);
            }

            bool connected = false, accepted = false; int worlds = 0, other = 0, maxSize = 0; double firstWorld = 0, lastWorld = 0;
            float maxGap = 0; var start = DateTime.Now; double nextState = 0, nextJoin = 0;
            while ((DateTime.Now - start).TotalSeconds < 20)
            {
                driver.ScheduleUpdate().Complete();
                double t = (DateTime.Now - start).TotalSeconds;
                NetworkEvent.Type ev;
                while ((ev = driver.PopEventForConnection(conn, out var reader)) != NetworkEvent.Type.Empty)
                {
                    if (ev == NetworkEvent.Type.Connect) { connected = true; Log("client connected to relay"); }
                    else if (ev == NetworkEvent.Type.Disconnect) { Log("FAIL client disconnected at " + t.ToString("F1") + " s"); return; }
                    else if (ev == NetworkEvent.Type.Data)
                    {
                        var bytes = new byte[reader.Length]; reader.ReadBytes(new Span<byte>(bytes));
                        if (bytes.Length < 3) continue;
                        if (bytes[2] == 3) { if (!accepted) Log("accepted as player " + bytes[3]); accepted = true; }
                        else if (bytes[2] == 5)
                        {
                            if (worlds == 0) firstWorld = t;
                            else maxGap = Mathf.Max(maxGap, (float)(t - lastWorld));
                            lastWorld = t; worlds++; maxSize = Mathf.Max(maxSize, bytes.Length);
                        }
                        else other++;
                    }
                }
                if (connected && !accepted && t >= nextJoin) { nextJoin = t + .4; Send(reliable, Packet(2, w => w.Write("ELI"))); }
                if (accepted && t >= nextState)
                {
                    nextState = t + .05;
                    var p = ElderboomGame.Active != null && ElderboomGame.Active.Player != null ? ElderboomGame.Active.Player.transform.position + Vector3.right * 2f : Vector3.zero;
                    Send(fast, Packet(4, w => { w.Write("ELI"); w.Write(p.x); w.Write(p.y); w.Write(p.z); w.Write(0f); w.Write(0f); w.Write((byte)0); w.Write((ushort)0); }));
                }
                await Task.Delay(16);
            }
            Log("connected=" + connected + " accepted=" + accepted + " snapshots=" + worlds + " (first at " + firstWorld.ToString("F1") + " s, max gap " + maxGap.ToString("F2") + " s, max size " + maxSize + " B) other=" + other);
            Log(worlds > 100 && maxGap < 2 ? "RESULT OK" : "RESULT FAIL");
        }
        catch (Exception e) { Log("FAIL " + e); }
        finally
        {
            if (driver.IsCreated) driver.Dispose();
            ElderboomLan.Shutdown();
            running = false;
            EditorApplication.isPlaying = false;
        }
    }
}
