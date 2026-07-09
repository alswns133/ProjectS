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
                fx += (uv.y - 0.5) * _EdgeSkew * skewSign;

                // ── 1) HP fill (좌→우) ──
                float filled = step(fx, _FillAmount);
                half4 col = lerp(_EmptyColor, _FillColor, filled);

                // ── 2) ECG 파형 ──
                // 시간에 따라 스크롤. _Alive 로 진폭을 죽여 flatline 으로.
                float phase = uv.x * _Repeat + _Time.y * _BeatSpeed;
                float wave  = ecgWave(phase) * _Amplitude * _Alive;

                float centerY = 0.5;
                float dist = abs(uv.y - (centerY + wave));
                // 라인: 거리 기반 SDF → 부드러운 두께
                float lineMask = 1.0 - smoothstep(0.0, _LineWidth, dist);
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
