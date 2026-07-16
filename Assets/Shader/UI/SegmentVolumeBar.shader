Shader "UI/SegmentVolumeBar"
{
    Properties
    {
        [PerRendererData] _MainTex ("Segment Texture (A = shape)", 2D) = "white" {}

        _Value    ("Fill Value (0~1)", Range(0, 1)) = 0.7
        _SegCount ("Segment Count", Float) = 20

        _OnColor  ("On Color",  Color) = (0.12, 0.73, 1.0, 1.0)
        _OffColor ("Off Color", Color) = (0.23, 0.35, 0.47, 0.35)
        _HotColor ("Hot Color", Color) = (1.0, 0.54, 0.24, 1.0)
        _HotStart ("Hot Start Ratio (0~1)", Range(0, 1)) = 0.8
        _EdgeSkew ("Edge Skew (fill 경계 기울기)", Range(-0.5,0.5)) = 0.0

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
            float  _HotStart;
            float _FlipX, _EdgeSkew;

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
                // 세그먼트 모양은 텍스처 알파가 전담 (스큐/간격/라운딩)
                fixed texAlpha = tex2D(_MainTex, i.uv).a;


                // 칸 계산
                float fillCells = _Value * _SegCount;          // 예: 0.73 * 20 = 14.6
                float cell  = floor(i.uv.x * _SegCount);       // 이 픽셀이 속한 칸 인덱스
                float local = frac(i.uv.x * _SegCount);        // 칸 내부 좌표 (0~1)

                // 마지막 칸 부분 채움:
                //  fillCells - cell >= 1  → 항상 켜짐 (완전히 찬 칸)
                //  0 < fillCells - cell < 1 → 진행률만큼만 켜짐
                //  fillCells - cell <= 0  → 꺼짐
                float on = step(local, fillCells - cell);

                // 상위 구간(기본 80%~)은 경고색
                float hot = step(_HotStart * _SegCount, cell);
                fixed4 onCol = lerp(_OnColor, _HotColor, hot);

                fixed4 col = lerp(_OffColor, onCol, on);
                col.a *= texAlpha;

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
