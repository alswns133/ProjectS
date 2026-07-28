// 밤에 불 켜진 빌딩 창문을 표현하는 단일 창문용 발광 셰이더.
// 창문 크기에 맞춘 쿼드 하나 = 창문 하나. 같은 머티리얼을 여러 창문에 복붙해도
// 오브젝트 월드 위치로 시드를 만들어 창문마다 색/밝기가 조금씩 달라진다.
// Emission이 HDR(1 초과)이므로 씬의 Bloom이 그대로 빛번짐을 만들어 준다.
Shader "ProjectS/Environment/BuildingWindowLights"
{
    Properties
    {
        [Header(Light)]
        [HDR] _ColorWarm ("Color Warm", Color) = (1.0, 0.62, 0.28, 1)
        [HDR] _ColorCool ("Color Cool", Color) = (0.45, 0.8, 1.0, 1)
        // 1이면 전부 Warm, 0이면 전부 Cool. 중간이면 창문마다 랜덤 배분.
        _WarmCoolRatio ("Warm To Cool Ratio", Range(0, 1)) = 0.65
        _Intensity ("Emission Intensity", Range(0, 20)) = 3
        // 창문(오브젝트)마다 밝기를 얼마나 다르게 할지. 0이면 모두 같은 밝기.
        _BrightnessVariation ("Brightness Variation", Range(0, 1)) = 0.4

        [Header(Variation)]
        // 불이 켜져 있을 확률. 1이면 항상 켜짐, 낮추면 일부 창문이 꺼진 채 배치된다.
        _LitRatio ("Lit Ratio", Range(0, 1)) = 1
        _UnlitColor ("Unlit Window Color", Color) = (0.02, 0.03, 0.05, 1)
        // 가장자리를 어둡게 해서 창틀 안쪽에서 빛이 나오는 느낌을 준다. 0이면 균일 발광.
        _EdgeFade ("Edge Fade", Range(0, 1)) = 0.35
        // 형광등처럼 깜빡일 확률. 0이면 깜빡임 없음.
        _FlickerRatio ("Flicker Ratio", Range(0, 1)) = 0.05

        // 패턴이 마음에 안 들 때 수동으로 섞는 값. 보통은 월드 위치 자동 시드로 충분하다.
        _Seed ("Seed", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            // 창문 평면이 벽면과 거의 겹쳐 배치되어도 Z-파이팅이 나지 않도록
            // 깊이를 카메라 쪽으로 살짝 당긴다.
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorWarm;
                half4 _ColorCool;
                float _WarmCoolRatio;
                float _Intensity;
                float _BrightnessVariation;
                float _LitRatio;
                half4 _UnlitColor;
                float _EdgeFade;
                float _FlickerRatio;
                float _Seed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                // x = 오브젝트별 시드, y = 포그 계수
                float2 seedFog : TEXCOORD1;
            };

            // 시드 + 오프셋 -> 0~1 의사 난수. 창문(오브젝트)마다 고정된 랜덤 값을 얻는 용도.
            float Hash(float seed, float offset)
            {
                float2 p = frac(float2(seed, seed + offset) * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;

                // 오브젝트 월드 위치로 시드를 만들어, 같은 머티리얼을 복붙해도
                // 창문마다 색과 밝기가 달라지게 한다.
                float3 origin = unity_ObjectToWorld._m03_m13_m23;
                float objectSeed = frac(dot(origin, float3(0.731, 0.463, 0.291))) * 43.7;

                output.seedFog = float2(
                    _Seed + objectSeed,
                    ComputeFogFactor(output.positionCS.z));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float seed = input.seedFog.x;

                // 창문별 랜덤: 켜짐 여부, 색 선택, 밝기 편차.
                float lit = step(Hash(seed, 1.7), _LitRatio);
                half3 litColor = Hash(seed, 5.2) < _WarmCoolRatio
                    ? _ColorWarm.rgb
                    : _ColorCool.rgb;
                float brightness = 1.0 - _BrightnessVariation * Hash(seed, 9.1);

                // 가장자리로 갈수록 어둡게. UV (0.5, 0.5)가 중앙이라고 가정한다 (Unity Quad 기준).
                float2 d = abs(input.uv - 0.5) * 2.0;
                float edge = max(d.x, d.y);
                float vignette = lerp(1.0, smoothstep(1.05, 0.35, edge), _EdgeFade);

                // 일부 창문만 형광등처럼 불규칙하게 깜빡인다.
                float isFlicker = step(Hash(seed, 3.3), _FlickerRatio);
                float flickerNoise = Hash(seed, floor(_Time.y * 16.0));
                float flicker = 1.0 - isFlicker * 0.45 * step(flickerNoise, 0.35);

                half3 color = lerp(
                    _UnlitColor.rgb,
                    litColor * (_Intensity * brightness * vignette * flicker),
                    lit);
                color = MixFog(color, input.seedFog.y);

                return half4(color, 1.0);
            }
            ENDHLSL
        }

        // 뎁스 프리패스/SSAO가 켜져 있어도 구멍이 생기지 않도록 DepthOnly 패스를 제공한다.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            // 포워드 패스와 깊이가 어긋나지 않게 동일한 오프셋을 적용한다.
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half DepthFrag(DepthVaryings input) : SV_Target
            {
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
