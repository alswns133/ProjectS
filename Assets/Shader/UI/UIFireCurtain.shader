// 보스 등장 연출용 풀스크린 불 가림막.
// 폭발이 화면을 삼켰다가(덮임) → 계속 이글거리며 머물다가(유지) → 중심부터 타서 걷히는(걷힘)
// 세 단계를 한 장의 머티리얼로 처리한다. FireCurtainFx가 _Cover/_Burn 두 값만 0→1로 민다.
//
// ═══ 이글거림은 어떻게 만드나 ═══════════════════════════════════════════════
// 흔한 디졸브 셰이더(안개 텍스처나 값 노이즈를 임계값으로 자르는 것)가 불처럼 안 보이는 이유는
// 하나다 — 무늬가 통째로 미끄러질 뿐 형태가 바뀌지 않는다. 불의 이글거림은 흘러가는 현상이 아니라
// 제자리에서 끓으며 형태가 갈리는 현상이라, 스크롤로는 나오지 않는다. 세 가지로 만든다.
//
//   ① 능선 노이즈(_Ridge). 값 노이즈는 부드러운 덩어리라 그대로 쓰면 '주황색 구름'이다.
//      1-|2n-1|로 골을 능선으로 세우면 덩어리가 가늘게 뻗는 불꽃 혀가 된다.
//   ② 옥타브별 차등 상승 + 자기 이류(_Boil). 옥타브마다 상승 속도를 다르게 주고, 아래 옥타브의
//      값이 위 옥타브의 좌표를 밀게 하면 층이 서로 말려 들어가며 끓는다. 전 옥타브가 같은 속도로
//      흐르면 그게 바로 '벽지가 미끄러지는' 느낌의 정체다.
//   ③ 다섯 구간 색 램프. 2색 lerp는 구조적으로 밍밍해진다. 검은 연기 → 진홍 → 주황 → 호박 →
//      흰빛을 급하게 지나가되 흰빛 구간을 아주 좁게 둬야 뜨거워 보인다.
//
// ★ 덮임과 걷힘은 같은 방사 필드를 쓴다
//   중심(_Center = 폭발의 화면 좌표)에서 퍼진 것이 같은 중심으로 되돌아가야 하나의 사건이 된다.
//   다만 경계 노이즈는 _BurnSeed로 어긋나게 해, 걷힘이 덮임의 역재생으로 보이지 않게 한다.
//
// ★ 알파는 경계에서만 흔들고 내부는 반드시 1이다
//   내부까지 노이즈로 뚫으면 가려진 동안 보스가 자리를 잡는 모습이 구멍으로 비친다.
//   그래서 명암 대비는 알파가 아니라 '색'으로만 만든다 — 어두운 부분은 투명이 아니라 연기다.
//
// ★ 시간은 _FxTime(스크립트가 unscaled로 채움)을 쓴다. 셰이더 내장 _Time은 timeScale을 타서
//   히트스톱·슬로우모션(SlowMotionController)이 걸리면 불까지 함께 느려진다.
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

        // ── 폭발 중심 (코드가 채운다) ────────────────────────────────────
        _Center ("Center (viewport 0~1)", Vector) = (0.5, 0.5, 0, 0)
        _RectSize ("Rect Size (px)", Vector) = (1920, 1080, 0, 0)
        _MaxRadius ("Max Radius (px)", Float) = 1100   // 중심에서 가장 먼 모서리까지. 이걸로 반지름을 정규화한다

        // ── 색 램프: 차가운 쪽부터 뜨거운 쪽까지 ─────────────────────────
        _SmokeColor ("Smoke (coldest)", Color) = (0.09, 0.045, 0.035, 1)  // 불꽃 사이의 어두운 연기
        [HDR] _FireColor ("Fire (mid)", Color) = (1.0, 0.24, 0.05, 1)     // 폭발 파티클 색과 맞춘다
        [HDR] _HotColor ("Hot", Color) = (1.0, 0.66, 0.18, 1)             // 호박색. HDR이라 Bloom이 집는다
        [HDR] _WhiteHot ("White Hot (peak)", Color) = (1.0, 0.95, 0.82, 1) // 가장 뜨거운 심지. 아주 좁게 나와야 한다
        _SootColor ("Soot (burnt edge)", Color) = (0.05, 0.03, 0.03, 1)    // 타고 남은 그을음 테두리

        // ── 이글거림 ─────────────────────────────────────────────────────
        _NoiseScale ("Noise Scale", Float) = 3.2        // 불꽃 덩어리 크기. 클수록 잘다
        _Ridge ("Ridge (tongue)", Range(0,1)) = 0.75    // 0이면 뭉게구름, 1이면 가늘게 뻗는 불꽃 혀
        _Boil ("Boil (self-advection)", Range(0,2)) = 0.9  // 층이 서로 말려 끓는 정도. 이글거림의 핵심
        _RiseSpeed ("Rise Speed", Float) = 0.55         // 열기가 위로 오르는 기준 속도(옥타브마다 배수가 다르다)
        _RadialStretch ("Radial Stretch", Range(0,3)) = 1.2  // 폭발이 밀어낸 방향으로 불꽃이 늘어나는 정도
        _SwirlSpeed ("Swirl Speed", Float) = 0.16       // 중심을 감고 도는 속도

        // ── 명암 ─────────────────────────────────────────────────────────
        _Contrast ("Contrast", Range(1,6)) = 3.0        // 낮으면 균일한 색면(=밍밍), 높으면 불꽃과 연기가 갈린다
        _Gamma ("Gamma", Range(0.4,3)) = 1.35           // 크면 뜨거운 부분이 좁아져 심지가 또렷해진다
        _CoreBoost ("Core Boost", Range(0,2)) = 0.7     // 중심이 더 뜨겁게 타는 정도
        _Flicker ("Flicker", Range(0,1)) = 0.22         // 전체 밝기 명멸

        // ── 경계 ─────────────────────────────────────────────────────────
        _EdgeNoise ("Edge Noise", Range(0,1)) = 0.3     // 경계가 일렁이는 폭. 0이면 원형 아이리스로 보인다
        _EdgeTongue ("Edge Tongue Scale", Float) = 5.5  // 경계를 핥는 불꽃 혀의 잘기
        _EdgeSoft ("Edge Softness", Range(0.001,0.4)) = 0.045
        _EmberWidth ("Ember Width", Range(0,0.4)) = 0.1   // 타는 자리에 남는 불씨 띠 두께
        _SootWidth ("Soot Width", Range(0,0.4)) = 0.07    // 불씨 바깥의 그을음 띠 두께
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
            fixed4 _SmokeColor;
            fixed4 _FireColor;
            fixed4 _HotColor;
            fixed4 _WhiteHot;
            fixed4 _SootColor;

            float _Cover;
            float _Burn;
            float4 _Center;
            float4 _RectSize;
            float _MaxRadius;

            float _NoiseScale;
            float _Ridge;
            float _Boil;
            float _RiseSpeed;
            float _RadialStretch;
            float _SwirlSpeed;

            float _Contrast;
            float _Gamma;
            float _CoreBoost;
            float _Flicker;

            float _EdgeNoise;
            float _EdgeTongue;
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

            // 값 노이즈. 셀 해시를 부드럽게 이어 붙인다.
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

            // 능선 노이즈. 값이 낮은 골을 접어 올려 날카로운 능선으로 세운다.
            // 값 노이즈 그대로는 부드러운 덩어리(=구름)라, 이걸 섞는 만큼 불꽃 혀가 뻗는다.
            float ridged(float n)
            {
                return 1.0 - abs(n * 2.0 - 1.0);
            }

            // ★ 불꽃용 fbm. 일반 fbm과 두 가지가 다르고, 그 둘이 이글거림의 전부다.
            //   (1) 옥타브마다 상승 속도가 다르다 — 같으면 무늬가 통째로 미끄러져 '흐르는 텍스처'가 된다.
            //   (2) 아래 옥타브의 값이 위 옥타브의 좌표를 민다(자기 이류) — 층이 서로 말려 끓는다.
            //   도메인 워프를 따로 두지 않는 이유도 이것이다. 이 이류가 곧 워프이고, 시간에 따라
            //   변형되므로 정적인 워프보다 훨씬 살아 있다.
            float flameFbm(float2 p, float t)
            {
                float v = 0.0;
                float amp = 0.5;
                float freq = 1.0;
                float carry = 0.0;

                for (int k = 0; k < 4; k++)
                {
                    float rise = _RiseSpeed * (0.55 + 0.6 * float(k));   // 위 옥타브일수록 빠르게 오른다
                    float2 pos = p * freq + float2(0.0, -t * rise) + carry * _Boil;

                    float n = valueNoise(pos);
                    n = lerp(n, ridged(n), _Ridge);

                    v += amp * n;
                    carry = (n - 0.5) * amp * 2.0;   // 다음 옥타브를 이 옥타브가 밀어낸다

                    freq *= 2.07;
                    amp *= 0.52;
                }

                return v;
            }

            // 온도 → 색. 다섯 구간을 급하게 지난다. 2색 lerp로는 무슨 색을 넣어도 밍밍해진다 —
            // 불이 뜨거워 보이는 건 색상 자체가 아니라 '흰빛 심지가 아주 좁다'는 분포이기 때문이다.
            float3 firePalette(float h)
            {
                h = saturate(h);

                float3 c = lerp(_SmokeColor.rgb, _FireColor.rgb * 0.4, smoothstep(0.00, 0.30, h));
                c = lerp(c, _FireColor.rgb,      smoothstep(0.24, 0.58, h));
                c = lerp(c, _HotColor.rgb,       smoothstep(0.55, 0.84, h));
                c = lerp(c, _WhiteHot.rgb,       smoothstep(0.88, 1.00, h));
                return c;
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

                // ── 경계 두 개. 알파는 오직 이것으로만 정한다(내부는 반드시 불투명) ──
                // 경계 노이즈도 불꽃 필드를 쓴다. 그래야 경계가 물결로 일렁이는 게 아니라
                // 불꽃 혀가 핥으며 갉아 들어가는 것으로 보인다.
                float coverTongue = flameFbm(p * _EdgeTongue, t * 1.3) - 0.5;
                float coverField = r + coverTongue * _EdgeNoise;
                float filled = 1.0 - smoothstep(_Cover - _EdgeSoft, _Cover + _EdgeSoft, coverField);

                // _Cover가 0이면 무조건 완전 투명이다. 경계 노이즈가 음수로 튀는 중심 한 점이
                // 대기 중에도 깜박이는 것을 막는다.
                filled *= step(0.0001, _Cover);

                // 걷힘: 같은 중심이되 시드를 어긋내 역재생으로 보이지 않게 한다.
                float burnTongue = flameFbm(p * _EdgeTongue + _BurnSeed, t * 1.6) - 0.5;
                float burnField = r + burnTongue * _EdgeNoise;
                float burned = 1.0 - smoothstep(_Burn - _EdgeSoft, _Burn + _EdgeSoft, burnField);

                float alpha = saturate(filled * (1.0 - burned));
                clip(alpha - 0.002);   // 다 걷힌 뒤 빈 픽셀까지 칠하지 않는다

                // ── 불의 결 ──
                // 중심을 감고 돌면서, 폭발이 밀어낸 방향(바깥)으로 늘어난 좌표에서 불을 뜬다.
                // 위로만 오르게 하면 모닥불이 되지 폭발의 화구로 안 읽힌다.
                float2 dir = d / max(1.0, length(d));
                float ang = _SwirlSpeed * t;
                float2 swirl = float2(dir.x * cos(ang) - dir.y * sin(ang),
                                      dir.x * sin(ang) + dir.y * cos(ang));

                float2 q = p * _NoiseScale - swirl * (r * _RadialStretch);
                float n = flameFbm(q, t);

                // 명암을 세게 벌린다. 이걸 안 하면 어떤 색을 넣어도 균일한 색면이 된다.
                float heat = saturate((n - 0.5) * _Contrast + 0.5);
                heat = pow(heat, _Gamma);

                // 중심이 더 뜨겁다. 화구의 심지.
                heat = saturate(heat + _CoreBoost * (1.0 - saturate(r)) * (1.0 - saturate(r)) * 0.55);

                // 전체 명멸. 불은 밝기가 일정하지 않다.
                heat *= 1.0 + (valueNoise(float2(t * 7.0, 0.5)) - 0.5) * _Flicker;

                float3 rgb = firePalette(heat);

                // 덮이는 앞머리: 퍼져 나가는 선단이 가장 밝다(폭발이 밀고 들어오는 압력).
                float coverEdge = 1.0 - smoothstep(0.0, _EmberWidth, abs(coverField - _Cover));
                rgb = lerp(rgb, _WhiteHot.rgb, coverEdge * step(_Burn, 0.001) * 0.9);

                // 타는 자리: 불씨 띠 → 그 바깥으로 그을음. 이 두 겹이 있어야
                // '지워졌다'가 아니라 '타서 없어졌다'로 읽힌다.
                float ember = saturate(burned) * (1.0 - smoothstep(_Burn, _Burn + _EmberWidth, burnField));
                float soot = (1.0 - smoothstep(_Burn + _EmberWidth, _Burn + _EmberWidth + _SootWidth, burnField))
                             * step(_Burn + _EmberWidth * 0.5, burnField);

                rgb = lerp(rgb, _SootColor.rgb, saturate(soot) * 0.85);
                rgb = lerp(rgb, _WhiteHot.rgb, saturate(ember));

                // 선택: 캡처한 폭발 프레임 등을 밑에 깔고 싶을 때만 쓴다(_BaseStrength 기본 0이라 무영향).
                rgb += tex2D(_MainTex, i.uv).rgb * _BaseStrength;

                return fixed4(rgb * i.color.rgb, alpha * i.color.a);
            }
            ENDCG
        }
    }

    Fallback "UI/Default"
}
