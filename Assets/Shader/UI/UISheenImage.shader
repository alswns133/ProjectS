// 정적 이미지 위로 빛의 고리(sheen)만 퍼져 나가는 UI 셰이더. UIDiagonalFlow의 sheen 부분을 떼어 방사형으로 바꾼 시험용이다.
//
// 이미지(_MainTex)는 움직이지 않고 그대로 그리고, 그 위에 중심점에서 바깥으로 퍼지는 빛 고리를 얹는다.
// 빛은 이미지에 "더해지는" 밝기라서 투명한 픽셀에는 아무것도 생기지 않는다 — 알파는 이미지 그대로이므로
// 빛이 이미지 윤곽 밖으로 번지지 않는다.
//
// 한 주기(_Period) = 고리가 퍼지는 구간 + 쉬는 구간(_PauseRatio). 쉬는 동안에는 고리가 화면 밖까지 나가 있어
// 아무것도 보이지 않으므로 별도 게이트 없이도 반짝임 사이가 깨끗하게 비고, 주기가 돌아올 때 중심에서 새로 시작한다.
//
// 사용: Image 또는 RawImage에 이 셰이더의 머티리얼을 지정한다(이미지는 Image의 Sprite / RawImage의 Texture).
// Image는 스프라이트가 아틀라스에 묶여 있으면 UV가 0~1이 아니라 중심 위치가 어긋난다 — 그럴 땐 RawImage를 쓰거나
// 스프라이트 패킹을 끈다. 시간은 _Time(timeScale 영향)을 쓴다.
//
// 고리가 타원으로 일그러지지 않게 화면 비율은 UV 미분(1픽셀당 UV 변화량)에서 자동으로 구한다.
// 회전한 오브젝트처럼 이 추정이 맞지 않을 때만 _AspectOverride에 가로/세로를 직접 넣는다.
Shader "ProjectS/UI Sheen Image"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)

        [Header(Sheen)]
        _SheenColor ("Sheen Color", Color) = (0.6, 0.82, 1, 1)
        _SheenStrength ("Sheen Strength", Range(0, 3)) = 1

        [Header(Radial)]
        // 고리가 시작하는 점(UV). (0.5, 0.5) = 이미지 중앙.
        _Center ("Center (UV)", Vector) = (0.5, 0.5, 0, 0)
        // 고리 두께(이미지 높이 대비). 클수록 퍼진 빛 띠가 두껍다.
        _SheenWidth ("Ring Width (of height)", Range(0.01, 0.5)) = 0.12
        // 켜면 바깥에서 중심으로 모여든다.
        [Toggle] _Inward ("Inward (Converge To Center)", Float) = 0

        [Header(Timing)]
        // 한 번 반짝이고 다음 반짝임이 시작될 때까지의 시간(초) — 쉬는 시간 포함.
        _Period ("Period (seconds)", Float) = 3
        // 한 주기 중 쉬는 비율. 0 = 쉬지 않고 연달아, 0.6 = 40%는 퍼지고 60%는 쉬는다.
        _PauseRatio ("Pause Ratio (0-0.95)", Range(0, 0.95)) = 0.5

        [Header(Mask)]
        // 0 = 이미지 전체에 균일하게 얹힘, 1 = 이미지의 밝은 부분에서만 빛남(금속·유리 반짝임 느낌).
        _LuminanceMask ("Only On Bright Areas", Range(0, 1)) = 0

        [Header(Advanced)]
        // 0 = 자동. 고리가 타원으로 보일 때만 가로/세로 비율을 직접 넣는다.
        _AspectOverride ("Aspect Override (W/H, 0 = auto)", Float) = 0

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
            "CanUseSpriteAtlas" = "True"
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

            fixed4 _SheenColor;
            float _SheenStrength;

            float4 _Center;
            float _SheenWidth;
            float _Inward;

            float _Period;
            float _PauseRatio;

            float _LuminanceMask;
            float _AspectOverride;

            float4 _ClipRect;

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

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, IN.texcoord) * IN.color;

                // 화면 비율(가로/세로): 1픽셀당 UV 변화량은 가로가 1/W, 세로가 1/H이므로 그 비가 W/H다.
                float aspect = _AspectOverride;
                if (aspect <= 0.0)
                {
                    aspect = fwidth(IN.texcoord.y) / max(fwidth(IN.texcoord.x), 0.000001);
                    aspect = clamp(aspect, 0.1, 10.0);
                }

                // 가로를 비율만큼 늘린 좌표에서 거리를 재야 고리가 타원이 아니라 원이 된다.
                float2 p = float2(IN.texcoord.x * aspect, IN.texcoord.y);
                float2 c = float2(_Center.x * aspect, _Center.y);
                float d = length(p - c);

                // 중심에서 가장 먼 모서리까지의 거리. 고리가 이만큼(+두께) 나가면 화면 밖으로 완전히 사라진다.
                float2 far = max(abs(c), abs(float2(aspect, 1.0) - c));
                float maxR = length(far);

                float w = max(_SheenWidth, 0.001);

                // 한 주기 중 퍼지는 구간의 진행도(0~1). 쉬는 구간에는 1에 머물러 고리가 화면 밖에 있다.
                float cycle = frac(_Time.y / max(_Period, 0.01));
                float k = saturate(cycle / max(1.0 - _PauseRatio, 0.01));
                if (_Inward > 0.5) k = 1.0 - k;

                // 시작(-w)·끝(maxR + w)을 화면 밖으로 잡아, 퍼지기 직전과 직후에 빛이 조금도 남지 않게 한다.
                float ring = lerp(-w, maxR + w, k);
                float x = (d - ring) / (w * 0.5);
                float sheen = exp(-x * x);

                // 쉬는 구간에도 가우시안 꼬리(약 2%)가 중심이나 모서리에 남는다. 그만큼을 깎아 쉬는 동안 완전히 비우고,
                // 쉬는 구간 자체는 확실히 끈다(경계에서 빛이 툭 켜지거나 꺼지지 않도록 깎은 뒤 다시 펴 준다).
                sheen = saturate((sheen - 0.02) / 0.98);
                sheen *= step(cycle, 1.0 - _PauseRatio);

                // 밝은 곳에서만 빛나게 할 수 있다: 0이면 균일, 1이면 이미지 밝기에 비례.
                float lum = dot(tex.rgb, float3(0.299, 0.587, 0.114));
                float mask = lerp(1.0, saturate(lum * 2.0), _LuminanceMask);

                fixed4 color;
                color.rgb = tex.rgb + _SheenColor.rgb * (sheen * mask * _SheenStrength * _SheenColor.a);
                color.a = tex.a;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
