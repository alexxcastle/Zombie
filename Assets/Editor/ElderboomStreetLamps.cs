using System.Linq;
using UnityEngine.AI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public static class ElderboomStreetLamps
{
    const string ScenePath = "Assets/Scenes/Elderboom_Unity_Port.unity";
    const string BulbPath = "Assets/Materials/Elderboom_Lamp_Bulb.mat";

    public static void Ensure()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var bulb = BulbMaterial();
        var lamps = new System.Collections.Generic.List<Transform>();
        foreach (var instance in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
        {
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(instance)) continue;
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance);
            if (string.IsNullOrEmpty(path) || !path.Replace('\\', '/').EndsWith("StreetLamp.glb")) continue;
            lamps.Add(instance.transform);
        }
        // Eleven existing fixtures distributed along the village roads and approaches.
        var places=new[]{new Vector2(-39,-11),new Vector2(-19,-5),new Vector2(4,-13),
            new Vector2(24,-6),new Vector2(42,8),new Vector2(-42,15),new Vector2(-21,26),
            new Vector2(0,17),new Vector2(20,34),new Vector2(43,40),new Vector2(-5,-37)};
        lamps=lamps.OrderBy(t=>t.position.x).ThenBy(t=>t.position.z).ToList();
        for(int i=0;i<lamps.Count;i++)
        {
            var lamp=lamps[i];
            lamp.localScale=Vector3.one*(4.2f/2.302f);
            var place=places[i%places.Length];
            var point=new Vector3(place.x,1f,place.y);
            if(NavMesh.SamplePosition(point,out var ground,5f,NavMesh.AllAreas))point=ground.position;
            else if(Terrain.activeTerrain!=null)point.y=Terrain.activeTerrain.SampleHeight(point)+Terrain.activeTerrain.transform.position.y;
            lamp.position=point;
            lamp.rotation=Quaternion.Euler(0,i%2==0?90:270,0);
            var light = LampLight(lamp);
            light.transform.SetParent(lamp, false);
            light.transform.localPosition = new Vector3(0f, 2.12f, 0f);
            light.transform.localRotation = Quaternion.identity;
            light.transform.localScale = Vector3.one;
            Tune(light);
            EnsureBulb(light.transform, bulb);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    static Light LampLight(Transform lamp)
    {
        var existing = lamp.GetComponentInChildren<Light>(true);
        if (existing != null && existing.name == "Warm_Lamp_Light") return existing;
        Light nearest = null;
        float nearestDistance = 6f;
        foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
        {
            if (light.name != "Warm_Lamp_Light" || light.transform.IsChildOf(lamp) || OwnedByLamp(light.transform)) continue;
            Vector3 delta = light.transform.position - lamp.position;
            delta.y = 0f;
            if (delta.magnitude < nearestDistance)
            {
                nearest = light;
                nearestDistance = delta.magnitude;
            }
        }
        if (nearest != null) return nearest;
        return new GameObject("Warm_Lamp_Light").AddComponent<Light>();
    }

    static bool OwnedByLamp(Transform light)
    {
        var parent = light.parent;
        if (parent == null || !PrefabUtility.IsAnyPrefabInstanceRoot(parent.gameObject)) return false;
        string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(parent.gameObject);
        return !string.IsNullOrEmpty(path) && path.Replace('\\', '/').EndsWith("StreetLamp.glb");
    }

    static void Tune(Light light)
    {
        light.type = LightType.Point;
        light.color = new Color(1f, 0.58f, 0.28f);
        light.range = 16f;
        light.shadows = LightShadows.None;
        if (light.GetComponent<HDAdditionalLightData>() == null) light.gameObject.AddComponent<HDAdditionalLightData>();
        light.lightUnit = LightUnit.Lumen;
        light.intensity = 6500f;
    }

    static void EnsureBulb(Transform light, Material bulb)
    {
        if (light.Find("LanternGlow") is Transform existing)
        {
            existing.localScale = Vector3.one * 0.08f;
            existing.localPosition = Vector3.zero;
            return;
        }
        var glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        glow.name = "LanternGlow";
        Object.DestroyImmediate(glow.GetComponent<Collider>());
        glow.transform.SetParent(light, false);
        glow.transform.localScale = Vector3.one * 0.08f;
        glow.GetComponent<MeshRenderer>().sharedMaterial = bulb;
    }

    static Material BulbMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(BulbPath);
        if (material == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
            material = new Material(Shader.Find("HDRP/Unlit"));
            AssetDatabase.CreateAsset(material, BulbPath);
        }
        var glow = new Color(1f, 0.46f, 0.16f);
        material.SetColor("_UnlitColor", glow);
        material.SetColor("_EmissiveColor", glow * 1.2f);
        material.SetFloat("_EmissiveExposureWeight", 0f);
        HDMaterial.ValidateMaterial(material);
        EditorUtility.SetDirty(material);
        return material;
    }
}
