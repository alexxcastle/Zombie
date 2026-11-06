using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

public static class ElderboomHK53Builder
{
    public const float AdsPitchDown = 1f;
    // Actual open aperture and front-post tip in the weapon's Unity coordinates.
    static readonly Vector3 RearAperture = new Vector3(.00125f, .249f, -.08285125f);
    static readonly Vector3 FrontPostTip = new Vector3(.000043f, .246769f, .306757f);
    public const string Asset = "Assets/GameAssets/Soph/HK53/HK53_FPS.gltf";
    public const string Prefab = "Assets/Resources/Elderboom/Weapons/hk53.prefab";
    [Serializable] sealed class ApertureData
    {
        public float[] positions,normals,uv;
        public int[] outer,inner;
    }
    static void ApplyClearAperture(Transform root)
    {
        const string meshPath="Assets/GameAssets/Soph/HK53/HK53_RearSight_Aperture.asset";
        var data=JsonUtility.FromJson<ApertureData>(File.ReadAllText("Assets/GameAssets/Soph/HK53/HK53_RearSight_Aperture.json"));
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        bool fresh=mesh==null;if(fresh)mesh=new Mesh{name="HK53 rear sight with open aperture"};else mesh.Clear();
        var vertices=new Vector3[data.positions.Length/3];var normals=new Vector3[vertices.Length];var uv=new Vector2[vertices.Length];
        for(int i=0;i<vertices.Length;i++)
        {
            vertices[i]=new Vector3(data.positions[i*3],data.positions[i*3+1],data.positions[i*3+2]);
            normals[i]=new Vector3(data.normals[i*3],data.normals[i*3+1],data.normals[i*3+2]);
            uv[i]=new Vector2(data.uv[i*2],data.uv[i*2+1]);
        }
        mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices=vertices;mesh.normals=normals;mesh.uv=uv;mesh.subMeshCount=2;
        mesh.SetTriangles(data.outer,0);mesh.SetTriangles(data.inner,1);mesh.RecalculateBounds();mesh.RecalculateTangents();
        if(fresh)AssetDatabase.CreateAsset(mesh,meshPath);else EditorUtility.SetDirty(mesh);
        var rear=ElderboomWeaponFraming.FindNamed(root,"Rear_Sight");rear.GetComponent<MeshFilter>().sharedMesh=mesh;
        var renderer=rear.GetComponent<MeshRenderer>();var original=renderer.sharedMaterials[0];
        const string materialPath="Assets/Materials/HK53_Aperture_Interior.mat";
        var interior=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if(interior==null)
        {
            interior=new Material(Shader.Find("HDRP/Lit")){name="HK53 aperture interior steel"};
            interior.SetColor("_BaseColor",new Color(.025f,.028f,.032f));interior.SetFloat("_Metallic",.75f);interior.SetFloat("_Smoothness",.32f);
            AssetDatabase.CreateAsset(interior,materialPath);
        }
        renderer.sharedMaterials=new[]{original,interior};
    }
    public static void Build()
    {
        AssetDatabase.Refresh();
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Asset);
        if(source == null) throw new InvalidOperationException("HK53 import missing");
        var root = new GameObject("Weapon_hk53");
        try
        {
            var model = UnityEngine.Object.Instantiate(source, root.transform); model.name = "Viewmodel";
            var clips = AssetDatabase.LoadAllAssetsAtPath(Asset).OfType<AnimationClip>().ToArray();
            var view = root.AddComponent<ElderboomViewmodel>(); view.model = model; view.operatorId = "SOPH";
            view.idle = clips.Single(c => c.name == "HK53_Idle_Breathing");
            view.reload = clips.Single(c => c.name == "HK53_Reload"); view.authoredReloadTiming = true;
            if (Mathf.Abs(view.idle.length - 4f) > .02f || Mathf.Abs(view.reload.length - 3.5f) > .02f)
                throw new InvalidOperationException("Incorrect HK53 clip durations");
            foreach(var a in model.GetComponentsInChildren<Animator>(true)) a.enabled = false;
            view.ResetToIdle();
            if(!ElderboomWeaponFraming.AlignAuthoredCamera(model.transform)) throw new InvalidOperationException("HK53 camera locator missing");
            var muzzleAxis = ElderboomWeaponFraming.FindNamed(root.transform, "MainWeapon_ADS_Front").position - ElderboomWeaponFraming.FindNamed(root.transform, "MainWeapon_ADS_Rear").position;
            if(Vector3.Dot(muzzleAxis.normalized, Vector3.forward) < .8f) throw new InvalidOperationException("HK53 camera/weapon forward mismatch");
            foreach(var camera in model.GetComponentsInChildren<Camera>(true)) UnityEngine.Object.DestroyImmediate(camera);
            foreach(var listener in model.GetComponentsInChildren<AudioListener>(true)) UnityEngine.Object.DestroyImmediate(listener);
            foreach(var node in root.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 19;
            var rear = ElderboomWeaponFraming.FindNamed(root.transform, "MainWeapon_ADS_Rear");
            var front = ElderboomWeaponFraming.FindNamed(root.transform, "MainWeapon_ADS_Front");
            ApplyClearAperture(root.transform);
            rear.localPosition = RearAperture;
            front.localPosition = FrontPostTip;
            var pose = new ElderboomWeaponPose { hipPosition = Vector3.zero, hipRotation = Quaternion.identity, adsRelief = .16f };
            pose.hasAds = ElderboomWeaponFraming.SolveSightPose(root.transform, rear, front, new Vector3(0,0,pose.adsRelief), out pose.adsPosition, out pose.adsRotation);
            if(!pose.hasAds) throw new InvalidOperationException("HK53 sights missing");
            // Lower the ADS view around the player's eye, keeping both sights
            // on the same screen ray instead of translating them out of alignment.
            var lower = Quaternion.AngleAxis(AdsPitchDown, Vector3.right);
            pose.adsPosition = lower * pose.adsPosition;
            pose.adsRotation = lower * pose.adsRotation;
            ElderboomWeaponFraming.Store(view, pose);
            var muzzle = ElderboomWeaponFraming.FindNamed(root.transform, "HK53_Muzzle");
            var flashObject = new GameObject("MuzzleFlash"); flashObject.layer = 19; flashObject.transform.SetParent(muzzle,false);
            var flash = flashObject.AddComponent<Light>(); flash.type = LightType.Point; flash.color = new Color(1,.63f,.22f); flash.intensity = 2f; flash.range = 3; flash.enabled = false;
            flashObject.AddComponent<HDAdditionalLightData>();
            // glTFast's HDRP materials retain the authored base color, normal and packed ORM maps.
            foreach(var material in model.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Distinct())
                if(material == null || !material.shader.isSupported) throw new InvalidOperationException("Unsupported HK53 material");
            PrefabUtility.SaveAsPrefabAsset(root, Prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("HK53_PREFAB_READY idle="+view.idle.length+" reload="+view.reload.length+" ads="+pose.adsPosition);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}
