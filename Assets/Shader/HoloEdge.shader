Shader "UI/HoloEdgeGlow"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}

        // 엣지 글로우
        _GlowColor    ("Glow Color",     Color)         = (0.3, 0.8, 1.0, 1.0)
        _GlowWidth    ("Glow Width",     Range(1, 10))  = 3.0    // 샘플링 픽셀 수
        _GlowPower    ("Glow Power",     Range(0.1, 5)) = 1.5
        _GlowIntensity("Glow Intensity", Range(0, 3))   = 1.5

        // 숨쉬기
        _PulseSpeed   ("Flicker Rate",    Range(0, 10))  = 3.0
        _PulseStrength("Flicker Chance",  Range(0, 1))   = 0.25

        // 텍스처 해상도 (실제 스프라이트 해상도 맞게)
        _TexWidth ("Tex Width",  Float) = 256
        _TexHeight("Tex Height", Float) = 256

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
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                float2 uv       : TEXCOORD0;
                float4 color    : COLOR;
                float4 worldPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4    _MainTex_ST;
            float4    _ClipRect;

            float4 _GlowColor;
            float  _GlowWidth;
            float  _GlowPower;
            float  _GlowIntensity;
            float  _PulseSpeed;
            float  _PulseStrength;
            float  _TexWidth;
            float  _TexHeight;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex   = UnityObjectToClipPos(v.vertex);
                o.uv       = TRANSFORM_TEX(v.uv, _MainTex);
                o.color    = v.color;
                o.worldPos = v.vertex;
                return o;
            }

        

            // ← 여기에 rand 추가
            float rand(float2 co)
            {
                return frac(sin(dot(co, float2(12.9898, 78.233))) * 43758.5453);
            }

           
           fixed4 frag(v2f i) : SV_Target
           {
                float2 uv = i.uv;

                // Image Color 필드 반영
                fixed4 col = tex2D(_MainTex, uv) * i.color;

                // ── 알파 경계 감지 ────────────────────────────
                float2 texel = float2(1.0 / _TexWidth, 1.0 / _TexHeight);

                float2 offsets[8] = {
                    float2(-1,  0), float2( 1,  0),
                    float2( 0, -1), float2( 0,  1),
                    float2(-1, -1), float2( 1, -1),
                    float2(-1,  1), float2( 1,  1)
                };

                float maxAlphaDiff = 0.0;
                for (int j = 0; j < 8; j++)
                {
                    float2 sampleUV = uv + offsets[j] * texel * _GlowWidth;
                    float neighborAlpha = tex2D(_MainTex, sampleUV).a;
                    maxAlphaDiff = max(maxAlphaDiff, abs(col.a - neighborAlpha));
                }

                float edge = pow(saturate(maxAlphaDiff), _GlowPower);

                // ── 숨쉬기 ────────────────────────────────────
                // 시간을 낮은 해상도로 끊어서 "툭툭" 상태 전환
                float t         = _Time.y * _PulseSpeed;
                float timeStep  = floor(t);
                float timeFrac  = frac(t);

                // 현재 상태: 켜짐(1) or 꺼짐(0)
                float flickerRand = rand(float2(timeStep, 0.3));
                float isOff       = step(1.0 - _PulseStrength, flickerRand);
                // PulseStrength가 높을수록 꺼지는 빈도 증가

                // 꺼질 때 빠르게 지직거리는 효과
                // timeFrac이 0 근처 (상태 전환 직후)에만 지직
                float glitch      = rand(float2(timeStep * 3.7, timeFrac * 10.0));
                float glitchOn    = step(0.6, glitch) * step(timeFrac, 0.15);
                // 상태 전환 후 0.15 구간 안에서만 발생

                // 최종 pulse 값
                // 꺼진 상태면 0.05 (완전히 꺼지지 않고 약간 남음)
                // 지직 상태면 랜덤하게 0~1 사이로 튀기
                float pulse = lerp(1.0, 0.05, isOff);
                pulse       = lerp(pulse, rand(float2(timeStep, timeFrac)), glitchOn);
                float glowFinal = edge * _GlowIntensity * pulse;

                // ── 글로우 합성 (알파 밖으로 번지게) ──────────
                col.rgb += _GlowColor.rgb * glowFinal;
                col.a    = max(col.a, glowFinal * _GlowColor.a);

                // ── UI Mask 클리핑 ────────────────────────────
                col.a *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);

                return col;
           }
            ENDCG
        }
    }
}