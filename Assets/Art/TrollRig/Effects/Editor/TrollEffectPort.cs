using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace TrollPresentation.Editor
{
    // Converts the two selected local reference effects to the project's existing URP shader.
    public static class TrollEffectPort
    {
        const string Root = "Assets/Art/TrollRig/Effects/";
        static Vector3 V(JToken t) => new Vector3((float)t["x"], (float)t["y"], (float)t["z"]);
        static Color C(JToken t) => new Color((float)t["r"],(float)t["g"],(float)t["b"],(float)t["a"]);
        static Gradient ReadGradient(JToken t)
        {
            var g=new Gradient();
            g.SetKeys(Enumerable.Range(0,(int)t["m_NumColorKeys"]).Select(i=>new GradientColorKey(C(t["key"+i]),(float)t["ctime"+i]/65535f)).ToArray(),
                Enumerable.Range(0,(int)t["m_NumAlphaKeys"]).Select(i=>new GradientAlphaKey((float)t["key"+i]["a"],(float)t["atime"+i]/65535f)).ToArray());
            g.mode=(GradientMode)(int)t["m_Mode"];return g;
        }
        static ParticleSystem.MinMaxGradient Colors(JToken t)
        {
            int mode=(int)t["minMaxState"];
            if(mode==0)return new ParticleSystem.MinMaxGradient(C(t["maxColor"]));
            if(mode==2)return new ParticleSystem.MinMaxGradient(C(t["minColor"]),C(t["maxColor"]));
            if(mode==3)return new ParticleSystem.MinMaxGradient(ReadGradient(t["minGradient"]),ReadGradient(t["maxGradient"]));
            var result=new ParticleSystem.MinMaxGradient(ReadGradient(t["maxGradient"]));result.mode=(ParticleSystemGradientMode)mode;return result;
        }
        static Texture2D DecodeMask(string imageName, bool dust)
        {
            // Source shaders use packed RGB masks, not the PNG's opaque alpha.
            string path=Root+"Textures/"+imageName;
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
            texture.LoadImage(File.ReadAllBytes(Root+"SourceData/"+imageName));
            var pixels=texture.GetPixels32();
            for(int i=0;i<pixels.Length;i++)pixels[i]=new Color32(255,255,255,dust?pixels[i].g:pixels[i].r);
            texture.SetPixels32(pixels);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static ParticleSystem.MinMaxCurve Curve(JToken c)
        {
            float max=(float)c["scalar"],min=(float)c["minScalar"];int mode=(int)c["minMaxState"];
            if(mode==0)return new ParticleSystem.MinMaxCurve(max);
            if(mode==3)return new ParticleSystem.MinMaxCurve(Mathf.Min(min,max),Mathf.Max(min,max));
            Func<JToken,AnimationCurve> read=v=>new AnimationCurve(v["m_Curve"].Select(k=>new Keyframe((float)k["time"],(float)k["value"],(float)k["inSlope"],(float)k["outSlope"])).ToArray());
            return mode==1?new ParticleSystem.MinMaxCurve(max,read(c["maxCurve"])):new ParticleSystem.MinMaxCurve(max,read(c["minCurve"]),read(c["maxCurve"]));
        }
        static JToken ClearReferences(JToken t)
        {
            if(t is JObject && t["m_PathID"] != null) return new JObject { ["instanceID"] = 0 };
            if(t is JObject) { var o=new JObject(); foreach(var p in ((JObject)t).Properties()) if(p.Name!="m_GameObject") o[p.Name]=ClearReferences(p.Value); return o; }
            if(t is JArray) return new JArray(t.Select(ClearReferences));
            return t.DeepClone();
        }
        [MenuItem("Tools/Troll/Reimport selected contact effects")]
        public static void ImportAndWire()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode before authoring prefabs.");
            Directory.CreateDirectory(Root+"Materials");Directory.CreateDirectory(Root+"Prefabs");Directory.CreateDirectory(Root+"Textures");AssetDatabase.Refresh();
            var materials=new Dictionary<string,Material>();
            foreach(var file in Directory.GetFiles(Root+"SourceData","resources_*.json")) {
                var j=JObject.Parse(File.ReadAllText(file));if((string)j["type"]!="Material")continue;
                string name=(string)j["name"], path=Root+"Materials/"+name+".mat";
                var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(!mat){mat=new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));AssetDatabase.CreateAsset(mat,path);}
                mat.name=name;var tex=j["textures"].FirstOrDefault();
                if(tex!=null) {
                    var record=JObject.Parse(File.ReadAllText(Root+"SourceData/"+(string)tex["key"]+".json"));
                    mat.SetTexture("_BaseMap",DecodeMask((string)record["image"],name.Contains("Dust")));
                }
                bool alpha=name.Contains("Black") || name.Contains("Dust");
                mat.SetFloat("_Surface",1);mat.SetFloat("_Blend",alpha?0:2);
                mat.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend",(float)(alpha?UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha:UnityEngine.Rendering.BlendMode.One));
                mat.SetFloat("_ZWrite",0);mat.SetFloat("_Cull",0);
                mat.SetColor("_BaseColor",name.Contains("Black")?new Color(.06f,.045f,.03f,1):Color.white);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");mat.SetOverrideTag("RenderType","Transparent");mat.renderQueue=3000;
                EditorUtility.SetDirty(mat);materials[(string)j["key"]]=mat;
            }
            foreach(string name in new[]{"Impact","Dust"}) {
                var source=JObject.Parse(File.ReadAllText(Root+"SourceData/"+name+".json"));var gos=new Dictionary<long,GameObject>();GameObject root=null;
                foreach(var n in source["nodes"]) {
                    var go=new GameObject((string)n["name"]);gos[(long)n["id"]]=go;
                    if(root==null){root=go;root.name="Troll "+name;}else go.transform.SetParent(gos[(long)n["parent"]].transform,false);
                    if(go!=root){go.transform.localPosition=V(n["position"]);var q=n["rotation"];go.transform.localRotation=new Quaternion((float)q["x"],(float)q["y"],(float)q["z"],(float)q["w"]);go.transform.localScale=V(n["scale"]);}
                    if(n["particles"]==null)continue;
                    var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                    EditorJsonUtility.FromJsonOverwrite(new JObject { ["ParticleSystem"]=ClearReferences(n["particles"]) }.ToString(),ps);
                    var main=ps.main;main.playOnAwake=false;main.loop=false;main.scalingMode=ParticleSystemScalingMode.Hierarchy;main.maxParticles=Mathf.Min(main.maxParticles,64);
                    var initial=n["particles"]["InitialModule"];
                    main.startLifetime=Curve(initial["startLifetime"]);main.startSpeed=Curve(initial["startSpeed"]);
                    main.startSize=Curve(initial["startSize"]);main.startRotation=Curve(initial["startRotation"]);main.gravityModifier=Curve(initial["gravityModifier"]);
                    main.startColor=Colors(initial["startColor"]);
                    var color=ps.colorOverLifetime;color.enabled=(bool)n["particles"]["ColorModule"]["enabled"];color.color=Colors(n["particles"]["ColorModule"]["gradient"]);
                    var size=ps.sizeOverLifetime;size.enabled=(bool)n["particles"]["SizeModule"]["enabled"];size.size=Curve(n["particles"]["SizeModule"]["curve"]);
                    var emission=ps.emission;
                    // Burst serialization changed between the source Unity version and this editor.
                    // Reapply the recovered counts through the public API; keep each contact one-shot.
                    var bursts=n["particles"]["EmissionModule"]["m_Bursts"];
                    if(bursts!=null) emission.SetBursts(bursts.Select(b=> {
                        var c=b["countCurve"];short hi=(short)Mathf.Clamp((float)c["scalar"],1,32);
                        short lo=(short)((int)c["minMaxState"]==3?Mathf.Clamp((float)c["minScalar"],1,hi):hi);
                        return new ParticleSystem.Burst((float)b["time"],lo,hi);
                    }).ToArray());
                    if(name=="Dust") {main.duration=.25f;main.startLifetime=new ParticleSystem.MinMaxCurve(.25f,.5f);main.startSize=new ParticleSystem.MinMaxCurve(1.2f,2.4f);main.startColor=new Color(.48f,.41f,.31f,.28f);emission.rateOverTime=0;emission.rateOverDistance=0;emission.SetBursts(new[]{new ParticleSystem.Burst(0,5)});}
                    foreach(var r in n["renderers"]) {
                        var renderer=go.GetComponent<ParticleSystemRenderer>();
                        EditorJsonUtility.FromJsonOverwrite(new JObject { ["ParticleSystemRenderer"]=ClearReferences(r["raw"]) }.ToString(),renderer);
                        renderer.sharedMaterials=r["materials"].Select(k=>materials[(string)k]).ToArray();
                        if(r["mesh"].Type!=JTokenType.Null)renderer.mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"SourceData/OriginalImpactQuad.asset");
                        renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Normal,ParticleSystemVertexStream.Color,ParticleSystemVertexStream.UV});
                        renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(root,Root+"Prefabs/Troll"+name+".prefab");UnityEngine.Object.DestroyImmediate(root);
            }
            const string rigPath="Assets/Prefabs/TrollRig.prefab";
            var rig=PrefabUtility.LoadPrefabContents(rigPath);
            try {
                foreach(var limb in rig.GetComponentsInChildren<MechLab.ServoLimb>(true)) {
                    var fx=limb.GetComponent<TrollContactEffects>();if(!fx)fx=limb.gameObject.AddComponent<TrollContactEffects>();
                    fx.impactPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"Prefabs/TrollImpact.prefab");
                    fx.dustPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"Prefabs/TrollDust.prefab");
                }
                PrefabUtility.SaveAsPrefabAsset(rig,rigPath);
            } finally {PrefabUtility.UnloadPrefabContents(rig);}
            AssetDatabase.SaveAssets();Debug.Log("Troll contact effects: two prefabs imported and wired to both existing hands.");
        }
    }
}
