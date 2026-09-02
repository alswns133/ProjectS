// 아이콘 위를 광원 하나가 일정 시간마다 한 바퀴 도는 UI 셰이더.
// [대기 _Interval초] → [_SweepTime초 동안 한 바퀴] 를 무한 반복한다.
//
// [왜 Mask를 안 쓰는가]
// 광원 스프라이트를 아이콘 모양으로 잘라내려고 Mask(스텐실)를 쓰면 가장자리가 계단으로 튄다.
// Mask는 알파를 기준값 하나로 자르는 1비트 판정이라, 아이콘 텍스처가 아무리 곱게 안티에일리어싱
// 되어 있어도 "통과/차단" 둘 중 하나로 뭉개지기 때문이다(RectMask2D는 사각형 경계라 더 심하다).
// 여기서는 자를 일이 없다 — 광원을 별도 오브젝트로 얹지 않고, 아이콘을 그리는 바로 그 픽셀에서
// 밝기만 더한다. 경계의 부드러움은 스프라이트 알파(_AlphaMask)와 smoothstep이 그대로 들고 간다.
//
// [구조]
//  · 각도   : 머리(head)가 시작 각도에서 출발해 한 바퀴 돌고, 그 뒤로 꼬리가 끌린다.
//  · 반지름 : 도형 경계(=1)를 기준으로 한 밴드. 테두리만 훑을지 아이콘 전체를 씻을지 정한다.
//  · 시간   : 대기 구간에는 아예 0을 곱해 끈다. 뜨고 지는 것은 _Fade가 맡는다.
//
// [도형]
// 육각형 아이콘이라 원형 밴드로는 테두리를 따라가지 못한다(모서리에서 뜨고 변에서 파고든다).
// 그래서 Shape=Hexagon이면 육각형 SDF로 거리를 잰다. 경계에서 값이 정확히 1이라
// 원형과 손잡이(_Radius/_Width)를 그대로 공유한다.
//
// ★ 블렌딩이 프리멀티플라이드(Blend One OneMinusSrcAlpha)다. 아이콘 색에는 마지막에 rgb *= a를
//   반드시 곱한다. 대신 광원은 알파를 거치지 않고 더하므로, _AlphaMask를 0으로 내리면
//   스프라이트 바깥으로도 빛이 번진다(아이콘을 넘어서는 후광이 필요할 때 쓴다).
// ★ Rect가 정사각형이 아니면 원/육각이 찌그러진다. 정사각으로 두거나 _Aspect에 (width/height)를 넣는다.
// ★ 스프라이트가 아틀라스에 묶이면 i.uv가 0~1이 아니다. 그때만 _UVRect에 (x, y, w, h)를 넣는다.
// ★ 머티리얼은 에셋 전역이다. 같은 .mat을 여러 아이콘이 쓰면 전부 같은 박자로 돈다.
//   따로 돌리려면 아이콘마다 머티리얼을 복제하고 _Phase만 다르게 준다.
// (2026-09-02 TH)
Shader "ProjectS/UI Icon Sweep Glow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)

        [Header(Glow)]
        // 광원 색. HDR이라 1을 넘겨 태울 수 있다(블룸이 있으면 여기서 번짐이 결정된다).
        [HDR] _GlowColor ("Glow Color", Color) = (0.55, 0.85, 1, 1)
        _Intensity ("Intensity", Range(0, 8)) = 1.6
        // 꼬리 길이. 한 바퀴가 1.0이므로 0.25면 늘 사분면 하나가 밝다.
        _Length ("Trail Length (turn)", Range(0.01, 1)) = 0.22
        // 꼬리가 죽는 곡선. 크게 줄수록 머리 근처에만 빛이 몰려 혜성처럼 보인다.
        _Falloff ("Trail Falloff", Range(0.2, 8)) = 2.5
        // 머리 끝의 밝은 코어. 0이면 코어 없이 흐르는 띠만 남는다.
        _HeadBoost ("Head Boost", Range(0, 4)) = 1.2
        _HeadSize ("Head Size (turn)", Range(0.001, 0.2)) = 0.03

        [Header(Timing)]
        // 한 바퀴가 끝나고 다음 바퀴까지 쉬는 시간. "일정 시간 뒤에"가 이 값이다.
        _Interval ("Wait (sec)", Range(0, 30)) = 3
        _SweepTime ("Lap Time (sec)", Range(0.05, 10)) = 0.9
        // 출발/도착에서 뜨고 지는 구간(진행도 비율). 0에 가까우면 시작 각도에서 툭 켜진다.
        _Fade ("Fade In Out", Range(0.001, 0.5)) = 0.18
        // 출발 각도. 0.25 = 12시, 0 = 3시, 0.5 = 9시, 0.75 = 6시.
        _StartAngle ("Start Angle (turn)", Range(0, 1)) = 0.25
        // 1 = 반시계, -1 = 시계.
        _Dir ("Direction (1 or -1)", Range(-1, 1)) = 1
        // 머티리얼을 복제해 아이콘마다 다른 값을 주면 여러 개가 한 박자로 도는 것을 피할 수 있다.
        _Phase ("Phase Offset (sec)", Range(0, 30)) = 0

        [Header(Shape)]
        [Enum(Circle, 0, Hexagon, 1)] _Hex ("Shape", Float) = 1
        // 육각형의 방향. 위아래가 평평하면 0, 위아래가 뾰족하면 30.
        _HexRotation ("Hexagon Rotation (deg)", Range(0, 60)) = 0
        // 도형이 Rect를 채우는 비율. 육각 꼭짓점이 Rect 좌우 끝에 닿게 그려져 있으면 0.87 근처다.
        _ShapeScale ("Shape Scale", Range(0.1, 1.5)) = 0.87
        // 밴드 중심. 1 = 도형 테두리 위. 낮추면 안쪽으로 들어온다.
        _Radius ("Band Radius", Range(0, 1.5)) = 0.92
        // 밴드 폭. 좁으면 테두리만 훑고, 크게 주면 아이콘 전체가 밝아진다.
        _Width ("Band Width", Range(0.01, 1.5)) = 0.4
        _Aspect ("Aspect (w/h)", Float) = 1
        // 1이면 스프라이트가 있는 곳에만 빛난다(가장자리 부드러움은 알파가 그대로 들고 간다).
        // 0으로 내리면 도형 밴드만 보고 칠해 아이콘 바깥까지 빛이 번진다.
        _AlphaMask ("Clip By Sprite Alpha", Range(0, 1)) = 1
        _UVRect ("UV Rect (atlas)", Vector) = (0, 0, 1, 1)

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
        Blend One OneMinusSrcAlpha   // 프리멀티플라이드 — 가산 하이라이트를 내기 위함
        ColorMask [_ColorMask]

        Pass
        {
            Name "IconSweepGlow"

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
                fixed4 color    : COLOR;      // Image 틴트 x CanvasGroup 알파
                float2 uv       : TEXCOORD0;
                float4 worldPos : TEXCOORD1;  // RectMask2D용
            };

            sampler2D _MainTex;
            float4 _ClipRect;

            fixed4 _Color;
            fixed4 _GlowColor;
            float _Intensity, _Length, _Falloff, _HeadBoost, _HeadSize;
            float _Interval, _SweepTime, _Fade, _StartAngle, _Dir, _Phase;
            float _Hex, _HexRotation, _ShapeScale, _Radius, _Width;
            float _Aspect, _AlphaMask;
            float4 _UVRect;

            #define TAU 6.28318530718

            inline float2 Rotate(float2 v, float rad)
            {
                float s, c;
                sincos(rad, s, c);
                return float2(v.x * c - v.y * s, v.x * s + v.y * c);
            }

            // 정육각형 부호 거리(내접 반지름 r, 위아래가 평평한 방향).
            // 경계에서 정확히 0이라 1을 더하면 "중심 0 / 테두리 1"이 되어, 방향과 무관하게
            // 원형과 같은 척도가 된다. _Radius를 도형이 바뀌어도 그대로 쓸 수 있는 이유다.
            inline float SdHexagon(float2 p, float r)
            {
                const float3 k = float3(-0.8660254, 0.5, 0.5773503);
                p = abs(p);
                p -= 2.0 * min(dot(k.xy, p), 0.0) * k.xy;
                p -= float2(clamp(p.x, -k.z * r, k.z * r), r);
                return length(p) * sign(p.y);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.worldPos = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 아틀라스 오프셋을 걷어내 스프라이트 로컬 0~1로 되돌린다.
                float2 uv = (i.uv - _UVRect.xy) / max(_UVRect.zw, 1e-4);
                float2 p = (uv - 0.5) * 2.0;
                p.x *= _Aspect;

                // [시간] 한 주기 = 대기 + 한 바퀴. 대기 구간에는 running이 0이라 통째로 꺼진다.
                float period = max(_Interval + _SweepTime, 1e-3);
                float t = fmod(_Time.y + _Phase, period);
                float prog = saturate((t - _Interval) / max(_SweepTime, 1e-3));
                float running = step(_Interval, t);

                // 출발과 도착에서 대칭으로 뜨고 진다. 없으면 시작 각도에서 광원이 툭 나타난다.
                float fade = max(_Fade, 1e-4);
                float env = smoothstep(0.0, fade, prog) * smoothstep(0.0, fade, 1.0 - prog) * running;

                // [각도] 머리에서 꼬리 방향으로 잰 거리(0~1바퀴).
                // dir을 곱한 뒤 frac을 태우면 시계/반시계가 같은 식 하나로 처리된다
                // (HLSL frac은 음수도 x - floor(x)라 그대로 감긴다).
                float dir = _Dir >= 0.0 ? 1.0 : -1.0;
                float ang = atan2(p.y, p.x) / TAU + 0.5;
                float head = _StartAngle + prog * dir;
                float d = frac((head - ang) * dir);

                float trail = saturate(1.0 - d / max(_Length, 1e-4));
                trail = pow(trail, max(_Falloff, 0.01));
                float core = 1.0 - smoothstep(0.0, max(_HeadSize, 1e-4), d);
                float sweep = saturate(trail + core * _HeadBoost);

                // [반지름] 테두리(=1)를 기준으로 한 밴드.
                float2 hp = Rotate(p, radians(_HexRotation));
                float scale = max(_ShapeScale, 1e-3);
                float shapeDist = lerp(length(p) / scale,
                                       1.0 + SdHexagon(hp / scale, 1.0),
                                       step(0.5, _Hex));
                float band = 1.0 - smoothstep(0.0, max(_Width, 1e-4), abs(shapeDist - _Radius));

                fixed4 col = tex2D(_MainTex, i.uv) * i.color;

                // 스프라이트 알파로 가린다. 자르는 게 아니라 곱하는 것이라 경계가 계단지지 않는다.
                float mask = lerp(1.0, col.a, _AlphaMask);
                float glow = sweep * band * env * mask * _Intensity;

                float alpha = col.a;

                #ifdef UNITY_UI_CLIP_RECT
                float clipping = UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                alpha *= clipping;
                glow *= clipping;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha - 0.001);
                #endif

                // 아이콘은 프리멀티플라이드로, 광원은 알파를 거치지 않고 가산으로 얹는다.
                float3 rgb = col.rgb * alpha + _GlowColor.rgb * (_GlowColor.a * glow);
                return fixed4(rgb, alpha);
            }
            ENDCG
        }
    }

    Fallback Off
}
