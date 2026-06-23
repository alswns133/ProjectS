Shader "UI/CRT_Glitch"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}

        // 고스팅
        _GhostOffset  ("Ghost Offset",   Range(0, 0.1)) = 0.03
        _GhostAlpha   ("Ghost Alpha",    Range(0, 1))   = 0.4

        // 롤링 스캔라인
        _ScanSpeed    ("Scan Speed",     Range(0, 2))   = 0.4
        _ScanWidth    ("Scan Width",     Range(0, 0.5)) = 0.08  // 선 굵기
        _ScanBend     ("Scan Bend",      Range(0, 0.3)) = 0.08  // 구부러짐 강도

        // 지직거림
        _JitterSpeed  ("Jitter Speed",   Range(0, 30))  = 12.0
        _JitterStrength("Jitter Strength",Range(0, 0.05)) = 0.01

        // UI Mask용
        _StencilComp      ("Stencil Comparison", Float) = 8
        _Stencil          ("Stencil ID",         Float) = 1
        _StencilOp        ("Stencil Operation",  Float) = 2
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

            float _GhostOffset;
            float _GhostAlpha;
            float _ScanSpeed;
            float _ScanWidth;
            float _ScanBend;
            float _JitterSpeed;
            float _JitterStrength;

            float rand(float seed)
            {
                return frac(sin(seed * 127.1) * 43758.5453);
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

                // ── 롤링 스캔라인 2개 ─────────────────────────
                float scanPos1 = frac(_Time.y * _ScanSpeed);
                float scanPos2 = frac(_Time.y * _ScanSpeed + 0.5);

                float dist1 = abs(uv.y - scanPos1);
                float dist2 = abs(uv.y - scanPos2);

                // 뾰족한 삼각파
                float onScan1 = max(0.0, 1.0 - dist1 / _ScanWidth);
                float onScan2 = max(0.0, 1.0 - dist2 / _ScanWidth);
                float onScan  = max(onScan1, onScan2);

                // ── 스캔라인 통과 시 UV 구부러짐 ─────────────
                float bend = sin(uv.y * 30.0 + _Time.y * 5.0)
                             * _ScanBend * onScan;
                uv.x += bend;

                // ── 지직거림 ──────────────────────────────────
                float jitterTime = floor(_Time.y * _JitterSpeed);
                float jitterX = (rand(jitterTime) - 0.5) * 2.0
                                * _JitterStrength;
               float glitchTrigger = rand(jitterTime + 99.0);
               float glitchIntensity = rand(jitterTime + 55.0) * 3.0 + 1.0; // 1~4배 강도
               jitterX *= step(0.55, glitchTrigger) * glitchIntensity;      // 0.85→0.55로 낮춤
                uv.x += jitterX;

                // ── 메인 텍스처 샘플링 ────────────────────────
                fixed4 col = tex2D(_MainTex, uv);

                // ── 고스팅 ────────────────────────────────────
                float2 ghostUV = uv + float2(_GhostOffset, 0.0);
                fixed4 ghost   = tex2D(_MainTex, ghostUV);
                col.rgb = lerp(col.rgb, ghost.rgb, _GhostAlpha * col.a);

                // ── 스캔라인 밝기 ─────────────────────────────
                col.rgb += onScan * 0.15;
                col.rgb -= onScan * 0.08;

                // ── UI Mask 클리핑 ────────────────────────────
                col.a *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                col.a *= i.color.a;

                return col;
            }
            ENDCG
        }
    }
}