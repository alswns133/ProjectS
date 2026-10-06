// 보스 사망 이펙트용 파티클 셰이더. 가산(Additive)이 기본이고, 블렌드 계수를 머티리얼에서
// 바꿔 알파 블렌드(연기)로도 쓴다.
//
// 1) 포그를 계산하지 않는다.
//    URP 기본 Unlit·Particles Unlit과 Shader Graph는 포그를 자동 적용하고 끄는 옵션이 없다
//    (EnergyAdditiveNoFog.shader가 같은 이유로 만들어졌다). 던전은 포그가 짙어서, 보스가
//    카메라에서 10m 넘게 떨어진 자리에서 죽으면 섬광과 충격파가 통째로 회색에 묻힌다.
//    사망 연출은 "거리와 무관하게 똑같이 터져야" 하므로 포그를 아예 받지 않는다.
//
// 2) URP의 Particles Unlit 대신 직접 쓰는 이유.
//    그쪽은 블렌드 모드를 머티리얼 키워드(_Surface/_Blend/...)로 간접 표현해서, 에디터
//    스크립트로 머티리얼을 만들면 ShaderGUI를 거치지 않아 키워드와 블렌드 상태가 어긋나기 쉽다
//    (가산으로 설정했는데 알파 블렌드로 그려지는 식). 여기서는 블렌드 계수가 머티리얼 프로퍼티라
//    BossDeathEffectGenerator가 숫자 두 개만 정확히 넣으면 끝이다.
//
// 3) 알파가 '밝기'다(가산일 때).
//    준비된 텍스처는 전부 RGB가 흰색이고 모양·음영이 알파 채널에 들어 있다.
//    Blend SrcAlpha One이므로 화면에 더해지는 양이 곧 알파가 되어, 알파 음영이 그대로 발광 세기로 읽힌다.
//
// 4) 노말맵(선택)은 '입체감'을 위한 가짜 조명이다.
//    빌보드 쿼드는 항상 카메라를 향하므로 탄젠트 공간이 곧 뷰 공간이다. 그래서 고정된 뷰 공간
//    광원 방향 하나와 내적만 해도 링이 평평한 데칼이 아니라 '빛나는 튜브'로 읽힌다.
//    실제 씬 조명을 쓰지 않는 이유: 발광체라 씬 조명에 반응하면 안 되고(어두운 방에서 꺼진다),
//    사망 연출은 어느 던전에서든 같은 모양이어야 하기 때문이다.
//    _NormalStrength 기본값 0 — 노말맵을 안 쓰는 머티리얼은 영향이 전혀 없다.
//
// 틴트(_BaseColor)는 HDR이다. 1을 넘는 값을 넣어야 URP 블룸이 물려 "타오르는" 느낌이 난다.
Shader "ProjectS/Particle Additive (No Fog)"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor][HDR] _BaseColor ("Tint (HDR)", Color) = (1, 1, 1, 1)

        [Normal] _NormalMap ("Normal Map (optional)", 2D) = "bump" {}
        _NormalStrength ("Normal Shading", Range(0, 1)) = 0

        // 머티리얼이 정하는 블렌드. 기본은 가산(SrcAlpha=5, One=1).
        // 연기처럼 가려야 하는 파티클은 생성기가 OneMinusSrcAlpha(10)로 바꿔 넣는다.
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 5
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "ParticleFx"
            Tags { "LightMode" = "UniversalForward" }

            // 셰이더에서 rgb에 알파를 곱하지 않는다 — 가산일 때 알파가 두 번 먹어 어두워진다.
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            // SRP Batcher 호환: 셰이더 코드가 '쓰는' 프로퍼티는 전부 이 CBUFFER 안에 있어야 한다.
            // 반대로 _SrcBlend/_DstBlend와 _NormalMap_ST는 일부러 넣지 않는다 — 앞의 둘은 코드가
            // 아니라 고정 기능 블렌드 상태로만 쓰이고(URP의 Lit도 같은 방식), _NormalMap은 _BaseMap과
            // 같은 UV로 샘플링해서 타일링 값을 쓸 일이 없다.
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _NormalStrength;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);

                // Texture Sheet Animation(플립북)은 Shuriken이 UV를 직접 다시 써서 넘긴다.
                // 따라서 여기서는 평범하게 샘플링만 하면 4x4 시트가 그대로 동작한다.
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                // 파티클의 Start Color / Color over Lifetime이 정점 색으로 들어온다.
                output.color = input.color;

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 c = tex * _BaseColor * input.color;

                // 노말맵을 쓰는 머티리얼만 입체 음영을 얹는다(_NormalStrength = 0이면 분기 자체가 무효).
                // 숫자에 h 접미사를 쓰지 않는다 — half 리터럴 표기는 HLSL 컴파일러·플랫폼마다
                // 받아주는 곳이 갈려서, 안 받는 쪽에서 셰이더가 통째로 에러를 낸다.
                if (_NormalStrength > 0.001)
                {
                    half3 n = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv));

                    // 고정 광원(좌상단 앞쪽). 빌보드라 탄젠트 공간 ≈ 뷰 공간이므로 이 값이 곧
                    // "화면 왼쪽 위에서 비추는 빛"이 된다. 바꾸면 하이라이트가 도는 방향이 바뀐다.
                    half3 lightDir = normalize(half3(-0.45, 0.55, 0.70));

                    half ndl = saturate(dot(n, lightDir));
                    // 0.35 바닥값: 그늘진 쪽도 완전히 꺼지면 발광체가 아니라 플라스틱으로 보인다.
                    half shade = 0.35 + 1.25 * ndl;
                    // 좁은 하이라이트 한 줄 — 튜브의 '모서리 빛'을 만든다.
                    half rim = pow(ndl, 12.0) * 0.8;

                    c.rgb *= lerp(1.0, shade + rim, _NormalStrength);
                }

                return c;
            }
            ENDHLSL
        }
    }

    // 그림자 패스·폴백 없음. 발광 파티클이라 그림자를 드리울 일이 없고,
    // 폴백이 있으면 URP가 아닌 패스를 타며 포그가 다시 붙을 수 있다.
    Fallback Off
}
