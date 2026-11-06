using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

// Extracts small reusable geometry assets; materials/textures remain references to the original GLB.
static class ElderboomHorseAssemblyBuilder
{
    const string Folder = "Assets/ElderboomGenerated/HorseSalvage";
    const string Report = "Logs/HorseAssembly";
    static readonly string[] Names = { "Chassis", "Front left leg", "Front right leg", "Rear right leg", "Rear left leg", "Head and neck" };
    static readonly string[] Areas = { "Village Square", "West Farm", "North Timber Yard", "East Cemetery", "South Creek", "Outer Woods" };
    static readonly Vector2[] Hints = { new Vector2(-25,-28), new Vector2(-62,-14), new Vector2(-8,52), new Vector2(83,0), new Vector2(25,-80), new Vector2(78,57) };

    [MenuItem("Elderboom/Horse/Build salvage objective")]
    public static void Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "Elderboom_Unity_Port" || scene.isDirty) throw new InvalidOperationException("Save the intended map scene before building horse salvage.");
        Directory.CreateDirectory(Folder); Directory.CreateDirectory(Report); AssetDatabase.Refresh();
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Elderboom/Mounts/MechHorse_Rider.gltf");
        if (source == null) throw new InvalidOperationException("Horse GLB missing");
        var preview = Object.Instantiate(source);
        preview.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var log = new StringBuilder();
        GameObject root = null;
        NavMeshDataInstance nav = default;
        try
        {
            var smr = preview.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(r => r.sharedMesh.vertexCount).First();
            var meshes = Extract(smr, preview.transform, log);
            var materials = smr.sharedMaterials;
            Object.DestroyImmediate(preview); preview = null;
            var navigation = Object.FindAnyObjectByType<ElderboomNavigation>();
            if (navigation == null || navigation.data == null) throw new InvalidOperationException("Map navigation missing");
            nav = NavMesh.AddNavMeshData(navigation.data);
            Physics.SyncTransforms();
            var positions = Enumerable.Range(0, 6).Select(FindSite).ToArray();
            var previous = Object.FindAnyObjectByType<ElderboomHorseAssembly>();
            if (previous != null) Undo.DestroyObjectImmediate(previous.gameObject);
            root = new GameObject("Horse_Salvage_Objective"); Undo.RegisterCreatedObjectUndo(root, "Build horse salvage objective");
            root.transform.position = positions[0];
            var quest = root.AddComponent<ElderboomHorseAssembly>();
            quest.parts = new ElderboomHorseAssembly.Part[5];
            quest.workshopVisual = new GameObject("Workshop_Chassis"); quest.workshopVisual.transform.SetParent(root.transform, false);
            Visual(meshes[0], materials, quest.workshopVisual.transform, "Incomplete_Chassis");
            var workshopCollider=quest.workshopVisual.AddComponent<BoxCollider>();workshopCollider.center=new Vector3(0,.8f,-.3f);workshopCollider.size=new Vector3(1.1f,1.5f,1.8f);
            var workshopObstacle=quest.workshopVisual.AddComponent<NavMeshObstacle>();workshopObstacle.shape=NavMeshObstacleShape.Box;workshopObstacle.center=workshopCollider.center;workshopObstacle.size=workshopCollider.size;workshopObstacle.carving=true;
            // Two simple maintenance trestles support the missing-leg chassis; removed on assembly.
            var metal = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Elderboom_Gate_Metal.mat") ?? materials[0];
            foreach (float z in new[] { -.6f, .4f })
            {
                Stand(quest.workshopVisual.transform, new Vector3(0,.72f,z), new Vector3(.95f,.09f,.12f), metal);
                foreach (float x in new[] { -.36f, .36f }) Stand(quest.workshopVisual.transform, new Vector3(x,.35f,z), new Vector3(.09f,.7f,.12f), metal);
            }
            for (int i = 1; i < 6; i++)
            {
                var anchor = new GameObject("Salvage_" + Names[i].Replace(" ", "_")); anchor.transform.SetParent(root.transform, true); anchor.transform.position = positions[i];
                var visual = Visual(meshes[i], materials, anchor.transform, Names[i]);
                visual.transform.localRotation = Quaternion.Euler(0, i * 47, i == 5 ? 80 : 90);
                var bounds = visual.GetComponent<Renderer>().bounds;
                visual.transform.position += new Vector3(positions[i].x - bounds.center.x, positions[i].y + .06f - bounds.min.y, positions[i].z - bounds.center.z);
                quest.parts[i-1] = new ElderboomHorseAssembly.Part { label=Names[i], area=Areas[i], anchor=anchor.transform, visual=visual };
            }
            for (int i=0;i<6;i++) log.AppendLine($"{Names[i]} | {Areas[i]} | {positions[i]:F2}");
            EditorUtility.SetDirty(quest);
            Validate(quest, log);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            File.WriteAllText(Report + "/layout.txt", log.ToString());
            Capture(quest);
        }
        finally { if (preview != null) Object.DestroyImmediate(preview); if(nav.valid)nav.Remove(); }
    }

    static int Group(string name)
    {
        if (!int.TryParse(name.Substring(name.LastIndexOf('_')+1), out int n)) return 0;
        if (n>=22 && n<=27) return 1;
        if (n>=28 && n<=33) return 2;
        if (n>=7 && n<=12) return 3;
        if (n>=13 && n<=18) return 4;
        if ((n>=19 && n<=21) || n==4) return 5;
        return 0;
    }

    static Mesh[] Extract(SkinnedMeshRenderer smr, Transform root, StringBuilder log)
    {
        var baked = new Mesh(); smr.BakeMesh(baked);
        try
        {
            var original = smr.sharedMesh;
            var weights = original.boneWeights;
            if (weights.Length != baked.vertexCount) throw new InvalidOperationException("Horse bone weights unavailable");
            var groups = smr.bones.Select(b=>Group(b.name)).ToArray();
            var byPart = Enumerable.Range(0,6).Select(_=>new List<int>()).ToArray();
            var indices = baked.triangles;
            // Assign each complete triangle to its strongest anatomical bone group.
            for (int t=0;t<indices.Length;t+=3)
            {
                var scores = new float[6];
                for(int k=0;k<3;k++)
                {
                    var w=weights[indices[t+k]];
                    scores[groups[w.boneIndex0]]+=w.weight0; scores[groups[w.boneIndex1]]+=w.weight1;
                    scores[groups[w.boneIndex2]]+=w.weight2; scores[groups[w.boneIndex3]]+=w.weight3;
                }
                int group=0; for(int k=1;k<6;k++)if(scores[k]>scores[group])group=k;
                byPart[group].Add(indices[t]); byPart[group].Add(indices[t+1]); byPart[group].Add(indices[t+2]);
            }
            var matrix=root.worldToLocalMatrix*smr.transform.localToWorldMatrix;
            var vertices=baked.vertices; var normals=baked.normals; var uv=baked.uv; var tangents=baked.tangents;
            var output=new Mesh[6];
            for(int g=0;g<6;g++)
            {
                if(byPart[g].Count<100)throw new InvalidOperationException("Missing geometry for "+Names[g]);
                var remap=new Dictionary<int,int>();var v=new List<Vector3>();var n=new List<Vector3>();var u=new List<Vector2>();var tan=new List<Vector4>();var tris=new List<int>();
                foreach(int old in byPart[g])
                {
                    if(!remap.TryGetValue(old,out int index))
                    {
                        index=v.Count;remap.Add(old,index);v.Add(matrix.MultiplyPoint3x4(vertices[old]));
                        n.Add(matrix.inverse.transpose.MultiplyVector(normals[old]).normalized);u.Add(uv[old]);
                        if(tangents.Length==vertices.Length){var a=tangents[old];var b=matrix.MultiplyVector(new Vector3(a.x,a.y,a.z)).normalized;tan.Add(new Vector4(b.x,b.y,b.z,a.w));}
                    }
                    tris.Add(index);
                }
                var mesh=new Mesh{name="Horse_"+Names[g],indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
                mesh.SetVertices(v);mesh.SetNormals(n);mesh.SetUVs(0,u);if(tan.Count==v.Count)mesh.SetTangents(tan);mesh.SetTriangles(tris,0);mesh.RecalculateBounds();
                string path=Folder+"/Part_"+g+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(existing!=null){EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);mesh=existing;EditorUtility.SetDirty(mesh);}else AssetDatabase.CreateAsset(mesh,path);
                output[g]=mesh;log.AppendLine($"{Names[g]}: {v.Count} vertices, {tris.Count/3} triangles, bounds {mesh.bounds.size:F2}");
            }
            if(output.Sum(m=>m.triangles.Length)!=indices.Length)throw new InvalidOperationException("Triangle partition lost geometry");
            return output;
        }
        finally { Object.DestroyImmediate(baked); }
    }

    static Vector3 FindSite(int i)
    {
        var area=Object.FindObjectsByType<ElderboomArea>(FindObjectsSortMode.None).First(a=>a.areaName==Areas[i]);
        var terrain=Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).OrderByDescending(t=>t.terrainData.size.x).First();
        for(int ring=0;ring<15;ring++)for(int a=0;a<(ring==0?1:16);a++)
        {
            var p=new Vector3(Hints[i].x+Mathf.Cos(a*Mathf.PI/8)*ring,0,Hints[i].y+Mathf.Sin(a*Mathf.PI/8)*ring);
            p.y=terrain.SampleHeight(p)+terrain.transform.position.y;
            if(!NavMesh.SamplePosition(p,out var hit,1.2f,NavMesh.AllAreas)||!area.Contains(hit.position))continue;
            p=hit.position;
            if (Physics.Raycast(p+Vector3.up*2,Vector3.up,20,~0,QueryTriggerInteraction.Ignore)) continue; // No workshop inside the church or other buildings.
            float radius=i==0?1.7f:1.1f;
            if(Physics.CheckCapsule(p+Vector3.up*1.8f,p+Vector3.up*2.4f,radius,~0,QueryTriggerInteraction.Ignore))continue;
            if(Object.FindObjectsByType<ElderboomStation>(FindObjectsSortMode.None).Any(s=>Vector3.Distance(s.transform.position,p)<5))continue;
            if(Object.FindObjectsByType<ElderboomGate>(FindObjectsSortMode.None).Any(s=>Vector3.Distance(s.transform.position,p)<7))continue;
            return p+Vector3.up*.03f;
        }
        throw new InvalidOperationException("No clear accessible salvage site in "+Areas[i]);
    }

    static GameObject Visual(Mesh mesh, Material[] materials, Transform parent, string name)
    {
        var go=new GameObject(name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=materials;return go;
    }
    static void Stand(Transform parent,Vector3 position,Vector3 scale,Material material)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name="Maintenance_Trestle";go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=scale;
        go.GetComponent<Renderer>().sharedMaterial=material;Object.DestroyImmediate(go.GetComponent<Collider>());
    }
    static void Validate(ElderboomHorseAssembly quest,StringBuilder log)
    {
        if(quest.parts.Length!=5)throw new InvalidOperationException("Expected five salvage components");
        for(int i=0;i<5;i++)
        {
            var part=quest.parts[i];
            if(part.visual.GetComponent<MeshFilter>().sharedMesh==null)throw new InvalidOperationException("Missing salvage mesh");
            if(!NavMesh.SamplePosition(part.anchor.position,out _,1,NavMesh.AllAreas))throw new InvalidOperationException("Inaccessible component "+part.label);
            log.AppendLine("PASS walkable site "+part.label);
        }
        log.AppendLine("PASS six anatomical mesh partitions retain every original triangle and material reference");
    }

    public static void Capture(ElderboomHorseAssembly quest)
    {
        Directory.CreateDirectory(Report);
        var sun=new GameObject("SalvageReviewLight");var light=sun.AddComponent<Light>();light.type=LightType.Directional;sun.AddComponent<HDAdditionalLightData>();light.intensity=8;sun.transform.rotation=Quaternion.Euler(55,30,0);
        try
        {
            for(int i=0;i<=quest.parts.Length;i++)
            {
                Vector3 at=i==0?quest.transform.position:quest.parts[i-1].anchor.position;
                var go=new GameObject("SalvageReviewCamera");var cam=go.AddComponent<Camera>();go.AddComponent<HDAdditionalCameraData>();cam.fieldOfView=55;cam.nearClipPlane=.05f;cam.farClipPlane=600;
                go.transform.position=at+new Vector3(2.4f,2.1f,-2.8f);go.transform.LookAt(at+Vector3.up*.6f);
                var rt=new RenderTexture(1000,700,24);var tex=new Texture2D(1000,700,TextureFormat.RGB24,false);var old=RenderTexture.active;
                try{cam.targetTexture=rt;for(int k=0;k<4;k++)cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1000,700),0,0);tex.Apply();File.WriteAllBytes(Report+"/site_"+i+".jpg",tex.EncodeToJPG(88));}
                finally{RenderTexture.active=old;cam.targetTexture=null;Object.DestroyImmediate(go);Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);}
            }
        }
        finally{Object.DestroyImmediate(sun);}
    }
}
