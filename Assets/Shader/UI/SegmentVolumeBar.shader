Shader "UI/SegmentVolumeBar"
{
    Properties
    {
        // 세그먼트 모양은 셰이더가 절차적으로 그리므로 텍스처는 선택적 마스크로만 사용.
        // Image의 Source Image가 None이면 흰색이 들어와 아무 영향 없음. (2026-07-20 TH 수정)
        [PerRendererData] _MainTex ("Optional Mask (A)", 2D) = "white" {}

        _Value    ("Fill Value (0~1)", Range(0, 1)) = 0.7
        _SegCount ("Segment Count", Float) = 20

        _OnColor  ("On Color",  Color) = (0.12, 0.73, 1.0, 1.0)
        _OffColor ("Off Color", Color) = (0.23, 0.35, 0.47, 0.35)
        _HotColor ("Hot Color", Color) = (1.0, 0.54, 0.24, 1.0)

        // 뒤에서 몇 칸을 경고색으로 칠할지. 비율(_HotStart) 방식은 칸 경계와
        // 어긋나는 문제가 있어 칸 단위로 변경. (2026-07-20 TH 수정)
        _HotCells ("Hot Cells (뒤에서 몇 칸)", Float) = 4

        // 칸 폭 대비 간격 비율. 각 칸 경계를 중심으로 이만큼 비운다.
        // 양 끝 경계에는 간격을 만들지 않아 바가 잔여 공간 없이 딱 맞게 마감됨. (2026-07-20 TH 추가)
        _GapRatio ("Gap Ratio (칸 폭 대비 간격)", Range(0, 0.9)) = 0.15

        // 세그먼트 기울기. 단위는 "칸 너비" (1 = 위/아래가 한 칸만큼 어긋남). 양수 = 위가 오른쪽.
        _FillSkew ("Segment Skew (cell units)", Range(-1, 1)) = 0.33

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
            Name "SegmentVolumeBar"

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
                fixed4 color    : COLOR;      // Image tint × CanvasGroup alpha (뮤트 알파가 여기로 들어옴)
                float2 uv       : TEXCOORD0;
                float4 worldPos : TEXCOORD1;  // RectMask2D용
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _ClipRect;

            float  _Value;
            float  _SegCount;
            fixed4 _OnColor;
            fixed4 _OffColor;
            fixed4 _HotColor;
            float  _HotCells;
            float  _GapRatio;
            float  _FillSkew;

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
                // 절차적 세그먼트: 텍스처 대신 셰이더가 직접 줄무늬를 그린다.
                // 모양·필 경계·Hot 경계가 전부 같은 격자에서 나오므로
                // 칸 수/기울기를 어떻게 바꿔도 서로 어긋나거나 잔여 공간이 남지 않는다.
                // (2026-07-20 TH 수정: 텍스처 격자 불일치로 남던 공간 문제 해결)

                // 기울어진 칸 좌표. 세로 중앙(0.5) 피벗, 양수 = 위쪽이 오른쪽으로 기움.
                float xN = (i.uv.x - (i.uv.y - 0.5) * _FillSkew / _SegCount) * _SegCount;

                // 스큐 때문에 모서리 픽셀은 xN이 [0, N] 밖으로 살짝 나갈 수 있음.
                // cellRaw는 간격 판정용(모서리 구분 필요), cell은 색/필 판정용(범위로 클램프).
                float cellRaw = floor(xN);
                float cell    = clamp(cellRaw, 0.0, _SegCount - 1.0);
                float local   = frac(xN);                      // 칸 내부 좌표 (0~1)

                // 간격: 각 칸 경계를 중심으로 _GapRatio(칸 폭 비율)만큼 비운다.
                // 바깥쪽 경계(x=0, x=1)는 제외해 양 끝이 잔여 공간 없이 수직으로 마감됨.
                float halfGap = _GapRatio * 0.5;
                float aa = fwidth(xN); // 픽셀당 칸 좌표 변화량 → 경계 안티앨리어싱 폭
                float gapL = (1.0 - smoothstep(halfGap - aa, halfGap + aa, local))
                             * step(0.5, cellRaw) * step(cellRaw, _SegCount - 0.5);
                float gapR = smoothstep(1.0 - halfGap - aa, 1.0 - halfGap + aa, local)
                             * step(-0.5, cellRaw) * step(cellRaw, _SegCount - 1.5);
                float shape = 1.0 - saturate(gapL + gapR);

                // 마지막 칸 부분 채움:
                //  fillCells - cell >= 1  → 항상 켜짐 (완전히 찬 칸)
                //  0 < fillCells - cell < 1 → 진행률만큼만 켜짐
                //  fillCells - cell <= 0  → 꺼짐
                float fillCells = _Value * _SegCount;          // 예: 0.73 * 20 = 14.6
                float on = step(local, fillCells - cell);

                // 뒤에서 _HotCells칸은 경고색. 칸 인덱스끼리 비교하므로
                // 세그먼트 개수와 무관하게 항상 칸 경계에 정확히 맞음 (2026-07-20 TH 수정)
                float hot = step(_SegCount - _HotCells, cell);
                fixed4 onCol = lerp(_OnColor, _HotColor, hot);

                fixed4 col = lerp(_OffColor, onCol, on);
                col.a *= shape;

                // 스프라이트가 있으면 추가 마스크로만 사용 (None이면 흰색이라 영향 없음)
                col.a *= tex2D(_MainTex, i.uv).a;

                // Image 틴트 & CanvasGroup 알파 반영 (뮤트 시 알파 감소가 그대로 적용됨)
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
