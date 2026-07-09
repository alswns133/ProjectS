Shader "ProjectS/UI/SgLightningBar"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}

        // ── SG fill ──
        _FillAmount ("Fill Amount", Range(0,1)) = 1
        _FillColor  ("Fill Color", Color) = (0.188, 0.729, 0.831, 0.18)  // #30BAD4 저알파 배경
        _EmptyColor ("Empty Color", Color) = (0.05, 0.08, 0.10, 1)
        [Toggle] _FlipX ("Flip Fill Direction", Float) = 0
        // 0 = 왼쪽이 기준(안쪽), 오른쪽부터 소모  ← SG(오른쪽 바)용
        // 1 = 오른쪽이 기준(안쪽), 왼쪽부터 소모  ← HP(왼쪽 바)에 재사용 시
        _EdgeSkew ("Edge Skew (fill 경계 기울기)", Range(-0.5,0.5)) = 0.0
        // UV 기준 전단량. 계산: (바 높이px / 바 너비px) / tan(기울기 각도)
        // 예) 400×40px 바, 78도 → (40/400)/tan(78°) ≈ 0.021. 방향 반대면 부호만 뒤집기.

        // ── 번개 ──
        _BoltColor  ("Bolt Color", Color) = (0.188, 0.729, 0.831, 1)     // #30BAD4
        _CoreColor  ("Bolt Core Color", Color) = (0.85, 1.0, 1.0, 1)     // 코어(밝은 중심)
        _Amplitude  ("Amplitude", Range(0,0.5)) = 0.24
        _JitterFps  ("Jitter FPS (튀는 속도)", Range(1,30)) = 8
        _LineWidth  ("Line Width", Range(0.002,0.1)) = 0.035
        _CoreWidth  ("Core Width", Range(0.001,0.05)) = 0.012
        _BranchStr  ("Branch Bolt Strength", Range(0,1)) = 0.4
        _Surge      ("Surge (스킬 사용 순간 0→1)", Range(0,1)) = 0

        // UI 마스크/클립 표준
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct Varyings   { float4 positionHCS:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            float  _FillAmount, _FlipX, _EdgeSkew;
            float4 _FillColor, _EmptyColor, _BoltColor, _CoreColor;
            float  _Amplitude, _JitterFps, _LineWidth, _CoreWidth, _BranchStr, _Surge;

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            // ── 해시 / 밸류 노이즈 ──
            // sin 기반 해시는 인자가 커지면 GPU float 정밀도가 무너져
            // 노이즈가 상수로 붕괴함(번개가 바닥에 깔리는 원인). sin 없는 해시 사용.
            float hash1(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            float vnoise(float x)
            {
                float i = floor(x);
                float f = frac(x);
                float u = f * f * (3.0 - 2.0 * f);        // smoothstep 보간
                return lerp(hash1(i), hash1(i + 1.0), u); // 0~1
            }

            // 지그재그 번개 오프셋: 저주파 굽이 + 고주파 잔떨림 (-0.5~0.5)
            float boltOffset(float x, float tq, float seed)
            {
                float n1 = vnoise(x * 6.0  + tq * 13.7 + seed) - 0.5;
                float n2 = vnoise(x * 22.0 + tq * 29.3 + seed) - 0.5;
                return n1 * 0.7 + n2 * 0.3;
            }

            // 한 가닥 그리기: 파형까지 거리 → 글로우 + 코어
            float2 boltLine(float2 uv, float tq, float seed, float amp)
            {
                float off  = boltOffset(uv.x, tq, seed) * amp;
                float dist = abs(uv.y - (0.5 + off));
                float glow = 1.0 - smoothstep(0.0, _LineWidth, dist);
                float core = 1.0 - smoothstep(0.0, _CoreWidth, dist);
                return float2(glow, core);
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;
                half4 spr = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

                // fill 좌표: _FlipX 로 소모 방향 전환
                float fx = lerp(uv.x, 1.0 - uv.x, _FlipX);

                // ★ 전단(shear): 경계선을 바 기울기에 맞춰 사선으로
                //   _FlipX 시 시각적 기울기 방향이 뒤집히지 않게 부호 보정
                float skewSign = lerp(1.0, -1.0, _FlipX);
                fx += (uv.y - 0.5) * _EdgeSkew * skewSign;

                // ── 1) fill 배경 ──
                float filled = step(fx, _FillAmount);
                half4 col = lerp(_EmptyColor, _FillColor, filled);

                // ── 2) 번개 ──
                // ★ tq를 256 주기로 래핑: 무한히 커지면 해시 입력이 float 정밀도를
                //   벗어나 노이즈가 붕괴함. 256틱마다 패턴이 반복되지만 체감 불가.
                float tq = fmod(floor(_Time.y * _JitterFps), 256.0);

                // 서지: 진폭·밝기 순간 증폭
                float amp   = _Amplitude * (1.0 + _Surge * 1.5);
                float boost = 1.0 + _Surge * 1.2;

                // 본체 + 분기 가닥(다른 시드, 살짝 어긋난 시간)
                float2 main   = boltLine(uv, tq,        0.0,  amp);
                float2 branch = boltLine(uv, tq + 7.0, 53.7, amp * 0.8) * _BranchStr;

                // 밝기 깜빡임: 시간 양자마다 랜덤 강도 (0.6~1.0)
                float flicker = lerp(0.6, 1.0, hash1(tq));

                float glow = saturate((main.x + branch.x) * flicker * boost);
                float core = saturate((main.y + branch.y) * boost);

                // 번개는 SG가 남아있는 구간에만 흐름 (경계는 살짝 소프트하게)
                float boltMask = smoothstep(_FillAmount + 0.02, _FillAmount - 0.02, fx);
                glow *= boltMask;
                core *= boltMask;

                col.rgb = lerp(col.rgb, _BoltColor.rgb, glow);
                col.rgb = lerp(col.rgb, _CoreColor.rgb, core);
                col.a   = max(col.a, glow * _BoltColor.a);

                col.a *= spr.a;    // 스프라이트 모양 마스크
                col   *= IN.color;
                return col;
            }
            ENDHLSL
        }
    }
    FallBack "UI/Default"
}
