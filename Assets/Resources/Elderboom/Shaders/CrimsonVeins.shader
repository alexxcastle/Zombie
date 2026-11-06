Shader "Elderboom/CrimsonVeins"
{
    Properties { _VeinScale("Vein scale", Float) = 13 }
    SubShader
    {
        Tags { "RenderPipeline"="HDRenderPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" }
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode"="ForwardOnly" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"
            float _VeinScale;
            struct Attributes { float3 positionOS:POSITION; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 local:TEXCOORD0; };
            Varyings Vert(Attributes v)
            {
                Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS);
                o.local=v.positionOS*_VeinScale; return o;
            }
            float3 Hash(float3 p) { return frac(sin(float3(dot(p,float3(127.1,311.7,74.7)),dot(p,float3(269.5,183.3,246.1)),dot(p,float3(113.5,271.9,124.6))))*43758.5453); }
            float4 Frag(Varyings i):SV_Target
            {
                float3 p=i.local + .18*sin(i.local.yzx*2.3);
                float3 cell=floor(p), local=frac(p); float nearest=100, second=100;
                [unroll] for(int z=-1;z<=1;z++) [unroll] for(int y=-1;y<=1;y++) [unroll] for(int x=-1;x<=1;x++)
                {
                    float3 o=float3(x,y,z); float3 d=o+Hash(cell+o)-local; float dist=dot(d,d);
                    if(dist<nearest){second=nearest;nearest=dist;} else second=min(second,dist);
                }
                float edge=sqrt(second)-sqrt(nearest);
                float veins=1-smoothstep(.012,.047,edge);
                clip(veins-.025);
                float core=1-smoothstep(.002,.014,edge);
                float pulse=.92+.08*sin(_Time.y*2.0+i.local.y*.4);
                // Kept exposure independent, like this project's particle shaders.
                return float4(lerp(float3(.44,.001,.003),float3(.95,.028,.012),core)*pulse,veins*.88);
            }
            ENDHLSL
        }
    }
}
