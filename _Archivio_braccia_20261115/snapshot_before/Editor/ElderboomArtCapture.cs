using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

// Reproducible comparison using the review's camera coordinates and the saved lighting.
[InitializeOnLoad]
public static class ElderboomArtCapture
{
    const string Pending="elderboom.art.capture";
    static double next;
    static int index;
    static Camera camera;
    static string[] views;
    static bool finished;
    static ElderboomArtCapture(){EditorApplication.update+=Tick;}
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        Directory.CreateDirectory("Logs/ArtReview/After");
        using(var sha=SHA256.Create())
            File.WriteAllLines("Logs/ArtReview/After/capture-source.txt",new[]{"UTC="+DateTime.UtcNow.ToString("O"),"SceneSHA256="+BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes("Assets/Scenes/Elderboom_Unity_Port.unity"))).Replace("-",""),"TerrainSHA256="+BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes("Assets/Terrain/Elderboom_Valley.asset"))).Replace("-",""),"Camera: perspective, FOV65, 1600x900, saved scene lighting, no inspection lights. Play Mode capture without saving scene."});
        var chapel=GameObject.Find("CentralChapel_Scale_1_25");
        if(chapel)File.WriteAllLines("Logs/ArtReview/After/chapel-parts.txt",chapel.GetComponentsInChildren<Renderer>().Select(r=>r.name+" "+r.bounds));
        SessionState.SetBool(Pending,true);EditorApplication.isPlaying=true;
    }
    static Vector3 Parse(string value)
    {var p=value.Split(',').Select(x=>float.Parse(x,System.Globalization.CultureInfo.InvariantCulture)).ToArray();return new Vector3(p[0],p[1],p[2]);}
    static void Tick()
    {
        if(!SessionState.GetBool(Pending,false))return;
        if(finished){if(!EditorApplication.isPlayingOrWillChangePlaymode){SessionState.SetBool(Pending,false);EditorApplication.Exit(0);}return;}
        if(!EditorApplication.isPlaying)return;
        try
        {
            if(next==0){next=EditorApplication.timeSinceStartup+5;return;}
            if(EditorApplication.timeSinceStartup<next)return;
            if(!camera)
            {
                var game=ElderboomGame.Active;if(game){game.StopAllCoroutines();game.enabled=false;foreach(var z in game.Zombies.ToArray())if(z)Object.Destroy(z.gameObject);}
                foreach(var player in Object.FindObjectsByType<HDRPSlicePlayer>(FindObjectsSortMode.None))player.gameObject.SetActive(false);
                var go=new GameObject("Art_Review_Camera");go.AddComponent<AudioListener>();camera=go.AddComponent<Camera>();go.AddComponent<HDAdditionalCameraData>();camera.fieldOfView=65;camera.nearClipPlane=.1f;camera.farClipPlane=1300;
                views=File.ReadAllLines("Logs/ArtReview/tour.txt").Concat(File.ReadAllLines("Logs/ArtReview/details-tour.txt")).Where(l=>l.Contains(" eye=")).Concat(new[]{
                    "31-sawbench eye=(-17.4, 3.2, 65.2) target=(-22.3, 2.0, 68.3)",
                    "32-log-receiving eye=(-13.5, 3.1, 52.0) target=(-17.0, 2.1, 58.0)",
                    "33-burial-garden eye=(67.0, 3.0, -22.0) target=(73.0, 2.2, -7.0)",
                    "34-baker-yard eye=(-4.0, 2.7, -0.6) target=(-5.72, 2.1, 3.8)",
                    "35-carpenter-yard eye=(-33.0, 2.7, -12.0) target=(-28.5, 2.0, -14.0)",
                    "36-wash-house eye=(46.0, 2.7, 24.5) target=(43.8, 2.1, 19.4)",
                    "37-sawmill-puddles eye=(-19.0, 2.8, 62.0) target=(-23.3, 1.1, 64.9)",
                    "38-farm-ruts eye=(-56.0, 2.8, -23.0) target=(-62.1, 1.0, -21.0)",
                    "39-cut-bank eye=(-14.0, 2.8, -41.0) target=(-10.0, -0.7, -47.7)"
                }).ToArray();
                Directory.CreateDirectory("Logs/ArtReview/After");
            }
            if(index>=views.Length){File.WriteAllLines("Logs/ArtReview/After/captured-views.txt",views);finished=true;EditorApplication.isPlaying=false;return;}
            var m=Regex.Match(views[index],@"^(\S+) eye=\(([^)]+)\) target=\(([^)]+)\)");
            if(!m.Success){index++;return;}
            var pos=Parse(m.Groups[2].Value);var target=Parse(m.Groups[3].Value);camera.transform.SetPositionAndRotation(pos,Quaternion.LookRotation(target-pos));
            var rt=new RenderTexture(1600,900,24);var tex=new Texture2D(1600,900,TextureFormat.RGB24,false);camera.targetTexture=rt;
            for(int i=0;i<8;i++)camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1600,900),0,0);tex.Apply();
            File.WriteAllBytes("Logs/ArtReview/After/"+m.Groups[1].Value+".jpg",tex.EncodeToJPG(90));
            RenderTexture.active=null;camera.targetTexture=null;Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);
            Debug.Log("Art capture "+m.Groups[1].Value);index++;next=EditorApplication.timeSinceStartup+.15;
        }
        catch(Exception e){Debug.LogException(e);SessionState.SetBool(Pending,false);EditorApplication.Exit(1);}
    }
}
