Shader "Custom/ToonOutline"
{
    Properties
    {
        [Header(Base)]
        _BaseMap   ("Base Map", 2D)       = "white" {}
        _BaseColor ("Base Color", Color)  = (1, 1, 1, 1)

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
        //  Pass 1 : Outline (부풀린 뒷면만 그려서 외곽선을 만든다)
        // ============================================================
        Pass
        {
            Name "Outline"
            Cull Front          // 앞면을 잘라내고 뒷면만 남긴다 (핵심)

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // SRP Batcher 호환: 머티리얼 프로퍼티는 이 블록 안에 모은다.
            // 두 Pass의 UnityPerMaterial 레이아웃이 동일해야 배칭이 깨지지 않는다.
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
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
                return _OutlineColor;   // 외곽선은 단색
            }
            ENDHLSL
        }

        // ============================================================
        //  Pass 2 : Base (캐릭터 본체)
        //  교육용으로 단순 텍스처 샘플링만. 실무에선 여기에
        //  라이팅/셀셰이딩을 넣거나, 본체는 URP/Lit를 따로 쓰기도 한다.
        // ============================================================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _OutlineColor;
                float  _OutlineWidth;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                return col;
            }
            ENDHLSL
        }
    }
}
