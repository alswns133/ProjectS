// 소프트웨어 마우스 커서 이미지 + 바깥 글로우(UI 전용). (2026-10-07 TH 추가)
//
// 글로우를 텍스처에 구워 넣지 않고 셰이더로 계산하는 이유: 색·크기·숨쉬기·클릭 반짝임을 인스펙터와 스크립트로 바꾸기 위함.
// 원리: 커서 알파를 주변 두 겹 원(16탭)에서 평균 내면 윤곽 바깥으로 번진 부드러운 알파가 된다(값싼 팽창+블러).
// 그 값에서 커서 본체가 덮는 부분을 빼 "바깥쪽만" 빛나게 한다.
//
// 글로우가 커서 바깥으로 번질 자리가 필요해서, 쿼드는 커서보다 크게 두고 _Pad 비율만큼 사방을 비워 쓴다.
// 크기·피벗(클릭 지점) 보정은 CircuitCursorFx.cs가 _Pad를 읽어 맞춘다.
// RawImage 전용이다 — UV 0~1이 텍스처 전체라는 전제라, 스프라이트 아틀라스에 들어간 Sprite(Image)에 쓰면 UV가 어긋난다.
Shader "ProjectS/UI Cursor Glow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Cursor Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)

        [Header(Layout)]
        _Pad ("Glow Padding (quad fraction per side)", Range(0, 0.4)) = 0.18

        [Header(Glow)]
        _GlowColor ("Glow Color", Color) = (1, 0.1, 0.15, 1)
        _GlowRadius ("Glow Radius (cursor UV)", Range(0.005, 0.25)) = 0.07
        _GlowStrength ("Glow Strength", Range(0, 6)) = 2.2
        _GlowFalloff ("Glow Falloff (gamma)", Range(0.3, 4)) = 1.4

        [Header(Pulse)]
        _PulseSpeed ("Breathing Speed (Hz)", Float) = 0.6
        _PulseAmount ("Breathing Amount", Range(0, 1)) = 0.25

        [Header(Click)]
        _ClickGlow ("Click Flash Strength", Range(0, 6)) = 2.5
        _ClickDuration ("Click Flash Duration (s)", Float) = 0.25

        // 아래는 스크립트가 넣는 값(인스펙터에서 만지지 않는다).
        [HideInInspector] _Now ("Now", Float) = 0
        [HideInInspector] _ClickTime ("Click Time", Float) = -100

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
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

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;

            float _Pad;

            fixed4 _GlowColor;
            float _GlowRadius;
            float _GlowStrength;
            float _GlowFalloff;

            float _PulseSpeed;
            float _PulseAmount;

            float _ClickGlow;
            float _ClickDuration;

            float _Now;
            float _ClickTime;

            float4 _ClipRect;

            // 두 겹 원 8방향씩. 바깥 원은 22.5° 돌려 놓아 8각형 무늬(밴딩)가 덜 보이게 한다.
            static const float2 GlowDirs[8] =
            {
                float2(1, 0), float2(0.7071, 0.7071), float2(0, 1), float2(-0.7071, 0.7071),
                float2(-1, 0), float2(-0.7071, -0.7071), float2(0, -1), float2(0.7071, -0.7071)
            };
            static const float2x2 OuterRot = float2x2(0.9239, -0.3827, 0.3827, 0.9239);

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color * _Color;
                return OUT;
            }

            // 텍스처 밖은 투명으로 본다. Wrap 모드가 Clamp여도 가장자리 픽셀이 늘어나 글로우가 번지는 것을 막기 위함.
            float AlphaAt(float2 uv)
            {
                float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
                return tex2D(_MainTex, saturate(uv)).a * inside;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = (IN.texcoord - _Pad) / max(1.0 - 2.0 * _Pad, 0.001);

                fixed4 tex = tex2D(_MainTex, saturate(uv));
                tex.a *= step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
                tex *= IN.color;

                float spread = 0.0;
                [unroll]
                for (int k = 0; k < 8; k++)
                {
                    spread += AlphaAt(uv + GlowDirs[k] * _GlowRadius * 0.45);
                    spread += AlphaAt(uv + mul(OuterRot, GlowDirs[k]) * _GlowRadius);
                }
                spread = pow(saturate(spread / 16.0 * 2.0), _GlowFalloff);

                // 숨쉬기 + 클릭 순간 반짝임. 1 - exp(-x)로 눌러 세기를 올려도 글로우가 딱딱한 띠로 뭉개지지 않게 한다.
                float breathing = 1.0 + _PulseAmount * sin(_Now * _PulseSpeed * 6.2831853);
                float click = saturate(1.0 - (_Now - _ClickTime) / max(_ClickDuration, 0.001));
                float strength = _GlowStrength * breathing + click * _ClickGlow;
                float glow = (1.0 - exp(-spread * strength)) * (1.0 - tex.a) * _GlowColor.a * IN.color.a;

                fixed4 color;
                color.a = tex.a + glow;
                color.rgb = (tex.rgb * tex.a + _GlowColor.rgb * glow) / max(color.a, 1e-4);

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
