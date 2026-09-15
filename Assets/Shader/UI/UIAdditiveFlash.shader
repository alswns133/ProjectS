// UI 이미지용 가산(빛) 셰이더. 화면 전체 번쩍임·플레어처럼 "덮는 것"이 아니라 "밝히는 것"에 쓴다.
//
// ── 왜 기본 UI 셰이더로는 안 되는가 ──────────────────────────────────────
// 기본 UI는 알파 합성(SrcAlpha OneMinusSrcAlpha)이라, 붉은 이미지를 올리면 아래 화면이 그만큼
// "붉은 색으로 대체"된다. 밝은 불꽃 위에서는 오히려 탁해지고, 전체가 붉은 막을 한 장 덮은 것처럼 읽힌다.
// 가산은 아래 화면에 색을 "더하므로" 어두운 곳은 붉게 물들고 밝은 불꽃은 더 하얗게 타올라
// 빛이 터진 것으로 읽힌다.
//
// ── 알파는 "밝기 손잡이"다 ───────────────────────────────────────────────
// 가산 합성은 원래 알파를 쓰지 않는다. 그러면 Image Color의 알파를 키프레임으로 움직여도 아무 변화가 없어
// 타임라인·애니메이션으로 번쩍임을 조절할 수 없다. 그래서 색에 알파를 미리 곱해(프리멀티플라이) 내보낸다.
// → Image Color 알파 0 = 안 보임, 1 = 최대 밝기. CanvasGroup 알파도 똑같이 먹는다.
//
// ── Blend Mode ─────────────────────────────────────────────────────────
//   · Additive : 그대로 더한다. 가장 세게 번쩍인다. 밝은 곳은 금방 하얗게 날아간다.
//   · Screen   : 밝은 곳일수록 덜 더한다. 날아가지 않고 부드럽게 밝아진다. 세기가 과하면 이쪽으로.
//
// 사용법: 화면을 덮는 Image에 이 셰이더로 만든 머티리얼(UIAdditiveFlash.mat)을 넣고,
//         Source Image는 비워 두거나 부드러운 그라데이션 텍스처를 넣는다. 세기는 Image Color 알파로 조절한다.
// (2026-09-15 TH)
Shader "ProjectS/UI Additive Flash"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // 색에 곱하는 배율. 1을 넘기면 알파를 끝까지 올리지 않아도 강하게 번쩍인다.
        _Intensity ("Intensity", Range(0, 4)) = 1

        [Enum(Additive, 1, Screen, 4)] _SrcBlend ("Blend Mode", Float) = 1

        // 가장자리를 가운데보다 밝게. 0이면 화면 전체가 고르게 밝아진다.
        // 가운데(경고 아이콘·BOSS 자리)가 하얗게 날아가 안 보이는 게 싫을 때 올린다.
        // Source Image가 비어 있을 때 기준이다 — Image의 UV(0~1)로 거리를 재기 때문.
        _EdgeBias ("Edge Bias", Range(0, 1)) = 0
        _EdgeFalloff ("Edge Falloff", Range(0.1, 4)) = 1.5

        // UI 공통(마스크/스텐실).
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
        // 대상 알파는 건드리지 않는다(Zero One). 렌더 텍스처·카메라 스택에서 번쩍임이 투명도에 새지 않게.
        Blend [_SrcBlend] One, Zero One
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                fixed4 color    : COLOR;
                float2 uv       : TEXCOORD0;
                float4 worldPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float _Intensity;
            float _EdgeBias;
            float _EdgeFalloff;
            float4 _ClipRect;

            v2f vert(appdata v)
            {
                v2f o;
                o.worldPos = v.vertex;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 src = tex2D(_MainTex, i.uv) * i.color;

                // 가장자리 가중. 가운데 0 → 모서리 1 거리에 곡선을 먹여, 편향만큼 가운데를 어둡게 한다.
                float edge = saturate(length(i.uv - 0.5) * 1.41421356);
                float edgeMask = lerp(1.0, pow(edge, _EdgeFalloff), _EdgeBias);

                float a = src.a * edgeMask;

                #ifdef UNITY_UI_CLIP_RECT
                a *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                #endif

                // 알파를 색에 미리 곱한다 — 가산 합성에서 알파를 밝기 손잡이로 쓰기 위함(파일 머리 설명).
                return fixed4(src.rgb * a * _Intensity, 0);
            }
            ENDCG
        }
    }

    Fallback Off
}
