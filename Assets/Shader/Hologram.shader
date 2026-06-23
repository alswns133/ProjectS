Shader "UI/Hologram"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}

        // 스캔라인
        _ScanLineSpeed  ("Scanline Speed",  Range(0, 5))    = 1.0
        _ScanLineDensity("Scanline Density",Range(10, 200)) = 80.0
        _ScanLineStrength("Scanline Strength", Range(0, 1)) = 0.3

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

            float  _ScanLineSpeed;
            float  _ScanLineDensity;
            float  _ScanLineStrength;

            float  _GlitchSpeed;
            float  _GlitchStrength;
            float  _GlitchThreshold;

            float  _ChromaOffset;

            float  _FlickerSpeed;
            float  _FlickerStrength;

            float4 _HoloColor;
            float  _HoloIntensity;

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

                // ── 스캔라인: 수평선이 위로 흐름 ─────────────
                float scanline = sin(uv.y * _ScanLineDensity
                                 - _Time.y * _ScanLineSpeed) * 0.5 + 0.5;
                // 어두운 선만 남기기 (밝은 부분은 거의 영향 없게)
                scanline = 1.0 - scanline * _ScanLineStrength;
                col.rgb *= scanline;

                // ── 홀로그램 색상 틴트 ───────────────────────
                col.rgb = lerp(col.rgb, col.rgb * _HoloColor.rgb, _HoloIntensity);

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