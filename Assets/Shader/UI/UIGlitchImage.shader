// UI 이미지(스프라이트)용 글리치 셰이더. ProjectS/UI Glitch Text와 같은 계열이지만
// SDF 폰트 아틀라스가 아니라 일반 스프라이트를 샘플링한다.
//
// 텍스트용을 그대로 쓸 수 없는 이유: 그쪽은 TMP가 알파 채널에 담은 거리값(SDF)을 0.5 기준으로
// 잘라 글자 모양을 복원한다. 스프라이트에는 그런 거리값이 없어서 같은 코드를 태우면 형체가 뭉개진다.
//
// 연출 개념은 동일하다. 화면을 격자(셀)로 나누고
//   · 글리치가 높을수록 살아 있는 셀이 적다      → 조각이 드문드문
//   · 살아남은 셀도 조금씩 어긋난 곳을 읽는다   → 신호가 흔들림
//   · 글리치가 0이면 전부 켜지고 어긋남 0        → 원본 그대로
//
// ★ 스프라이트 아틀라스 주의: _CellOffset·_RgbSplit는 UV를 미는 값이라, 아틀라스에 묶인
//   스프라이트에서 크게 주면 옆 칸의 다른 그림을 끌어온다. 0.01을 넘기지 않는다.
Shader "ProjectS/UI Glitch Image"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // 연출의 단일 손잡이. 1이면 조각만 남아 형체를 알아볼 수 없고, 0이면 원본 그대로다.
        _Glitch ("Glitch", Range(0,1)) = 0.15

        _CellSize ("Cell Size (px)", Float) = 10          // 조각 하나의 크기. 작을수록 잘게 부서진다
        _Scatter ("Scatter", Range(0,1)) = 0.8            // 글리치 1일 때 사라지는 셀의 비율
        _CellOffset ("Cell Offset", Float) = 0.004        // 살아남은 셀이 어긋나는 정도(UV)
        _RgbSplit ("RGB Split", Float) = 0.003            // 색수차
        _FlickerSpeed ("Flicker Speed", Float) = 20       // 초당 조각 재배치 횟수
        _Scanline ("Scanline", Range(0,1)) = 0.3          // 가로 주사선 농도

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
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float4 color : COLOR;
                float2 uv    : TEXCOORD0;
                float2 local : TEXCOORD1;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float _Glitch;
            float _CellSize;
            float _Scatter;
            float _CellOffset;
            float _RgbSplit;
            float _FlickerSpeed;
            float _Scanline;

            float hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                // UI 정점은 로컬 픽셀 단위다. 격자를 로컬에 깔아야 조각 크기가 일정하다.
                o.local = v.vertex.xy;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float g = saturate(_Glitch);

                // 글리치가 0이면 계산을 통째로 건너뛴다. 꺼진 상태에서 비용을 남기지 않기 위함이다.
                if (g <= 0.001)
                {
                    fixed4 src = tex2D(_MainTex, i.uv) * i.color;
                    return src;
                }

                float t = floor(_Time.y * _FlickerSpeed);
                float2 cell = floor(i.local / max(1.0, _CellSize));

                float rAlive = hash21(cell + t * 0.137);
                float rOffX  = hash21(cell * 1.7 + t * 0.311);
                float rOffY  = hash21(cell * 2.3 + t * 0.577);

                // 살아 있는 셀 비율. 글리치가 높을수록 많이 꺼져 조각만 남는다.
                float alive = step(_Scatter * g, rAlive);

                float2 jitter = (float2(rOffX, rOffY) - 0.5) * 2.0 * _CellOffset * g;
                float2 uv = i.uv + jitter;

                // 색수차. 조각 단위로 세기를 달리해 균일하지 않게 만든다.
                float split = _RgbSplit * g * (0.5 + rOffX * 0.5);
                fixed4 cr = tex2D(_MainTex, uv + float2(split, 0));
                fixed4 cg = tex2D(_MainTex, uv);
                fixed4 cb = tex2D(_MainTex, uv - float2(split, 0));

                float3 rgb = float3(cr.r, cg.g, cb.b) * i.color.rgb;
                float alpha = max(max(cr.a, cg.a), cb.a) * i.color.a * alive;

                // 주사선. 로컬 Y 기준이라 조각 격자와 어긋나 화면 신호처럼 겹쳐 보인다.
                float scan = 1.0 - _Scanline * g * step(0.5, frac(i.local.y * 0.25));
                rgb *= scan;

                return fixed4(rgb, alpha);
            }
            ENDCG
        }
    }

    Fallback Off
}
