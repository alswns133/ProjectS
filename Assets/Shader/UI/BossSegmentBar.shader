// 보스 HP 바(홀로그램 세그먼트형) 전용 UI 셰이더.
//
// 목업의 A안을 Image 한 장으로 접은 것이다. 목업에서는 배경·고스트·필·세그먼트 컷·주사선·
// 리딩 엣지·플래시가 7겹으로 쌓여 있지만, 여기서는 전부 이 한 패스가 그린다.
// 겹쳐 깔지 않는 이유는 드로우콜보다 정렬 쪽이 크다 — 필과 고스트를 각각 Image로 두면
// 두 RectTransform이 따로 반올림되면서 저체력에서 1px씩 어긋난다.
//
// ★ 다단(로아식) 바를 유지한다. _Fill은 "현재 줄 안의 채움"이지 전체 HP 비율이 아니다.
//   남은 줄 수·줄별 색 순환은 BossHpView가 계산해 _FillColor/_BehindColor로 밀어 넣는다.
//   그래서 한 줄을 다 깎으면 _Fill이 1로 되돌아오고 색만 바뀐다.
//
// ★ 저체력 min-pixel clamp는 마스크에만 걸린다(_MinFillPixels). 칸 격자 자체는 uv에서만
//   나오므로 clamp와 무관하고, clamp된 채움이 "원본이 아직 들어가지도 않은 칸"을 켜지 않도록
//   ceil(원본 칸)로 상한을 막는다. 이 상한이 없으면 체력이 0에 가까울 때 2칸이 켜져
//   실제보다 한 칸 더 남은 것처럼 읽힌다.
//
// ★ Mask/RectMask2D 호환: _Stencil* 6종 + Stencil 블록 + UNITY_UI_CLIP_RECT가 모두 있어야
//   마스크 안에 들어갔을 때 잘리거나 사라지지 않는다. (SegmentVolumeBar와 같은 보일러플레이트)
//   (2026-09-07 TH 추가)
Shader "ProjectS/UI Boss Segment Bar"
{
    Properties
    {
        // 세그먼트는 셰이더가 절차적으로 그린다. 스프라이트는 선택적 추가 마스크일 뿐이라
        // Image의 Source Image가 None이면 흰색이 들어와 아무 영향이 없다.
        [PerRendererData] _MainTex ("Optional Mask (A)", 2D) = "white" {}

        [Header(Fill)]
        _Fill ("Fill - 현재 줄 채움 (0~1)", Range(0, 1)) = 0.7
        _GhostFill ("Ghost Fill - 지연 잔상 (0~1)", Range(0, 1)) = 0.85
        _Segments ("Segments - 한 줄 칸 수", Float) = 20
        _GapRatio ("Gap Ratio - 칸 폭 대비 간격", Range(0, 0.9)) = 0.14
        _FillSkew ("Segment Skew - 칸 기울기 (칸 단위)", Range(-1, 1)) = 0

        [Header(Color)]
        // 아래 3색은 BossHpView가 줄 수 팔레트에서 뽑아 매 프레임 갱신한다(인스펙터 값은 프리뷰용).
        _FillColor ("Fill Color - 현재 줄 색", Color) = (1, 0.18, 0.43, 1)
        _BehindColor ("Behind Color - 뒤에 드러날 다음 줄 색", Color) = (0.05, 0.05, 0.07, 0.85)
        _GhostColor ("Ghost Color - 잔상 색 (a=농도)", Color) = (1, 0.85, 0.9, 0.55)
        _CoreColor ("Core Color - 리딩 엣지 화이트", Color) = (1, 0.9, 0.94, 1)
        // 필 안쪽(왼쪽 끝)을 얼마나 어둡게 깔지. 목업의 hostile-deep 에서 hostile 로 가는 그라디언트.
        _DeepScale ("Deep Scale - 필 안쪽 어둡기", Range(0, 1)) = 0.45
        // 필 앞쪽 끝을 코어색으로 얼마나 물들일지. 그라디언트가 항상 리딩 엣지에서 밝게 끝난다.
        _CoreBlend ("Core Blend - 앞쪽 코어 물들기", Range(0, 1)) = 0.85

        [Header(Leading Edge)]
        _CoreWidth ("Core Width (px)", Range(0, 14)) = 3
        _CoreGlow ("Core Glow (px)", Range(0, 60)) = 26

        [Header(Low HP Clamp)]
        // Track의 실제 픽셀 폭. px 단위 계산(코어 폭·최소 채움)이 해상도/앵커에 안 흔들리게
        // BossHpView가 RectTransform에서 읽어 밀어 넣는다. 안 밀면 기본값으로 대충 그려진다.
        _PixelWidth ("Track Pixel Width (C#이 갱신)", Float) = 640
        // 체력이 남아 있는 한 최소 이 픽셀만큼은 그린다. 0이면 clamp 없음.
        _MinFillPixels ("Min Fill Pixels", Range(0, 12)) = 3

        [Header(Motion)]
        _ScanLines ("Scan Lines - 주사선 개수", Float) = 26
        _ScanSpeed ("Scan Speed", Float) = 0.6
        _ScanStrength ("Scan Strength", Range(0, 1)) = 0.42
        _StreamSpeed ("Stream Speed - 대각 스트림", Float) = 0.32
        _StreamStrength ("Stream Strength", Range(0, 1)) = 0.16

        [Header(Impact)]
        // 아래 둘은 BossHpView가 피격/줄 넘어감에 맞춰 1로 올렸다 감쇠시킨다.
        _GlitchGate ("Glitch Gate", Range(0, 1)) = 0
        _HitFlash ("Hit Flash", Range(0, 1)) = 0

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
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "BossSegmentBar"

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
                fixed4 color    : COLOR;      // Image tint × CanvasGroup alpha
                float2 uv       : TEXCOORD0;
                float4 worldPos : TEXCOORD1;  // RectMask2D용
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _ClipRect;

            float  _Fill;
            float  _GhostFill;
            float  _Segments;
            float  _GapRatio;
            float  _FillSkew;

            fixed4 _FillColor;
            fixed4 _BehindColor;
            fixed4 _GhostColor;
            fixed4 _CoreColor;
            float  _DeepScale;
            float  _CoreBlend;

            float  _CoreWidth;
            float  _CoreGlow;

            float  _PixelWidth;
            float  _MinFillPixels;

            float  _ScanLines;
            float  _ScanSpeed;
            float  _ScanStrength;
            float  _StreamSpeed;
            float  _StreamStrength;

            float  _GlitchGate;
            float  _HitFlash;

            v2f vert(appdata v)
            {
                v2f o;
                o.worldPos = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float px = max(_PixelWidth, 1.0);
                float2 uv = i.uv;

                // 글리치 게이트: 가로 밴드마다 uv.x를 통째로 민다(신호 끊김).
                // 밴드 난수를 시간으로 재추첨해 순간마다 다른 줄이 어긋난다.
                float band = floor(uv.y * 7.0);
                float seed = band * 91.17 + floor(_Time.y * 22.0) * 13.73;
                float noise = frac(sin(seed) * 43758.5453);
                uv.x += (noise - 0.5) * 0.09 * _GlitchGate;

                // 칸 격자. 세로 중앙(0.5) 피벗으로 기울인다.
                // 격자는 uv에서만 나온다 = min-pixel clamp의 영향을 받지 않는다.
                float xN = (uv.x - (uv.y - 0.5) * _FillSkew / _Segments) * _Segments;
                float cellRaw = floor(xN);
                float cell    = clamp(cellRaw, 0.0, _Segments - 1.0);
                float local   = frac(xN);
                // 픽셀당 칸 좌표 변화량 -> 경계 AA 폭. 0이 되면 아래 smoothstep이 정의되지 않으므로 하한을 둔다
                // (바가 극단적으로 축소돼 미분값이 0으로 뭉개지는 경우 대비).
                float aa      = max(fwidth(xN), 1e-5);

                // 칸 사이 간격. 바깥쪽 경계(x=0, x=1)는 비우지 않아 양 끝이 수직으로 마감된다.
                float halfGap = _GapRatio * 0.5;
                float gapL = (1.0 - smoothstep(halfGap - aa, halfGap + aa, local))
                             * step(0.5, cellRaw) * step(cellRaw, _Segments - 0.5);
                float gapR = smoothstep(1.0 - halfGap - aa, 1.0 - halfGap + aa, local)
                             * step(-0.5, cellRaw) * step(cellRaw, _Segments - 1.5);
                float shape = 1.0 - saturate(gapL + gapR);

                // min-pixel clamp (마스크 전용).
                // 저체력에서 채움이 1px 밑으로 내려가면 반올림에 먹혀 아예 사라진다.
                // 그래서 그리는 폭만 _MinFillPixels로 끌어올리되, ceil(원본 칸)으로 상한을 건다 —
                // 상한이 없으면 원본이 들어가지도 않은 다음 칸이 켜져 한 칸 더 남은 것처럼 보인다.
                // 체력이 정확히 0이면 clamp도 걸지 않는다(죽었는데 3px이 남는 것을 막는다).
                float rawCells  = _Fill * _Segments;
                float minCells  = (_MinFillPixels / px) * _Segments;
                float drawCells = _Fill > 0.0001 ? min(max(rawCells, minCells), ceil(rawCells)) : 0.0;
                float drawFill  = drawCells / _Segments;

                float ghostCells = max(_GhostFill * _Segments, drawCells);   // 잔상은 항상 채움 이상

                // 채움/잔상 마스크. 마지막 칸은 진행률만큼만 켜진다.
                float onFill  = 1.0 - smoothstep(-aa, aa, local - (drawCells  - cell));
                float onGhost = 1.0 - smoothstep(-aa, aa, local - (ghostCells - cell));

                // 채움 색: 안쪽은 어둡고 리딩 엣지로 갈수록 밝아진다.
                // 그라디언트를 바 전체가 아니라 채움 폭으로 정규화하므로,
                // 체력이 얼마든 밝은 끝이 항상 리딩 엣지에 붙는다(목업 .fill의 CSS 그라디언트와 같은 거동).
                float gt = saturate(uv.x / max(drawFill, 0.0001));
                fixed3 fillCol = lerp(_FillColor.rgb * _DeepScale, _FillColor.rgb, smoothstep(0.0, 0.5, gt));
                fillCol = lerp(fillCol, _CoreColor.rgb, smoothstep(0.86, 1.0, gt) * _CoreBlend);

                // 흐르는 대각 스트림(채움 위에만).
                float stream = frac((uv.x * 2.2 - uv.y * 0.9) * 6.0 - _Time.y * _StreamSpeed * 6.0);
                fillCol += smoothstep(0.72, 0.88, stream) * _StreamStrength;

                // 합성: 뒤(다음 줄) -> 잔상 -> 채움
                fixed4 col;
                col.rgb = _BehindColor.rgb;
                col.a   = _BehindColor.a;

                float ghostBand = saturate(onGhost - onFill) * _GhostColor.a;
                col.rgb = lerp(col.rgb, _GhostColor.rgb, ghostBand);
                col.a   = lerp(col.a, 1.0, ghostBand);

                col.rgb = lerp(col.rgb, fillCol, onFill);
                col.a   = lerp(col.a, _FillColor.a, onFill);

                // 주사선. 주기의 1/3만 어둡다(목업의 1px on / 2px off).
                float scan = frac(uv.y * _ScanLines - _Time.y * _ScanSpeed);
                col.rgb *= 1.0 - (1.0 - step(0.34, scan)) * _ScanStrength;

                // 리딩 엣지: 흰 코어 + 채움색 글로우. 픽셀 거리로 재서 바 길이에 안 흔들린다.
                float edgeDist = abs(uv.x - drawFill) * px;
                float core = 1.0 - smoothstep(0.0, max(_CoreWidth * 0.5, 0.001), edgeDist);
                float glow = 1.0 - smoothstep(0.0, max(_CoreGlow, 0.001), edgeDist);
                float alive = step(0.0001, _Fill);
                col.rgb += (_CoreColor.rgb * core + _FillColor.rgb * glow * 0.5) * alive;
                col.a = max(col.a, saturate(core + glow * 0.35) * alive);

                // 피격 백색 가산.
                col.rgb = lerp(col.rgb, fixed3(1, 1, 1), saturate(_HitFlash) * 0.8);

                // 칸 간격으로 뚫고, 스프라이트가 있으면 추가 마스크로만 쓴다.
                col.a *= shape;
                col.a *= tex2D(_MainTex, uv).a;

                // Image 틴트 & CanvasGroup 알파
                col *= i.color;

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a - 0.001);
                #endif

                return col;
            }
            ENDCG
        }
    }
}
