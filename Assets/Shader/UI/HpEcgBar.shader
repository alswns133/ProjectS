Shader "ProjectS/UI/HpEcgBar"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}

        // ── HP fill (기존 FillGauge 의 _FillAmount 그대로 사용) ──
        _FillAmount ("Fill Amount", Range(0,1)) = 1
        _FillColor  ("Fill Color", Color) = (0.886, 0.294, 0.290, 1)   // #E24B4A 레드 HP
        _EmptyColor ("Empty Color", Color) = (0.07, 0.09, 0.11, 1)     // 빈 영역 (어두운 배경)

         [Toggle] _FlipX ("Flip Fill Direction", Float) = 0
        // 0 = 왼쪽이 기준(안쪽), 오른쪽부터 소모  ← SG(오른쪽 바)용
        // 1 = 오른쪽이 기준(안쪽), 왼쪽부터 소모  ← HP(왼쪽 바)에 재사용 시
        _EdgeSkew ("Edge Skew (fill 경계 기울기)", Range(-0.5,0.5)) = 0.0
        // UV 기준 전단량. 계산: (바 높이px / 바 너비px) / tan(기울기 각도)
        // 예) 400×40px 바, 78도 → (40/400)/tan(78°) ≈ 0.021. 방향 반대면 부호만 뒤집기.

        // ── ECG 파형 ──
        _EcgColor   ("ECG Color", Color) = (1, 0.35, 0.29, 1)
        _Alive      ("Alive (0=flatline,1=beat)", Range(0,1)) = 1
        _Amplitude  ("Amplitude", Range(0,2)) = 0.55   // 바 높이 대비 파형 진폭
        _BeatSpeed  ("Beat Speed", Float) = 0.9          // 심박수(스크롤 속도)
        _LineWidth  ("Line Width", Range(0.002,0.08)) = 0.02
        _Repeat     ("Beats Across Bar", Float) = 3    // 바 가로에 몇 박동 보일지
        _EcgGlow    ("Glow Boost", Range(1,4)) = 1.6

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

            float  _FillAmount;
            float4 _FillColor, _EmptyColor, _EcgColor;
            float  _Alive, _Amplitude, _BeatSpeed, _LineWidth, _Repeat, _EcgGlow;
            float _FlipX, _EdgeSkew;


            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            // 가우시안 펄스
            float gauss(float t, float c, float w) { float x=(t-c)*w; return exp(-x*x); }

            // 한 주기(0~1) PQRST 근사 — 데모 미리보기와 동일한 계수
            float ecgWave(float t)
            {
                t = frac(t);
                float p  =  gauss(t,0.15,30.0)*0.15;
                float q  = -gauss(t,0.28,60.0)*0.10;
                float r  =  gauss(t,0.32,70.0)*1.00;   // R 스파이크
                float s  = -gauss(t,0.36,55.0)*0.28;
                float tw =  gauss(t,0.58,14.0)*0.28;
                return p+q+r+s+tw;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;

                // ── 0) 소스 스프라이트 샘플 (모양 마스크) ──
                half4 spr = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

                float fx = lerp(uv.x, 1.0 - uv.x, _FlipX);
                float skewSign = lerp(1.0, -1.0, _FlipX);
                float s = _EdgeSkew * skewSign;
                // 기울기 기준점을 잔여량에 따라 이동한다.
                // 풀피(_FillAmount=1)에선 중앙(uv.y-0.5) 기준 → 바가 정확히 꽉 참.
                // 0에 가까울수록 하단(uv.y) 기준으로 옮겨 → 낮은 잔여량 구간의 비율을 맞춤.
                // 두 끝을 각각 최적점에 고정해 스프라이트 모양과 어긋나 짤리는 문제를 없앤다.
                fx += (uv.y - 1.0 * _FillAmount) * s;

                // ── 1) HP fill (좌→우) ──
                float filled = step(fx, _FillAmount);
                half4 col = lerp(_EmptyColor, _FillColor, filled);

                // ── 2) ECG 파형 ──
                // 시간에 따라 스크롤. _Alive 로 진폭을 죽여 flatline 으로.
                float phase = uv.x * _Repeat + _Time.y * _BeatSpeed;

                // ecgWave 의 최대 진폭은 R(=1.0). 기준선이 중앙이라 편차가 0.5를 넘으면 바를 벗어난다.
                // 그런데 잘라야 하는 건 곡선이 아니라 '선의 바깥쪽'이다 — 선은 곡선에서
                // _LineWidth 만큼 번지고, 픽셀 경계 보정(smoothstep 의 ±1px)까지 더 나간다.
                // 그 여유를 빼지 않으면 R 꼭대기가 딱 경계에 걸려 위아래로 삐져나온다.
                float margin = _LineWidth + fwidth(uv.y) * 1.5;
                float maxDev = max(0.5 - margin, 0.0);

                // 기본 진폭을 먼저 자르고 그 다음 _Alive 로 줄인다. 순서를 뒤집으면 _Amplitude 가 클 때
                // 웬만한 체력 구간이 전부 상한에 걸려, 체력이 깎일수록 파형이 약해지는 변화가 죽는다.
                float amp   = min(_Amplitude, maxDev) * _Alive;
                float wave  = ecgWave(phase) * amp;

                float centerY = 0.5;

                // 라인: 기울기로 정규화한 거리. abs(uv.y - y) 를 그대로 쓰면 두께가 항상
                // '세로'로만 재져서, R 스파이크처럼 경사가 급한 곳에서는 인접 픽셀 열의 선분이
                // 두께보다 멀리 떨어져 점선처럼 끊긴다. fwidth 로 나눠 픽셀 단위 거리로 바꾸면
                // 경사와 무관하게 두께가 일정해진다.
                float f    = uv.y - (centerY + wave);
                float grad = max(fwidth(f), 1e-5);
                float dPx  = abs(f) / grad;                          // 선까지의 픽셀 거리
                float wPx  = _LineWidth / max(fwidth(uv.y), 1e-5);   // 선 두께도 픽셀로

                float lineMask = 1.0 - smoothstep(wPx - 1.0, wPx + 1.0, dPx);
                lineMask = saturate(lineMask * _EcgGlow);

                col.rgb = lerp(col.rgb, _EcgColor.rgb, lineMask);
                col.a   = max(col.a, lineMask * _EcgColor.a);

                col.a *= spr.a;    // ★ 스프라이트 모양(헥사곤 등) 밖은 잘라냄
                col   *= IN.color; // UI 틴트/알파 반영
                return col;
            }
            ENDHLSL
        }
    }
    FallBack "UI/Default"
}
