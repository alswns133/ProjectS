Shader "UI/HoloEdgeGlow"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}

        // 테두리 글로우
        _GlowColor    ("Glow Color",     Color)         = (0.3, 0.8, 1.0, 1.0)
        _GlowWidth    ("Glow Width (px)", Range(1, 80)) = 20.0   // 가장자리에서 안쪽으로 번지는 폭(스크린 px)
        _GlowPower    ("Glow Falloff",   Range(0.1, 6)) = 2.0    // 클수록 가장자리에 딱 붙음
        _GlowIntensity("Glow Intensity", Range(0, 3))   = 1.2

        // 은은한 숨쉬기 (부드러운 사인)
        _BreathSpeed  ("Breath Speed",   Range(0, 6))   = 1.5
        _BreathMin    ("Breath Min",     Range(0, 1))   = 0.6    // 최소 밝기(완전히 안 꺼짐)

        // 스프라이트 모양(둥근 모서리/투명 여백) 따라가기
        _UseAlphaMask ("Use Alpha Mask", Range(0, 1))   = 1.0

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
            float  _BreathSpeed;
            float  _BreathMin;
            float  _UseAlphaMask;

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

                // ── 사각형 가장자리까지의 거리 (스크린 픽셀 단위) ──
                // uv.x의 왼/오른 가장자리, uv.y의 위/아래 가장자리 중 가장 가까운 거리.
                // fwidth로 스크린 픽셀 환산 → 패널을 늘려도 테두리 두께 일정.
                float2 duv    = min(uv, 1.0 - uv);
                float2 px     = duv / max(fwidth(uv), 1e-5);
                float  distPx = min(px.x, px.y);

                // ── 테두리 글로우 밴드: 가장자리 0px에서 _GlowWidth까지 페이드 ──
                float glow = 1.0 - smoothstep(0.0, _GlowWidth, distPx);
                glow = pow(glow, _GlowPower);

                // 둥근 모서리/투명 여백을 스프라이트 알파로 따라가기 (옵션)
                glow *= lerp(1.0, saturate(col.a * 2.0), _UseAlphaMask);

                // ── 은은한 숨쉬기 (완전히 안 꺼짐) ──
                float breath = sin(_Time.y * _BreathSpeed) * 0.5 + 0.5;
                breath = lerp(_BreathMin, 1.0, breath);

                float glowFinal = glow * _GlowIntensity * breath;

                // ── 합성 ──
                col.rgb += _GlowColor.rgb * glowFinal;
                col.a    = max(col.a, glowFinal * _GlowColor.a);

                // ── UI Mask 클리핑 ──
                col.a *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);

                return col;
            }
            ENDCG
        }
    }
}
