// 커서가 지나간 자리에 회로(전선) 무늬를 드러냈다가 천천히 지우는 전체 화면 UI 셰이더. (2026-10-07 TH 추가)
//
// 구조: 화면 전체를 덮는 RawImage 한 장. 텍스처(_MainTex)는 CircuitCursorFx.cs가 매 프레임 갱신하는
// "흔적 버퍼"(R = 흔적 세기 0~1, 찍힌 순간 1 → 사라지는 시간 동안 0으로 선형 감소)이고,
// 회로 무늬는 텍스처가 아니라 이 셰이더가 화면 좌표로 계산한다. 즉 무늬는 화면에 고정돼 있고 흔적은 그걸 비추는 마스크다
// — 커서가 "숨어 있던 회로를 긁어 드러내는" 느낌을 내기 위함이고, 같은 자리를 다시 지나가면 같은 회로가 다시 보인다.
//
// 회로 생성(격자 노드 사이를 잇는 선):
//  - 가로/세로 배선은 몇 칸씩 끊기지 않고 이어지게(run) 해서 기판 배선처럼 길게 뻗는다. 칸마다 따로 뽑으면 미로처럼 보인다.
//  - 대각선(45°)은 칸마다 "/" 또는 "\" 중 하나만 — 둘 다 허용하면 X자로 교차해 배선답지 않다.
//  - 노드 일부에 비아(고리 패드)를 둔다.
//  픽셀이 속한 칸 주변 3×3 칸의 선분만 검사한다(선 굵기 + 글로우가 한 칸보다 작다는 전제).
//
// 사라짐: 흔적 세기를 선분마다 다른 문턱(_Dissolve)으로 잘라 배선이 "한 가닥씩" 꺼지며 사라진다.
// 막 그려진 부분(흔적 세기 1 근처)은 _HeadColor로 하얗게 달아올랐다가 식으며 _Color로 돌아간다.
// 전류 펄스는 같은 직선 위에서 끊기지 않게 선분 길이가 아니라 화면 좌표 투영값으로 흐른다.
//
// 흔적이 0인 픽셀은 바로 빠져나가므로, 커서가 멈춘 동안 화면 대부분의 비용은 텍스처 1회 샘플이다.
// 합성은 가산(Blend One One) — 어두운 배경에서 겹칠수록 밝아지는 빛으로 읽히게.
Shader "ProjectS/UI Cursor Circuit Trail"
{
    Properties
    {
        [PerRendererData] _MainTex ("Trail Buffer (script)", 2D) = "black" {}

        [Header(Circuit)]
        _CellSize ("Cell Size (px)", Float) = 18
        _LineWidth ("Trace Width (px)", Float) = 1.6
        _StraightDensity ("Straight Trace Density", Range(0, 1)) = 0.45
        _DiagDensity ("Diagonal Trace Density", Range(0, 1)) = 0.18
        _PadDensity ("Via Pad Density", Range(0, 1)) = 0.14
        _PadRadius ("Via Pad Radius (px)", Float) = 3

        [Header(Color)]
        _Color ("Trace Color", Color) = (1, 0.12, 0.16, 1)
        _HeadColor ("Fresh Trace Color", Color) = (1, 0.8, 0.74, 1)
        _HeadRange ("Fresh Threshold (trail value)", Range(0, 1)) = 0.82
        _GlowWidth ("Trace Glow Width (px)", Float) = 4
        _GlowStrength ("Trace Glow Strength", Range(0, 2)) = 0.55
        _Intensity ("Overall Intensity", Range(0, 4)) = 1.4

        [Header(Current Flow)]
        _FlowSpeed ("Flow Speed (px per s)", Float) = 140
        _FlowSpacing ("Flow Pulse Spacing (px)", Float) = 70
        _FlowLength ("Flow Pulse Length (0-1)", Range(0.02, 1)) = 0.22
        _FlowBoost ("Flow Pulse Brightness", Range(0, 4)) = 1.6

        [Header(Fade)]
        _Dissolve ("Per-Trace Dissolve Spread (0-0.9)", Range(0, 0.9)) = 0.4
        _FadeCurve ("Fade Curve (gamma)", Range(0.3, 3)) = 1.3

        // 아래는 스크립트가 매 프레임 넣는 값(인스펙터에서 만지지 않는다).
        [HideInInspector] _RectSize ("Rect Size", Vector) = (1920, 1080, 0, 0)
        [HideInInspector] _Now ("Now", Float) = 0

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
        // 가산 합성. rgb에 세기를 미리 곱해 내보낸다(알파 1 초과가 8비트 타깃에서 잘리는 것을 피하기 위함).
        Blend One One
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

            float _CellSize;
            float _LineWidth;
            float _StraightDensity;
            float _DiagDensity;
            float _PadDensity;
            float _PadRadius;

            fixed4 _Color;
            fixed4 _HeadColor;
            float _HeadRange;
            float _GlowWidth;
            float _GlowStrength;
            float _Intensity;

            float _FlowSpeed;
            float _FlowSpacing;
            float _FlowLength;
            float _FlowBoost;

            float _Dissolve;
            float _FadeCurve;

            float4 _RectSize;
            float _Now;

            float4 _ClipRect;

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

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            // 노드 n에서 오른쪽 노드로 가는 가로 배선이 있는가. 3칸 단위로 같은 값을 써서 배선이 길게 이어진다.
            // 줄마다 시작 위치를 어긋나게(행 해시만큼 밀기) 해서 모든 줄의 끊김이 한 세로줄에 몰리지 않게 한다.
            float HorzExists(float2 n)
            {
                float run = floor((n.x + Hash(float2(n.y, 9.1)) * 5.0) / 3.0);
                return step(Hash(float2(run, n.y) + 0.13), _StraightDensity);
            }

            // 노드 n에서 위쪽 노드로 가는 세로 배선. 가로보다 조금 성기게 둬 가로 배선이 주가 되게 한다(기판 느낌).
            float VertExists(float2 n)
            {
                float run = floor((n.y + Hash(float2(n.x, 4.2)) * 5.0) / 3.0);
                return step(Hash(float2(n.x, run) + 0.71), _StraightDensity * 0.7);
            }

            // 선분 a→b(px)까지의 거리가 지금까지의 최소보다 가까우면 기록한다.
            // along: 전류 펄스 좌표. 선분 길이가 아니라 화면 좌표를 진행 방향에 투영한 값이라 같은 직선의 이웃 선분과 이어진다.
            void Trace(float2 p, float2 a, float2 b, float exists, float2 key,
                       inout float best, inout float along, inout float2 bestKey)
            {
                if (exists < 0.5) return;

                float2 ab = b - a;
                float t = saturate(dot(p - a, ab) / max(dot(ab, ab), 1e-4));
                float d = length(p - a - ab * t);
                if (d >= best) return;

                float2 dir = normalize(ab);
                // 같은 직선(진행 방향에 수직인 좌표가 같은 선) 단위로 흐르는 방향을 뒤집어 전부 한쪽으로만 흐르지 않게 한다.
                float lane = floor(dot(a, float2(-dir.y, dir.x)) + 0.5);
                float flowSign = Hash(float2(lane, dir.x * 3.0 + dir.y)) > 0.5 ? 1.0 : -1.0;

                best = d;
                along = dot(p, dir) * flowSign;
                bestKey = key;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float trail = tex2D(_MainTex, IN.texcoord).r;
                if (trail <= 0.002) return fixed4(0, 0, 0, 0);

                float cs = max(_CellSize, 2.0);
                float2 p = IN.texcoord * _RectSize.xy;
                float2 s = floor(p / cs);

                float best = 1e5;
                float along = 0.0;
                float2 key = float2(0, 0);
                float onPad = 0.0;

                [unroll]
                for (int j = -1; j <= 1; j++)
                {
                    [unroll]
                    for (int i = -1; i <= 1; i++)
                    {
                        float2 n = s + float2(i, j);
                        float2 a = n * cs;

                        Trace(p, a, a + float2(cs, 0), HorzExists(n), n + float2(0.11, 0.0), best, along, key);
                        Trace(p, a, a + float2(0, cs), VertExists(n), n + float2(0.0, 0.23), best, along, key);

                        // 칸(n이 왼쪽 아래 모서리)마다 대각선은 하나만.
                        float h = Hash(n + 0.37);
                        float diagHalf = _DiagDensity * 0.5;
                        Trace(p, a, a + float2(cs, cs), step(h, diagHalf), n + float2(0.31, 0.31), best, along, key);
                        Trace(p, a + float2(0, cs), a + float2(cs, 0), step(1.0 - diagHalf, h), n + float2(0.47, 0.53), best, along, key);

                        // 비아: 노드 둘레의 고리. 전류 펄스는 흐르지 않는다.
                        if (Hash(n + 0.91) < _PadDensity)
                        {
                            float ring = abs(length(p - a) - _PadRadius);
                            if (ring < best)
                            {
                                best = ring;
                                along = 0.0;
                                key = n + float2(0.77, 0.19);
                                onPad = 1.0;
                            }
                        }
                    }
                }

                // 선분마다 다른 문턱으로 잘라, 흔적이 식을 때 배선이 한꺼번에가 아니라 한 가닥씩 꺼지게 한다.
                float r = Hash(key);
                float fade = saturate((trail - r * _Dissolve) / max(1.0 - _Dissolve, 0.001));
                fade = pow(fade, _FadeCurve);
                if (fade <= 0.0) return fixed4(0, 0, 0, 0);

                float halfWidth = _LineWidth * 0.5;
                float core = 1.0 - smoothstep(halfWidth - 0.75, halfWidth + 0.75, best);
                float glow = exp(-max(best - halfWidth, 0.0) / max(_GlowWidth, 0.01)) * _GlowStrength;

                // 전류 펄스: 앞머리가 밝고 꼬리로 갈수록 옅어지는 짧은 띠가 배선을 따라 흐른다.
                float phase = frac((along - _Now * _FlowSpeed) / max(_FlowSpacing, 1.0) + r);
                float pulse = pow(saturate((phase - (1.0 - _FlowLength)) / max(_FlowLength, 0.001)), 2.0) * (1.0 - onPad);

                float brightness = (core + glow) * (1.0 + pulse * _FlowBoost);

                // 막 그려진 부분은 하얗게 달아올랐다가 식으며 기본 색으로.
                float3 color = lerp(_Color.rgb, _HeadColor.rgb, smoothstep(_HeadRange, 1.0, trail));

                float amount = brightness * fade * _Intensity * _Color.a * IN.color.a;

                #ifdef UNITY_UI_CLIP_RECT
                amount *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                return fixed4(color * amount, 0.0);
            }
            ENDCG
        }
    }
}
