Shader "UI/Hologram"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}

        // ── CRT 스캔라인 (일정 간격의 어두운 가로줄) ──────
        _ScanLineCount  ("Scan Line Count",   Range(1, 400)) = 180  // 화면(UV)에 박히는 라인 개수 — 클수록 촘촘
        _ScanLineWidth  ("Scan Line Width",   Range(0, 1))   = 0.5  // 어두운 선 두께 (셀 대비 비율)
        _ScanLineSoft   ("Scan Line Soft",    Range(0, 0.5)) = 0.05 // 선 가장자리 부드러움 (0이면 하드엣지, 앨리어싱 방지용)
        _ScanStrength   ("Scan Strength",     Range(0, 1))   = 0.5  // 어두운 정도
        _ScanRollSpeed  ("Scan Roll Speed",   Range(-5, 5))  = 0.0  // 롤링 속도 (0이면 고정 / CRT는 보통 0~살짝)

        // 글리치 (가로로 밀림)
        _GlitchSpeed    ("Glitch Speed",    Range(0, 20))   = 5.0
        _GlitchStrength ("Glitch Strength", Range(0, 0.1))  = 0.02
        _GlitchThreshold("Glitch Threshold",Range(0, 1))    = 0.92  // 높을수록 글리치 드물게

        // RGB 색수차
        _ChromaOffset   ("Chroma Offset",   Range(0, 0.05)) = 0.01

        // 깜빡임
        _FlickerSpeed   ("Flicker Speed",   Range(0, 30))   = 10.0
        _FlickerStrength("Flicker Strength",Range(0, 1))    = 0.15

        // 홀로그램 색상 틴트
        _HoloColor      ("Holo Color",      Color)          = (0.3, 0.8, 1.0, 1.0)
        _HoloIntensity  ("Holo Intensity",  Range(0, 2))    = 0.5

        // ── 글로우 (엣지 / 외곽 헤일로) ──────────────────
        _GlowColor      ("Glow Color",       Color)         = (0.3, 0.8, 1.0, 1.0)
        _GlowStrength   ("Glow Strength",    Range(0, 5))   = 1.5   // 빛나는 세기
        _GlowWidth      ("Glow Width (uv)",  Vector)        = (0.01, 0.01, 0, 0) // 번지는 반경(x,y) — 세로긴 패널이면 y를 줄여 균형
        _GlowPulseSpeed ("Glow Pulse Speed", Range(0, 10))  = 2.0   // 숨쉬듯 밝기 변동 속도 (0이면 고정)
        _GlowPulseAmount("Glow Pulse Amount",Range(0, 1))   = 0.2   // 변동 폭

        // UI Mask용
        _StencilComp      ("Stencil Comparison", Float) = 8
        _Stencil          ("Stencil ID",         Float) = 0
        _StencilOp        ("Stencil Operation",  Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask  ("Stencil Read Mask",  Float) = 255
        _ColorMask        ("Color Mask",         Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
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
            #include "UnityUI.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                float2 uv       : TEXCOORD0;
                float4 color    : COLOR;
                float4 worldPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4    _MainTex_ST;
            float4    _ClipRect;

            // 스캔
            float  _ScanLineCount;
            float  _ScanLineWidth;
            float  _ScanLineSoft;
            float  _ScanStrength;
            float  _ScanRollSpeed;

            float  _GlitchSpeed;
            float  _GlitchStrength;
            float  _GlitchThreshold;

            float  _ChromaOffset;

            float  _FlickerSpeed;
            float  _FlickerStrength;

            float4 _HoloColor;
            float  _HoloIntensity;

            float4 _GlowColor;
            float  _GlowStrength;
            float4 _GlowWidth;
            float  _GlowPulseSpeed;
            float  _GlowPulseAmount;

            // ── 간단한 의사난수 함수 ──────────────────────────
            float rand(float2 co)
            {
                return frac(sin(dot(co, float2(12.9898, 78.233))) * 43758.5453);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex   = UnityObjectToClipPos(v.vertex);
                o.uv       = TRANSFORM_TEX(v.uv, _MainTex);
                o.color    = v.color;
                o.worldPos = v.vertex;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;

                // ── 글리치: 특정 행만 옆으로 밀기 ───────────
                // 시간을 낮은 해상도로 끊어서 "툭툭" 튀는 느낌
                float timeStep   = floor(_Time.y * _GlitchSpeed) / _GlitchSpeed;
                float rowSeed    = floor(uv.y * 30.0);           // 30개 행 단위
                float glitchRand = rand(float2(rowSeed, timeStep));

                float glitchX = 0;
                if (glitchRand > _GlitchThreshold)
                {
                    // 임계값 넘는 행만 밀기
                    glitchX = (glitchRand - _GlitchThreshold)
                              / (1.0 - _GlitchThreshold)
                              * _GlitchStrength;
                    // 방향 랜덤
                    glitchX *= (rand(float2(rowSeed + 1.0, timeStep)) > 0.5) ? 1 : -1;
                }
                uv.x += glitchX;

                // ── RGB 색수차: 채널별로 UV를 살짝 다르게 ───
                float2 uvR = uv + float2( _ChromaOffset, 0);
                float2 uvG = uv;
                float2 uvB = uv + float2(-_ChromaOffset, 0);

                float r = tex2D(_MainTex, uvR).r;
                float g = tex2D(_MainTex, uvG).g;
                float b = tex2D(_MainTex, uvB).b;
                float a = tex2D(_MainTex, uvG).a;

                fixed4 col = fixed4(r, g, b, a);

                // ── CRT 스캔라인: 일정 간격 어두운 가로줄 ────
                // uv.y에 라인 개수를 곱해 "셀" 반복 좌표를 만든다.
                // frac로 각 셀을 0~1로 쪼개고, 셀 중앙(0.5)에 어두운 선을 박는다.
                float coord = uv.y * _ScanLineCount + _Time.y * _ScanRollSpeed;
                float f     = frac(coord);            // 0~1, 셀 하나당 한 주기
                float dist  = abs(f - 0.5);           // 0(셀 중앙) ~ 0.5(셀 경계)
                float halfW = _ScanLineWidth * 0.5;   // 선 반폭

                // dist < halfW → 선 내부(어두움=1), dist > halfW+soft → 밝음(0)
                float scanLine = 1.0 - smoothstep(halfW, halfW + _ScanLineSoft, dist);

                col.rgb *= 1.0 - scanLine * _ScanStrength;

                // ── 홀로그램 색상 틴트 ───────────────────────
                col.rgb = lerp(col.rgb, col.rgb * _HoloColor.rgb, _HoloIntensity);

                // ── 글로우: 알파를 링으로 샘플링해 형태를 부풀리고 ──
                //    (부풀린 알파 - 원본 알파) = 바깥쪽 헤일로 영역만 빛나게.
                //    스프라이트에 투명 여백이 있으면 바깥으로 번지고,
                //    꽉 찬 아트면 안쪽 테두리가 빛난다.
                float2 gw = _GlowWidth.xy;
                float glowA = 0.0;
                glowA += tex2D(_MainTex, uv + float2( gw.x,     0)).a;
                glowA += tex2D(_MainTex, uv + float2(-gw.x,     0)).a;
                glowA += tex2D(_MainTex, uv + float2(    0,  gw.y)).a;
                glowA += tex2D(_MainTex, uv + float2(    0, -gw.y)).a;
                glowA += tex2D(_MainTex, uv + float2( gw.x,  gw.y)).a;
                glowA += tex2D(_MainTex, uv + float2(-gw.x,  gw.y)).a;
                glowA += tex2D(_MainTex, uv + float2( gw.x, -gw.y)).a;
                glowA += tex2D(_MainTex, uv + float2(-gw.x, -gw.y)).a;
                glowA *= 0.125; // /8

                float halo  = saturate(glowA - a);                       // 원본보다 바깥쪽
                float pulse = 1.0 + sin(_Time.y * _GlowPulseSpeed) * _GlowPulseAmount;
                float glow  = halo * _GlowStrength * pulse;

                col.rgb += _GlowColor.rgb * glow;   // 색을 더해 빛나게
                col.a    = saturate(col.a + glow);  // 헤일로가 보이도록 알파도 확장

                // ── 깜빡임: 알파를 불규칙하게 ───────────────
                float flickerTime = floor(_Time.y * _FlickerSpeed);
                float flicker     = rand(float2(flickerTime, 0.0));
                // 대부분은 안정적, 가끔만 확 튀게
                flicker = 1.0 - _FlickerStrength * step(0.85, flicker);
                col.a *= flicker;

                // ── UI Mask 클리핑 ────────────────────────────
                col.a *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                col.a *= i.color.a;

                return col;
            }
            ENDCG
        }
    }
}
