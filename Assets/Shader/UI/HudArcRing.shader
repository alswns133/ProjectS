// 호(arc) 조각과 워프 선으로 이루어진 SF HUD 링 UI 셰이더.
// 텍스처 없이 전부 절차적으로 그린다 — 스프라이트는 틴트/마스크 용도로만 곱한다.
//
// 세 겹으로 되어 있다.
//  · 링 1 / 링 2 : 원주를 따라 흐르는 호 조각. 같은 띠 안에서 두 겹이 겹친다
//                  (_Offset2를 반지름 단보다 작게 둔다 — 벌리면 별개의 원 두 개가 된다).
//  · 워프       : 링과 같이 돌지 않고, 중심점을 향해 빨려드는 방사형 실선.
//
// [호 조각의 구조]
// 각도를 슬롯으로 쪼개고, 슬롯마다 (켜짐/꺼짐 · 반지름 단 · 두께 단) 세 값을 뽑는다.
// 슬롯 자체에는 간격을 주지 않기 때문에 이웃한 켜짐 슬롯끼리는 그대로 이어져 하나의
// 긴 호가 된다. "각진 계단"은 여기서 공짜로 나온다 — 이어진 두 슬롯의 반지름 단이
// 다르면 호가 가다가 툭 옆 트랙으로 넘어가고, 그 이음매가 직각 계단으로 보인다.
//
// [Spread — 조각이 생기는 간격]
// 밀도(_Density)만으로 성글게 하면 긴 호가 점점이 부서지기만 하고 "간격"은 안 생긴다.
// 그래서 슬롯보다 큰 단위(_Group 칸씩)로 구간 자체를 통째로 죽이는 게이트를 하나 더 둔다.
// _Spread를 올리면 호의 길이는 유지된 채 빈 구간만 넓어진다. 조밀함을 푸는 손잡이는
// _Density가 아니라 이쪽이다.
//
// [워프]
// 각 방사선은 자기 위상으로 바깥에서 중심을 향해 이동하고, 한 번 지나갈 때마다 길이와
// 켜짐 여부를 새로 뽑는다. 각도 폭이 고정이라 중심에 가까울수록 선이 저절로 가늘어져
// 원근이 생긴다 — 이래서 링과 같이 돌리면 안 된다. 돌리는 순간 방사가 아니라
// 소용돌이로 읽힌다.
//
// ★ 안티에일리어싱은 "잘린 끝"에만 건다. 이웃 슬롯이 켜져 있으면 거기는 끝이 아니라
//   이어지는 자리라, 손대면 긴 호 한가운데에 세로 실금이 생긴다.
// ★ 각도 방향 AA에 fwidth(각도)를 쓰면 atan2가 끊기는 이음매에 세로줄이 하나 생긴다.
//   그래서 픽셀 크기를 반지름으로 나눠(= 그 반지름에서의 각 픽셀 폭) 직접 구한다.
// ★ Image의 Rect가 정사각형이 아니면 원이 찌그러진다. 정사각으로 두거나
//   _Aspect에 (width / height)를 넣어 보정한다.
// ★ 블렌딩이 프리멀티플라이드(Blend One OneMinusSrcAlpha)다. 마지막에 rgb *= a를
//   반드시 곱한다 — 빼먹으면 반투명 가장자리만 과하게 탄다.
// (2026-08-25 TH)
Shader "ProjectS/UI Hud Arc Ring"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        [HDR] _Color ("Color", Color) = (1, 1, 1, 1)

        [Header(Ring)]
        // 링 1의 기준 반지름. 1이면 Rect 경계에 붙어 반지름 단이 밖으로 나갈 때 잘린다.
        _Radius ("Radius", Range(0.1, 1)) = 0.74

        [Header(Ring 1)]
        // 각도 분할 수. 조각 하나의 최소 길이를 정한다. 크게 두고 밀도를 올리는 쪽이
        // "긴 호 + 아주 짧은 파편"이 섞인 레퍼런스 인상에 가깝다. 낮추면 조각 크기가
        // 전부 고만고만해진다.
        _Slots1 ("Slot Count", Range(8, 256)) = 128
        // 슬롯이 켜져 있을 확률. 붙어서 켜지면 긴 호, 띄엄띄엄이면 짧은 토막.
        // 0.8을 넘겨야 끊긴 자리가 "구멍"이 아니라 "이음매"로 읽힌다.
        _Density1 ("Fill Density", Range(0, 1)) = 0.82
        // 구간을 통째로 비우는 비율. 조밀함을 푸는 값은 이쪽이다.
        _Spread1 ("Spread (Empty Gaps)", Range(0, 1)) = 0.12
        // 게이트 한 칸이 슬롯 몇 개인가. 클수록 빈 구간도, 긴 호도 함께 커진다.
        _Group1 ("Gap Size", Range(1, 24)) = 10
        // 칸을 통째로 켜서 긴 호를 만들 비율. 0이면 조각 길이가 전부 한두 칸으로 비슷해진다.
        _Long1 ("Long Arc Ratio", Range(0, 1)) = 0.35
        _Thin1 ("Thin Width", Range(0.002, 0.1)) = 0.008
        _Thick1 ("Thick Width", Range(0.002, 0.15)) = 0.026
        // 반지름 단의 간격. 0이면 계단 없이 한 줄로 정렬된다.
        // 두께(_Thick)와 비슷하거나 작게 둔다 — 크면 조각이 링에서 떨어져 나간다.
        _Step1 ("Radius Step", Range(0, 0.1)) = 0.013
        // 두께를 재는 기준 모서리. 0 = 안쪽 정렬(한쪽만 어긋나는 계단), 0.5 = 가운데.
        _Anchor1 ("Thickness Anchor", Range(0, 1)) = 0
        _Rate1 ("Refresh Rate", Range(0, 10)) = 0.4
        _Spin1 ("Spin", Range(-2, 2)) = 0.01

        [Header(Ring 2)]
        // 링 1에서 얼마나 밀어놓을 것인가. 반지름 단(_Step)보다 작게 두는 게 핵심이다 —
        // 그래야 두 겹이 같은 띠 안에서 서로 끼어들어 한 덩어리로 읽힌다. 크게 벌리면
        // 그냥 별개의 원 두 개가 되어 화면만 시끄러워진다.
        _Offset2 ("Radius Offset", Range(-0.2, 0.2)) = 0.006
        _Slots2 ("Slot Count", Range(8, 256)) = 160
        // 링 2는 굵은 호 옆을 스치는 실선 담당이라 성글게 둔다. 여기까지 빽빽하면
        // 두 겹이 뭉개져 그냥 두꺼운 원이 된다.
        _Density2 ("Fill Density", Range(0, 1)) = 0.3
        _Spread2 ("Spread (Empty Gaps)", Range(0, 1)) = 0.3
        _Group2 ("Gap Size", Range(1, 24)) = 8
        // 링 1과 다른 값을 주면 두 겹의 조각 길이 성격이 갈려 더 다양해진다.
        _Long2 ("Long Arc Ratio", Range(0, 1)) = 0.25
        _Thin2 ("Thin Width", Range(0.002, 0.1)) = 0.003
        _Thick2 ("Thick Width", Range(0.002, 0.15)) = 0.011
        _Step2 ("Radius Step", Range(0, 0.1)) = 0.02
        _Anchor2 ("Thickness Anchor", Range(0, 1)) = 0
        _Rate2 ("Refresh Rate", Range(0, 10)) = 0.6
        // 링 1과 같은 방향으로 두되 속도만 다르게 준다. 반대로 돌리면 두 겹이 서로
        // 밀치는 것처럼 보여 링 전체가 불안정해진다. 같은 방향이면 흐름은 한 방향으로
        // 안정되면서도 속도 차 때문에 조합은 계속 바뀐다.
        // _Spin1과 같은 값으로 맞추면 두 겹이 완전히 물려 한 덩어리로 돈다.
        _Spin2 ("Spin", Range(-2, 2)) = 0.035
        _Alpha2 ("Alpha", Range(0, 1)) = 1

        [Header(Warp Lines)]
        // 중심을 향해 빨려드는 방사형 실선. 링과 함께 돌리지 않는다.
        _SlotsW ("Slot Count", Range(8, 256)) = 96
        _DensityW ("Fill Density", Range(0, 1)) = 0.3
        _SpreadW ("Spread (Empty Gaps)", Range(0, 1)) = 0.4
        _GroupW ("Gap Size", Range(1, 24)) = 5
        // 한 칸에서 선이 차지하는 각도 비율. 선마다 이 사이에서 3단으로 뽑힌다.
        _WidthMinW ("Line Width Min", Range(0.02, 1)) = 0.07
        _WidthMaxW ("Line Width Max", Range(0.02, 1)) = 0.28
        _LenMinW ("Length Min", Range(0.01, 0.5)) = 0.05
        _LenMaxW ("Length Max", Range(0.01, 0.8)) = 0.18
        // 선마다 속도를 다르게 흩뿌리는 폭. 0이면 모든 선이 같은 주기로 돌아와
        // 커튼처럼 규칙적으로 밀려든다. 올리면 주기가 서로 어긋나 불규칙해진다.
        _SpeedVarW ("Speed Variance", Range(0, 0.9)) = 0.55
        // 출발 반지름을 통과할 때마다 흔드는 폭. 선들이 같은 선상에서 출발하지 않게.
        _JitterW ("Start Jitter", Range(0, 0.5)) = 0.12
        // 출발 반지름과 도착 반지름. 기본은 링에서 출발해 중심 근처에서 사라진다.
        _StartW ("Travel From", Range(0, 1.5)) = 0.7
        _EndW ("Travel To", Range(0, 1.5)) = 0.08
        // 음수면 흐름이 뒤집혀 중심에서 바깥으로 뻗어나간다.
        // (From/To를 바꿔 적을 필요 없다 — 부호만 뒤집으면 된다.)
        _SpeedW ("Travel Speed", Range(-4, 4)) = 0.5
        // 기본값이 0인 이유는 레퍼런스 이미지에 방사선이 아예 없기 때문이다.
        // 워프가 필요하면 여기부터 올린다.
        _AlphaW ("Alpha", Range(0, 1)) = 0

        [Header(Rect)]
        // Rect가 정사각이 아닐 때 보정값 (width / height).
        _Aspect ("Aspect (w/h)", Float) = 1
        // 아틀라스에 묶인 스프라이트 대응. (xMin, yMin, width, height)
        _UVRect ("Sprite UV Rect", Vector) = (0, 0, 1, 1)

        // --- UI Mask / RectMask2D 호환용 표준 프로퍼티 ---
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
        Blend One OneMinusSrcAlpha   // 프리멀티플라이드 — 가산 하이라이트를 내기 위함
        ColorMask [_ColorMask]

        Pass
        {
            Name "HudArcRing"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;      // Image 틴트 x CanvasGroup 알파
                float2 uv       : TEXCOORD0;
                float4 worldPos : TEXCOORD1;  // RectMask2D용
            };

            sampler2D _MainTex;
            float4 _ClipRect;

            fixed4 _Color;
            float _Radius;
            float _Slots1, _Density1, _Spread1, _Group1, _Long1;
            float _Thin1, _Thick1, _Step1, _Anchor1, _Rate1, _Spin1;
            float _Offset2, _Slots2, _Density2, _Spread2, _Group2, _Long2;
            float _Thin2, _Thick2, _Step2, _Anchor2, _Rate2, _Spin2, _Alpha2;
            float _SlotsW, _DensityW, _SpreadW, _GroupW, _WidthMinW, _WidthMaxW, _LenMinW, _LenMaxW;
            float _SpeedVarW, _JitterW, _StartW, _EndW, _SpeedW, _AlphaW;
            float _Aspect;
            float4 _UVRect;

            #define TAU 6.28318530718

            // 슬롯 번호 i와 갱신 회차 t로 0~1 난수를 뽑는다.
            // i를 fmod(N)으로 감는 이유는 0번과 N번이 같은 슬롯이어야 링이 닫히기 때문
            // (안 감으면 3시 방향에 이음매가 하나 보인다).
            // t도 감아주지 않으면 플레이가 길어질수록 frac() 정밀도가 무너져 패턴이 뭉친다.
            inline float Hash(float i, float t, float N, float salt)
            {
                float2 p = float2(fmod(i + N * 64.0, N) + salt, fmod(t, 1024.0));
                p = frac(p * float2(127.1, 311.7));
                p += dot(p, p + 34.53);
                return frac(p.x * p.y * 43758.5453);
            }

            // 반지름 방향 밴드. inner~outer 사이만 1.
            inline float Band(float r, float inner, float outer, float aa)
            {
                return (1.0 - smoothstep(outer - aa, outer + aa, r)) * smoothstep(inner - aa, inner + aa, r);
            }

            // 한 칸(slot) 안에서 가운데 fill 비율만 남기는 마스크.
            inline float SlotMask(float f, float fill, float aa)
            {
                float halfW = fill * 0.5;
                return 1.0 - smoothstep(halfW - aa, halfW + aa, abs(f - 0.5));
            }

            // idx번 슬롯이 켜져 있는가. 슬롯 자체의 확률 x 구간 게이트(_Spread),
            // 여기에 셀 통째로 켜는 긴 호(longRatio)를 얹는다.
            // 게이트를 슬롯보다 큰 단위로 거는 게 핵심이다 — 밀도만 낮추면 조각이
            // 잘게 부서질 뿐 "간격"이 생기지 않는다.
            float SlotOn(float idx, float slots, float density, float group, float spread,
                         float longRatio, float rate, float time, float salt)
            {
                // 슬롯마다 갱신 시점을 흩뿌린다. 위상이 같으면 링 전체가 한 박자에 갈려서
                // 정보가 흐르는 게 아니라 메트로놈처럼 보인다.
                float clk = floor(time * rate + Hash(idx, 0, slots, salt + 5.5));
                float on = step(Hash(idx, clk, slots, salt), density);

                // 게이트는 슬롯 개수와 무관한 자기 분할을 쓴다. slots가 group으로 나누어
                // 떨어지지 않아도 0도 지점에서 이음매가 생기지 않게 하기 위함.
                float gn = max(floor(slots / max(group, 1.0)), 1.0);
                float gidx = floor((idx + 0.5) / slots * gn);
                float gclk = floor(time * rate * 0.4 + Hash(gidx, 0, gn, salt + 61.2));
                float gate = step(Hash(gidx, gclk, gn, salt + 77.7), 1.0 - spread);

                // 셀 하나를 통째로 켜서 긴 호를 만든다. 슬롯 단위 확률만으로는 켜진 구간이
                // 늘 한두 칸이라 조각 길이가 전부 비슷해진다 — 짧은 토막과 긴 호가
                // 섞이려면 길이의 출처가 둘이어야 한다. 셀이 연달아 켜지면 더 길어진다.
                float longOn = step(Hash(gidx, gclk, gn, salt + 91.1), longRatio);

                return max(on, longOn) * gate;
            }

            // 호 조각 한 레이어.
            // aaSlot은 "화면 1픽셀이 슬롯 단위로 몇 칸인가" — 잘린 끝을 무르게 하는 데 쓴다.
            float ArcLayer(float r, float ang, float aaR, float aaSlot,
                           float slots, float density, float group, float spread, float longRatio,
                           float radius, float stepSize, float thMin, float thMax, float anchor,
                           float rate, float time, float salt)
            {
                float s = ang * slots;
                float idx = floor(s);
                float f = s - idx;

                float on  = SlotOn(idx,       slots, density, group, spread, longRatio, rate, time, salt);
                float onL = SlotOn(idx - 1.0, slots, density, group, spread, longRatio, rate, time, salt);
                float onR = SlotOn(idx + 1.0, slots, density, group, spread, longRatio, rate, time, salt);

                // 반지름 단 3단(-1, 0, +1)과 두께 단 3단. 이어진 슬롯끼리 값이 다르면
                // 그 경계가 계단이 된다 — 레퍼런스의 각진 부분이 이것.
                float clk = floor(time * rate + Hash(idx, 0, slots, salt + 5.5));
                float ro = (floor(Hash(idx, clk, slots, salt + 11.3) * 3.0) - 1.0) * stepSize;
                float th = lerp(thMin, thMax, floor(Hash(idx, clk, slots, salt + 23.7) * 3.0) * 0.5);
                float rc = radius + ro;

                // 두께를 어느 모서리에 붙여 재는가. 0이면 안쪽 모서리가 기준이라 두께가
                // 바뀌어도 안쪽 선은 그대로 이어지고 바깥쪽만 툭 튀어나온다 — 레퍼런스의
                // 계단이 대부분 이 "한쪽만 어긋난" 모양이다. 0.5(가운데 기준)로 두면
                // 두께가 바뀔 때 양쪽이 동시에 벌어져 계단이 아니라 마디로 보인다.
                float inner = rc - th * anchor;
                float band = Band(r, inner, inner + th, aaR);

                // 이웃이 꺼져 있는 쪽만 끝이다. 켜져 있는 쪽은 이어져야 하므로 건드리지 않는다.
                float dEdge = min(lerp(f, 10.0, onL), lerp(1.0 - f, 10.0, onR));
                float endAA = smoothstep(0.0, max(aaSlot, 1e-5), dEdge);

                return band * on * endAA;
            }

            // 중심을 향해 빨려드는 방사형 실선.
            // 링과 달리 각도 위치가 고정이고, 대신 반지름이 시간에 따라 안으로 흐른다.
            float WarpLayer(float r, float ang, float aaR, float aaAng, float time)
            {
                float slots = floor(_SlotsW);
                float s = ang * slots;
                float idx = floor(s);
                float f = s - idx;

                // 선마다 위상도 속도도 다르다. 위상만 흩뿌리면 주기가 전부 같아서
                // 결국 같은 간격으로 밀려드는 커튼이 된다 — 속도를 어긋내야 불규칙해진다.
                float speed = _SpeedW * lerp(1.0 - _SpeedVarW, 1.0 + _SpeedVarW,
                                             Hash(idx, 0, slots, 13.9));
                float cyc = time * speed + Hash(idx, 0, slots, 91.4);
                float passIdx = floor(cyc);   // pass는 HLSL 예약어라 쓸 수 없다
                float travel = cyc - passIdx;

                // 시간 대신 통과 회차(pass)를 넣는다 — 이 선이 다시 출발할 때만 갈리게.
                // 긴 호(longRatio)는 0이다. 켜지면 이웃한 선들이 셀 단위로 한꺼번에 떠서
                // 부챗살처럼 뭉치는데, 방사선에는 그게 규칙성으로만 읽힌다.
                float on = SlotOn(idx, slots, _DensityW, _GroupW, _SpreadW, 0.0, 1.0, passIdx, 43.9);
                float len = lerp(_LenMinW, _LenMaxW, Hash(idx, passIdx, slots, 57.3));

                // 두께도 통과할 때마다 3단으로 새로 뽑는다. 호 조각의 두께 단과 같은 규칙이라
                // 굵은 선과 실선이 섞여도 같은 계통으로 읽힌다.
                float width = lerp(_WidthMinW, _WidthMaxW,
                                   floor(Hash(idx, passIdx, slots, 71.5) * 3.0) * 0.5);

                // 출발 반지름도 매번 흔든다. 안 흔들면 선 끝이 한 원 위에 정렬돼
                // 보이지 않는 테두리가 하나 생긴 것처럼 보인다.
                float start = _StartW + (Hash(idx, passIdx, slots, 29.4) - 0.5) * _JitterW;

                // 안쪽 끝(head)이 바깥에서 중심 쪽으로 이동한다.
                // _SpeedW가 음수면 cyc가 거꾸로 흘러 travel이 1에서 0으로 내려가고,
                // 그대로 방향만 뒤집힌다 — 분기 없이 부호 하나로 안팎이 갈린다.
                float head = lerp(start, _EndW, travel);

                // 바깥 끝은 링의 경계(_Radius)에서 자른다. 안 자르면 출발 흔들림과 길이가
                // 겹쳐 선이 링 밖으로 삐져나가고, 원의 윤곽이 무너져 보인다.
                // 잘린 동안은 선이 링 밑에서 자라 나오는 것처럼 보인다.
                float outer = max(min(head + len, _Radius), head);
                float band = Band(r, head, outer, aaR) * step(0.001, outer - head);

                // 출발할 때 켜지고 도착할 때 꺼진다. 안 그러면 중심에서 선이 툭 끊긴다.
                float env = sin(saturate(travel) * 3.14159265);

                return band * SlotMask(f, width, aaAng * slots) * on * env;
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.worldPos = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 아틀라스 오프셋을 걷어내 스프라이트 로컬 0~1로 되돌린다.
                float2 uv = (i.uv - _UVRect.xy) / max(_UVRect.zw, 1e-4);
                float2 p = (uv - 0.5) * 2.0;
                p.x *= _Aspect;

                float r = length(p);
                float ang = atan2(p.y, p.x) / TAU + 0.5;      // 0~1

                float px = fwidth(r);                          // 화면 1픽셀의 정규화 길이
                float aaR = px + 1e-5;
                // 이 반지름에서 픽셀 하나가 덮는 각도(0~1 단위). 이음매 없는 각도 AA용.
                float aaAng = px / max(r, 1e-3) / TAU;

                float time = _Time.y;

                float r1 = ArcLayer(r, frac(ang + _Spin1 * time), aaR, aaAng * floor(_Slots1),
                                    floor(_Slots1), _Density1, _Group1, _Spread1, _Long1,
                                    _Radius, _Step1, _Thin1, _Thick1, _Anchor1, _Rate1, time, 0.0);

                float r2 = ArcLayer(r, frac(ang + _Spin2 * time), aaR, aaAng * floor(_Slots2),
                                    floor(_Slots2), _Density2, _Group2, _Spread2, _Long2,
                                    _Radius + _Offset2, _Step2, _Thin2, _Thick2, _Anchor2, _Rate2, time, 37.7)
                           * _Alpha2;

                float w = WarpLayer(r, ang, aaR, aaAng, time) * _AlphaW;

                float shape = saturate(r1 + r2 + w);

                fixed4 col = i.color * tex2D(_MainTex, i.uv);
                float alpha = col.a * shape;

                #ifdef UNITY_UI_CLIP_RECT
                alpha *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha - 0.001);
                #endif

                return fixed4(col.rgb * alpha, alpha);   // 프리멀티플라이드
            }
            ENDCG
        }
    }
}
