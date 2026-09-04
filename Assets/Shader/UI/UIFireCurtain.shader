// 보스 등장 연출용 풀스크린 불 가림막.
// 폭발이 화면을 삼켰다가(덮임) → 계속 타며 머물다가(유지) → 중심부터 타서 걷히는(걷힘)
// 세 단계를 한 장의 머티리얼로 처리한다. FireCurtainFx가 _Cover/_Burn 두 값만 0→1로 민다.
//
// ★ 왜 단색 Image + 디졸브가 아닌가
//   붉은 색면을 깔았다 지우면 '커튼이 걷혔다'로 읽혀 앞의 폭발과 인과가 끊긴다. 가림막이
//   정지된 색면이 아니라 계속 대류하는 불이어야 "폭발이 유지되는 중"으로 읽힌다.
//   그래서 내부는 fbm 노이즈가 매 프레임 흐르고, 명멸(_Flicker)이 밝기를 흔든다.
//
// ★ 덮임과 걷힘은 같은 방사 필드를 쓴다
//   중심(_Center = 폭발의 화면 좌표)에서 퍼진 것이 같은 중심으로 되돌아가야 하나의 사건이 된다.
//   다만 경계 노이즈는 _BurnSeed로 어긋나게 해, 걷힘이 덮임의 역재생으로 보이지 않게 한다.
//
// ★ 알파는 경계에서만 흔들고 내부는 반드시 1이다
//   내부까지 노이즈로 뚫으면 가려진 동안 보스가 자리를 잡는 모습이 구멍으로 비친다.
//   노이즈는 '색'만 흔들고, 알파는 방사 경계 두 개(filled·burned)로만 결정한다.
//
// ★ 시간은 _FxTime(스크립트가 unscaled로 채움)을 쓴다. 셰이더 내장 _Time은 timeScale을 타서
//   히트스톱·슬로우모션이 걸리면 불까지 함께 느려진다(SlowMotionController와 겹치는 구간이다).
Shader "ProjectS/UI Fire Curtain"
{
    Properties
    {
        [PerRendererData] _MainTex ("Base (optional)", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // 캡처한 폭발 프레임 등을 불 아래에 깔고 싶을 때만 올린다. 기본 0 —
        // Image/RawImage가 _MainTex를 제 스프라이트(흰색)로 덮어쓰므로, 0이 아니면 화면이 하얗게 뜬다.
        _BaseStrength ("Base Strength", Range(0,1)) = 0

        // ── 연출의 두 손잡이 (코드가 민다) ───────────────────────────────
        _Cover ("Cover", Range(0,1.3)) = 0        // 0 아무것도 없음 → 1 화면을 다 덮음
        _Burn ("Burn", Range(0,1.3)) = 0          // 0 그대로 → 1 다 타서 걷힘

        // ── 폭발 중심 (코드가 채움) ──────────────────────────────────────
        _Center ("Center (viewport 0~1)", Vector) = (0.5, 0.5, 0, 0)
        _RectSize ("Rect Size (px)", Vector) = (1920, 1080, 0, 0)
        _MaxRadius ("Max Radius (px)", Float) = 1100   // 중심에서 가장 먼 모서리까지. 이걸로 반지름을 정규화한다

        // ── 색 ───────────────────────────────────────────────────────────
        [HDR] _FireColor ("Fire Color", Color) = (1.0, 0.24, 0.05, 1)   // 폭발 파티클 색과 맞춘다
        [HDR] _HotColor ("Hot Color", Color) = (1.0, 0.78, 0.30, 1)     // 코어·불씨. HDR이라 Bloom이 집는다
        _SootColor ("Soot Color", Color) = (0.05, 0.03, 0.03, 1)        // 타고 남은 그을음 테두리

        // ── 불의 결 ─────────────────────────────────────────────────────
        _NoiseScale ("Noise Scale", Float) = 3.0        // 불꽃 덩어리 크기. 클수록 잘다
        _Turbulence ("Turbulence", Range(0,1)) = 0.45   // 도메인 워프 세기. 0이면 구름, 1이면 소용돌이
        _RiseSpeed ("Rise Speed", Float) = 0.35         // 열기가 위로 오르는 속도
        _SwirlSpeed ("Swirl Speed", Float) = 0.12       // 중심을 감고 도는 속도
        _Flicker ("Flicker", Range(0,1)) = 0.25         // 전체 밝기 명멸
        _CoreBoost ("Core Boost", Range(0,2)) = 0.8     // 중심이 더 밝게 타는 정도

        // ── 경계 ─────────────────────────────────────────────────────────
        _EdgeNoise ("Edge Noise", Range(0,1)) = 0.28    // 경계가 일렁이는 폭. 0이면 원형 아이리스로 보인다
        _EdgeSoft ("Edge Softness", Range(0.001,0.4)) = 0.05
        _EmberWidth ("Ember Width", Range(0,0.4)) = 0.09  // 타는 자리에 남는 불씨 띠 두께
        _SootWidth ("Soot Width", Range(0,0.4)) = 0.06    // 불씨 바깥의 그을음 띠 두께
        _BurnSeed ("Burn Seed", Float) = 17.3           // 걷힘 경계를 덮임과 어긋나게 하는 오프셋

        _FxTime ("Fx Time (unscaled)", Float) = 0

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
                float2 local : TEXCOORD1;   // 렉트 로컬 좌표(px). 화면 어디인지 알아야 방사 필드를 만든다
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _FireColor;
            fixed4 _HotColor;
            fixed4 _SootColor;

            float _Cover;
            float _Burn;
            float4 _Center;
            float4 _RectSize;
            float _MaxRadius;

            float _NoiseScale;
            float _Turbulence;
            float _RiseSpeed;
            float _SwirlSpeed;
            float _Flicker;
            float _CoreBoost;

            float _EdgeNoise;
            float _EdgeSoft;
            float _EmberWidth;
            float _SootWidth;
            float _BurnSeed;
            float _FxTime;
            float _BaseStrength;

            float hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // 값 노이즈. 셀 해시를 부드럽게 이어 붙여 경계가 톱니가 아니라 물결로 일렁이게 한다.
            float valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // 옥타브 3장. 한 장짜리 노이즈는 '구름'이라 불로 안 읽힌다 —
            // 큰 덩어리 위에 잔불이 얹혀야 불꽃의 결이 생긴다.
            float fbm(float2 p)
            {
                float v = 0.0;
                float a = 0.5;

                for (int k = 0; k < 3; k++)
                {
                    v += a * valueNoise(p);
                    p = p * 2.03 + 11.7;
                    a *= 0.5;
                }

                return v;
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.local = v.vertex.xy;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 폭발의 화면 좌표를 원점으로 삼는다. 화면 정중앙이 아니라 '터진 그 자리'에서
                // 퍼져야 앞 장면과 이어진다.
                float2 centerPx = (_Center.xy - 0.5) * _RectSize.xy;
                float2 d = i.local - centerPx;

                // 세로를 기준으로 정규화해 화면비와 무관하게 원형을 유지한다.
                float2 p = d / max(1.0, _RectSize.y);
                float r = length(d) / max(1.0, _MaxRadius);   // 0(중심) ~ 1(가장 먼 모서리)

                float t = _FxTime;

                // ── 불의 결: 위로 오르며 중심을 감고 도는 좌표에서 fbm을 뜬다 ──
                float ang = atan2(p.y, p.x) + t * _SwirlSpeed;
                float2 flow = float2(cos(ang), sin(ang)) * r;
                float2 q = p * _NoiseScale + float2(0.0, -t * _RiseSpeed) + flow * _Turbulence;

                // 도메인 워프. 노이즈로 좌표를 한 번 더 밀어 층이 서로 말려 들어가게 한다.
                float2 warp = float2(fbm(q + 3.1), fbm(q - 5.7)) - 0.5;
                float n = fbm(q + warp * _Turbulence * 2.0);

                // ── 경계 두 개. 알파는 오직 이것으로만 정한다(내부는 반드시 불투명) ──
                float edgeN = (fbm(p * 2.4 + t * 0.15) - 0.5) * _EdgeNoise;

                // 덮임: _Cover가 커질수록 filled 영역이 중심에서 바깥으로 자란다.
                float coverField = r + edgeN;
                float filled = 1.0 - smoothstep(_Cover - _EdgeSoft, _Cover + _EdgeSoft, coverField);

                // _Cover가 0이면 무조건 완전 투명이다. 경계 노이즈가 음수로 튀는 중심 한 점이
                // 대기 중에도 깜박이는 것을 막는다.
                filled *= step(0.0001, _Cover);

                // 걷힘: 같은 중심이되 노이즈 시드를 어긋내 역재생으로 보이지 않게 한다.
                float burnEdgeN = (fbm(p * 2.4 + _BurnSeed - t * 0.2) - 0.5) * _EdgeNoise;
                float burnField = r + burnEdgeN;
                float burned = 1.0 - smoothstep(_Burn - _EdgeSoft, _Burn + _EdgeSoft, burnField);

                float alpha = saturate(filled * (1.0 - burned));
                clip(alpha - 0.002);   // 다 걷힌 뒤 빈 픽셀까지 칠하지 않는다

                // ── 색 ──
                // 중심일수록, 노이즈가 높을수록 뜨겁다.
                float heat = saturate(n * 1.25 + _CoreBoost * (1.0 - saturate(r)) * 0.6);
                heat *= 1.0 + (valueNoise(float2(t * 9.0, 0.5)) - 0.5) * _Flicker;

                float3 rgb = lerp(_FireColor.rgb, _HotColor.rgb, saturate(heat));

                // 덮이는 앞머리: 퍼져 나가는 선단이 가장 밝다(폭발이 밀고 들어오는 압력).
                float coverEdge = 1.0 - smoothstep(0.0, _EmberWidth, abs(coverField - _Cover));
                rgb = lerp(rgb, _HotColor.rgb, coverEdge * step(_Burn, 0.001));

                // 타는 자리: 불씨 띠 → 그 바깥으로 그을음. 이 두 겹이 있어야
                // '지워졌다'가 아니라 '타서 없어졌다'로 읽힌다.
                float ember = saturate(burned) * (1.0 - smoothstep(_Burn, _Burn + _EmberWidth, burnField));
                float soot = (1.0 - smoothstep(_Burn + _EmberWidth, _Burn + _EmberWidth + _SootWidth, burnField))
                             * step(_Burn + _EmberWidth * 0.5, burnField);

                rgb = lerp(rgb, _SootColor.rgb, saturate(soot) * 0.85);
                rgb = lerp(rgb, _HotColor.rgb, saturate(ember));

                // 선택: 캡처한 폭발 프레임 등을 밑에 깔고 싶을 때만 쓴다(_BaseStrength 기본 0이라 무영향).
                rgb += tex2D(_MainTex, i.uv).rgb * _BaseStrength;

                return fixed4(rgb * i.color.rgb, alpha * i.color.a);
            }
            ENDCG
        }
    }

    Fallback "UI/Default"
}
