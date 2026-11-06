Shader "Elderboom/RouteText"
{
    Properties
    {
        _MainTex("Font Atlas", 2D) = "white" {}
        _FaceColor("Face Color", Color) = (1,1,1,1)
        _GradientScale("Gradient Scale", Float) = 5
        _TextureWidth("Texture Width", Float) = 512
        _TextureHeight("Texture Height", Float) = 512
        _WeightNormal("Normal Weight", Float) = 0
        _WeightBold("Bold Weight", Float) = .5
        _ScaleRatioA("Scale Ratio", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="HDRenderPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode"="ForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _FaceColor;
            struct Attributes { float3 positionOS:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS=TransformObjectToHClip(v.positionOS);
                o.uv=v.uv;o.color=v.color*_FaceColor;
                return o;
            }
            float4 Frag(Varyings i):SV_Target
            {
                float distance=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).a;
                float edge=max(fwidth(distance),.01);
                float alpha=smoothstep(.5-edge,.5+edge,distance)*i.color.a;
                clip(alpha-.003);
                return float4(i.color.rgb*.65,alpha);
            }
            ENDHLSL
        }
    }
}
