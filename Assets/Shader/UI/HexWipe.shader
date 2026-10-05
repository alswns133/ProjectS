// UI/Cyberpunk/HexWipe
// 육각형 셀 단위 와이프 트랜지션 (UGUI Image / RawImage 용, Built-in & URP 공용)
//  - _Progress 0→1 : 화면을 육각 셀로 덮음 (Invert=0) / 덮인 화면을 걷어냄 (Invert=1)
//  - 셀마다 방향 + 랜덤 지연, 셀 중심에서 육각형이 자라나며 경계에 네온 링
//  - 아직 전환되지 않은 셀 앞쪽에 육각 그리드 윤곽이 미리 번쩍이는 프리뷰 효과
Shader "UI/Cyberpunk/HexWipe"
{
    Properties
    {
        [PerRendererData] _MainTex ("Fill Texture", 2D) = "white" {}
        _Color ("Fill Tint", Color) = (0.04, 0.05, 0.12, 1)

        [Header(Wipe)]
        _Progress ("Progress", Range(0, 1)) = 0
        [Toggle] _Invert ("Invert (Reveal Mode)", Float) = 0
        _CellScale ("Cell Count (Vertical)", Float) = 10
        _Aspect ("Aspect (Width / Height)", Float) = 1.7778
        _Direction ("Direction (XY)", Vector) = (1, 0, 0, 0)
        _Randomness ("Randomness", Range(0, 1)) = 0.25
        _Spread ("Spread (Per-Cell Duration)", Range(0.02, 1)) = 0.3
        _FillOverlap ("Fill Overlap", Range(0.5, 0.6)) = 0.53

        [Header(Edge Glow)]
        [HDR] _EdgeColor ("Edge Color", Color) = (0, 2.4, 3, 1)
        _EdgeWidth ("Edge Width", Range(0.002, 0.15)) = 0.035

        [Header(Preview Grid)]
        [HDR] _PreviewColor ("Preview Color", Color) = (0, 0.6, 0.9, 1)
        _PreviewRange ("Preview Range", Range(0, 0.5)) = 0.12
        _PreviewWidth ("Preview Line Width", Range(0.002, 0.1)) = 0.02

        // --- UGUI 공통 (Mask / RectMask2D 호환) ---
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
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
        // 프리멀티플라이드 알파: 채움은 알파 블렌드, 네온 링은 가산 합성
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "HexWipe"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

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

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            float _Progress;
            float _Invert;
            float _CellScale;
            float _Aspect;
            float4 _Direction;
            float _Randomness;
            float _Spread;
            float _FillOverlap;

            float4 _EdgeColor;
            float _EdgeWidth;

            float4 _PreviewColor;
            float _PreviewRange;
            float _PreviewWidth;

            v2f vert (appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            // 음수에서도 안전한 mod
            float2 Mod2(float2 a, float2 b) { return a - b * floor(a / b); }

            // 육각형 거리 (경계 = 0.5)
            float HexDist(float2 p)
            {
                p = abs(p);
                return max(dot(p, normalize(float2(1.0, 1.7320508))), p.x);
            }

            // xy = 셀 내부 로컬 좌표, zw = 셀 ID(중심 좌표)
            float4 HexGrid(float2 p)
            {
                const float2 r = float2(1.0, 1.7320508);
                const float2 h = r * 0.5;
                float2 a = Mod2(p, r) - h;
                float2 b = Mod2(p - h, r) - h;
                float2 gv = dot(a, a) < dot(b, b) ? a : b;
                return float4(gv, p - gv);
            }

            float Hash21(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            fixed4 frag (v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;

                // 1) 육각 그리드 (화면비 보정)
                float2 p = uv * float2(_Aspect, 1.0) * _CellScale;
                float4 hex = HexGrid(p);
                float2 gv = hex.xy;
                float2 id = hex.zw;
                float hd = HexDist(gv);

                // 2) 셀 중심을 0~1 UV로 되돌려 방향 진행값 계산
                float2 c = id / _CellScale;
                c.x /= _Aspect;
                float2 dir = normalize(_Direction.xy + float2(1e-5, 0));
                float extent = 0.5 * (abs(dir.x) + abs(dir.y));
                float d = saturate(dot(c - 0.5, dir) / extent * 0.5 + 0.5);

                // 3) 셀별 시작 시점 (방향 + 랜덤)
                float cellT = lerp(d, Hash21(id), _Randomness);
                float tp = _Progress * (1.0 + _Spread);
                float lp = saturate((tp - cellT) / _Spread);   // 셀 로컬 진행도 0~1
                float active = step(1e-4, lp);

                // 4) 셀 중심에서 육각형이 자라남
                float aa = max(fwidth(hd), 1e-4);
                float fillR = lp * _FillOverlap;
                float inside = (1.0 - smoothstep(fillR - aa, fillR + aa, hd)) * active;
                float mask = lerp(inside, 1.0 - inside, step(0.5, _Invert));

                // 5) 성장 경계의 네온 링 (셀 전환이 끝나갈수록 소멸)
                float ring = 1.0 - smoothstep(0.0, _EdgeWidth, abs(hd - fillR));
                ring *= active * (1.0 - smoothstep(0.8, 1.0, lp));

                // 6) 곧 전환될 셀에 육각 윤곽 프리뷰
                float ahead = cellT - tp;
                float pre = saturate(1.0 - ahead / max(_PreviewRange, 1e-4)) * step(0.0, ahead);
                float outline = 1.0 - smoothstep(0.0, _PreviewWidth, abs(hd - 0.47));
                float preview = pre * pre * outline * (1.0 - active);

                // 7) 합성 (프리멀티플라이드)
                fixed4 tex = (tex2D(_MainTex, uv) + _TextureSampleAdd) * IN.color;
                float fillA = tex.a * mask;
                float3 rgb = tex.rgb * fillA
                           + _EdgeColor.rgb * _EdgeColor.a * ring
                           + _PreviewColor.rgb * _PreviewColor.a * preview;
                float4 col = float4(rgb, fillA);

                #ifdef UNITY_UI_CLIP_RECT
                col *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a + ring + preview - 0.001);
                #endif

                return col;
            }
            ENDCG
        }
    }
}
