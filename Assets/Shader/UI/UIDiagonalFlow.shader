// 배경 무늬가 우상단에서 좌하단으로 흘러가는 UI 셰이더. 캐릭터 생성 페이지 배경용.
//
// 무늬를 오브젝트로 깔거나 RectTransform을 움직이지 않고 UV를 시간으로 밀어 흘린다 — 드로우콜 1번이고,
// 페이지가 SetActive로 꺼졌다 켜져도 시간 기준이라 멈춤/재시작 코드가 필요 없다.
// 흐르는 방향은 PageWipe(UIPageWipe.shader)의 진행 방향(우상→좌하)과 맞춘다. 전환 연출과 배경이 같은 방향으로 움직여
// 화면 전체가 하나의 흐름으로 읽히게 하기 위함이다.
//
// 무늬는 두 가지다:
//  · 기본(프로시저럴): 마름모 격자 윤곽선. 텍스처 없이도 동작한다.
//  · 텍스처: _UseTex를 켜고 _PatternTex(반복 가능, Wrap Mode = Repeat)를 넣으면 그 알파를 무늬로 쓴다.
// 레이어 두 장을 서로 다른 크기·속도로 겹쳐 깊이감을 주고, 같은 방향으로 지나가는 빛 띠(sheen)를 얹는다.
// 합성은 가산(어두운 배경 위에 얹는 빛)이고, 색은 UI 원칙대로 파랑 한 계열 + 반투명만 쓴다.
//
// 가로/세로 반복 수(_Tiling)는 칸이 정사각형으로 보이도록 화면 비율에 맞춰 잡는다(16:9면 16, 9).
Shader "ProjectS/UI Diagonal Flow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture (unused)", 2D) = "white" {}

        [Header(Pattern)]
        [Toggle] _UseTex ("Use Pattern Texture", Float) = 0
        _PatternTex ("Pattern Texture (tileable)", 2D) = "white" {}
        [Enum(Alpha, 0, Luminance, 1)] _MaskChannel ("Pattern Read From", Float) = 0

        // 켜면 칸마다 A·B 텍스처를 번갈아 그린다. 다음 줄은 한 칸 밀려 체크무늬가 된다.
        [Toggle] _Checker ("Checker: Alternate A / B", Float) = 0
        _PatternTexB ("Pattern Texture B (checker partner)", 2D) = "white" {}
        _Tiling ("Tiles (X, Y)", Vector) = (16, 9, 0, 0)
        // 한 칸에서 비워 둘 비율(0 = 칸을 가득 채움, 0.5 = 그림이 절반 크기). 값이 클수록 사이가 넓어져 화면이 한산해진다.
        _CellGap ("Cell Gap (X, Y)", Vector) = (0.4, 0.4, 0, 0)
        _LineWidth ("Line Width (procedural)", Range(0.005, 0.2)) = 0.04

        [Header(Color)]
        _Color ("Color", Color) = (0.24, 0.47, 0.86, 1)
        _Alpha ("Pattern Alpha", Range(0, 1)) = 0.22

        [Header(Flow)]
        // UV 기준 진행 방향. 기본값은 우상단 → 좌하단(PageWipe와 같은 방향).
        _Direction ("Direction (X, Y)", Vector) = (-1, -1, 0, 0)
        _Speed ("Flow Speed (tiles per second)", Float) = 0.12

        [Header(Depth Layer)]
        _Layer2Scale ("Back Layer Tile Scale", Range(0.2, 1)) = 0.5
        _Layer2Speed ("Back Layer Speed Ratio", Range(0, 1)) = 0.45
        _Layer2Alpha ("Back Layer Alpha", Range(0, 1)) = 0.5

        [Header(Sheen)]
        _SheenAlpha ("Sheen Strength", Range(0, 1)) = 0.5
        _SheenScale ("Sheen Spacing", Range(0.1, 3)) = 0.6
        _SheenSpeed ("Sheen Speed", Float) = 0.18
        _SheenSharp ("Sheen Sharpness", Range(1, 30)) = 7

        [Header(Edge)]
        _EdgeFade ("Edge Fade (0-0.5)", Range(0, 0.5)) = 0.12

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

            sampler2D _PatternTex;
            sampler2D _PatternTexB;
            float _Checker;
            float4 _CellGap;
            float _UseTex;
            float _MaskChannel;
            float4 _Tiling;
            float _LineWidth;

            fixed4 _Color;
            float _Alpha;

            float4 _Direction;
            float _Speed;

            float _Layer2Scale;
            float _Layer2Speed;
            float _Layer2Alpha;

            float _SheenAlpha;
            float _SheenScale;
            float _SheenSpeed;
            float _SheenSharp;

            float _EdgeFade;

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

            // 반복 좌표 p(칸 단위)에서 무늬 값(0~1)을 구한다.
            float Pattern(float2 p)
            {
                if (_UseTex > 0.5)
                {
                    // frac(p)를 그대로 tex2D에 넣으면 타일 경계에서 UV 미분이 튀어 밉맵이 한 단계 낮은 쪽으로 점프하고,
                    // 그 자리에 격자 모양 선(사각형 틀)이 비친다. 연속인 p의 미분을 직접 넘겨 막는다.
                    // 체크무늬: 칸 번호(가로+세로)의 홀짝으로 A/B를 고른다. 다음 줄은 홀짝이 뒤집혀 자동으로 한 칸 밀린다.
                    // 음수 칸 번호에서도 홀짝이 어긋나지 않게 fmod 대신 frac(합 × 0.5)를 쓴다.
                    float2 cell = floor(p);
                    float odd = step(0.25, frac((cell.x + cell.y) * 0.5)) * _Checker;

                    // 간격: 한 칸 안에서 그림이 차지하는 비율만 남기고 나머지는 비운다. 칸 크기(_Tiling)는 그대로라
                    // 흐름 속도·체크무늬 배열은 변하지 않고, 그림만 작아져 사이가 벌어진다.
                    float2 fill = max(1.0 - _CellGap.xy, 0.05);
                    float2 local = (frac(p) - 0.5) / fill + 0.5;
                    float2 inside = step(0.0, local) * step(local, 1.0);
                    float2 dx = ddx(p) / fill;
                    float2 dy = ddy(p) / fill;

                    float4 texel = odd > 0.5
                        ? tex2Dgrad(_PatternTexB, local, dx, dy)
                        : tex2Dgrad(_PatternTex, local, dx, dy);
                    texel *= inside.x * inside.y;

                    // 알파 채널에 무늬가 있는 텍스처(투명 배경)와, 불투명 흑백 텍스처(밝은 곳이 무늬)를 가른다.
                    // 알파로 읽는데 텍스처가 불투명이면 알파가 전부 1이라 화면이 통째로 사각형으로 채워진다.
                    return _MaskChannel > 0.5 ? dot(texel.rgb, float3(0.299, 0.587, 0.114)) * texel.a : texel.a;
                }

                // 마름모 격자 윤곽선: 칸 중심에서의 맨해튼 거리가 일정한 선이 마름모다.
                float2 d = abs(frac(p) - 0.5) / max(1.0 - _CellGap.xy, 0.05);
                float diamond = d.x + d.y;
                float aa = max(fwidth(diamond), 0.0001);
                return 1.0 - smoothstep(_LineWidth, _LineWidth + aa * 1.5, abs(diamond - 0.36));
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;
                float2 dirN = normalize(_Direction.xy + float2(0.00001, 0.0));

                // 내용이 dirN 방향으로 움직이려면 샘플 위치를 반대로 민다.
                float t = _Time.y;
                float2 p1 = uv * _Tiling.xy - dirN * _Speed * t;
                float2 p2 = uv * _Tiling.xy * _Layer2Scale - dirN * _Speed * _Layer2Speed * t + float2(0.37, 0.61);

                float layers = saturate(Pattern(p1) + Pattern(p2) * _Layer2Alpha);

                // 같은 방향으로 지나가는 빛 띠. x = dirN 축 위치, 시간에 따라 x가 커지는 쪽(=진행 방향)으로 흐른다.
                float ph = frac(dot(uv, dirN) * _SheenScale - t * _SheenSpeed);
                float sheen = exp(-pow((ph - 0.5) * _SheenSharp, 2.0));

                // 가장자리로 갈수록 옅어져 화면 밖에서 흘러 들어오는 것처럼 보인다.
                float fade = 1.0;
                if (_EdgeFade > 0.0)
                {
                    fade = smoothstep(0.0, _EdgeFade, uv.x) * smoothstep(0.0, _EdgeFade, 1.0 - uv.x)
                         * smoothstep(0.0, _EdgeFade, uv.y) * smoothstep(0.0, _EdgeFade, 1.0 - uv.y);
                }

                fixed4 color;
                color.rgb = _Color.rgb;
                color.a = layers * (1.0 + sheen * _SheenAlpha * 3.0) * _Alpha * fade * _Color.a * IN.color.a;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
