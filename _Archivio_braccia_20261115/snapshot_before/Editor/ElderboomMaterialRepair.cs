using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

public static class ElderboomMaterialRepair
{
    public static bool NativeHdrp(Material source) => source!=null && source.shader!=null &&
        (source.shader.name.StartsWith("HDRP/") || source.GetTag("RenderPipeline",false,"")=="HDRenderPipeline"
            // glTFast ships one graph with several pipeline subshaders. GetTag
            // can inspect its first subshader rather than the active HDRP target.
            || (source.shader.name.IndexOf("glTF",StringComparison.OrdinalIgnoreCase)>=0 && source.shader.isSupported));
    static Texture Texture(Material source,params string[] names)
    {
        foreach(string name in names)if(source.HasProperty(name) && source.GetTexture(name)!=null)return source.GetTexture(name);
        return null;
    }
    static float Float(Material source,float fallback,params string[] names)
    {foreach(string name in names)if(source.HasProperty(name))return source.GetFloat(name);return fallback;}
    static Color ColorValue(Material source,Color fallback,params string[] names)
    {foreach(string name in names)if(source.HasProperty(name))return source.GetColor(name);return fallback;}
    static Texture2D FindMap(Material source,string kind)
    {
        string asset=AssetDatabase.GetAssetPath(source);
        if(string.IsNullOrEmpty(asset))return null;
        string directory=Path.GetDirectoryName(asset)?.Replace('\\','/');
        if(string.IsNullOrEmpty(directory))return null;
        string folder=directory.EndsWith("/source",StringComparison.OrdinalIgnoreCase)?Path.GetDirectoryName(directory).Replace('\\','/')+"/textures":directory;
        if(!AssetDatabase.IsValidFolder(folder))return null;
        bool arms=source.name.IndexOf("arm",StringComparison.OrdinalIgnoreCase)>=0 || source.name.IndexOf("hand",StringComparison.OrdinalIgnoreCase)>=0;
        var candidates=AssetDatabase.FindAssets("t:Texture2D",new[]{folder}).Select(AssetDatabase.GUIDToAssetPath)
            .Where(p=>(Path.GetFileName(p).ToLowerInvariant().Contains("arm")||Path.GetFileName(p).ToLowerInvariant().Contains("hand"))==arms).ToArray();
        string[] tokens=kind=="color"?new[]{"color","albedo","diffuse"}:kind=="normal"?new[]{"normal"}:kind=="ao"?new[]{"ao","occlusion"}:new[]{kind};
        string path=candidates.FirstOrDefault(p=>tokens.Any(t=>Path.GetFileNameWithoutExtension(p).IndexOf(t,StringComparison.OrdinalIgnoreCase)>=0));
        return path==null?null:AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    static void DataTexture(Texture texture,bool normal=false)
    {
        if(texture==null)return;
        var importer=AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;if(importer==null)return;
        bool dirty=importer.sRGBTexture || (normal && importer.textureType!=TextureImporterType.NormalMap);
        if(!dirty)return;
        importer.sRGBTexture=false;if(normal)importer.textureType=TextureImporterType.NormalMap;
        importer.SaveAndReimport();
    }
    static Color[] Pixels(Texture texture,int width,int height,Color fallback)
    {
        if(texture==null)return Enumerable.Repeat(fallback,width*height).ToArray();
        var rt=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
        var previous=RenderTexture.active;Graphics.Blit(texture,rt);RenderTexture.active=rt;
        var copy=new Texture2D(width,height,TextureFormat.RGBA32,false,true);copy.ReadPixels(new Rect(0,0,width,height),0,0);copy.Apply();
        var pixels=copy.GetPixels();UnityEngine.Object.DestroyImmediate(copy);RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);return pixels;
    }
    public static Material Convert(Material source,string identity)
    {
        if(source==null || NativeHdrp(source))return source;
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source,out string guid,out long local);
        string name=string.Concat(source.name.Select(c=>char.IsLetterOrDigit(c)||c=='_'?c:'_'));
        string path="Assets/Materials/PBR_"+identity+"_"+name+"_"+(guid??"").Substring(0,Mathf.Min(8,(guid??"").Length))+"_"+local+".mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null){material=new Material(Shader.Find("HDRP/Lit"));AssetDatabase.CreateAsset(material,path);}
        var color=Texture(source,"_BaseMap","_MainTex","_BaseColorMap")??FindMap(source,"color");
        var normal=Texture(source,"_BumpMap","_NormalMap")??FindMap(source,"normal");
        var metallic=Texture(source,"_MetallicGlossMap");
        var specular=Texture(source,"_SpecGlossMap");
        var separateMetal=metallic==null?FindMap(source,"metallic"):null;
        var roughness=FindMap(source,"roughness");var smoothness=roughness==null?FindMap(source,"smoothness"):null;
        var ao=Texture(source,"_OcclusionMap")??FindMap(source,"ao");
        DataTexture(normal,true);DataTexture(metallic);DataTexture(specular);DataTexture(separateMetal);DataTexture(roughness);DataTexture(smoothness);DataTexture(ao);
        material.SetTexture("_BaseColorMap",color);material.SetColor("_BaseColor",ColorValue(source,Color.white,"_BaseColor","_Color"));
        if(normal!=null){material.SetTexture("_NormalMap",normal);material.SetFloat("_NormalScale",Float(source,1,"_BumpScale","_NormalScale"));}
        material.SetFloat("_Metallic",metallic!=null||separateMetal!=null?1:Float(source,0,"_Metallic"));
        material.SetFloat("_Smoothness",Float(source,.5f,"_Smoothness","_Glossiness"));
        if(metallic!=null||separateMetal!=null||roughness!=null||smoothness!=null||specular!=null||ao!=null)
        {
            Texture sizeMap=metallic??separateMetal??roughness??smoothness??specular??ao;
            int width=sizeMap.width,height=sizeMap.height;
            var metalPixels=Pixels(metallic??separateMetal,width,height,new Color(Float(source,0,"_Metallic"),0,0,1));
            var roughPixels=Pixels(roughness,width,height,Color.black);var smoothPixels=Pixels(smoothness,width,height,Color.white);
            var specPixels=Pixels(specular,width,height,Color.white);var aoPixels=Pixels(ao,width,height,Color.white);
            var alphaPixels=Float(source,0,"_SmoothnessTextureChannel")==1?Pixels(color,width,height,Color.white):null;
            var output=new Color[width*height];
            for(int i=0;i<output.Length;i++)
            {
                float smooth=roughness!=null?1-roughPixels[i].r:smoothness!=null?smoothPixels[i].r:
                    alphaPixels!=null?alphaPixels[i].a:metallic!=null?metalPixels[i].a:specular!=null?specPixels[i].a:Float(source,.5f,"_Smoothness","_Glossiness");
                if(metallic!=null||specular!=null)smooth*=Float(source,1,"_GlossMapScale");
                output[i]=new Color(metalPixels[i].r,aoPixels[i].g,1,Mathf.Clamp01(smooth));
            }
            var mask=new Texture2D(width,height,TextureFormat.RGBA32,false,true);mask.SetPixels(output);mask.Apply();
            string maskPath=Path.ChangeExtension(path,".png");File.WriteAllBytes(maskPath,mask.EncodeToPNG());UnityEngine.Object.DestroyImmediate(mask);
            AssetDatabase.ImportAsset(maskPath);var importer=(TextureImporter)AssetImporter.GetAtPath(maskPath);importer.sRGBTexture=false;importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.SaveAndReimport();
            material.SetTexture("_MaskMap",AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath));
            material.SetFloat("_SmoothnessRemapMin",0);material.SetFloat("_SmoothnessRemapMax",1);
            material.SetFloat("_AORemapMin",0);material.SetFloat("_AORemapMax",1);
        }
        if(specular!=null){material.SetFloat("_MaterialID",4);material.SetTexture("_SpecularColorMap",specular);material.SetColor("_SpecularColor",ColorValue(source,Color.white,"_SpecColor"));}
        var emission=Texture(source,"_EmissionMap","_EmissiveColorMap");material.SetTexture("_EmissiveColorMap",emission);
        material.SetColor("_EmissiveColor",ColorValue(source,Color.black,"_EmissionColor","_EmissiveColor"));
        bool transparent=Float(source,0,"_Surface")>0 || Float(source,0,"_Mode")>=2;
        material.SetFloat("_SurfaceType",transparent?1:0);material.SetFloat("_AlphaCutoffEnable",source.IsKeywordEnabled("_ALPHATEST_ON")?1:0);
        material.SetFloat("_AlphaCutoff",Float(source,.5f,"_Cutoff"));
        foreach(string target in new[]{"_BaseColorMap","_MaskMap","_NormalMap"})
        {
            string from=source.HasProperty("_BaseMap")?"_BaseMap":source.HasProperty("_MainTex")?"_MainTex":null;
            if(from!=null){material.SetTextureScale(target,source.GetTextureScale(from));material.SetTextureOffset(target,source.GetTextureOffset(from));}
        }
        HDMaterial.ValidateMaterial(material);EditorUtility.SetDirty(material);return material;
    }
    public static int Restore(GameObject source,Transform model,string identity)
    {
        var originals=source.GetComponentsInChildren<Renderer>(true).ToDictionary(r=>AnimationUtility.CalculateTransformPath(r.transform,source.transform));
        int count=0;
        foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            string path=AnimationUtility.CalculateTransformPath(renderer.transform,model);
            if(!originals.TryGetValue(path,out var original))continue;
            renderer.sharedMaterials=original.sharedMaterials.Select(m=>Convert(m,identity)).ToArray();count++;
        }
        return count;
    }
}
