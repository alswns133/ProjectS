// 레이드 아레나 경계 배리어 셰이더.
//
// 설계 의도: "평소에는 없는 벽".
//    아레나를 둘러싼 도시 경관이 이 맵의 핵심 자산이라, 상시로 켜져 있는 반투명 막을
//    세우면 그걸 다 가린다. 하늘의 헥사 돔과 무늬가 겹쳐 지저분해지는 문제도 있다.
//    그래서 기본 상태는 거의 완전한 투명이고, 플레이어가 경계에 접근하거나 부딪힌
//    지점에서만 국소적으로 밝아진다.
//
// 발광은 두 갈래다. 하나만 있으면 안 된다.
//    - 근접(Proximity): 경계 가까이 있는 "동안" 계속 켜진다. 구석에 몰려 싸울 때
//      자기가 벽에 붙어 있다는 걸 계속 알려주는 쪽.
//    - 파문(Ripple)   : 부딪힌 "순간" 퍼져나갔다 사라진다. 튕겨나왔다는 피드백.
//
// 포그를 계산하지 않는다. EnergyAdditiveNoFog와 같은 이유이며, 씬의 포그 색이
// 어두운 적갈색이라 청록 배리어에 섞이면 색이 탁해진다.
//
// 접촉점은 ProjectS.Effects.ArenaBarrier가 MaterialPropertyBlock으로 매 프레임
// 덮어쓴다. 프로퍼티 이름과 MAX_POINTS를 바꾸면 그쪽도 같이 고쳐야 한다.
Shader "ProjectS/Arena Barrier"
{
    Properties
    {
        // 비워두면 흰색으로 잡혀 무늬 없이 발광만 나온다. 그 상태로도 동작은 확인할 수 있다.
        // 타일링은 _HexTiling으로만 조절하므로 인스펙터의 Tiling/Offset 칸은 감춰둔다.
        [MainTexture][NoScaleOffset] _BaseMap ("Hex Pattern (R)", 2D) = "white" {}
        // 알파가 곧 최대 발광 세기다. EnergyAdditive와 같은 규약.
        [MainColor][HDR] _BaseColor ("Base Color", Color) = (0.10, 0.85, 1.0, 1.0)

        // x = 가로 반복 수, y = 세로 반복 수.
        // 원통 메시라면 가로는 반드시 정수로 둘 것. 소수면 UV가 감기는 지점에 이음매가 보인다.
        _HexTiling ("Hex Tiling (XY)", Vector) = (24, 6, 0, 0)

        [Header(Proximity)]
        // 접촉점에서 이 반경 안쪽이 밝아진다. 너무 크면 벽 전체가 켜져 상시 벽이 된다.
        _GlowRadius ("Glow Radius", Float) = 3.5

        [Header(Ripple)]
        _RippleSpeed ("Ripple Speed", Float) = 9
        _RingWidth ("Ring Width", Float) = 1.3
        // 이 값은 C# 쪽에서도 읽어 파문 수명을 판단한다. 여기가 단일 기준점이다.
        _RippleLife ("Ripple Life", Float) = 0.8

        [Header(Ambient)]
        // 정면으로 볼 때는 거의 안 보이고, 비스듬히 볼수록 진해진다.
        // 시야 정면의 도시 경관은 그대로 두고 가장자리에서만 "벽이 있다"를 인지시키는 장치.
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 3
        // 0.05 근처에서 시작할 것. 키우면 결국 상시 벽이 되어 원래 피하려던 문제로 돌아간다.
        _AmbientLevel ("Ambient Level", Range(0, 1)) = 0.05

        [Header(Shape)]
        // 위로 갈수록 사라지게 한다. 윗변이 칼같이 잘리면 판때기로 보인다.
        _TopFadeStart ("Top Fade Start (V)", Range(0, 1)) = 0.55
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
            Name "ArenaBarrier"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One
            ZWrite Off
            // 아레나 안에서도 밖에서도 보여야 한다.
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ArenaBarrier.cs의 MaxPoints와 반드시 같아야 한다.
            #define MAX_POINTS 8

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _HexTiling;
                float _GlowRadius;
                float _RippleSpeed;
                float _RingWidth;
                float _RippleLife;
                float _FresnelPower;
                float _AmbientLevel;
                float _TopFadeStart;
            CBUFFER_END

            // 배열은 CBUFFER 밖에 둔다. UnityPerMaterial 안에는 배열을 넣을 수 없어
            // SRP Batcher 대상에서 빠지지만, 배리어는 씬에 하나뿐이라 문제되지 않는다.
            float4 _ProximityPoints[MAX_POINTS];  // xyz = 월드 좌표, w = 세기(0~1)
            float4 _RipplePoints[MAX_POINTS];     // xyz = 월드 좌표, w = 발생 시각
            int _ProximityCount;
            int _RippleCount;

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 positionWS = IN.positionWS;
                float glow = 0;

                // 근접: 경계에 붙어 있는 동안 계속 켜진다.
                // 제곱해서 떨어뜨리는 이유는, 선형이면 발광 가장자리가 또렷한 원으로 보이기 때문.
                [loop]
                for (int p = 0; p < _ProximityCount; p++)
                {
                    float d = distance(positionWS, _ProximityPoints[p].xyz);
                    float f = 1.0 - saturate(d / max(_GlowRadius, 1e-4));
                    glow = max(glow, f * f * _ProximityPoints[p].w);
                }

                // 파문: 부딪힌 순간 퍼져나갔다 사라진다.
                [loop]
                for (int r = 0; r < _RippleCount; r++)
                {
                    float age = _Time.y - _RipplePoints[r].w;
                    float d = distance(positionWS, _RipplePoints[r].xyz);

                    float ring = saturate(1.0 - abs(d - age * _RippleSpeed) / max(_RingWidth, 1e-4));
                    ring *= saturate(1.0 - age / max(_RippleLife, 1e-4));
                    ring *= step(0.0, age);

                    glow = max(glow, ring);
                }

                float hex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv * _HexTiling.xy).r;

                float3 normalWS = normalize(IN.normalWS);
                float3 viewWS = normalize(GetWorldSpaceViewDir(positionWS));
                // Cull Off라 뒷면에서는 노말이 반대로 온다. abs로 양쪽을 같게 취급한다.
                float fresnel = pow(1.0 - saturate(abs(dot(normalWS, viewWS))), _FresnelPower);

                float topFade = 1.0 - smoothstep(_TopFadeStart, 1.0, IN.uv.y);

                float strength = saturate(glow * hex + fresnel * _AmbientLevel) * topFade;
                return half4(_BaseColor.rgb, strength * _BaseColor.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
