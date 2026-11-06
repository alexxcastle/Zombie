Shader "Elderboom/FeelParticles"
{
    // Particles, tracers and bullet holes. Vertex colour and _Color tint the
    // texture; _SrcBlend/_DstBlend select alpha (5,10) or additive (5,1).
    // Output is exposure-independent, like BloodParticles, so night scenes stay readable.
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        _Color("Tint", Color) = (1,1,1,1)
        _Intensity("Intensity", Float) = 1
        [HideInInspector] _SrcBlend("Src", Float) = 5
        [HideInInspector] _DstBlend("Dst", Float) = 10
    }
    SubShader
    {
        Tags { "RenderPipeline"="HDRenderPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode"="ForwardOnly" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _Color; float _Intensity;
            struct Attributes { float3 positionOS:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.uv = v.uv; o.color = v.color;
                return o;
            }
            float4 Frag(Varyings i):SV_Target
            {
                float4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color * _Color;
                clip(c.a - .003);
                return float4(c.rgb * _Intensity, c.a);
            }
            ENDHLSL
        }
    }
}
