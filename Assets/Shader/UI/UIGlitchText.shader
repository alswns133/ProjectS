// TextMeshPro 텍스트용 글리치 셰이더. TMP의 SDF 폰트 아틀라스를 직접 샘플링하므로
// 화면 캡처(GrabPass)가 필요 없다 — URP에서는 GrabPass를 쓸 수 없어 이 방식이 아니면
// UI 텍스트에 지지직 효과를 넣을 수 없다.
//
// 연출 컨셉: 글자가 날아다니는 것이 아니라, 제자리에서 잘게 부서진 파편이 흩어져 있다가
// 점점 제자리를 찾아 글자로 뭉치는 것. 그래서 정점(버텍스)은 건드리지 않는다 —
// 정점을 옮기면 단어가 통째로 튀고 쿼드가 기울어져(전단) '파편이 모인다'는 인상이 사라진다.
//
// 부서짐은 화면을 격자로 나눈 '셀' 단위로 만든다.
//   · 글리치가 높을수록 살아 있는 셀이 적다      → 파편이 드문드문
//   · 살아 있는 셀도 조금씩 어긋난 곳을 읽는다   → 조각이 제자리가 아님
//   · 글리치가 0으로 가면 전부 켜지고 어긋남 0   → 온전한 글자
//
// 사용법: 텍스트에 GlitchTextFx를 붙이고 Glitch Shader 슬롯에 이 셰이더를 넣는다
//         (컴포넌트가 폰트 머티리얼 인스턴스의 셰이더를 런타임에 교체한다).
//
// 한계: TMP의 외곽선·언더레이·마스킹(_ClipRect)은 지원하지 않는다. 면(face)만 그린다.
Shader "ProjectS/UI Glitch Text"
{
    Properties
    {
        [HideInInspector] _MainTex ("Font Atlas", 2D) = "white" {}
        _FaceColor ("Face Color", Color) = (1,1,1,1)

        // 연출의 단일 손잡이. 1이면 파편만 흩어져 글자를 알아볼 수 없고, 0이면 원본 그대로다.
        // 코드(GlitchTextFx)가 이 값만 1 → 0으로 떨어뜨린다.
        _Glitch ("Glitch", Range(0,1)) = 1

        // ── 파편(셀) ──────────────────────────────────────────────────────
        _CellSize ("Cell Size (px)", Float) = 9          // 파편 한 조각의 크기. 작을수록 잘게 부서진다
        _Scatter ("Scatter", Range(0,1)) = 0.85          // 글리치 1일 때 사라지는 셀의 비율
        _CellOffset ("Cell Offset", Float) = 0.006       // 살아남은 셀이 어긋나는 정도(아틀라스 UV)

        // ★ UV 계열은 '폰트 아틀라스' 기준이라 아주 작아야 한다. 아틀라스에는 모든 글자가 격자로
        //   담겨 있어서 0.05만 밀어도 옆 칸의 다른 글자를 끌어온다(알록달록한 남의 글자 조각이 된다).
        //   0.01을 넘기지 않는다.
        _RgbSplit ("RGB Split", Float) = 0.0022          // 색수차

        _FlickerSpeed ("Flicker Speed", Float) = 18      // 초당 파편 재배치 횟수
        _Scanline ("Scanline", Range(0,1)) = 0.25        // 가로 주사선 농도
        _Softness ("Edge Softness", Range(0,1)) = 0.15

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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv    : TEXCOORD0;
                float2 local : TEXCOORD1;   // 오브젝트 로컬 좌표. 파편 격자를 글자와 무관하게 깔기 위함
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _FaceColor;

            float _Glitch;
            float _CellSize;
            float _Scatter;
            float _CellOffset;
            float _RgbSplit;
            float _FlickerSpeed;
            float _Scanline;
            float _Softness;

            // 정점은 건드리지 않는다. 옮기는 순간 단어가 통째로 튀고 쿼드가 기울어져
            // '파편이 제자리를 찾아간다'는 연출이 성립하지 않는다.
            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _FaceColor;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.local = v.vertex.xy;
                return o;
            }

            // 2D 좌표 → 0~1 난수. 셀마다 다른 값을 뽑되 프레임 사이에는 유지돼야 해서
            // (매 프레임 완전 난수면 파편이 아니라 흰 노이즈로 보인다) 시간은 계단으로 끊어 넣는다.
            float hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // SDF 아틀라스에서 글자 알파를 뽑는다. TMP는 거리값을 알파 채널에 담으므로
            // 0.5를 경계로 부드럽게 자른다. fwidth로 화면 배율에 맞춰 두께를 잡아야
            // 크게 키웠을 때 가장자리가 계단처럼 깨지지 않는다.
            float SampleGlyph(float2 uv)
            {
                float d = tex2D(_MainTex, uv).a;
                float w = max(fwidth(d), 0.0001) * (1.0 + _Softness * 6.0);
                return smoothstep(0.5 - w, 0.5 + w, d);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float g = saturate(_Glitch);

                // 글리치가 0이면 계산을 통째로 건너뛴다. 연출이 끝난 뒤에도 셰이더는 계속 붙어 있으므로
                // 꺼진 상태에서 비용을 남기지 않기 위함이다.
                if (g <= 0.001)
                {
                    float a0 = SampleGlyph(i.uv);
                    return fixed4(i.color.rgb, i.color.a * a0);
                }

                float t = floor(_Time.y * _FlickerSpeed);

                // 파편 격자. 글자가 아니라 화면(로컬 좌표)에 격자를 깔아야, 여러 글자에 걸쳐
                // 같은 크기의 조각으로 부서진다. 글자마다 격자가 달라지면 크기가 들쭉날쭉해 보인다.
                float2 cell = floor(i.local / max(1.0, _CellSize));

                float rAlive  = hash21(cell + t * 0.137);
                float rOffX   = hash21(cell * 1.7 + t * 0.311);
                float rOffY   = hash21(cell * 2.3 + t * 0.577);

                // 살아 있는 셀 비율. 글리치 1에서 대부분이 꺼져 파편만 남고, 0으로 갈수록 전부 켜진다.
                // 이 감쇠가 곧 "파편이 모여 글자가 된다"의 정체다.
                float alive = step(_Scatter * g, rAlive);

                // 살아남은 조각도 제자리가 아니다. 어긋남이 줄어들면서 글자 모양이 맞아 들어간다.
                float2 jitter = (float2(rOffX, rOffY) - 0.5) * 2.0 * _CellOffset * g;
                float2 uv = i.uv + jitter;

                // 색수차. 조각 단위로 세기를 달리해 균일하지 않게 만든다.
                float split = _RgbSplit * g * (0.5 + rOffX * 0.5);
                float aR = SampleGlyph(uv + float2(split, 0));
                float aG = SampleGlyph(uv);
                float aB = SampleGlyph(uv - float2(split, 0));

                float3 rgb = i.color.rgb * float3(aR, aG, aB);
                float alpha = max(max(aR, aG), aB) * i.color.a * alive;

                // 주사선. 로컬 Y 기준이라 파편 격자와 어긋나 화면 신호처럼 겹쳐 보인다.
                float scan = 1.0 - _Scanline * g * step(0.5, frac(i.local.y * 0.25));
                rgb *= scan;

                return fixed4(rgb, alpha);
            }
            ENDCG
        }
    }

    Fallback "TextMeshPro/Distance Field"
}
