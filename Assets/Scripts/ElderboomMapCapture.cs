// Test / preview tooling: compiled in the Editor and development builds only.
#if UNITY_EDITOR || DEBUG
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public static class ElderboomMapCapture
{
    public static IEnumerator Capture(string directory)
    {
        Directory.CreateDirectory(directory);
        var cameraObject=new GameObject("Orthographic map export");
        var camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;camera.aspect=1;
        camera.nearClipPlane=1;camera.farClipPlane=1600;camera.cullingMask=~(1<<19);
        var hd=cameraObject.AddComponent<HDAdditionalCameraData>();
        hd.clearColorMode=HDAdditionalCameraData.ClearColorMode.Color;hd.backgroundColorHDR=new Color(.025f,.035f,.04f);
        hd.antialiasing=HDAdditionalCameraData.AntialiasingMode.None;
        var volume=new GameObject("Map export daylight").AddComponent<Volume>();volume.isGlobal=true;volume.priority=1000;
        volume.sharedProfile=ScriptableObject.CreateInstance<VolumeProfile>();
        var exposure=volume.sharedProfile.Add<Exposure>(true);exposure.mode.Override(ExposureMode.Fixed);exposure.fixedExposure.Override(12.2f);
        volume.sharedProfile.Add<Fog>(true).enabled.Override(false);
        volume.sharedProfile.Add<MotionBlur>(true).intensity.Override(0);
        volume.sharedProfile.Add<DepthOfField>(true).focusMode.Override(DepthOfFieldMode.Off);
        volume.sharedProfile.Add<Bloom>(true).intensity.Override(0);
        // Temporary daylight is only for the exported map; no scene/profile is saved.
        foreach(var light in Object.FindObjectsByType<Light>())if(light.type==LightType.Directional)
        {light.color=new Color(1,.96f,.9f);light.intensity=80000;light.transform.rotation=Quaternion.Euler(58,25,0);}
        var bounds=new Bounds();bool found=false;
        foreach(var terrain in Object.FindObjectsByType<Terrain>())
        {
            var b=new Bounds(terrain.transform.position+terrain.terrainData.size*.5f,terrain.terrainData.size);
            if(!found){bounds=b;found=true;}else bounds.Encapsulate(b);
            terrain.basemapDistance=2500;terrain.treeDistance=2500;terrain.treeMaximumFullLODCount=8000;terrain.detailObjectDistance=0;
        }
        if(!found)throw new System.InvalidOperationException("No map terrain found");
        foreach(var other in Object.FindObjectsByType<Camera>())if(other!=camera)other.enabled=false;
        var report=new System.Text.StringBuilder("Orthographic: true\nUp on image: Unity +Z\nDaylight export only; gameplay lighting unchanged on disk.\n");
        float span=Mathf.Max(bounds.size.x,bounds.size.z)*1.025f;
        SetView(camera,bounds.center,span,bounds.max.y+300);
        for(int i=0;i<6;i++)yield return null;
        Debug.Log("MAP_EXPORT_FULL_TERRAIN");
        string full=Path.Combine(directory,"Elderboom_mappa_completa_ortografica_4096.png");
        report.AppendLine("Terrain bounds: "+bounds+"; image span metres="+span.ToString("F2"));
        report.AppendLine(ElderboomAbilityShots.Capture(camera,full,4096,4096));
        // Include the entire authored settlement, its outer courtyards and approaches.
        var village=new Bounds(new Vector3(5.7f,0,-.7f),new Vector3(190,120,190));
        SetView(camera,village.center,190,260);
        for(int i=0;i<4;i++)yield return null;
        Debug.Log("MAP_EXPORT_VILLAGE");
        string detail=Path.Combine(directory,"Elderboom_villaggio_ortografica_4096.png");
        report.AppendLine("Village bounds: "+village+"; image span metres=190.00");
        report.AppendLine(ElderboomAbilityShots.Capture(camera,detail,4096,4096));
        File.WriteAllText(Path.Combine(directory,"map_export.txt"),report.ToString());
        Object.Destroy(cameraObject);Object.Destroy(volume.gameObject);
    }
    static void SetView(Camera camera,Vector3 center,float width,float height)
    {
        camera.orthographicSize=width*.5f;
        camera.transform.SetPositionAndRotation(new Vector3(center.x,height,center.z),Quaternion.LookRotation(Vector3.down,Vector3.forward));
    }
}
#endif
