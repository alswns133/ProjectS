// 원형(Radial 360) 게이지 위로 에너지 밴드가 흐르는 UI 셰이더.
// 밴드는 채움 시작점에서 출발해 채움 끝점으로 갈수록 밝아진다.
//
// 밝기는 흰색을 더하는 게 아니라 "밑색을 곱해서" 올린다. 그래서 틸 구간은 틸로,
// 옐로 프리뷰 구간은 옐로로 밝아지고, 아트가 그라디언트를 바꿔도 셰이더는 손댈 게 없다.
// 흰색으로 빠지는 건 _BleachStart를 넘긴 최고점뿐이다 (밝기만 올리면 색이 짙어 보이기만 해서).
//
// ★ 채움은 이 셰이더가 자르지 않는다. Image의 Filled 메시가 이미 잘라주므로,
//   _Fill은 "끝점이 어디냐"(램프 기준)로만 쓴다. RadialFlowGaugeFx가 매 프레임
//   Image.fillAmount를 그대로 밀어넣기 때문에 둘이 어긋날 수 없다.
//   _AngleOffset / _Dir도 Image의 FillOrigin·Clockwise에서 자동으로 계산돼 들어온다.
//
// ★ 블렌딩이 프리멀티플라이드(Blend One OneMinusSrcAlpha)다. 일반 알파 블렌드에서는
//   최종 기여가 rgb×a로 눌려 rgb를 올려도 잘 안 밝아진다. 대신 마지막에 rgb *= a를
//   반드시 곱한다 — 빼먹으면 반투명 영역만 과하게 탄다.
//
// ★ 글로우는 스프라이트가 불투명한 픽셀 안에서만 산다. 링 바깥으로 새어나오는 헤일로가
//   필요하면 뒤에 더 넓은 글로우 링 이미지를 따로 깔아야 한다 (HUD 캔버스가
//   Screen Space - Overlay라 URP Bloom이 UI에 닿지 않는다).
// (2026-08-21 TH)
Shader "ProjectS/UI Radial Flow Gauge"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}

        [Header(Gauge)]
        _Fill ("Fill (0~1) - 컴포넌트가 채움", Range(0, 1)) = 0.62

        // 링의 진행도 p를 뽑는 매핑. 컴포넌트가 Image 설정에서 계산해 넣는다.
        // _AngleOffset: 12시를 0으로 본 시계방향 시작 각도(0~1). Top=0, Right=0.25, Bottom=0.5, Left=0.75
        // _Dir: 1=시계방향, -1=반시계방향
        _AngleOffset ("Angle Offset (0~1)", Range(0, 1)) = 0
        _Dir ("Direction (1 or -1)", Float) = 1

        [Header(Flow Band)]
        // 밴드 머리의 위치(0~1). 음수면 밴드가 통째로 사라진다(쉬는 구간).
        _FlowHead ("Flow Head", Float) = -1
        _TrailLen ("Trail Length (0~1)", Range(0.01, 1)) = 0.15
        _TrailFalloff ("Trail Falloff", Range(0.5, 6)) = 2.2

        [Header(Heat)]
        // 담금질. 채움 끝점이 12시(=1.0)에 가까울수록 컴포넌트가 _Heat을 올린다.
        _Heat ("Heat (0~1)", Range(0, 1)) = 0
        _HeatLen ("Heat Length (0~1)", Range(0.01, 1)) = 0.12
        _HeatPow ("Heat Falloff", Range(0.5, 6)) = 2
        _HeatBlend ("Heat Blend", Range(0, 1)) = 1

        // 블랙바디 3단: 암적 → 주황 → 백열. 뒤 둘은 HDR이라 1을 넘겨 과노출시킬 수 있다.
        _HeatCool ("Heat Cool", Color) = (0.35, 0.03, 0, 1)
        [HDR] _HeatMid ("Heat Mid", Color) = (1.2, 0.35, 0.05, 1)
        [HDR] _HeatHot ("Heat Hot", Color) = (1.8, 1.35, 0.85, 1)

        [Header(Brightness)]
        _FlowIntensity ("Flow Intensity", Range(0, 3)) = 1
        _GlowBoost ("Glow Boost", Range(0, 4)) = 1.4

        // 끝으로 갈수록 밝아지는 램프. 값이 클수록 밝아지는 구간이 끝쪽에 몰린다.
        _RampPow ("End Ramp Power", Range(0.1, 6)) = 1.8
        // 시작점에서의 최소 밝기. 0이면 밴드가 출발 직후엔 아예 안 보인다.
        _RampFloor ("Ramp Floor", Range(0, 1)) = 0.15

        _BleachStart ("Bleach Start", Range(0, 1)) = 0.7
        _AlphaGain ("Glow Alpha Gain", Range(0, 1)) = 0.35

        // 아틀라스에 묶인 스프라이트 대응. (xMin, yMin, width, height) — 컴포넌트가 채운다.
        // 이게 틀리면 극좌표 중심이 어긋나 밴드가 링을 안 타고 비스듬히 지나간다.
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
            Name "RadialFlowGauge"

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
                fixed4 color    : COLOR;      // Image 틴트 × CanvasGroup 알파
                float2 uv       : TEXCOORD0;
                float4 worldPos : TEXCOORD1;  // RectMask2D용
            };

            sampler2D _MainTex;
            float4 _ClipRect;

            float  _Fill;
            float  _AngleOffset;
            float  _Dir;

            float  _FlowHead;
            float  _TrailLen;
            float  _TrailFalloff;

            float  _Heat;
            float  _HeatLen;
            float  _HeatPow;
            float  _HeatBlend;
            fixed4 _HeatCool;
            float4 _HeatMid;
            float4 _HeatHot;

            float  _FlowIntensity;
            float  _GlowBoost;
            float  _RampPow;
            float  _RampFloor;
            float  _BleachStart;
            float  _AlphaGain;

            float4 _UVRect;

            v2f vert(appdata v)
            {
                v2f o;
                o.worldPos = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv) * i.color;

                // 스프라이트 rect 안에서의 정규화 좌표(0~1). 아틀라스에 묶여 있어도 중심이 맞는다.
                float2 luv = (i.uv - _UVRect.xy) / max(_UVRect.zw, 1e-5);
                float2 d = luv - 0.5;

                // 12시를 0으로 본 시계방향 각도(0~1).
                // atan2(x, y)는 +Y축 기준 시계방향이라 12시 시작이 그대로 나온다.
                float aCW = frac(atan2(d.x, d.y) * 0.15915494 + 1.0);

                // Image의 FillOrigin/Clockwise에 맞춘 진행도. 채움 시작점이 0.
                float p = frac((aCW - _AngleOffset) * _Dir);

                // 밴드: 머리(_FlowHead) 뒤쪽으로 _TrailLen 만큼 꼬리가 붙는다.
                float back  = _FlowHead - p;
                float trail = saturate(1.0 - back / _TrailLen) * step(0.0, back);
                trail = pow(trail, _TrailFalloff);

                // ★ 끝으로 갈수록 밝아지는 램프. 채움 끝점(_Fill)에서 1이 된다.
                float ramp = _RampFloor
                           + (1.0 - _RampFloor) * pow(saturate(p / max(_Fill, 1e-4)), _RampPow);

                float glow = trail * ramp * _FlowIntensity;

                // ── 담금질 ───────────────────────────────────────────────
                // 채움 끝점(p = _Fill)에 고정된 열. 흐름 밴드와 달리 움직이지 않고,
                // 끝점에서 뒤로 _HeatLen 만큼만 번진다.
                // 끝점 부근에만 얹는 국소 열. 위치에 따라 달라지는 값이라 셰이더 몫이다.
                // ★ 게이지 <b>전체</b>가 붉어지는 몸통 색은 여기서 하지 않는다 —
                //   스프라이트 × Image 틴트 위에 또 lerp를 얹으면 중간에서 섞여 탁해진다.
                //   몸통은 RadialFlowGaugeFx가 Image.color를 직접 잡는다.
                float toTip = saturate(1.0 - (_Fill - p) / _HeatLen);
                float heat  = pow(toTip, _HeatPow) * _Heat;

                float3 hc = lerp(_HeatCool.rgb, _HeatMid.rgb, saturate(heat * 2.0));
                hc = lerp(hc, _HeatHot.rgb, saturate(heat * 2.0 - 1.0));

                // ★ 여기만 곱하기가 아니라 대체(lerp)다.
                //   밑색이 청록이라 곱해서는 절대 붉어지지 않는다 — 곱으로 바꾸면 열이 안 보인다.
                col.rgb = lerp(col.rgb, hc, saturate(heat * _HeatBlend));

                // 밑색을 곱해 올린다 = 현재 게이지 그라디언트를 그대로 따라간다.
                // 열을 먼저 깔았으므로, 밴드가 달아오른 구간을 지날 때 붉은색이 더 타오른다
                // (= 내려쳐서 더 달아오르는 그림. 따로 처리하지 않아도 순서만으로 나온다).
                col.rgb += col.rgb * glow * _GlowBoost;

                // 최고점만 흰색으로 빠뜨려 "탄다"는 느낌을 준다.
                col.rgb = lerp(col.rgb, float3(1, 1, 1),
                               saturate((glow - _BleachStart) / max(1e-4, 1.0 - _BleachStart)));

                // 알파는 원래 알파에 비례해서만 올린다. 그래야 투명한 픽셀로 빛이 새지 않는다.
                col.a = saturate(col.a + col.a * (glow + heat) * _AlphaGain);

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a - 0.001);
                #endif

                // 프리멀티플라이드. Blend One OneMinusSrcAlpha와 짝이라 빼먹으면 안 된다.
                col.rgb *= col.a;
                return col;
            }
            ENDCG
        }
    }
}
