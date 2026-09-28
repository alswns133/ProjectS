// 육각 타일 배경 + 중심에서 퍼지는 웨이브(UI 전용). 로그인 화면 배경이 쓴다.
//
// 타일을 오브젝트로 깔지 않고 이 셰이더 하나가 화면 전체의 육각 그리드를 계산한다. 타일 수천 개를 Image로 두면
// 매 프레임 웨이브 값이 바뀔 때마다 캔버스 리빌드가 일어나 가장 무거운 화면이 되기 때문이다(여기선 드로우콜 1번).
//
// 웨이브는 픽셀이 아니라 "칸 중심"과 원점 사이 거리로 판정한다 — 그래야 빛의 띠가 흐르는 게 아니라
// 타일이 한 칸씩 차례로 튀어 오른다. 타일 무늬를 반복하는 텍스처로는 칸 중심을 알 수 없어 이 느낌이 안 난다.
//
// 그리드는 위아래가 평평한(flat-top) 육각 배열. 타일 스프라이트(Synty Pip_Hexagon)가 flat-top이라 맞췄다.
// 시간·원점·크기·웨이브 슬롯은 HexWaveBackground.cs가 매 프레임 넣는다(_Time 대신 unscaled 시각을 쓰기 위함).
//
// 홀로그램 표현(표면이 아니라 "투사된 빛"으로 보이게):
//  ① 가산 합성 — 겹칠수록 밝아지는 빛. ② 테두리 발광 — 속은 옅고 윤곽만 밝게.
//  ④ 글리치 — 가끔 가로 띠가 옆으로 튀며 번쩍(결합 순간엔 강제로 크게).
//  ⑦ 투사 감쇠 — 화면 위아래로 갈수록 옅어짐.
//  (③ 스캔라인은 넣었다가 제거했다 — 촘촘한 타일 위에 가로줄이 겹쳐 화면이 지저분해졌다.)
// 색은 UI 원칙대로 파랑 한 계열만 쓴다(글리치도 색을 가르지 않고 위치·밝기만 흔든다).
Shader "ProjectS/UI Hex Wave"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture (unused)", 2D) = "white" {}

        [Header(Tile)]
        [Toggle] _UseTileTex ("Use Tile Texture", Float) = 1
        _TileTex ("Tile Texture (flat-top hexagon)", 2D) = "white" {}
        _TileFill ("Hexagon Width In Texture (0-1)", Range(0.1, 1)) = 0.75
        _CellSize ("Cell Size (center to corner, px)", Float) = 22
        _TileScale ("Tile Scale In Cell", Range(0.1, 1.2)) = 0.86

        [Header(Color)]
        _Color ("Base Color", Color) = (0.24, 0.47, 0.86, 1)
        _WaveColor ("Wave Color", Color) = (0.6, 0.82, 1, 1)
        _BaseAlpha ("Base Alpha", Range(0, 1)) = 0.07
        _WaveAlpha ("Wave Alpha", Range(0, 1)) = 0.55
        _Flicker ("Idle Flicker", Range(0, 1)) = 0.35

        [Header(Wave Response)]
        _WavePop ("Tile Pop Scale", Range(0, 1)) = 0.28
        _WavePush ("Tile Push Outward (px)", Float) = 4
        _WaveReach ("Wave Reach (px)", Float) = 1400

        [Header(Center Clear)]
        _ClearSoftness ("Clear Edge Softness (px)", Float) = 60

        [Header(Hologram Edge Glow)]
        _EdgeWidth ("Edge Width (px)", Range(0.5, 8)) = 2
        _EdgeGlow ("Edge Brightness", Range(0, 4)) = 1.6
        _FillDim ("Inner Fill Brightness", Range(0, 1)) = 0.3

        [Header(Hologram Glitch)]
        _GlitchRate ("Glitch Checks Per Second", Float) = 7
        _GlitchChance ("Glitch Chance Per Check", Range(0, 1)) = 0.05
        _GlitchOffset ("Glitch Max Shift (px)", Float) = 14
        _GlitchBandHeight ("Glitch Band Height (px)", Vector) = (6, 36, 0, 0)
        _GlitchBrightness ("Glitch Band Brightness", Range(0, 3)) = 1.2
        _GlitchKickDuration ("Kick Glitch Duration (s)", Float) = 0.35

        [Header(Hologram Projection Fade)]
        _VerticalFade ("Top/Bottom Fade (0-0.5)", Range(0, 0.5)) = 0.22

        // 아래는 스크립트가 매 프레임 넣는 값(인스펙터에서 만지지 않는다).
        [HideInInspector] _RectSize ("Rect Size", Vector) = (1920, 1080, 0, 0)
        [HideInInspector] _WaveOrigin ("Wave Origin (px)", Vector) = (960, 540, 0, 0)
        [HideInInspector] _ClearRadius ("Clear Radius (px)", Float) = 0
        [HideInInspector] _Now ("Now", Float) = 0
        [HideInInspector] _GlitchKickTime ("Glitch Kick Time", Float) = -100
        [HideInInspector] _Wave0 ("Wave 0 (start, speed, width, strength)", Vector) = (0, 0, 1, 0)
        [HideInInspector] _Wave1 ("Wave 1", Vector) = (0, 0, 1, 0)
        [HideInInspector] _Wave2 ("Wave 2", Vector) = (0, 0, 1, 0)
        [HideInInspector] _Wave3 ("Wave 3", Vector) = (0, 0, 1, 0)

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
        // ① 가산 합성: 뒤 배경에 빛을 더한다. 어두운 배경 위에서 겹칠수록 밝아져 표면이 아니라 빛으로 읽힌다.
        Blend SrcAlpha One
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _TileTex;
            float _UseTileTex;
            float _TileFill;
            float _CellSize;
            float _TileScale;

            fixed4 _Color;
            fixed4 _WaveColor;
            float _BaseAlpha;
            float _WaveAlpha;
            float _Flicker;

            float _WavePop;
            float _WavePush;
            float _WaveReach;
            float _ClearSoftness;

            float _EdgeWidth;
            float _EdgeGlow;
            float _FillDim;

            float _GlitchRate;
            float _GlitchChance;
            float _GlitchOffset;
            float4 _GlitchBandHeight;
            float _GlitchBrightness;
            float _GlitchKickDuration;
            float _GlitchKickTime;

            float _VerticalFade;

            float4 _RectSize;
            float4 _WaveOrigin;
            float _ClearRadius;
            float _Now;
            float4 _Wave0;
            float4 _Wave1;
            float4 _Wave2;
            float4 _Wave3;

            float4 _ClipRect;

            static const float Sqrt3 = 1.7320508;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color;
                return OUT;
            }

            // 큐브 좌표 반올림으로 가장 가까운 육각 칸(축 좌표 q, r)을 구한다.
            float2 HexRound(float2 qr)
            {
                float3 c = float3(qr.x, qr.y, -qr.x - qr.y);
                float3 rc = round(c);
                float3 d = abs(rc - c);

                if (d.x > d.y && d.x > d.z) rc.x = -rc.y - rc.z;
                else if (d.y > d.z) rc.y = -rc.x - rc.z;

                return rc.xy;
            }

            // flat-top 육각형의 부호 거리(안쪽 음수). inradius = 중심에서 평평한 변까지 거리.
            float HexSdf(float2 p, float inradius)
            {
                const float3 k = float3(-0.8660254, 0.5, 0.5773503);
                p = abs(p);
                p -= 2.0 * min(dot(k.xy, p), 0.0) * k.xy;
                p -= float2(clamp(p.x, -k.z * inradius, k.z * inradius), inradius);
                return length(p) * sign(p.y);
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            // 웨이브 하나의 세기. 원점에서 거리 d인 칸에 파도 앞면이 지나갈 때 1에 가깝다.
            // wave = (시작 시각, 속도 px/s, 띠 폭 px, 세기). 멀리 갈수록 약해져 화면 끝에서 자연스럽게 사라진다.
            float WaveAt(float4 wave, float d)
            {
                float age = _Now - wave.x;
                if (wave.w <= 0.0 || age < 0.0) return 0.0;

                float front = age * wave.y;
                float band = (d - front) / max(wave.z, 1.0);
                float falloff = saturate(1.0 - front / max(_WaveReach, 1.0));
                return exp(-band * band) * falloff * wave.w;
            }

            // ④ 글리치. 시간을 짧은 구간으로 끊고, 구간마다 확률로 가로 띠 하나를 골라 옆으로 민다.
            // 결합 순간(kick)에는 확률을 무시하고 구간을 더 잘게 끊어 여러 띠가 연달아 크게 튄다.
            // 반환: x = 가로 이동(px), y = 띠 안 여부(0~1, 밝기 가중).
            float2 Glitch(float y)
            {
                float kick = saturate(1.0 - (_Now - _GlitchKickTime) / max(_GlitchKickDuration, 0.001));
                float rate = max(_GlitchRate, 0.01) * (1.0 + kick * 3.0);
                float slice = floor(_Now * rate);

                float chance = max(_GlitchChance, kick);
                if (Hash(float2(slice, 1.7)) >= chance) return float2(0.0, 0.0);

                float bandY = Hash(float2(slice, 3.1)) * _RectSize.y;
                float bandH = lerp(_GlitchBandHeight.x, _GlitchBandHeight.y, Hash(float2(slice, 5.3))) * (1.0 + kick);
                float inBand = step(abs(y - bandY), bandH * 0.5);

                float shift = (Hash(float2(slice, 7.9)) * 2.0 - 1.0) * _GlitchOffset * (1.0 + kick * 2.0);
                return float2(shift * inBand, inBand);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float s = max(_CellSize, 1.0);
                float2 p = IN.texcoord * _RectSize.xy;

                // 글리치는 그리드를 계산하기 전에 좌표를 밀어야 타일째로 어긋난다(픽셀만 밀면 타일이 찢어져 보임).
                float2 glitch = Glitch(p.y);
                p.x += glitch.x;

                // 픽셀 → 속한 칸 → 칸 중심(px).
                float2 qr = HexRound(float2((2.0 / 3.0) * p.x / s, (-1.0 / 3.0 * p.x + Sqrt3 / 3.0 * p.y) / s));
                float2 center = float2(s * 1.5 * qr.x, s * Sqrt3 * (qr.y + qr.x * 0.5));

                float2 toCell = center - _WaveOrigin.xy;
                float d = length(toCell);

                float wave = saturate(WaveAt(_Wave0, d) + WaveAt(_Wave1, d) + WaveAt(_Wave2, d) + WaveAt(_Wave3, d));

                // 파도가 지나가는 칸은 바깥으로 살짝 밀리고 커진다(칸 전체가 들썩이는 느낌).
                float2 pushed = center + toCell / max(d, 0.001) * wave * _WavePush;
                float2 local = p - pushed;
                float scale = _TileScale * (1.0 + wave * _WavePop);

                // ② 테두리 발광: 타일 모양(텍스처든 대체 모양이든)과 같은 육각형의 윤곽 띠를 따로 구해 밝게 올린다.
                // 텍스처 알파로는 테두리를 알 수 없어서, 같은 크기의 육각형 거리 함수로 계산한다.
                float sdf = HexSdf(local, s * 0.8660254 * scale);
                float edge = saturate(1.0 + sdf / max(_EdgeWidth, 0.01)) * step(sdf, 0.0);

                float mask;
                if (_UseTileTex > 0.5)
                {
                    // 칸 너비(2s) × 배율이 텍스처 안 육각형 너비(_TileFill)에 오도록 UV를 잡는다.
                    float2 uv = 0.5 + local / (2.0 * s * scale) * _TileFill;
                    float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
                    mask = tex2D(_TileTex, saturate(uv)).a * inside;
                }
                else
                {
                    // 텍스처가 없을 때의 대체 모양: 부드러운 가장자리의 flat-top 육각형.
                    mask = 1.0 - smoothstep(-1.0, 1.0, sdf);
                }

                // 속은 어둡게, 윤곽은 밝게. 파도가 지나갈 땐 속도 함께 차올라 칸 전체가 빛난다.
                float body = lerp(_FillDim, 1.0, wave) + edge * _EdgeGlow;

                // 대기 중에도 칸마다 제각각 희미하게 깜빡인다 — 불확실하고 불안정한 세계의 표면.
                float h = Hash(qr);
                float flicker = 1.0 + _Flicker * sin(_Now * (0.6 + h * 1.4) + h * 6.2831);

                // 게이지 바로 뒤는 비운다. 게이지가 반투명이라 타일이 비치면 가운데가 지저분해진다.
                float clear = smoothstep(_ClearRadius, _ClearRadius + _ClearSoftness, d);

                // ⑦ 투사 감쇠: 위아래 끝으로 갈수록 옅어져 화면 밖 어딘가에서 쏘아낸 영상처럼 보인다.
                float vy = IN.texcoord.y;
                float projection = smoothstep(0.0, _VerticalFade, vy) * smoothstep(0.0, _VerticalFade, 1.0 - vy);
                if (_VerticalFade <= 0.0) projection = 1.0;

                // ④ 글리치 띠는 순간 밝아진다.
                float glitchBoost = 1.0 + glitch.y * _GlitchBrightness;

                fixed4 color;
                color.rgb = lerp(_Color.rgb, _WaveColor.rgb, wave);
                color.a = mask * clear * body * projection * glitchBoost
                        * (_BaseAlpha * flicker + wave * _WaveAlpha) * _Color.a * IN.color.a;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
