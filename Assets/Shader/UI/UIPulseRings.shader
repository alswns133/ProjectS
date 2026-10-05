// 클래스 선택 페이지 배경 연출(UI 전용). 두 가지만 그린다:
//  · 상시: 중심의 선명한 점선 고리 두 겹. 서로 반대 방향으로 돌고, 반지름·칸 수·두께가 달라 겹쳐 보이지 않고 층으로 읽힌다.
//  · 터짐: 클래스를 고르는 순간 선택한 카드 위치에서 링이 한 번 퍼진다(PulseRingsBurst.cs가 트리거).
// 일정 간격으로 계속 퍼지는 링은 두지 않는다 — 배경이 쉬지 않고 움직이면 카드 선택의 반응이 묻힌다.
// 가산 합성(어두운 배경 위에 얹는 빛)이고 파랑 한 계열만 쓴다.
//
// 점선 고리는 거리·각도를 픽셀 폭(1픽셀 = fwidth(uv.y))으로 안티앨리어싱해 해상도와 무관하게 선명한 가장자리를 만든다.
//
// 사용: 배경 Image(스프라이트 없음)에 이 셰이더의 머티리얼을 지정하고, 카드보다 뒤·기존 배경보다 앞에 둔다.
// 링이 타원으로 일그러지지 않게 화면 비율은 UV 미분(1픽셀당 UV 변화량)에서 자동으로 구한다.
// 거리는 모두 "이미지 높이"를 1로 잰 값이다.
Shader "ProjectS/UI Pulse Rings"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture (unused)", 2D) = "white" {}

        [Header(Color)]
        _Color ("Color", Color) = (0.35, 0.65, 1, 1)

        [Header(Center)]
        // 점선 고리들의 중심(UV). (0.5, 0.5) = 이미지 중앙. 터짐의 기본 시작점도 여기다.
        _Center ("Center (UV)", Vector) = (0.5, 0.5, 0, 0)
        // 터진 링이 사라지는 지점(높이 대비). 중심에서 화면 모서리까지의 거리보다 크게 둔다.
        _MaxRadius ("Burst Max Radius (of height)", Range(0.3, 3)) = 1.1

        [Header(Ring A Inner)]
        _DashAlpha ("Alpha", Range(0, 1)) = 0.85
        _DashRadius ("Radius", Range(0.1, 2)) = 0.5
        // 선 두께(높이 대비). 0.004 = 1080p에서 약 4픽셀.
        _DashWidth ("Width", Range(0.001, 0.05)) = 0.004
        _DashCount ("Dash Count", Range(4, 160)) = 48
        // 한 칸 중 선이 차지하는 비율. 낮을수록 점선 사이가 넓다.
        _DashFill ("Dash Fill (0-1)", Range(0.2, 0.95)) = 0.6
        // 점선 사이를 잇는 옅은 실선 고리의 밝기. 0이면 점선만 보인다.
        _BaseRingAlpha ("Solid Ring Under Dashes", Range(0, 1)) = 0.2
        // 도는 속도(바퀴/초). 양수 = 반시계, 음수 = 시계.
        _DashSpin ("Spin (turns per second)", Float) = 0.05

        [Header(Ring B Outer)]
        _Dash2Alpha ("Alpha", Range(0, 1)) = 0.65
        _Dash2Radius ("Radius", Range(0.1, 2)) = 0.6
        _Dash2Width ("Width", Range(0.001, 0.05)) = 0.003
        _Dash2Count ("Dash Count", Range(4, 160)) = 72
        _Dash2Fill ("Dash Fill (0-1)", Range(0.2, 0.95)) = 0.5
        _Base2RingAlpha ("Solid Ring Under Dashes", Range(0, 1)) = 0.12
        // A와 반대 방향(음수)으로 돌아 두 겹이 서로 엇갈린다.
        _Dash2Spin ("Spin (turns per second)", Float) = -0.07

        [Header(Burst)]
        // 선택 순간 한 번 터지는 링. 시작 시각·중심은 PulseRingsBurst.cs가 넣는다(터지기 전에는 시작 시각이 아주 먼 과거).
        _BurstDuration ("Burst Duration (seconds)", Range(0.2, 3)) = 0.9
        _BurstAlpha ("Burst Alpha", Range(0, 3)) = 1.4
        _BurstWidth ("Burst Ring Width Start", Range(0.005, 0.15)) = 0.035
        // 첫 링 뒤를 따라 한 겹 더 퍼지는 메아리 링의 지연(초).
        _BurstEchoDelay ("Echo Ring Delay (seconds)", Range(0, 0.6)) = 0.14
        [HideInInspector] _BurstStart ("Burst Start Time", Float) = -1000
        [HideInInspector] _BurstCenter ("Burst Center (UV)", Vector) = (0.5, 0.5, 0, 0)

        [Header(Advanced)]
        // 0 = 자동. 링이 타원으로 보일 때만 가로/세로 비율을 직접 넣는다.
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
        Blend SrcAlpha One
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

            fixed4 _Color;

            float4 _Center;
            float _MaxRadius;

            float _DashAlpha;
            float _DashRadius;
            float _DashWidth;
            float _DashCount;
            float _DashFill;
            float _BaseRingAlpha;
            float _DashSpin;

            float _Dash2Alpha;
            float _Dash2Radius;
            float _Dash2Width;
            float _Dash2Count;
            float _Dash2Fill;
            float _Base2RingAlpha;
            float _Dash2Spin;

            float _BurstDuration;
            float _BurstAlpha;
            float _BurstWidth;
            float _BurstEchoDelay;
            float _BurstStart;
            float4 _BurstCenter;

            float _AspectOverride;

            float4 _ClipRect;

            static const float TwoPi = 6.2831853;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color;
                return OUT;
            }

            // 점선 고리 한 겹의 밝기(0~1). 두 겹이 같은 계산을 쓰므로 한 함수로 묶는다.
            // d = 중심까지의 거리, angle = 화면 고정 각도(바퀴 단위), px = 1픽셀 길이(높이 대비).
            float DashedRing(float d, float angle, float px, float radius, float width, float count, float fill,
                float spin, float alpha, float baseAlpha)
            {
                // 반경 방향: "선 중심에서의 거리 - 반두께"를 1픽셀 폭으로 자른 거리장(SDF)이라 가장자리가 칼같이 선다.
                float radial = 1.0 - smoothstep(-px, px, abs(d - radius) - width * 0.5);

                // 각도 방향: 칸 좌표 a(0~1)에서 앞쪽 [0, fill]만 켠다. 가장자리 폭은 호의 1픽셀을 칸 좌표로 환산한 값이다.
                // atan2의 이음선(±π)은 한 바퀴가 칸 수(정수)만큼 튀는 것이라 frac 뒤에는 이어진다.
                float n = round(count);
                float a = frac((angle + _Time.y * spin) * n);
                float aa = clamp(px * n / (TwoPi * max(d, 0.001)), 0.0005, 0.25);
                float dash = smoothstep(0.0, aa, a) * smoothstep(0.0, aa, fill - a);

                // 점선 사이를 옅은 실선이 이어 주면 고리의 윤곽이 읽혀 점선이 더 또렷해 보인다.
                return radial * saturate(dash * alpha + baseAlpha);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;

                // 화면 비율(가로/세로): 1픽셀당 UV 변화량은 가로가 1/W, 세로가 1/H이므로 그 비가 W/H다.
                float aspect = _AspectOverride;
                if (aspect <= 0.0)
                {
                    aspect = fwidth(uv.y) / max(fwidth(uv.x), 0.000001);
                    aspect = clamp(aspect, 0.1, 10.0);
                }

                // 가로를 비율만큼 늘린 좌표에서 거리를 재야 링이 타원이 아니라 원이 된다.
                float2 rel = float2(uv.x * aspect, uv.y) - float2(_Center.x * aspect, _Center.y);
                float d = length(rel);

                // 1픽셀의 길이(높이 대비). 모든 가장자리를 이 폭으로 부드럽게 깎아 계단 없이 또렷하게 만든다.
                float px = max(fwidth(uv.y), 0.00001);
                float angle = atan2(rel.y, rel.x) / TwoPi;

                // 두 겹이 겹치는 곳은 더 밝게 더해지지 않도록 큰 쪽을 쓴다.
                float ringA = DashedRing(d, angle, px, _DashRadius, _DashWidth, _DashCount, _DashFill,
                    _DashSpin, _DashAlpha, _BaseRingAlpha);
                float ringB = DashedRing(d, angle, px, _Dash2Radius, _Dash2Width, _Dash2Count, _Dash2Fill,
                    _Dash2Spin, _Dash2Alpha, _Base2RingAlpha);
                float dashRing = max(ringA, ringB);

                // 선택 순간의 터짐: 선택한 카드 위치에서 첫 링이 빠르게 퍼지고(처음에 빠르고 끝에서 느려지는 이징),
                // 조금 늦게 옅은 메아리 링이 뒤따른다. 시작 시각이 아주 먼 과거이거나 끝났으면 p가 범위 밖이라 아무것도 그리지 않는다.
                float2 brel = float2(uv.x * aspect, uv.y) - float2(_BurstCenter.x * aspect, _BurstCenter.y);
                float bd = length(brel);
                float age = _Time.y - _BurstStart;
                float burst = 0.0;
                for (int b = 0; b < 2; b++)
                {
                    float p = (age - (float)b * _BurstEchoDelay) / max(_BurstDuration, 0.05);
                    if (p > 0.0 && p < 1.0)
                    {
                        float e = 1.0 - pow(1.0 - p, 3.0);
                        float r = e * _MaxRadius * 1.3;
                        float w = lerp(_BurstWidth, _BurstWidth * 0.3, p);
                        float x = (bd - r) / max(w, 0.0005);
                        burst += exp(-x * x) * pow(1.0 - p, 1.5) * (b == 0 ? 1.0 : 0.5);
                    }
                }

                fixed4 color;
                color.rgb = _Color.rgb;
                color.a = saturate(burst * _BurstAlpha + dashRing) * _Color.a * IN.color.a;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
