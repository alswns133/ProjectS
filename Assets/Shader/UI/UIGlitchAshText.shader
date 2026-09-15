// TextMeshPro 텍스트용 "글리치 + 재 디졸브" 병합 셰이더. 보스 등장 BOSS 텍스트 전용.
//
// ── 왜 병합하는가 ────────────────────────────────────────────────────────
// TMP 텍스트 하나는 머티리얼 하나로 그려지고, 머티리얼은 셰이더를 하나만 가진다.
// BOSS 텍스트는 등장할 때 지지직거리고(ProjectS/UI Glitch Text), 물러날 때 재로 타야(ProjectS/UI Ash Dissolve Text) 하는데
// 두 컴포넌트가 같은 폰트 머티리얼 인스턴스의 셰이더를 서로 갈아끼우면 나중에 바꾼 쪽만 남는다.
// 그래서 두 효과를 한 셰이더에 담았다. 원본 두 셰이더는 다른 화면(사망 팝업·파티창 등)이 쓰고 있어 그대로 둔다.
//
// ── 구성 ────────────────────────────────────────────────────────────────
//   · 글리치 계산(셀·슬라이스·고스트)은 ProjectS/UI Glitch Text와 같다. 설명은 그 파일 머리를 본다.
//   · 재 디졸브 계산은 ProjectS/UI Ash Dissolve Text와 같다. 설명은 그 파일 머리를 본다.
//   · 재는 글리치 "뒤에" 곱한다. 타 없어진 자리는 파편·고스트까지 함께 사라져야 한다
//     — 고스트에 재를 안 곱하면 글자가 다 탄 뒤에도 빨강/시안 사본이 허공에 남는다.
//   · 재 경계는 밀기 전 좌표(local)로 잰다. 고스트가 좌우로 밀려도 같은 자리에서 함께 탄다.
//
// ── 기본값 ──────────────────────────────────────────────────────────────
// _Glitch·_Dissolve 모두 기본 0이다. 셰이더만 바뀌고 값을 아무도 안 넣으면 멀쩡한 글자로 보이게 하기 위함이다.
//
// 사용법: AshDissolveFx의 Dissolve Shader 슬롯에 이 셰이더를 넣는다(재 값은 AshDissolveFx가 넣는다).
//         글리치 세기는 BossIntroFx가 넣고, 모양 값은 BossIntroFx의 Text Glitch Look 머티리얼에서 복사한다.
//
// 한계: TMP의 외곽선·언더레이·마스킹(_ClipRect)은 지원하지 않는다(원본 두 셰이더와 같다).
// (2026-09-15 TH)
Shader "ProjectS/UI Glitch Ash Text"
{
    Properties
    {
        [HideInInspector] _MainTex ("Font Atlas", 2D) = "white" {}
        _FaceColor ("Face Color", Color) = (1,1,1,1)
        _Softness ("Glyph Softness", Range(0,1)) = 0.15

        // ── 글리치 ──────────────────────────────────────────────────────
        _Glitch ("Glitch", Range(0,1)) = 0

        _CellSize ("Cell Size (px)", Float) = 9
        _CellAspect ("Cell Aspect (W/H)", Range(0.25, 8)) = 1
        _RowWidthJitter ("Row Width Jitter", Range(0, 1)) = 0
        _Scatter ("Scatter", Range(0,1)) = 0.85
        _CellOffset ("Cell Offset", Float) = 0.006
        _RgbSplit ("RGB Split", Float) = 0.0022
        _FlickerSpeed ("Flicker Speed", Float) = 18
        _Scanline ("Scanline", Range(0,1)) = 0.25

        _SliceHeight ("Slice Height (px)", Float) = 14
        _SliceAmount ("Slice Amount", Range(0,1)) = 0
        _SliceOffset ("Slice Offset (px)", Range(0,16)) = 4

        _GhostOffset ("Ghost Offset (px)", Float) = 0
        _GhostJitter ("Ghost Jitter (px)", Float) = 0
        _GhostIdle ("Ghost Idle", Range(0,1)) = 0.35
        [HDR] _GhostColorL ("Ghost Color (Left)", Color) = (1, 0.06, 0.22, 1)
        [HDR] _GhostColorR ("Ghost Color (Right)", Color) = (0.1, 0.92, 1, 1)

        // ── 재 디졸브 ────────────────────────────────────────────────────
        _Dissolve ("Dissolve", Range(0,1)) = 0
        _NoiseScale ("Noise Scale (px)", Float) = 26
        _NoiseAmount ("Noise Amount", Range(0,1)) = 0.35
        _DirectionWeight ("Direction Weight", Range(0,1)) = 0.55
        _EdgeWidth ("Ember Width", Range(0,0.5)) = 0.07
        _EdgeSoft ("Edge Softness", Range(0.001,0.3)) = 0.03
        [HDR] _EdgeColor ("Ember Color", Color) = (1, 0.45, 0.12, 1)
        _LocalMinY ("Local Min Y", Float) = -50
        _LocalHeight ("Local Height", Float) = 100

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
        ColorMask [_ColorMask]

        CGINCLUDE
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
            float2 local : TEXCOORD1;   // 밀기 전 로컬 좌표. 파편 격자와 재 경계를 모두 이 좌표로 잰다
            UNITY_VERTEX_OUTPUT_STEREO
        };

        sampler2D _MainTex;
        float4 _MainTex_ST;
        fixed4 _FaceColor;
        float _Softness;

        float _Glitch;
        float _CellSize;
        float _CellAspect;
        float _RowWidthJitter;
        float _Scatter;
        float _CellOffset;
        float _RgbSplit;
        float _FlickerSpeed;
        float _Scanline;

        float _SliceHeight;
        float _SliceAmount;
        float _SliceOffset;

        float _GhostOffset;
        float _GhostJitter;
        float _GhostIdle;
        fixed4 _GhostColorL;
        fixed4 _GhostColorR;

        float _Dissolve;
        float _NoiseScale;
        float _NoiseAmount;
        float _DirectionWeight;
        float _EdgeWidth;
        float _EdgeSoft;
        fixed4 _EdgeColor;
        float _LocalMinY;
        float _LocalHeight;

        float hash21(float2 p)
        {
            float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.x + p3.y) * p3.z);
        }

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

        // ── 재 ────────────────────────────────────────────────────────────
        // x = 살아 있는 정도(0이면 탔음), y = 불씨 띠 가중치. ProjectS/UI Ash Dissolve Text와 같은 계산이다.
        float2 AshMask(float2 local)
        {
            // 타기 전에는 계산을 건너뛴다. 등장·머묾 구간 내내 이 경로다.
            if (_Dissolve <= 0.001) return float2(1.0, 0.0);

            float h = saturate((local.y - _LocalMinY) / max(0.0001, _LocalHeight));
            float n = valueNoise(local / max(1.0, _NoiseScale));
            float field = lerp(n, h + (n - 0.5) * _NoiseAmount, _DirectionWeight);

            float cut = _Dissolve * (1.0 + _EdgeWidth + _EdgeSoft);
            float alive = smoothstep(cut, cut + _EdgeSoft, field);
            float ember = alive * (1.0 - smoothstep(cut + _EdgeSoft, cut + _EdgeSoft + _EdgeWidth, field));
            return float2(alive, ember);
        }

        // ── 글리치 ─────────────────────────────────────────────────────────
        float GhostShift(float side)
        {
            if (side == 0.0) return 0.0;

            float amount = lerp(_GhostIdle, 1.0, saturate(_Glitch));
            float t = floor(_Time.y * max(_FlickerSpeed, 0.0001));
            float r = hash21(float2(side * 7.31, t));
            return side * (_GhostOffset + (r - 0.5) * 2.0 * _GhostJitter) * amount;
        }

        v2f vertCore(appdata v, float side)
        {
            v2f o;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

            o.local = v.vertex.xy;

            float4 shifted = v.vertex;
            shifted.x += GhostShift(side);

            o.pos = UnityObjectToClipPos(shifted);
            o.color = v.color * _FaceColor;
            o.uv = TRANSFORM_TEX(v.uv, _MainTex);
            return o;
        }

        v2f vert (appdata v)       { return vertCore(v,  0.0); }
        v2f vertGhostL (appdata v) { return vertCore(v, -1.0); }
        v2f vertGhostR (appdata v) { return vertCore(v,  1.0); }

        float SampleGlyph(float2 uv)
        {
            float d = tex2D(_MainTex, uv).a;
            float w = max(fwidth(d), 0.0001) * (1.0 + _Softness * 6.0);
            return smoothstep(0.5 - w, 0.5 + w, d);
        }

        float UvPerLocalX(v2f i)
        {
            float dLocal = ddx(i.local.x);
            float safe = (abs(dLocal) < 1e-6) ? 1e-6 : dLocal;
            return ddx(i.uv.x) / safe;
        }

        // 반환값 x=글자 알파, y=색수차 좌측, z=색수차 우측.
        float3 GlitchAlpha(v2f i, float g, float seed)
        {
            float t = floor(_Time.y * max(_FlickerSpeed, 0.0001));
            float2 uv = i.uv;

            float uvPerLocalX = UvPerLocalX(i);

            float slice = floor(i.local.y / max(1.0, _SliceHeight));
            float sliceAlive = step(1.0 - _SliceAmount * g, hash21(float2(slice, t) + seed));
            float sliceShift = (hash21(float2(slice * 3.13, t * 1.7) + seed) - 0.5) * 2.0;
            uv.x += sliceShift * _SliceOffset * g * sliceAlive * uvPerLocalX;

            float cellH = max(1.0, _CellSize);
            float row = floor(i.local.y / cellH);

            float rRow = hash21(float2(row * 5.17, t * 0.73) + seed);
            float widthScale = pow(4.0, (rRow * 2.0 - 1.0) * _RowWidthJitter);
            float cellW = max(1.0, _CellSize * max(0.01, _CellAspect) * widthScale);

            float rowPhase = (_RowWidthJitter > 0.0001) ? hash21(float2(row * 9.41, t * 0.29) + seed) * cellW : 0.0;

            float2 cell = float2(floor((i.local.x + rowPhase) / cellW), row);

            float rAlive = hash21(cell + t * 0.137 + seed);
            float rOffX  = hash21(cell * 1.7 + t * 0.311 + seed);
            float rOffY  = hash21(cell * 2.3 + t * 0.577 + seed);

            float alive = step(_Scatter * g, rAlive);

            uv += (float2(rOffX, rOffY) - 0.5) * 2.0 * _CellOffset * g;

            float split = _RgbSplit * g * (0.5 + rOffX * 0.5);
            float aR = SampleGlyph(uv + float2(split, 0));
            float aG = SampleGlyph(uv);
            float aB = SampleGlyph(uv - float2(split, 0));

            return float3(aG, aR, aB) * alive;
        }

        // ── 본체 ──────────────────────────────────────────────────────────
        fixed4 frag (v2f i) : SV_Target
        {
            float g = saturate(_Glitch);
            float2 ash = AshMask(i.local);

            float3 rgb;
            float alpha;

            if (g <= 0.001)
            {
                rgb = i.color.rgb;
                alpha = i.color.a * SampleGlyph(i.uv);
            }
            else
            {
                float3 a = GlitchAlpha(i, g, 0.0);
                rgb = i.color.rgb * float3(a.y, a.x, a.z);
                alpha = max(max(a.x, a.y), a.z) * i.color.a;

                float scan = 1.0 - _Scanline * g * step(0.5, frac(i.local.y * 0.25));
                rgb *= scan;
            }

            // 불씨 색은 글리치로 갈라진 색 위에 덮는다. 타는 경계는 신호 손상과 무관하게 불빛이어야 한다.
            rgb = lerp(rgb, _EdgeColor.rgb, ash.y);
            return fixed4(rgb, alpha * ash.x);
        }

        // ── 고스트 ────────────────────────────────────────────────────────
        fixed4 fragGhost(v2f i, fixed4 tint, float seed)
        {
            if (_GhostOffset <= 0.0001 && _GhostJitter <= 0.0001) discard;

            float g = saturate(_Glitch);
            float a = (g <= 0.001) ? SampleGlyph(i.uv) : GlitchAlpha(i, g, seed).x;

            // 탄 자리의 고스트도 함께 지운다(파일 머리 설명).
            a *= AshMask(i.local).x;

            clip(a - 0.003);
            return fixed4(tint.rgb * tint.a * a * i.color.a, 0);
        }

        fixed4 fragGhostL (v2f i) : SV_Target { return fragGhost(i, _GhostColorL, 11.0); }
        fixed4 fragGhostR (v2f i) : SV_Target { return fragGhost(i, _GhostColorR, 29.0); }
        ENDCG

        Pass
        {
            Name "GhostL"
            Blend One One
            CGPROGRAM
            #pragma vertex vertGhostL
            #pragma fragment fragGhostL
            ENDCG
        }

        Pass
        {
            Name "GhostR"
            Blend One One
            CGPROGRAM
            #pragma vertex vertGhostR
            #pragma fragment fragGhostR
            ENDCG
        }

        Pass
        {
            Name "Face"
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
    }

    Fallback "TextMeshPro/Distance Field"
}
