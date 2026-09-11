// 버튼 전반에 두루 붙이는 사이버펑크 셰이더. 가장자리 글로우 + 글리치 두 가지만 한다.
//
// [글리치는 색을 건드리지 않는다 — 2026-09-10 방향 확정]
// 초판에는 색수차(좌우로 어긋난 사본을 자홍/청록으로 얹기)와 강조색 막대가 있었는데 전부 걷어냈다.
// 버튼은 "눌러야 할 것"을 알리는 물건이라, 색이 점멸하면 읽는 사람의 눈이 그 색을 좇느라
// 정작 무슨 버튼인지가 산만해진다. 그래서 이 셰이더의 글리치는 오직 **위치**만 건드린다.
//   · 찢김   : 가로 띠 단위로 좌우로 뚝뚝 어긋난다(계단처럼 끊기는 어긋남).
//   · 흔들림 : Y를 따라 부드럽게 굽이치는 가로 파동(연속적인 흔들림).
//   · 탈락   : 띠가 통째로 사라진다. 이것도 모양이지 색이 아니다.
// 원본 그림의 색은 어느 단계에서도 바뀌지 않는다. 새 기능을 넣을 때 이 원칙을 깨지 말 것 —
// "글리치니까 색이 튀어야 한다"는 것이 바로 되돌린 판단이다.
//
// [주사선을 뺀 이유]
// 같은 계열의 ProjectS/UI Glitch Image에는 _Scanline이 있지만 여기에는 없다.
// 배경(홀로그램·안개·간판)이 이미 주사선을 깔고 있어서, 버튼까지 같은 줄무늬를 얹으면
// 버튼이 배경에 잠겨 "눌 수 있는 것"으로 안 읽힌다. 버튼은 배경과 달라야 눈에 띈다.
//
// [왜 _RectBounds가 필요한가]
// 슬라이스 높이와 글로우 두께는 "버튼 안에서 몇 %"로 재야 버튼 크기가 달라도 같아 보인다.
// 그런데 UI 셰이더는 자기 rect 크기를 자동으로 받지 못한다. i.uv로 대신하면 9-Sliced 이미지에서
// 깨진다 — 아홉 조각이 저마다 0~1 UV를 갖고 있어서 띠와 글로우가 조각 경계마다 끊긴다.
// 그래서 CyberButtonFx가 RectTransform.rect를 _RectBounds(xMin, yMin, w, h)로 넣어준다.
//   · CyberButtonFx가 붙어 있으면  -> 로컬 좌표 기준. 9-Sliced·Tiled 다 정확하다.
//   · 비어 있으면(0)               -> i.uv를 rect로 간주하는 폴백. Simple/Filled 이미지에서만 맞다.
// 에디터에서 재생하지 않을 때는 폴백으로 보인다(머티리얼 에셋을 편집 중에 더럽히지 않으려고
// 컴포넌트가 재생 중에만 값을 넣는다). 최종 모양은 플레이 모드에서 확인할 것.
//
// [글로우 클리핑]
// 글로우는 rect 테두리를 기준으로 그린다. 그래서 버튼 그림 주위에 투명 여백이 있는 스프라이트에서는
// 그림 밖 허공에 빛나는 사각형이 뜬다(특히 모서리가 브래킷처럼 남는다). 셰이더는 "그림이 어디까지인가"를
// rect만 봐서는 알 수 없기 때문이다. _GlowClip이 스프라이트 알파로 글로우를 잘라 이것을 막고,
// _GlowBleed가 그 경계 밖으로 빛을 조금 흘려보내 하드컷을 눅인다.
//   · 그림 주위에 여백이 있는 스프라이트 -> _GlowClip = 1 (기본값)
//   · rect를 꽉 채우는 그림 / 스프라이트 없는 빈 Image -> 알파가 어디나 1이라 켜도 꺼도 같다
//   · 일부러 버튼 바깥까지 후광을 내고 싶다 -> _GlowClip을 0으로. 단 그 후광은 그림이 아니라
//     rect 모양으로 나온다. rect를 그림에 맞춰 줄이는 편이 대개 낫다.
//
// ★ 블렌딩이 프리멀티플라이드(Blend One OneMinusSrcAlpha)다. 스프라이트 색에는 마지막에 rgb *= a를
//   반드시 곱한다. 글로우는 알파를 거치지 않고 더하므로 스프라이트가 비어 있는 자리(알파 0)에도 빛이 남는다.
// ★ UV를 미는 값(_SliceOffset, _Wobble)은 아틀라스에 묶인 스프라이트에서 옆 칸의 다른 그림을
//   끌어올 수 있다. _UVRect에 (x, y, w, h)를 넣어 두면 그 범위로 잘라내 남의 그림을 막는다.
// ★ 머티리얼은 에셋 전역이다. 같은 .mat을 여러 버튼이 쓰면 전부 같은 박자로 튄다.
//   CyberButtonFx가 버튼마다 머티리얼을 복제하고 _Phase를 다르게 준다.
// (2026-09-10 TH)
Shader "ProjectS/UI Cyber Button"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)

        [Header(Edge Glow)]
        // HDR이라 1을 넘겨 태울 수 있다(블룸이 켜져 있으면 여기서 번짐이 결정된다).
        [HDR] _GlowColor ("Glow Color", Color) = (0.08, 0.9, 1, 1)
        // 짧은 변 대비 비율. 0.09면 세로 100px 버튼에서 9px 두께의 띠가 된다.
        _GlowWidth ("Glow Width (short side ratio)", Range(0.001, 0.5)) = 0.09
        // 클수록 테두리에 바짝 붙고, 작을수록 안쪽까지 넓게 번진다.
        _GlowPower ("Glow Falloff", Range(0.2, 8)) = 2.6
        _GlowIntensity ("Glow Intensity", Range(0, 8)) = 1.5
        // 숨쉬듯 밝기가 오르내리는 폭. 0이면 고정 밝기.
        _GlowPulse ("Glow Pulse", Range(0, 1)) = 0.3
        _GlowPulseSpeed ("Glow Pulse Speed", Float) = 2.2
        // 버튼 안쪽 전체에 은은하게 깔리는 색. _FillIntensity가 0이면 없다.
        [HDR] _FillColor ("Inner Fill Color", Color) = (0.5, 0.12, 0.9, 1)
        _FillIntensity ("Inner Fill Intensity", Range(0, 4)) = 0.2
        // 1이면 스프라이트가 있는 자리에만 빛난다. 0으로 내리면 rect 전체에 그린다.
        // 버튼 그림 주위에 투명 여백이 있는 스프라이트에서 반드시 1이어야 한다(위 [글로우 클리핑] 참고).
        _GlowClip ("Clip Glow By Sprite Alpha", Range(0, 1)) = 1
        // 그림 가장자리 밖으로 빛이 새어 나오는 폭(rect 폭 대비). 0이면 그림 경계에서 딱 끊긴다.
        _GlowBleed ("Glow Bleed (width ratio)", Range(0, 0.1)) = 0.01

        [Header(Glitch  Position Only)]
        // 코드가 밀어 넣는 값(클릭 순간 등). 아래 자동 버스트와 큰 쪽이 이긴다.
        _Glitch ("Glitch (external)", Range(0, 1)) = 0
        // 평상시 세기. 0이면 버스트 사이에는 완전히 멀쩡해 보인다.
        _GlitchIdle ("Glitch Idle", Range(0, 1)) = 0.1
        // 자동 버스트: [_BurstInterval 대기] -> [_BurstTime 동안 크게] 반복. 스크립트 없이도 튄다.
        _BurstStrength ("Burst Strength", Range(0, 1)) = 0.8
        _BurstInterval ("Burst Wait (sec)", Range(0, 20)) = 2.6
        _BurstTime ("Burst Time (sec)", Range(0.01, 2)) = 0.14
        // 셰이더는 _Time을 공유한다. 이 값이 같으면 화면의 모든 버튼이 딱 붙어서 함께 튄다.
        _Phase ("Phase Offset (sec)", Range(0, 30)) = 0
        _FlickerSpeed ("Flicker Speed", Float) = 16

        [Space(6)]
        // 버튼 높이를 몇 개의 가로 띠로 나눌지. 많을수록 잘게 찢긴다.
        _SliceCount ("Slice Count", Range(2, 80)) = 20
        _SliceAmount ("Slice Amount", Range(0, 1)) = 0.55
        // 밀리는 최대 거리(rect 폭 대비). 대부분의 띠는 이보다 훨씬 조금 밀리고 가끔 크게 튄다.
        _SliceOffset ("Slice Offset (width ratio)", Range(0, 0.5)) = 0.09
        // 띠가 통째로 사라지는 비율. 색이 아니라 모양이 뚫리는 것이라 이 셰이더의 방향과 어긋나지 않는다.
        _Dropout ("Slice Dropout", Range(0, 1)) = 0.12

        [Space(6)]
        // 흔들림. 띠 찢김이 '뚝뚝 끊기는 어긋남'이라면 이쪽은 '연속적으로 굽이치는 흔들림'이다.
        // 둘이 겹쳐야 신호가 흔들리며 찢기는 인상이 난다. 하나만 있으면 각각 떨림/깨짐으로만 보인다.
        _Wobble ("Wobble (width ratio)", Range(0, 0.1)) = 0.006
        _WobbleSpeed ("Wobble Speed", Float) = 3.4
        // 세로로 몇 번 굽이치는가. 크면 잔물결, 작으면 통째로 휘청인다.
        _WobbleScale ("Wobble Scale", Range(1, 60)) = 14

        [Header(Interaction)]
        // CyberButtonFx가 마우스 올림/누름에 맞춰 0과 1 사이로 움직인다.
        _Hover ("Hover", Range(0, 1)) = 0
        _HoverGlow ("Hover Glow Boost", Range(0, 4)) = 1.1
        _HoverGlitch ("Hover Glitch Boost", Range(0, 1)) = 0.25

        [Header(Rect)]
        // CyberButtonFx가 채운다. (0,0,0,0)이면 UV 폴백으로 돈다. 위 [왜 _RectBounds가 필요한가] 참고.
        _RectBounds ("Rect Bounds (xMin, yMin, w, h)", Vector) = (0, 0, 0, 0)
        // 스프라이트가 아틀라스에 묶였을 때만 (x, y, w, h)를 넣는다. UV를 이 범위로 잘라 남의 그림을 막는다.
        _UVRect ("UV Rect (atlas)", Vector) = (0, 0, 1, 1)
        // _RectBounds가 비었을 때만 쓰는 가로세로비(폭/높이).
        _Aspect ("Aspect (w/h) fallback", Float) = 1

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
        Blend One OneMinusSrcAlpha   // 프리멀티플라이드 — 알파 0인 자리에도 글로우를 더하기 위함
        ColorMask [_ColorMask]

        Pass
        {
            Name "CyberButton"

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
                float4 color : COLOR;
                float2 uv    : TEXCOORD0;
                float2 local : TEXCOORD1;   // 오브젝트 로컬 좌표. rect 전체에 연속이라 9-Sliced에서도 안 끊긴다
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;

            fixed4 _GlowColor;
            float _GlowWidth;
            float _GlowPower;
            float _GlowIntensity;
            float _GlowPulse;
            float _GlowPulseSpeed;
            fixed4 _FillColor;
            float _FillIntensity;
            float _GlowClip;
            float _GlowBleed;

            float _Glitch;
            float _GlitchIdle;
            float _BurstStrength;
            float _BurstInterval;
            float _BurstTime;
            float _Phase;
            float _FlickerSpeed;

            float _SliceCount;
            float _SliceAmount;
            float _SliceOffset;
            float _Dropout;

            float _Wobble;
            float _WobbleSpeed;
            float _WobbleScale;

            float _Hover;
            float _HoverGlow;
            float _HoverGlitch;

            float4 _RectBounds;
            float4 _UVRect;
            float _Aspect;

            // 2D 좌표 -> 0~1 난수. 띠마다 다른 값을 뽑되 프레임 사이에는 유지돼야 해서
            // (매 프레임 완전 난수면 찢김이 아니라 흰 노이즈로 보인다) 시간은 계단으로 끊어 넣는다.
            float hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // 지금 이 순간의 글리치 세기. 스크립트가 없어도 스스로 튀게 하는 것이 목적이다.
            // 주기마다 난수로 한 번씩 건너뛰어, 초시계처럼 규칙적으로 튀는 인상을 지운다.
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

            // 굽이치는 가로 흔들림. 주기가 다른 두 파동을 겹쳐 규칙적인 사인파로 안 보이게 한다.
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

                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.local = v.vertex.xy;
                o.color = v.color * _Color;
                return o;
            }

            // 아틀라스 밖으로 새어 나가지 않게 자른다. _UVRect 기본값(0,0,1,1)이면 사실상 무해하다.
            float2 ClampUV(float2 uv)
            {
                float2 lo = _UVRect.xy + 0.0005;
                float2 hi = _UVRect.xy + _UVRect.zw - 0.0005;
                return clamp(uv, lo, hi);
            }

            // 가장자리에서 안쪽으로 번지는 띠. t는 rect 안 0~1 좌표, size는 rect의 (폭, 높이).
            // 두 축을 각각 실제 길이로 환산해서 재기 때문에 가로로 긴 버튼에서도 띠 두께가 일정하다.
            float EdgeGlow(float2 t, float2 size)
            {
                float2 d = min(t, 1.0 - t) * size;
                float dist = min(d.x, d.y);
                float unit = max(min(size.x, size.y), 0.0001);
                float e = saturate(1.0 - dist / max(_GlowWidth * unit, 0.0001));
                return pow(e, _GlowPower);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // rect 좌표. _RectBounds가 있으면 로컬 좌표로, 없으면 UV로 대신한다.
                float hasRect = step(0.0001, _RectBounds.z) * step(0.0001, _RectBounds.w);
                float2 tRect = (i.local - _RectBounds.xy) / max(_RectBounds.zw, 0.0001);
                float2 tUv = (i.uv - _UVRect.xy) / max(_UVRect.zw, 0.0001);
                float2 t = lerp(tUv, tRect, hasRect);
                float2 size = lerp(float2(max(_Aspect, 0.0001), 1.0), _RectBounds.zw, hasRect);

                float g = GlitchAmount();

                // 캔버스 로컬 1단위를 밀려면 아틀라스 UV를 얼마나 밀어야 하는가.
                // ddx는 분기 안에 두지 않는다 — 그래디언트 명령은 흐름이 갈린 곳에서 값이 보장되지 않는다.
                float dLocal = ddx(i.local.x);
                float safeLocal = (abs(dLocal) < 1e-6) ? 1e-6 : dLocal;
                float uvPerLocalX = ddx(i.uv.x) / safeLocal;
                // rect 폭 대비 비율 -> UV 이동량. 폴백일 때는 UV 자체가 rect라 비율을 그대로 쓴다.
                float ratioToUv = lerp(_UVRect.z, _RectBounds.z * uvPerLocalX, hasRect);

                // 세로 환산. 글로우 클리핑의 번짐(_GlowBleed)을 가로세로 같은 길이로 주기 위해 필요하다
                // (가로만 번지면 위아래 경계에서만 딱 끊겨 티가 난다).
                float dLocalY = ddy(i.local.y);
                float safeLocalY = (abs(dLocalY) < 1e-6) ? 1e-6 : dLocalY;
                float uvPerLocalY = ddy(i.uv.y) / safeLocalY;
                float ratioToUvY = lerp(_UVRect.w, _RectBounds.z * uvPerLocalY, hasRect);

                // 찢김. 띠 번호는 rect 세로 좌표에서 뽑는다(로컬 기준이라 아홉 조각에 걸쳐 연속이다).
                float count = max(_SliceCount, 2.0);
                float band = floor(t.y * count);
                float tick = floor((_Time.y + _Phase) * max(_FlickerSpeed, 0.0001));

                float rAlive = hash21(float2(band, tick));
                float rMag   = hash21(float2(band * 3.13 + 7.7, tick * 1.7));
                float rSign  = hash21(float2(band * 5.77, tick * 2.3));
                float rDrop  = hash21(float2(band * 9.13, tick * 0.71));

                float alive = step(1.0 - _SliceAmount * g, rAlive);
                // 세제곱이라 대부분의 띠는 아주 조금 밀리고 가끔 한 줄이 크게 튄다.
                // 균등 난수로 밀면 전부 비슷하게 흔들려 '고장난 신호'가 아니라 '떨림'으로 보인다.
                float mag = rMag * rMag * rMag;
                float dir = (rSign < 0.5) ? -1.0 : 1.0;

                // 찢김(끊기는 어긋남) + 흔들림(연속 파동). 둘 다 가로 이동일 뿐 색은 건드리지 않는다.
                float shift = dir * mag * _SliceOffset * g * alive + WobbleShift(t.y, g);
                float drop = step(rDrop, _Dropout * g);

                float2 uv = i.uv + float2(shift * ratioToUv, 0);
                fixed4 cM = tex2D(_MainTex, ClampUV(uv));

                // 글로우 마스크. 그림이 어디까지인지는 알파만이 안다 — 위 [글로우 클리핑] 참고.
                // 네 방향에서 알파의 최대값을 가져와(팽창) 경계 밖으로 _GlowBleed만큼 빛을 흘린다.
                // 탭을 분기로 감싸지 않는 이유는 텍스처 샘플이 그래디언트를 쓰기 때문이다.
                // _GlowBleed가 0이면 네 탭이 전부 가운데와 같은 자리라 결과만 같고 손해는 없다.
                float2 bleed = float2(_GlowBleed * ratioToUv, _GlowBleed * ratioToUvY);
                float coverage = cM.a;
                coverage = max(coverage, tex2D(_MainTex, ClampUV(uv + float2(bleed.x, 0))).a);
                coverage = max(coverage, tex2D(_MainTex, ClampUV(uv - float2(bleed.x, 0))).a);
                coverage = max(coverage, tex2D(_MainTex, ClampUV(uv + float2(0, bleed.y))).a);
                coverage = max(coverage, tex2D(_MainTex, ClampUV(uv - float2(0, bleed.y))).a);
                float glowMask = lerp(1.0, saturate(coverage), _GlowClip);

                // 원본 색 그대로. 여기서 채널을 갈라놓거나 다른 색으로 덮지 않는다.
                fixed4 src = cM * i.color;
                float alpha = src.a * (1.0 - drop);
                float3 rgb = src.rgb;

                // 테두리 글로우도 같은 만큼 밀린다. 그래야 그림만 찢기고 테두리는 제자리에 남는
                // 어색함이 없다. 색은 _GlowColor 하나로 고정이라 점멸하지 않는다.
                float eM = EdgeGlow(t + float2(shift, 0), size);

                float pulse = 1.0 + _GlowPulse * sin((_Time.y + _Phase) * _GlowPulseSpeed);
                float power = _GlowIntensity * pulse * (1.0 + _Hover * _HoverGlow);

                float3 glow = _GlowColor.rgb * _GlowColor.a * eM * power;

                // 안쪽 채움. 테두리 글로우와 겹치지 않게 가장자리에서는 뺀다.
                glow += _FillColor.rgb * _FillColor.a * _FillIntensity
                      * (1.0 - eM) * (1.0 - drop) * (1.0 + _Hover * _HoverGlow * 0.5);

                // 그림 바깥으로 새는 빛을 여기서 한 번에 잘라낸다.
                glow *= glowMask;

                // 프리멀티플라이드: 스프라이트 색만 알파를 곱하고, 빛은 그대로 더한다.
                return fixed4(rgb * alpha + glow * i.color.a, alpha);
            }
            ENDCG
        }
    }

    Fallback Off
}
