// 페이지 전환용 헥사곤 그리드 와이프(UI 전용). 화면을 육각 셀 단위로 우상단 → 좌하단 순서로 덮고, 같은 방향으로 걷어 낸다.
// (cyberpunk_wipe_edge_patterns.html 의 C타입 "헥사곤 그리드"를 옮긴 것)
//
// 경계가 매끈한 선이 아니라 "셀이 한 칸씩 켜지는" 계단이다. 셀마다 진행 방향 위치(v)가 있고,
// v = 진행 방향 위치 × 0.85 + 셀 해시 × 0.15 — 해시가 섞여 경계가 가지런하지 않고 셀이 들쭉날쭉 번진다.
//  · v < 앞쪽 경계 - 띠 폭  → 덮인 셀(fill)
//  · 앞쪽 경계 - 띠 폭 ≤ v < 앞쪽 경계 → 경계 셀(네온 발광)
//  · 그 앞은 아직 아무것도 없음(투명)
// 걷을 때는 뒤쪽 경계가 같은 규칙으로 같은 방향으로 지나가며 셀을 투명하게 되돌린다.
//
// _Progress 하나로 구동한다: 0→1 = 덮는 중, 1→2 = 걷는 중. PageWipeView.cs가 매 프레임 _Progress를,
// 재생 시작 때 _Aspect(가로/세로)를 넣는다 — 셀이 화면 비율과 무관하게 정육각형으로 보이게 하기 위함이다.
// 우상단 = UV (1,1), 좌하단 = UV (0,0). 색은 UI 원칙대로 파랑 한 계열만 쓴다.
Shader "ProjectS/UI Page Wipe"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture (unused)", 2D) = "white" {}

        [Header(Color)]
        _Color ("Fill Color", Color) = (0.02, 0.05, 0.16, 1)
        _EdgeColor ("Edge Neon Color", Color) = (0.35, 0.65, 1, 1)

        [Header(Cell)]
        // 정육각형 중심~꼭짓점 길이(화면 높이 대비). 작을수록 셀이 잘고 많다.
        _CellSize ("Cell Size (of screen height)", Range(0.02, 0.3)) = 0.078
        _CellLine ("Cell Seam Darkness", Range(0, 1)) = 0.35
        _CellLineWidth ("Cell Seam Width", Range(0.01, 0.3)) = 0.07

        [Header(Edge)]
        // 경계 셀이 차지하는 폭(진행 방향 위치 기준). 클수록 네온 띠가 두껍다.
        _EdgeWidth ("Edge Band Width", Range(0.01, 0.3)) = 0.06
        _EdgeFlicker ("Edge Cell Flicker", Range(0, 1)) = 0.25

        [Header(Direction)]
        // 1 = 화면 대각선. 클수록 경계가 가로로 눕고, 작을수록 세로로 선다.
        _Skew ("Slant (Y weight)", Range(0.3, 3)) = 1

        [Header(Scanline)]
        _ScanCount ("Scanline Count", Float) = 45
        _ScanStrength ("Scanline Strength", Range(0, 0.5)) = 0.08

        // 아래는 스크립트가 넣는 값(인스펙터에서 만지지 않는다).
        [HideInInspector] _Progress ("Progress (0-2)", Range(0, 2)) = 0
        [HideInInspector] _Aspect ("Aspect (W/H)", Float) = 1.7778

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

            fixed4 _Color;
            fixed4 _EdgeColor;
            float _CellSize;
            float _CellLine;
            float _CellLineWidth;
            float _EdgeWidth;
            float _EdgeFlicker;
            float _Skew;
            float _ScanCount;
            float _ScanStrength;
            float _Progress;
            float _Aspect;

            float4 _ClipRect;

            static const float Sqrt3 = 1.7320508;

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

            // 큐브 좌표 반올림으로 가장 가까운 육각 칸(축 좌표 q, r)을 구한다.
            float2 HexRound(float2 qr)
            {
                float3 c = float3(qr.x, qr.y, -qr.x - qr.y);
                float3 rc = round(c);
                float3 d = abs(rc - c);

                if (d.x > d.y && d.x > d.z) rc.x = -rc.y - rc.z;
                else if (d.y > d.z) rc.y = -rc.x - rc.z;

                return rc.xy;
            }

            // flat-top 육각형의 부호 거리(안쪽 음수). inradius = 중심에서 평평한 변까지 거리.
            float HexSdf(float2 p, float inradius)
            {
                const float3 k = float3(-0.8660254, 0.5, 0.5773503);
                p = abs(p);
                p -= 2.0 * min(dot(k.xy, p), 0.0) * k.xy;
                p -= float2(clamp(p.x, -k.z * inradius, k.z * inradius), inradius);
                return length(p) * sign(p.y);
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            // 진행도(0~1)를 경계 위치로 바꾼다. 셀 값 v의 실제 범위(약 -0.1 ~ 1.1)를 모두 지나가도록 양 끝을 넉넉히 잡아,
            // 진행도 0에서는 어떤 셀도 덮이지 않고 1에서는 경계 셀까지 포함해 전부 덮이게 한다.
            float FrontPos(float k)
            {
                return lerp(-0.15, 1.2 + _EdgeWidth, saturate(k));
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;

                // 셀이 정육각형이 되도록 가로를 화면 비율만큼 늘린 좌표에서 격자를 계산한다.
                float s = max(_CellSize, 0.005);
                float2 p = float2(uv.x * _Aspect, uv.y);

                float2 qr = HexRound(float2((2.0 / 3.0) * p.x / s, (-1.0 / 3.0 * p.x + Sqrt3 / 3.0 * p.y) / s));
                float2 center = float2(s * 1.5 * qr.x, s * Sqrt3 * (qr.y + qr.x * 0.5));

                // 셀 중심의 진행 방향 위치: 우상단 0 → 좌하단 1. 같은 셀은 한 덩어리로 움직이므로 픽셀이 아니라 중심으로 잰다.
                float2 cuv = float2(center.x / _Aspect, center.y);
                float t = ((1.0 - cuv.x) + (1.0 - cuv.y) * _Skew) / (1.0 + _Skew);
                float cellRand = Hash(qr);
                float v = t * 0.85 + cellRand * 0.15;

                float lead = FrontPos(_Progress);          // 덮는 앞쪽 경계
                float trail = FrontPos(_Progress - 1.0);   // 걷는 뒤쪽 경계
                float e = _EdgeWidth;

                // 앞쪽 경계: 그보다 뒤(v 작음)는 덮임, 경계 띠 안은 네온, 앞은 비어 있음.
                float leadCovered = 1.0 - step(lead - e, v);
                float leadEdge = step(lead - e, v) * (1.0 - step(lead, v));

                // 뒤쪽 경계: 그보다 뒤(v 작음)는 이미 걷힘, 경계 띠 안은 네온, 앞은 아직 덮여 있음.
                float trailGone = 1.0 - step(trail - e, v);
                float trailEdge = step(trail - e, v) * (1.0 - step(trail, v));

                float fill = leadCovered * (1.0 - trailGone);
                float edge = max(leadEdge, trailEdge);

                // 셀 사이 이음새: 셀 가장자리로 갈수록 어둡게 — 육각 셀 단위로 켜진다는 것이 면에서도 읽히게 한다.
                float inside = -HexSdf(p - center, s * 0.8660254);
                float seamW = _CellLineWidth * s;
                float seam = 1.0 - smoothstep(seamW, seamW + max(fwidth(inside), 0.0001) * 1.5, inside);

                // 경계 셀은 칸마다 제각각 밝기가 흔들려 지직거리는 네온처럼 보인다.
                float flick = 1.0 - _EdgeFlicker * cellRand;

                // 덮인 면의 가로 스캔라인. 경계 셀에는 얹지 않는다(네온이 지저분해진다).
                float scan = step(0.5, frac(uv.y * _ScanCount)) * _ScanStrength;

                fixed3 fillRgb = (_Color.rgb + _EdgeColor.rgb * scan) * (1.0 - seam * _CellLine);
                fixed3 edgeRgb = _EdgeColor.rgb * flick * (1.0 - seam * _CellLine * 0.5);

                fixed4 color;
                color.rgb = edge > 0.5 ? edgeRgb : fillRgb;
                color.a = (edge > 0.5 ? _EdgeColor.a : fill * _Color.a) * IN.color.a;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
