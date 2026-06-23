Shader "UI/WaveHPBar_URP"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}

        // HP
        _FillAmount ("Fill Amount", Range(0, 1)) = 1.0

        // 색상
        _FullColor  ("Full HP Color",  Color) = (0.2, 0.9, 0.3, 1)
        _LowColor   ("Low HP Color",   Color) = (0.9, 0.2, 0.2, 1)
        _BgColor    ("BG Color",       Color) = (0.1, 0.1, 0.1, 0.8)

        // 출렁임
        _WaveSpeed  ("Wave Speed",  Range(0, 10)) = 3.0
        _WaveFreq   ("Wave Freq",   Range(0, 30)) = 8.0
        _WaveAmp    ("Wave Amp",    Range(0, 0.1)) = 0.03

        // Rim
        _RimColor   ("Rim Color",   Color) = (0, 1, 1, 1)
        _RimPower   ("Rim Power",   Range(0.5, 10)) = 2
        _RimWidth   ("Rim Width",   Range(0, 0.5)) = 0.08

        // 원형 클리핑 여부 (1이면 원 밖을 잘라냄)
        [Toggle] _CircleClip ("Circle Clip", Float) = 1

        // UI Mask용
        _StencilComp      ("Stencil Comparison", Float) = 8
        _Stencil          ("Stencil ID",         Float) = 0
        _StencilOp        ("Stencil Operation",  Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask  ("Stencil Read Mask",  Float) = 255
        _ColorMask        ("Color Mask",         Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
            "RenderPipeline"="UniversalPipeline"
        }

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
            Name "Default"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // URP 코어 라이브러리
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float4 color       : COLOR;
                float4 worldPos    : TEXCOORD1;
            };

            // 텍스처는 URP 방식으로 선언
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // 머티리얼 프로퍼티는 CBUFFER로 묶어야 SRP Batcher 호환됨
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _ClipRect;

                float  _FillAmount;

                float4 _FullColor;
                float4 _LowColor;
                float4 _BgColor;

                float  _WaveSpeed;
                float  _WaveFreq;
                float  _WaveAmp;

                float4 _RimColor;
                float  _RimPower;
                float  _RimWidth;

                float  _CircleClip;
            CBUFFER_END

            // ── UI 클리핑 함수 (UnityUI.cginc의 URP 대체) ──
            // ClipRect 영역 밖이면 0, 안이면 1을 반환
            half UnityGet2DClipping(float2 position, float4 clipRect)
            {
                float2 inside = step(clipRect.xy, position) * step(position, clipRect.zw);
                return inside.x * inside.y;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color       = IN.color;
                OUT.worldPos    = IN.positionOS;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;

                // ── 원형 거리 계산 (Rim과 클리핑 공용) ──────
                float2 center = uv - 0.5;
                float  dist   = length(center);

                // ── 원형 클리핑: 원 밖 픽셀 버리기 ──────────
                // _CircleClip이 1이고 반지름 0.5 밖이면 그리지 않음
                if (_CircleClip > 0.5 && dist > 0.5)
                    discard;

                // ── 출렁임 ──────────────────────────────────
                // x 위치에 따라 sin파로 경계선을 위아래로 흔들기
                float wave = sin(uv.x * _WaveFreq + _Time.y * _WaveSpeed) * _WaveAmp;

                // 출렁이는 경계선 기준으로 채워진 영역 판별
                float fillEdge = _FillAmount + wave;
                float isFilled = step(uv.y, fillEdge);  // 1 = 채워짐, 0 = 빔

                // ── HP 비율에 따라 색 변화 ───────────────────
                // smoothstep으로 50% 이하부터 빨갛게 (자연스러운 위험 표현)
                float colorT = smoothstep(0.0, 0.5, _FillAmount);
                float4 hpColor = lerp(_LowColor, _FullColor, colorT);

                // ── 최종 색 합성 ─────────────────────────────
                float4 col = lerp(_BgColor, hpColor, isFilled);

                // ── 원형 Rim Glow ───────────────────────────
                float rim = saturate((dist - (0.5 - _RimWidth)) / _RimWidth);
                rim = pow(rim, _RimPower);
                col.rgb += _RimColor.rgb * rim;

                // ── UI Mask 클리핑 ───────────────────────────
                col.a *= UnityGet2DClipping(IN.worldPos.xy, _ClipRect);
                col.a *= IN.color.a;

                return col;
            }
            ENDHLSL
        }
    }

    Fallback "UI/Default"
}
