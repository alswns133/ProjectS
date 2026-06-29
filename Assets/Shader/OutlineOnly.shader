Shader "Custom/OutlineOnly"
{
    Properties
    {
        [Header(Outline)]
        _OutlineColor ("Outline Color", Color)         = (0, 0, 0, 1)
        _OutlineWidth ("Outline Width", Range(0, 0.05)) = 0.01
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "Opaque"
            "Queue"          = "Geometry"
        }

        // ============================================================
        //  외곽선 Pass 하나만. 본체는 기존 Synty 머티리얼이 담당한다.
        // ============================================================
        Pass
        {
            Name "Outline"
            Cull Front          // 앞면을 잘라내고 부풀린 뒷면만 남긴다

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float  _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;

                // 정점을 노멀 방향으로 밀어내 메시를 부풀린다 (오브젝트 공간).
                float3 inflatedOS = IN.positionOS.xyz + IN.normalOS * _OutlineWidth;

                OUT.positionHCS = TransformObjectToHClip(inflatedOS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
