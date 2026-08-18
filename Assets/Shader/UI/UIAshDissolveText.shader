// TextMeshPro 텍스트가 아래에서 위로 타들어가며 재가 되는 셰이더.
// TMP의 SDF 폰트 아틀라스를 직접 샘플링하므로 화면 캡처(GrabPass)가 필요 없다 —
// URP에서는 GrabPass를 쓸 수 없어 이 방식이 아니면 UI 텍스트에 디졸브를 넣을 수 없다.
// (ProjectS/UI Glitch Text와 같은 계열이다.)
//
// 연출 개념: 타는 경계선이 아래에서 위로 훑고 지나가며, 지나간 자리는 사라지고
// 경계 바로 위에는 불씨가 남는다.
//   · _Dissolve 0   → 원본 그대로
//   · _Dissolve 1   → 전부 타 없어짐
//   · 경계는 노이즈로 흔들어 직선이 아니게 한다(직선이면 종이가 아니라 셔터가 내려오는 것처럼 보인다)
//
// 세로 진행이므로 글자의 아래/위를 알아야 한다. 정점 좌표만으로는 어디가 밑인지 알 수 없어
// AshDissolveFx가 _LocalMinY·_LocalHeight로 범위를 넣어 준다.
//
// ★ 여러 줄을 태울 때 방향을 너무 세우면 안 된다. 세로로 떨어진 두 줄에 하나의 경계선을 훑게 하면
//   아래 줄이 전부 탄 뒤에 위 줄이 타기 시작해 '한꺼번에'가 아니라 '차례대로'로 보인다.
//   _DirectionWeight를 낮추면 위치보다 노이즈가 순서를 정해 전체가 고루 삭아 없어진다.
Shader "ProjectS/UI Ash Dissolve Text"
{
    Properties
    {
        [HideInInspector] _MainTex ("Font Atlas", 2D) = "white" {}
        _FaceColor ("Face Color", Color) = (1,1,1,1)

        // 연출의 단일 손잡이. 코드가 이 값만 0 → 1로 올린다.
        _Dissolve ("Dissolve", Range(0,1)) = 0

        // ── 타는 경계 ─────────────────────────────────────────────────────
        _NoiseScale ("Noise Scale (px)", Float) = 26      // 클수록 경계가 굵게 일렁인다
        _NoiseAmount ("Noise Amount", Range(0,1)) = 0.35  // 경계가 흔들리는 폭

        // 0이면 방향 없이 전체가 고루 삭아 없어지고, 1이면 아래에서 위로 훑는 선이 된다.
        // 세로로 떨어진 여러 줄을 태울 때 1로 두면 아래 줄이 다 탄 뒤 위 줄이 타 '차례대로'로 보인다.
        _DirectionWeight ("Direction Weight", Range(0,1)) = 0.55
        _EdgeWidth ("Ember Width", Range(0,0.5)) = 0.07   // 불씨 띠의 두께
        _EdgeSoft ("Edge Softness", Range(0.001,0.3)) = 0.03
        [HDR] _EdgeColor ("Ember Color", Color) = (1, 0.45, 0.12, 1)

        // ── 진행 범위(코드가 채운다) ───────────────────────────────────────
        _LocalMinY ("Local Min Y", Float) = -50
        _LocalHeight ("Local Height", Float) = 100

        _Softness ("Glyph Softness", Range(0,1)) = 0.15

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
                float2 local : TEXCOORD1;
            };

            sampler2D _MainTex;
            fixed4 _FaceColor;
            fixed4 _EdgeColor;
            float _Dissolve;
            float _NoiseScale;
            float _NoiseAmount;
            float _DirectionWeight;
            float _EdgeWidth;
            float _EdgeSoft;
            float _LocalMinY;
            float _LocalHeight;
            float _Softness;

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

            // SDF 아틀라스에서 글자 알파를 뽑는다. TMP는 거리값을 알파 채널에 담으므로 0.5를 경계로 자른다.
            // fwidth로 화면 배율에 맞춰 두께를 잡아야 크게 키웠을 때 가장자리가 계단처럼 깨지지 않는다.
            float SampleGlyph(float2 uv)
            {
                float d = tex2D(_MainTex, uv).a;
                float w = max(fwidth(d), 0.0001) * (1.0 + _Softness * 6.0);
                return smoothstep(0.5 - w, 0.5 + w, d);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.local = v.vertex.xy;
                o.color = v.color * _FaceColor;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float glyph = SampleGlyph(i.uv);

                // 시작 전에는 계산을 건너뛴다. 연출이 끝난 뒤에도 셰이더는 붙어 있으므로 비용을 남기지 않는다.
                if (_Dissolve <= 0.001)
                {
                    return fixed4(i.color.rgb, i.color.a * glyph);
                }

                // 0(맨 아래) ~ 1(맨 위).
                float h = saturate((i.local.y - _LocalMinY) / max(0.0001, _LocalHeight));
                float n = valueNoise(i.local / max(1.0, _NoiseScale));

                // 방향 가중치로 '훑는 선'과 '고루 삭음' 사이를 고른다.
                // 1이면 아래에서 위로 올라가는 선, 0이면 위치와 무관하게 노이즈가 정한 순서로 사라진다.
                float field = lerp(n, h + (n - 0.5) * _NoiseAmount, _DirectionWeight);

                // 불씨 띠와 번짐까지 지나가야 완전히 타므로 그만큼 더 밀어 올린다.
                float cut = _Dissolve * (1.0 + _EdgeWidth + _EdgeSoft);

                float alive = smoothstep(cut, cut + _EdgeSoft, field);
                float ember = alive * (1.0 - smoothstep(cut + _EdgeSoft, cut + _EdgeSoft + _EdgeWidth, field));

                float3 rgb = lerp(i.color.rgb, _EdgeColor.rgb, ember);
                float alpha = glyph * i.color.a * alive;

                return fixed4(rgb, alpha);
            }
            ENDCG
        }
    }

    Fallback "TextMeshPro/Distance Field"
}
