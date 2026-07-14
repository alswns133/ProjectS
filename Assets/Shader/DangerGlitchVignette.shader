Shader "ProjectS/UI/DangerGlitchVignette"
{
    Properties
    {
        [PerRendererData] _MainTex ("Vignette Sprite", 2D) = "white" {}

        // C#에서 매 HP 변경 시 세팅. 0 = 안전, 1 = 빈사.
        _Danger ("Danger", Range(0, 1)) = 0

        // 글리치 전체 배율.
        _Glitch ("Glitch Strength", Range(0, 2)) = 1

        // 초당 글리치 갱신 횟수. SG 바와 동일한 시간 양자화 리듬.
        _JitterFps ("Jitter FPS", Range(4, 30)) = 12

        // RGB 채널 분리 최대 폭 (UV 단위).
        _RgbSplit ("RGB Split", Range(0, 0.08)) = 0.035
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Danger;
                float _Glitch;
                float _JitterFps;
                float _RgbSplit;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            // Hoskins 스타일 곱셈 해시. SG 바에서 검증된 것과 동일 계열 —
            // sin 기반 해시는 큰 입력에서 정밀도가 무너지므로 사용하지 않는다.
            float hash11(float n)
            {
                n = frac(n * 0.1031);
                n *= n + 33.33;
                n *= n + n;
                return frac(n);
            }

            float hash21(float2 p)
            {
                return hash11(dot(p, float2(127.1, 311.7)));
            }

            float vnoise(float x)
            {
                float i = floor(x);
                float f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(hash11(i), hash11(i + 1.0), f);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color; // Image.color — 기존 maxAlpha 제어가 여기로 들어온다.
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // 시간 양자화 + fmod 256 랩핑 (SG 바와 동일한 정밀도 보호).
                float tq = fmod(floor(_Time.y * _JitterFps), 256.0);
                float t = _Time.y;

                float g = saturate(_Glitch * _Danger);

                // guard: 원래 알파가 0.2 미만인 지역(시야 중앙)에서는 모든 글리치
                // 효과를 강제로 0으로 만든다. UV 변위가 없어도 RGB 분리 고스트가
                // 안쪽으로 번지는 것을 막는 역할.
                half4 texC = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float guard = smoothstep(0.02, 0.2, texC.a);

                // --- 1. RGB 채널 분리 (알파 마스크를 어긋나게 샘플) ---
                float off = _RgbSplit * g * (0.5 + 0.5 * vnoise(t * 3.7)) * guard;
                half aR = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2(off, 0)).a;
                half aB = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv - float2(off, 0)).a;

                // --- 2. 플리커 (신호 끊김 / 과전압) ---
                float fr = hash11(tq * 3.77);
                float flick = 1.0;
                if (fr < 0.2 * g)
                    flick = 0.15 + 0.5 * hash11(tq * 9.1);
                else if (fr > 1.0 - 0.12 * g)
                    flick = 1.8;

                // --- 3. 균열 라인 2줄 + 블록 노이즈 ---
                float crack = 0.0;
                [unroll]
                for (int k = 0; k < 2; k++)
                {
                    float seed = tq * 1.91 + k * 37.3;
                    if (hash11(seed) < 0.45 * g)
                    {
                        float cy = hash11(seed * 4.71);
                        float w = 0.002 + 0.005 * hash11(seed * 6.3);
                        crack += (1.0 - smoothstep(0.0, w, abs(i.uv.y - cy))) * texC.a * guard * 1.6;
                    }
                }
                float blk = step(1.0 - 0.09 * g, hash21(floor(i.uv * float2(12.0, 7.0)) + tq))
                          * 0.7 * g * texC.a * guard;

                // --- 4. 저주파 펄스 (Danger가 높을수록 빨라짐) ---
                float pulse = 0.85 + 0.15 * sin(t * (3.0 + 6.0 * _Danger));

                // --- 합성 ---
                half4 col = texC * i.color; // 스프라이트 색 × Image.color (기존 알파 제어 유지)

                // 채널 분리: 중앙 대비 어긋난 마스크 차이만큼 R/B를 밀어 넣는다.
                col.r += saturate(aR - texC.a) * i.color.a * _Danger * guard;
                col.b += saturate(aB - texC.a) * i.color.a * _Danger * 0.5 * guard;

                // 균열·블록은 밝은 스파이크로 가산.
                col.rgb += half3(1.0, 0.25, 0.2) * (crack + blk) * i.color.a;
                col.a = saturate(col.a * flick * pulse + (crack + blk) * 0.6 * i.color.a);

                return col;
            }
            ENDHLSL
        }
    }
}
