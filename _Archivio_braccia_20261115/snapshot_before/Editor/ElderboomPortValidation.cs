using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

public static class ElderboomPortValidation
{
    const string Scene="Assets/Scenes/Elderboom_Unity_Port.unity";
    static int errors;
    static bool running;

    public static void ValidateSaved()
    {
        EditorSceneManager.OpenScene(Scene,OpenSceneMode.Single);
        int missing=0;
        foreach(var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            foreach(var node in root.GetComponentsInChildren<Transform>(true))missing+=GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(node.gameObject);
        Require(missing==0,"Scene contains "+missing+" missing scripts");
        Require(UnityEngine.Object.FindObjectsByType<ElderboomSpawnPoint>(FindObjectsSortMode.None).Length>=4,"Not enough connected spawns");
        Require(UnityEngine.Object.FindAnyObjectByType<ElderboomGame>()!=null,"Game director missing");
        Require(UnityEngine.Object.FindAnyObjectByType<ElderboomNavigation>()?.data!=null,"Navigation asset missing");
        Require(UnityEngine.Object.FindAnyObjectByType<HDRPSlicePlayer>()!=null,"FPS controller missing");
        Require(UnityEngine.Object.FindObjectsByType<ElderboomStation>(FindObjectsSortMode.None).Length==6,"Station count differs from Elderboom");
        foreach(string key in new[]{"Elderboom/Zombie","Elderboom/ZombieFemale"})
        {
            var prefab=Resources.Load<GameObject>(key);Require(prefab!=null,"Missing "+key);
            Require(prefab.GetComponent<ElderboomZombie>()!=null,"Missing zombie component "+key);
        }
        foreach(var definition in ElderboomWeapons.All.Where(w=>!w.IsOperatorPrimary))Require(Resources.Load<GameObject>(definition.resource)!=null,"Missing weapon prefab "+definition.name);
        Directory.CreateDirectory("Logs");File.WriteAllText("Logs/elderboom_saved_validation.json","{\"status\":\"passed\",\"missingScripts\":0,\"weapons\":"+ElderboomWeapons.All.Length+"}");
        Debug.Log("ELDERBOOM_SAVED_VALIDATION_PASSED");
    }

    public static void Smoke()
    {
        ValidateSaved();running=true;errors=0;
        Application.logMessageReceived+=OnLog;
        EditorApplication.playModeStateChanged+=OnPlayState;
        EditorApplication.isPlaying=true;
    }
    static void OnLog(string text,string trace,LogType type)
    {
        if(running && (type==LogType.Error || type==LogType.Exception || type==LogType.Assert))errors++;
    }
    static void OnPlayState(PlayModeStateChange state)
    {
        if(state==PlayModeStateChange.EnteredPlayMode)
        {
            // Domain reload resets static fields. Restore the callback state.
            running=true;Application.logMessageReceived-=OnLog;Application.logMessageReceived+=OnLog;
            var game=UnityEngine.Object.FindAnyObjectByType<ElderboomGame>();ElderboomValidationHost.Create().StartCoroutine(Verify(game));
        }
    }
    [InitializeOnLoadMethod]
    static void RestoreCallback()
    {
        if(Environment.GetCommandLineArgs().Contains("-executeMethod") && Environment.GetCommandLineArgs().Contains("ElderboomPortValidation.Smoke"))
        {EditorApplication.playModeStateChanged-=OnPlayState;EditorApplication.playModeStateChanged+=OnPlayState;}
    }
    static IEnumerator Verify(ElderboomGame game)
    {
        yield return null;
        game.BeginSession("SOPH","NORMAL");
        yield return new WaitForSeconds(4f);
        var zombies=UnityEngine.Object.FindObjectsByType<ElderboomZombie>(FindObjectsSortMode.None);
        if(zombies.Length==0){Debug.LogError("Smoke: wave did not produce zombies");}
        else
        {
            var zombie=zombies[0];Vector3 before=zombie.transform.position;
            zombie.ApplyPlasmaBlind(2);if(!zombie.PlasmaBlinded)Debug.LogError("Smoke: permanent blindness was not applied");
            zombie.FreezeFor(.2f);yield return new WaitForSeconds(.3f);
            zombie.TakeDamage(999,Vector3.zero);if(game.Credits!=1100)Debug.LogError("Smoke: kill credits not awarded exactly once");
        }
        var player=UnityEngine.Object.FindAnyObjectByType<HDRPSlicePlayer>();
        if(player.GetComponent<ElderboomArsenal>()==null)Debug.LogError("Smoke: arsenal failed to initialize");
        float previous=player.Health;player.TakeDamage(10);player.Heal(5);if(Mathf.Abs(player.Health-(previous-5))>.1f)Debug.LogError("Smoke: health transition failed");
        yield return null;
        File.WriteAllText("Logs/elderboom_runtime_smoke.json","{\"status\":\""+(errors==0?"passed":"failed")+"\",\"errors\":"+errors+",\"spawnedZombies\":"+zombies.Length+"}");
        Debug.Log("ELDERBOOM_RUNTIME_SMOKE_"+(errors==0?"PASSED":"FAILED"));
        int exit=errors==0?0:1;running=false;EditorApplication.isPlaying=false;EditorApplication.delayCall+=()=>EditorApplication.Exit(exit);
    }
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
