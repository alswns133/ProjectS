Shader "Custom/GradientShader"
{
    Properties
    {
        _TopColor ("Top Color", Color) = (0,0,0,1)
        _BottomColor ("Bottom Color", Color) = (0.8,1,0,1)

        _GradientOffset ("Gradient Offset", Float) = 0
        _GradientScale ("Gradient Scale", Float) = 5

        _EmissionStrength ("Emission Strength", Float) = 5
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "RenderPipeline"="UniversalPipeline"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            float4 _TopColor;
            float4 _BottomColor;

            float _GradientOffset;
            float _GradientScale;

            float _EmissionStrength;

            Varyings vert (Attributes IN)
            {
                Varyings OUT;

                VertexPositionInputs posInput =
                    GetVertexPositionInputs(IN.positionOS.xyz);

                OUT.positionCS = posInput.positionCS;
                OUT.positionWS = posInput.positionWS;

                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float h =
                    saturate(
                        (IN.positionWS.y + _GradientOffset)
                        / _GradientScale
                    );

                h = 1 - h;

                float3 col =
                    lerp(
                        _TopColor.rgb,
                        _BottomColor.rgb,
                        h
                    );

                col *= _EmissionStrength;

                return half4(col, 1);
            }

            ENDHLSL
        }
    }
}