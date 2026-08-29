// 레이드 아레나를 덮는 에너지 방벽(오버레이 구체) 전용 셰이더.
//
// 1) 포그를 계산하지 않는다.
//    URP 기본 Unlit과 Shader Graph는 포그를 자동 적용하고 끄는 옵션이 없다.
//    돔 구체는 카메라를 감싸고 있어 방향마다 거리가 크게 달라지는데, 포그를 먹으면
//    플레이어가 움직일 때마다 방벽 색이 출렁인다.
//
// 2) 같은 텍스처를 서로 다른 배율과 속도로 두 번 겹친다.
//    구체가 100배 스케일이라 텍스처를 한 번만 감으면 원본 1픽셀이 화면 수십 픽셀이 되어
//    어떤 디테일도 뭉개진다. 여러 번 타일링해야 가는 선이 가는 선으로 보인다.
//    두 겹이 다른 속도로 흐르면 시차가 생겨 정지된 막이 아니라 흐르는 에너지로 읽힌다.
//
// 프로퍼티 이름은 URP 관례(_BaseMap / _BaseColor)를 따른다.
// ProjectS.Effects.EnergyDomeEffect가 MaterialPropertyBlock으로
// _BaseColor / _BaseMap_ST / _DetailOffset 을 덮어쓰므로 이름을 바꾸면 연출이 멈춘다.
Shader "ProjectS/Energy Additive (No Fog)"
{
    Properties
    {
        [MainTexture] _BaseMap ("Energy Map", 2D) = "black" {}
        [MainColor][HDR] _BaseColor ("Base Color", Color) = (0.04, 0.90, 0.93, 0.12)

        // 텍스처가 검은 배경 + 밝은 갈래일 때, 갈래만 보이고 면은 사라진다.
        // 이 값으로 면 전체에 바닥 밝기를 깔아 "빛나는 막 위에 방전이 흐르는" 그림을 만든다.
        _BaseLevel ("Base Level", Range(0, 1)) = 0.35

        [Header(Second Layer)]
        _DetailScale ("Detail Scale", Range(1.1, 6)) = 2.3
        _DetailStrength ("Detail Strength", Range(0, 1)) = 0.7
        // 스크립트가 매 프레임 덮어쓴다. 인스펙터 값은 초기 위치용.
        _DetailOffset ("Detail Offset", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "EnergyAdditive"
            Tags { "LightMode" = "UniversalForward" }

            // Additive: 알파가 곧 발광 세기가 된다.
            Blend SrcAlpha One
            ZWrite Off
            // 구체 안쪽에서 보므로 양면을 그린다.
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uvBase     : TEXCOORD0;
                float2 uvDetail   : TEXCOORD1;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _BaseLevel;
                float  _DetailScale;
                half   _DetailStrength;
                float  _DetailOffset;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);

                output.uvBase = TRANSFORM_TEX(input.uv, _BaseMap);

                // 두 번째 겹은 배율을 어긋나게(정수배 금지) 잡아야 두 겹이 겹쳐 보이지 않는다.
                output.uvDetail = input.uv * _BaseMap_ST.xy * _DetailScale
                                + _BaseMap_ST.zw
                                + float2(_DetailOffset, 0);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half first  = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uvBase).r;
                half second = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uvDetail).r;

                half energy = saturate(_BaseLevel + first + second * _DetailStrength);

                // MixFog를 호출하지 않는다. 이 한 줄이 없는 것이 이 셰이더의 존재 이유다.
                return half4(_BaseColor.rgb * energy, energy * _BaseColor.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
