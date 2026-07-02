Shader "UI/HoloEdgeGlow"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}

        // 엣지 글로우
        _GlowColor    ("Glow Color",     Color)         = (0.3, 0.8, 1.0, 1.0)
        _GlowWidth    ("Glow Width (px)", Range(1, 30)) = 8.0    // 바깥으로 번지는 픽셀 폭
        _GlowPower    ("Glow Falloff",   Range(0.1, 5)) = 1.5    // 클수록 가장자리로 빨리 사라짐
        _GlowIntensity("Glow Intensity", Range(0, 3))   = 1.2

        // 은은한 숨쉬기 (부드러운 사인)
        _BreathSpeed  ("Breath Speed",   Range(0, 6))   = 1.5
        _BreathMin    ("Breath Min",     Range(0, 1))   = 0.6    // 최소 밝기(완전히 안 꺼짐)

        // 안쪽 라인에도 살짝 글로우 (0이면 바깥 halo만)
        _InnerGlow    ("Inner Glow",     Range(0, 1))   = 0.0

        // 텍스처 해상도 (실제 스프라이트 해상도랑 맞춰야 함)
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

            // 소프트 글로우 링 개수 (많을수록 부드럽지만 무거움)
            #define GLOW_RINGS 5

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
            float  _BreathSpeed;
            float  _BreathMin;
            float  _InnerGlow;
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

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv  = i.uv;
                fixed4 col = tex2D(_MainTex, uv) * i.color;   // Image Color 반영

                float2 texel = float2(1.0 / _TexWidth, 1.0 / _TexHeight);

                float2 offsets[8] = {
                    float2(-1,  0), float2( 1,  0),
                    float2( 0, -1), float2( 0,  1),
                    float2(-1, -1), float2( 1, -1),
                    float2(-1,  1), float2( 1,  1)
                };

                // ── 다중 링 샘플링으로 부드러운 외곽 halo ──────
                // 가까운 링에서 불투명 픽셀이 잡힐수록 글로우가 강함.
                // 바깥으로 갈수록 먼 링에서만 잡혀서 자연스럽게 페이드.
                float glow = 0.0;
                float wsum = 0.0;
                [unroll]
                for (int r = 1; r <= GLOW_RINGS; r++)
                {
                    float dist = ((float)r / (float)GLOW_RINGS) * _GlowWidth;
                    float w    = 1.0 - ((float)(r - 1) / (float)GLOW_RINGS); // 안쪽 링일수록 가중치↑

                    float ringMax = 0.0;
                    for (int j = 0; j < 8; j++)
                    {
                        float2 sUV = uv + offsets[j] * texel * dist;
                        ringMax = max(ringMax, tex2D(_MainTex, sUV).a);
                    }
                    glow += ringMax * w;
                    wsum += w;
                }
                glow /= wsum;

                // 내가 투명하고 주변이 불투명할수록 큼 = 테두리 바깥 halo
                float outerGlow = saturate(glow - col.a);
                // 내가 불투명한 안쪽에도 약간 얹고 싶으면 _InnerGlow 사용
                float innerGlow = saturate(glow) * col.a * _InnerGlow;
                float edge = pow(saturate(outerGlow + innerGlow), _GlowPower);

                // ── 은은한 숨쉬기 (완전히 안 꺼짐) ────────────
                float breath = sin(_Time.y * _BreathSpeed) * 0.5 + 0.5;   // 0~1
                breath = lerp(_BreathMin, 1.0, breath);

                float glowFinal = edge * _GlowIntensity * breath;

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
