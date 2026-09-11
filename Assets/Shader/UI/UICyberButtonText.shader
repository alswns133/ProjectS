// 버튼 안 라벨(TextMeshPro)용 글리치 셰이더. UICyberButton과 한 쌍으로 쓴다.
//
// [글리치는 색을 건드리지 않는다 — 2026-09-10 방향 확정]
// 초판에는 고스트 Pass 두 장(글자 사본을 좌우로 밀어 자홍/청록으로 얹기)과 채널별 색수차,
// 강조색 막대가 있었는데 전부 걷어냈다. 버튼 라벨은 **무슨 버튼인지 읽히는 것**이 존재 이유라,
// 글자 주위에서 색이 점멸하면 눈이 그 색을 좇느라 정작 글자를 못 읽는다.
// 그래서 이 셰이더의 글리치는 오직 **위치**만 건드린다.
//   · 찢김   : 가로 블록 단위로 좌우로 뚝뚝 어긋난다(계단처럼 끊기는 어긋남).
//   · 흔들림 : Y를 따라 부드럽게 굽이치는 가로 파동(연속적인 흔들림).
//   · 떨림   : 글자 전체가 덜컥거린다(_Shake).
//   · 탈락   : 블록이 통째로 사라진다. 이것도 모양이지 색이 아니다.
// 글자 색은 TMP의 정점 색과 _FaceColor 그대로다. 새 기능을 넣을 때 이 원칙을 깨지 말 것 —
// "글리치니까 색이 튀어야 한다"는 것이 바로 되돌린 판단이다.
//
// [ProjectS/UI Glitch Text와 무엇이 다른가]
// 그쪽은 파티창 사망 표기·사망 팝업 제목처럼 "부서진 파편이 모여 글자가 되는" 한 번짜리 연출이라
// 셀(가루) 단위로 부서지고, _Glitch를 1 -> 0으로 떨어뜨리는 동안만 살아 있고, 주사선이 깔려 있다.
// 이쪽은 버튼 라벨이라 목적이 다르다. 항상 읽혀야 하고, 끝나는 연출이 아니라 계속 흔들리는 상태이며,
// 배경과 구별되어야 한다. 두 셰이더는 서로 대체재가 아니다 — 사망 표기는 그대로 그쪽을 쓴다.
//
// ★ _SliceOffset·_Wobble은 '폰트 아틀라스 UV'를 밀어서 어긋남을 만든다. 아틀라스에는 모든 글자가
//   격자로 담겨 있어서, 글리프 패딩(보통 글자 크기의 5% 남짓)을 넘겨 밀면 옆 칸의 다른 글자를 끌어온다.
//   그래서 밀림은 16px에 묶여 있다. 그보다 크게 어긋난 그림이 필요하면 UV가 아니라 정점을 옮겨야 하는데
//   (그러면 UV도 함께 따라가 남의 글자를 끌어올 일이 없다), 정점은 쿼드 단위라 글자별로만 옮길 수 있고
//   가로 블록 단위로는 못 옮긴다. 블록 찢김이 필요해서 UV 방식을 택했고, 그 대가가 이 상한이다.
// ★ 프리멀티플라이드(Blend One OneMinusSrcAlpha)다. 글자 색에는 알파를 곱하고 글로우는 그대로 더한다.
//   그래야 글자 바깥(알파 0)에도 빛이 남는다.
// ★ TMP의 외곽선·언더레이·마스킹(_ClipRect)은 지원하지 않는다. 면(face)과 글로우만 그린다.
// ★ 머티리얼은 폰트 단위로 공유된다. 반드시 인스턴스(TMP_Text.fontMaterial)에 물릴 것 —
//   CyberButtonFx가 대신 해 준다. fontSharedMaterial을 만지면 같은 폰트를 쓰는 화면의 모든 글자가 떨린다.
// ★ 모양 값은 이 머티리얼이 아니라 CyberButtonFx 인스펙터에서 조절한다. 라벨 머티리얼이
//   재생 중에만 만들어지는 인스턴스라, 여기 적어 둔 값은 재생 전에 손댈 방법이 없기 때문이다.
// (2026-09-10 TH)
Shader "ProjectS/UI Cyber Button Text"
{
    Properties
    {
        [HideInInspector] _MainTex ("Font Atlas", 2D) = "white" {}
        _FaceColor ("Face Color", Color) = (1, 1, 1, 1)

        [Header(Glitch  Position Only)]
        // 코드가 밀어 넣는 값(클릭 순간 등). 아래 자동 버스트와 큰 쪽이 이긴다.
        _Glitch ("Glitch (external)", Range(0, 1)) = 0
        // 평상시 세기. 라벨은 읽혀야 하므로 낮게 둔다.
        _GlitchIdle ("Glitch Idle", Range(0, 1)) = 0.08
        _BurstStrength ("Burst Strength", Range(0, 1)) = 0.75
        _BurstInterval ("Burst Wait (sec)", Range(0, 20)) = 2.6
        _BurstTime ("Burst Time (sec)", Range(0.01, 2)) = 0.14
        // 셰이더는 _Time을 공유한다. 라벨마다 다르게 주지 않으면 화면의 모든 글자가 함께 튄다.
        _Phase ("Phase Offset (sec)", Range(0, 30)) = 0
        _FlickerSpeed ("Flicker Speed", Float) = 18

        [Space(6)]
        // 블록 하나의 높이(캔버스 단위). 글자 높이의 1/4~1/6쯤.
        _SliceHeight ("Slice Height (px)", Float) = 12
        _SliceAmount ("Slice Amount", Range(0, 1)) = 0.55
        // 밀리는 최대 거리. 대부분의 블록은 이보다 훨씬 조금 밀린다. 위 ★ 참고(16 초과 금지).
        _SliceOffset ("Slice Offset (px)", Range(0, 16)) = 7
        _Dropout ("Slice Dropout", Range(0, 1)) = 0.12
        // 글자 전체가 덜컥 흔들리는 폭(캔버스 단위). 0이면 제자리에서만 찢긴다.
        _Shake ("Shake (px)", Range(0, 12)) = 0.8

        [Space(6)]
        // 흔들림. 블록 찢김이 '뚝뚝 끊기는 어긋남'이라면 이쪽은 '연속적으로 굽이치는 흔들림'이다.
        _Wobble ("Wobble (px)", Range(0, 12)) = 1.2
        _WobbleSpeed ("Wobble Speed", Float) = 3.4
        // 세로로 몇 번 굽이치는가(캔버스 1단위당 라디안). 크면 잔물결, 작으면 글자 전체가 휘청인다.
        _WobbleScale ("Wobble Scale", Range(0.01, 1)) = 0.16

        [Space(6)]
        _Softness ("Edge Softness", Range(0, 1)) = 0.12

        [Header(Glow)]
        // 기본값이 흰색인 것은 의도다. 글자에 없던 색을 더하지 않고 번짐만 주기 위함 —
        // 색을 넣고 싶으면 여기서 넣되, 깜박이지 않는 고정색이라 읽는 데 방해가 되지는 않는다.
        [HDR] _GlowColor ("Glow Color", Color) = (1, 1, 1, 1)
        _Glow ("Glow Intensity", Range(0, 4)) = 0.5
        // SDF 거리 단위. 크게 줄수록 멀리 번지지만, 폰트 아틀라스의 패딩을 넘으면 잘린다.
        _GlowWidth ("Glow Width (sdf)", Range(0.01, 0.4)) = 0.16

        [Header(Interaction)]
        // CyberButtonFx가 마우스 올림/누름에 맞춰 0과 1 사이로 움직인다.
        _Hover ("Hover", Range(0, 1)) = 0
        _HoverGlow ("Hover Glow Boost", Range(0, 4)) = 1.2
        _HoverGlitch ("Hover Glitch Boost", Range(0, 1)) = 0.3

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
        Blend One OneMinusSrcAlpha   // 프리멀티플라이드 — 글자 바깥에도 글로우를 더하기 위함
        ColorMask [_ColorMask]

        Pass
        {
            Name "Face"

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
                float2 local : TEXCOORD1;   // 오브젝트 로컬 좌표. 블록 격자를 글자와 무관하게 깔기 위함
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _FaceColor;

            float _Glitch;
            float _GlitchIdle;
            float _BurstStrength;
            float _BurstInterval;
            float _BurstTime;
            float _Phase;
            float _FlickerSpeed;

            float _SliceHeight;
            float _SliceAmount;
            float _SliceOffset;
            float _Dropout;
            float _Shake;

            float _Wobble;
            float _WobbleSpeed;
            float _WobbleScale;

            float _Softness;

            fixed4 _GlowColor;
            float _Glow;
            float _GlowWidth;

            float _Hover;
            float _HoverGlow;
            float _HoverGlitch;

            // 2D 좌표 -> 0~1 난수. 블록마다 다른 값을 뽑되 프레임 사이에는 유지돼야 해서
            // (매 프레임 완전 난수면 찢김이 아니라 흰 노이즈로 보인다) 시간은 계단으로 끊어 넣는다.
            float hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // 지금 이 순간의 글리치 세기. UICyberButton과 같은 규칙이라, 같은 _Phase를 주면
            // 버튼 바탕과 라벨이 한 박자로 흔들린다(CyberButtonFx가 그렇게 맞춰 준다).
            float GlitchAmount()
            {
                float t = _Time.y + _Phase;
                float period = max(_BurstInterval + _BurstTime, 0.0001);
                float cycle = frac(t / period);

                float inBurst = step(1.0 - _BurstTime / period, cycle);
                float alive = step(0.35, hash21(float2(floor(t / period), 3.7)));

                float self = _GlitchIdle + inBurst * alive * _BurstStrength;
                return saturate(max(self, _Glitch) + _Hover * _HoverGlitch);
            }

            // 굽이치는 가로 흔들림(캔버스 단위). 주기가 다른 두 파동을 겹쳐 규칙적인 사인파로 안 보이게 한다.
            // 글리치가 0일 때도 절반쯤 남긴다 — 흔들림은 '이따금 터지는 사고'가 아니라
            // 이 화면이 늘 불안정하다는 상태 표현이라, 평상시에 멎어 있으면 버스트만 도드라진다.
            float WobbleShift(float y, float g)
            {
                float t = _Time.y + _Phase;
                float w = sin(y * _WobbleScale + t * _WobbleSpeed)
                        + sin(y * _WobbleScale * 2.3 - t * _WobbleSpeed * 1.7) * 0.5;
                return w * _Wobble * lerp(0.45, 1.0, g);
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float g = GlitchAmount();

                // 격자는 흔들기 '전' 좌표로 깐다. 그래야 글자가 흔들릴 때 찢기는 자리가
                // 통째로 따라 움직이지 않고, 블록이 제자리에서 어긋나는 것으로 보인다.
                o.local = v.vertex.xy;

                // 글자 전체가 덜컥거리는 떨림. 블록 찢김만 있으면 제자리에서 부스럭거리기만 해서
                // '신호가 끊긴' 인상이 약하다. 제곱이라 버스트 순간에만 크게 흔들린다.
                float tick = floor((_Time.y + _Phase) * max(_FlickerSpeed, 0.0001));
                float2 shake = (float2(hash21(float2(tick, 1.7)), hash21(float2(tick, 4.3))) - 0.5)
                               * 2.0 * _Shake * g * g;

                float4 shifted = v.vertex;
                shifted.xy += shake;

                o.pos = UnityObjectToClipPos(shifted);
                o.color = v.color * _FaceColor;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            // SDF 아틀라스에서 글자 알파를 뽑는다. TMP는 거리값을 알파 채널에 담으므로
            // 0.5를 경계로 부드럽게 자른다. fwidth로 화면 배율에 맞춰 두께를 잡아야
            // 크게 키웠을 때 가장자리가 계단처럼 깨지지 않는다.
            float FaceAlpha(float d)
            {
                float w = max(fwidth(d), 0.0001) * (1.0 + _Softness * 6.0);
                return smoothstep(0.5 - w, 0.5 + w, d);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float g = GlitchAmount();

                // 캔버스 1단위를 밀려면 아틀라스 UV를 얼마나 밀어야 하는가.
                // 화면 해상도·폰트 크기·캔버스 배율이 달라도 인스펙터의 px 값이 같은 두께로 보이게 하는 환산이다
                // (두 미분 모두 화면 공간 기준이라 나누면 배율이 상쇄된다).
                // ddx는 분기 안에 두지 않는다 — 그래디언트 명령은 흐름이 갈린 곳에서 값이 보장되지 않는다.
                float dLocal = ddx(i.local.x);
                float safeLocal = (abs(dLocal) < 1e-6) ? 1e-6 : dLocal;
                float uvPerLocalX = ddx(i.uv.x) / safeLocal;

                float tick = floor((_Time.y + _Phase) * max(_FlickerSpeed, 0.0001));
                float band = floor(i.local.y / max(1.0, _SliceHeight));

                float rAlive = hash21(float2(band, tick));
                float rMag   = hash21(float2(band * 3.13 + 7.7, tick * 1.7));
                float rSign  = hash21(float2(band * 5.77, tick * 2.3));
                float rDrop  = hash21(float2(band * 9.13, tick * 0.71));

                float alive = step(1.0 - _SliceAmount * g, rAlive);
                // 세제곱이라 대부분의 블록은 아주 조금 밀리고 가끔 한 줄이 크게 튄다.
                // 균등 난수로 밀면 전부 비슷하게 흔들려 '고장난 신호'가 아니라 '떨림'으로 보인다.
                float mag = rMag * rMag * rMag;
                float dir = (rSign < 0.5) ? -1.0 : 1.0;

                // 찢김(끊기는 어긋남) + 흔들림(연속 파동). 둘 다 가로 이동일 뿐 색은 건드리지 않는다.
                float shiftPx = dir * mag * _SliceOffset * g * alive + WobbleShift(i.local.y, g);
                float2 uv = i.uv + float2(shiftPx * uvPerLocalX, 0);

                float drop = step(rDrop, _Dropout * g);
                float keep = (1.0 - drop) * i.color.a;

                float d = tex2D(_MainTex, uv).a;
                float a = FaceAlpha(d);

                // 글자 색 그대로. 채널을 갈라놓거나 다른 색으로 덮지 않는다.
                float3 rgb = i.color.rgb * a * keep;

                // 외곽 글로우. SDF는 글자 바깥에서도 거리값이 이어지므로 면 알파를 빼면 테두리 빛만 남는다.
                float outer = saturate(smoothstep(0.5 - _GlowWidth, 0.5, d) - a);
                rgb += _GlowColor.rgb * _GlowColor.a * outer
                     * _Glow * (1.0 + _Hover * _HoverGlow) * keep;

                // 프리멀티플라이드: 글자 색에는 이미 알파가 곱해져 있고 글로우는 그대로 더해졌다.
                return fixed4(rgb, a * keep);
            }
            ENDCG
        }
    }

    Fallback "TextMeshPro/Distance Field"
}
