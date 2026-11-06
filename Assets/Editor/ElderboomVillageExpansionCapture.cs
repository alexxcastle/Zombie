using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object=UnityEngine.Object;

static class ElderboomVillageExpansionCapture
{
    public static void Run()
    {
        int index=int.Parse(Environment.GetEnvironmentVariable("ELDERBOOM_EXPANSION_VIEW")??"0");
        string folder="Logs/VillageExpansion";Directory.CreateDirectory(folder);
        try
        {
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
            if(index==0)ElderboomClaudeBlocks.RefreshExpansionApproaches();
            var root=GameObject.Find("Elderboom_Village_Expansion").transform;
            var ids=new[]{"apothecary","granary","cooper_workshop","smokehouse","river"};
            var centers=new[]{new Vector3(-26,2,17),new Vector3(-84,2,-2),new Vector3(-33,2,75.5f),new Vector3(36,2,-81),new Vector3(7,1,-48)};
            var target=centers[index];
            foreach(var player in Object.FindObjectsByType<HDRPSlicePlayer>(FindObjectsInactive.Include))Object.DestroyImmediate(player.transform.root.gameObject);
            foreach(var game in Object.FindObjectsByType<ElderboomGame>(FindObjectsInactive.Include))Object.DestroyImmediate(game.gameObject);
            foreach(var t in Object.FindObjectsByType<Terrain>())t.drawTreesAndFoliage=false;
            // Keep a local neighborhood and the actual saved lighting. This is an
            // inspection-only scene; the full terrain/foliage stays saved in the map.
            foreach(var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
            {
                if(r==null || r is ParticleSystemRenderer)continue;
                var b=r.bounds;float dx=Mathf.Max(b.min.x-target.x,0,target.x-b.max.x),dz=Mathf.Max(b.min.z-target.z,0,target.z-b.max.z);
                if(dx*dx+dz*dz>42*42)Object.DestroyImmediate(r.gameObject);
            }
            EditorUtility.UnloadUnusedAssetsImmediate();GC.Collect();
            Vector3 eye,look;
            if(index<4)
            {
                var building=root.Find(ids[index]);var rs=building.GetComponentsInChildren<Renderer>();var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);
                float d=Mathf.Max(b.size.x,b.size.z)*1.55f;
                look=b.center-Vector3.up*b.size.y*.12f;
                eye=look+building.forward*d+building.right*d*.6f+Vector3.up*b.size.y*.6f;
            }
            else {eye=new Vector3(18,5.5f,-59);look=new Vector3(3,1.5f,-47);}
            var go=new GameObject("Expansion_Review_Camera");var camera=go.AddComponent<Camera>();go.AddComponent<HDAdditionalCameraData>();
            camera.fieldOfView=58;camera.nearClipPlane=.1f;camera.farClipPlane=180;
            go.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(look-eye));
            var rt=new RenderTexture(1280,900,24);var tex=new Texture2D(1280,900,TextureFormat.RGB24,false);camera.targetTexture=rt;
            for(int i=0;i<5;i++)camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1280,900),0,0);tex.Apply();
            File.WriteAllBytes(folder+"/"+ids[index]+"-placed.jpg",tex.EncodeToJPG(93));
            File.WriteAllText(folder+"/"+ids[index]+"-capture.txt",$"Actual saved map neighborhood, saved lighting. Distant objects and terrain tree rendering disabled for inspection only. eye={eye:F2}, target={look:F2}. No scene edits saved.");
            RenderTexture.active=null;camera.targetTexture=null;Object.DestroyImmediate(tex);Object.DestroyImmediate(rt);Object.DestroyImmediate(go);
            EditorApplication.Exit(0);
        }
        catch(Exception e){File.WriteAllText(folder+"/capture-error-"+index+".txt",e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
    }
}
