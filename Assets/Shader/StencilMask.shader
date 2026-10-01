Shader "ProjectS/StencilMask"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "StencilMask"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            ColorMask 0          // 화면에는 아무것도 안 그린다
            ZWrite Off
            ZTest LEqual         // '지금 화면에 보이는' 표면에만 찍힌다

            Stencil { Ref 1  Comp Always  Pass Replace }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 vert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}