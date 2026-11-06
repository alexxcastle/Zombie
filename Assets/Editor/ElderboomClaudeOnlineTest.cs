using System;
using System.IO;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEditor;
using UnityEngine;

// Editor automation: Logs/claude_online_test.txt runs an end-to-end Relay check without play mode:
// sign in anonymously, host an allocation and get a join code, join it with a second driver, connect,
// send a message both ways. Result in Logs/claude_online_test_result.txt.
[InitializeOnLoad]
static class ElderboomClaudeOnlineTest
{
    const string Flag = "Logs/claude_online_test.txt", Result = "Logs/claude_online_test_result.txt";
    static double nextPoll;
    static bool running;
    static double playSeen;

    static ElderboomClaudeOnlineTest() { EditorApplication.update += Poll; }

    // Unity Services only start in play mode: the flag enters play mode (menu scene), the test runs, play mode ends.
    const string Pending = "claude_online_test_pending";
    static void Poll()
    {
        if (running || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        if (SessionState.GetBool(Pending, false))
        {
            if (!EditorApplication.isPlaying) { if (!EditorApplication.isPlayingOrWillChangePlaymode) SessionState.SetBool(Pending, false); return; }
            if (playSeen <= 0) playSeen = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - playSeen < 3) return;
            playSeen = 0;
            SessionState.SetBool(Pending, false);
            running = true;
            _ = Run();
            return;
        }
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Flag);
        File.WriteAllText(Result, "");
        var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        if (!scene.isDirty && File.Exists("Assets/Scenes/ZombieTest_Menu.unity"))
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/ZombieTest_Menu.unity");
        SessionState.SetBool(Pending, true);
        EditorApplication.isPlaying = true;
    }

    static void Log(string line)
    {
        File.AppendAllText(Result, DateTime.Now.ToString("HH:mm:ss") + " " + line + "\n");
        Debug.Log("[ElderboomClaudeOnlineTest] " + line);
    }

    static async Task Run()
    {
        NetworkDriver hostDriver = default, clientDriver = default;
        try
        {
            await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
            Log("signed in " + AuthenticationService.Instance.PlayerId);

            var allocation = await RelayService.Instance.CreateAllocationAsync(3);
            string code = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Log("allocation " + allocation.Region + " code " + code);
            var hostData = allocation.ToRelayServerData("dtls");
            var hs = new NetworkSettings(Allocator.Temp); hs.WithRelayParameters(ref hostData);
            hostDriver = NetworkDriver.Create(hs);
            var hostPipe = hostDriver.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage));
            if (hostDriver.Bind(NetworkEndpoint.AnyIpv4) != 0 || hostDriver.Listen() != 0) throw new Exception("host bind/listen failed");

            var join = await RelayService.Instance.JoinAllocationAsync(code);
            Log("joined allocation with code");
            var clientData = join.ToRelayServerData("dtls");
            var cs = new NetworkSettings(Allocator.Temp); cs.WithRelayParameters(ref clientData);
            clientDriver = NetworkDriver.Create(cs);
            var clientPipe = clientDriver.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage));
            if (clientDriver.Bind(NetworkEndpoint.AnyIpv4) != 0) throw new Exception("client bind failed");
            var toHost = clientDriver.Connect(clientData.Endpoint);

            NetworkConnection toClient = default;
            bool clientConnected = false, hostGot = false, clientGot = false;
            var until = DateTime.Now.AddSeconds(30);
            while (DateTime.Now < until && !(hostGot && clientGot))
            {
                hostDriver.ScheduleUpdate().Complete();
                clientDriver.ScheduleUpdate().Complete();
                NetworkConnection c;
                while ((c = hostDriver.Accept()) != default(NetworkConnection)) { toClient = c; Log("host accepted a connection"); }
                NetworkEvent.Type ev;
                while ((ev = clientDriver.PopEventForConnection(toHost, out var reader)) != NetworkEvent.Type.Empty)
                {
                    if (ev == NetworkEvent.Type.Connect && !clientConnected)
                    {
                        clientConnected = true; Log("client connected through relay");
                        if (clientDriver.BeginSend(clientPipe, toHost, out var w) == 0) { w.WriteInt(1234); clientDriver.EndSend(w); }
                    }
                    else if (ev == NetworkEvent.Type.Data) { clientGot = reader.ReadInt() == 4321; Log("client received reply " + clientGot); }
                    else if (ev == NetworkEvent.Type.Disconnect) throw new Exception("client disconnected");
                }
                if (toClient != default(NetworkConnection))
                    while ((ev = hostDriver.PopEventForConnection(toClient, out var reader)) != NetworkEvent.Type.Empty)
                    {
                        if (ev == NetworkEvent.Type.Data)
                        {
                            hostGot = reader.ReadInt() == 1234; Log("host received message " + hostGot);
                            if (hostDriver.BeginSend(hostPipe, toClient, out var w) == 0) { w.WriteInt(4321); hostDriver.EndSend(w); }
                        }
                    }
                await Task.Delay(20);
            }
            Log(hostGot && clientGot ? "RESULT OK" : "RESULT FAIL (timeout) connected=" + clientConnected + " hostGot=" + hostGot + " clientGot=" + clientGot);
        }
        catch (Exception e) { Log("RESULT FAIL " + e.GetType().Name + ": " + e.Message); }
        finally
        {
            if (clientDriver.IsCreated) clientDriver.Dispose();
            if (hostDriver.IsCreated) hostDriver.Dispose();
            running = false;
            EditorApplication.isPlaying = false;
        }
    }
}
