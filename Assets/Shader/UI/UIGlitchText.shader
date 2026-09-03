// TextMeshPro 텍스트용 글리치 셰이더. TMP의 SDF 폰트 아틀라스를 직접 샘플링하므로
// 화면 캡처(GrabPass)가 필요 없다 — URP에서는 GrabPass를 쓸 수 없어 이 방식이 아니면
// UI 텍스트에 지지직 효과를 넣을 수 없다.
//
// 연출 컨셉: 글자가 날아다니는 것이 아니라, 제자리에서 잘게 부서진 파편이 흩어져 있다가
// 점점 제자리를 찾아 글자로 뭉치는 것. 그래서 본체의 정점(버텍스)은 건드리지 않는다 —
// 정점을 옮기면 단어가 통째로 튀고 쿼드가 기울어져(전단) '파편이 모인다'는 인상이 사라진다.
//
// 부서짐은 세 겹이다.
//   · 셀       : 화면을 잘게 나눈 사각 격자. 살아남은 셀만 그려 '가루가 된' 인상을 만든다.
//   · 슬라이스 : 가로로 긴 띠. 띠 단위로 좌우로 밀려 '신호가 어긋난' 인상을 만든다.
//   · 고스트   : 글자 사본을 좌우로 밀어 빨강/시안으로 얹는 색수차(아래 설명).
//   글리치가 0으로 가면 셋 다 잦아들고 온전한 글자만 남는다.
//
// ── 고스트(좌 빨강 / 우 시안)를 왜 별도 Pass로 그리는가 ────────────────────────
// _RgbSplit은 '폰트 아틀라스 UV'를 밀어서 색수차를 낸다. 아틀라스에는 모든 글자가 격자로
// 담겨 있어서, 글리프 패딩(보통 글자 크기의 5% 남짓)을 넘겨 밀면 옆 칸의 다른 글자를 끌어온다.
// 그래서 _RgbSplit은 0.01에 묶여 있고, 그 이상으로 뚜렷한 그림자는 낼 수 없다.
//
// 뚜렷한 그림자는 아틀라스를 미는 대신 '쿼드 자체'를 밀어서 만든다. 정점을 옮기면 UV도 함께
// 따라가므로 아무리 멀리 밀어도 남의 글자를 끌어올 일이 없다. 그래서 고스트만 Pass를 따로 둔다.
//   Pass 1 GhostL — 왼쪽으로 민 사본, 빨강, 가산 합성
//   Pass 2 GhostR — 오른쪽으로 민 사본, 시안, 가산 합성
//   Pass 3 Face   — 원래 자리의 글자. 알파 합성이라 가운데를 덮고 가장자리에만 색이 남는다
// 가산 합성이라 '어두운 배경'을 전제로 한다. 밝은 배경에서는 고스트가 묻혀 거의 안 보인다.
//
// ★ 고스트 관련 값은 전부 기본 0이다(꺼짐). 이 셰이더는 여러 씬에서 이미 쓰이고 있어서,
//   기본값을 켜면 남의 씬 연출까지 바뀐다. 켜는 것은 GlitchTextFx 인스펙터에서 텍스트마다 개별로 한다.
//
// 사용법: 텍스트에 GlitchTextFx를 붙이고 Glitch Shader 슬롯에 이 셰이더를 넣는다
//         (컴포넌트가 폰트 머티리얼 인스턴스의 셰이더를 런타임에 교체한다).
//
// 한계: TMP의 외곽선·언더레이·마스킹(_ClipRect)은 지원하지 않는다. 면(face)만 그린다.
// (2026-09-02 TH: 슬라이스 · 고스트 Pass 추가)
Shader "ProjectS/UI Glitch Text"
{
    Properties
    {
        [HideInInspector] _MainTex ("Font Atlas", 2D) = "white" {}
        _FaceColor ("Face Color", Color) = (1,1,1,1)

        // 연출의 단일 손잡이. 1이면 파편만 흩어져 글자를 알아볼 수 없고, 0이면 원본 그대로다.
        // 코드(GlitchTextFx)가 이 값만 1 → 0으로 떨어뜨린다.
        _Glitch ("Glitch", Range(0,1)) = 1

        // ── 파편(셀) ──────────────────────────────────────────────────────
        _CellSize ("Cell Size (px)", Float) = 9          // 파편 한 조각의 크기. 작을수록 잘게 부서진다
        _Scatter ("Scatter", Range(0,1)) = 0.85          // 글리치 1일 때 사라지는 셀의 비율
        _CellOffset ("Cell Offset", Float) = 0.006       // 살아남은 셀이 어긋나는 정도(아틀라스 UV)

        // ★ UV 계열은 '폰트 아틀라스' 기준이라 아주 작아야 한다. 아틀라스에는 모든 글자가 격자로
        //   담겨 있어서 0.05만 밀어도 옆 칸의 다른 글자를 끌어온다(알록달록한 남의 글자 조각이 된다).
        //   0.01을 넘기지 않는다.
        _RgbSplit ("RGB Split", Float) = 0.0022          // 색수차

        _FlickerSpeed ("Flicker Speed", Float) = 18      // 초당 파편 재배치 횟수
        _Scanline ("Scanline", Range(0,1)) = 0.25        // 가로 주사선 농도
        _Softness ("Edge Softness", Range(0,1)) = 0.15

        // ── 슬라이스(가로 띠 어긋남) ───────────────────────────────────────
        // 셀이 '가루'라면 슬라이스는 '어긋난 띠'다. 참고 이미지의 계단처럼 밀린 가로줄이 이것.
        _SliceHeight ("Slice Height (px)", Float) = 14   // 띠 하나의 높이(캔버스 단위)
        _SliceAmount ("Slice Amount", Range(0,1)) = 0    // 글리치 1일 때 밀려나는 띠의 비율. 0 = 끄기
        // 밀리는 거리(캔버스 단위). 이것도 결국 아틀라스 UV를 미는 것이라, 크게 주면 위 ★와 같은
        // 이유로 남의 글자가 묻어 나온다. 8을 넘기지 않는 것을 권장한다.
        _SliceOffset ("Slice Offset (px)", Range(0,16)) = 4

        // ── 고스트(좌 빨강 / 우 시안) ──────────────────────────────────────
        // 0이면 Pass가 픽셀을 하나도 안 쓴다. 켜는 값은 _GhostOffset 하나다.
        _GhostOffset ("Ghost Offset (px)", Float) = 0    // 사본이 좌우로 밀리는 기본 거리
        _GhostJitter ("Ghost Jitter (px)", Float) = 0    // 프레임마다 덜컥거리는 폭
        // 글리치가 0으로 잡힌 뒤에도 남는 비율. 참고 이미지처럼 '항상 갈라져 있는' 정지 화면을
        // 원하면 1에 가깝게, 연출이 끝나면 깔끔해지길 원하면 0으로 둔다.
        _GhostIdle ("Ghost Idle", Range(0,1)) = 0.35
        [HDR] _GhostColorL ("Ghost Color (Left)", Color) = (1, 0.06, 0.22, 1)
        [HDR] _GhostColorR ("Ghost Color (Right)", Color) = (0.1, 0.92, 1, 1)

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

        // 세 Pass가 완전히 같은 부서짐 계산을 공유한다. CGINCLUDE는 각 Pass의 CGPROGRAM 앞에
        // 그대로 붙으므로, 여기에 진입점을 전부 정의해 두고 Pass는 #pragma로 고르기만 한다
        // (같은 코드를 세 번 적지 않기 위함. 안 쓰는 함수는 컴파일 때 걸러진다).
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
            float2 local : TEXCOORD1;   // 오브젝트 로컬 좌표. 파편 격자를 글자와 무관하게 깔기 위함
            UNITY_VERTEX_OUTPUT_STEREO
        };

        sampler2D _MainTex;
        float4 _MainTex_ST;
        fixed4 _FaceColor;

        float _Glitch;
        float _CellSize;
        float _Scatter;
        float _CellOffset;
        float _RgbSplit;
        float _FlickerSpeed;
        float _Scanline;
        float _Softness;

        float _SliceHeight;
        float _SliceAmount;
        float _SliceOffset;

        float _GhostOffset;
        float _GhostJitter;
        float _GhostIdle;
        fixed4 _GhostColorL;
        fixed4 _GhostColorR;

        // 2D 좌표 → 0~1 난수. 셀마다 다른 값을 뽑되 프레임 사이에는 유지돼야 해서
        // (매 프레임 완전 난수면 파편이 아니라 흰 노이즈로 보인다) 시간은 계단으로 끊어 넣는다.
        float hash21(float2 p)
        {
            float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.x + p3.y) * p3.z);
        }

        // 고스트 사본이 밀려나는 거리(캔버스 단위). side는 -1(왼쪽) / +1(오른쪽) / 0(본체).
        // 좌우가 서로 다른 난수를 쓰기 때문에 두 사본이 대칭으로 붙어 다니지 않고 따로 논다.
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

            // 격자는 밀기 '전' 좌표로 깐다. 그래야 고스트가 격자 위를 미끄러지며 지나가고,
            // 사본마다 부서지는 자리가 통째로 따라 움직이지 않는다.
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

        // SDF 아틀라스에서 글자 알파를 뽑는다. TMP는 거리값을 알파 채널에 담으므로
        // 0.5를 경계로 부드럽게 자른다. fwidth로 화면 배율에 맞춰 두께를 잡아야
        // 크게 키웠을 때 가장자리가 계단처럼 깨지지 않는다.
        float SampleGlyph(float2 uv)
        {
            float d = tex2D(_MainTex, uv).a;
            float w = max(fwidth(d), 0.0001) * (1.0 + _Softness * 6.0);
            return smoothstep(0.5 - w, 0.5 + w, d);
        }

        // 캔버스 1단위를 밀려면 아틀라스 UV를 얼마나 밀어야 하는가.
        // 화면 해상도·폰트 크기·캔버스 배율이 달라도 인스펙터의 px 값이 같은 두께로 보이게 하는 환산이다
        // (두 미분 모두 화면 공간 기준이라 나누면 배율이 상쇄된다).
        float UvPerLocalX(v2f i)
        {
            float dLocal = ddx(i.local.x);
            float safe = (abs(dLocal) < 1e-6) ? 1e-6 : dLocal;
            return ddx(i.uv.x) / safe;
        }

        // 세 Pass가 공유하는 부서짐. 반환값 x=글자 알파, y=색수차 좌측, z=색수차 우측.
        // seed로 사본마다 다른 난수를 먹여 본체와 고스트가 서로 다른 자리에서 부서지게 한다.
        float3 GlitchAlpha(v2f i, float g, float seed)
        {
            float t = floor(_Time.y * max(_FlickerSpeed, 0.0001));
            float2 uv = i.uv;

            // ddx는 분기 안에 두지 않는다. 그래디언트 명령은 흐름이 갈린 곳에서 값이 보장되지 않아,
            // 조건이 유니폼이어도 컴파일러에 따라 경고·오작동이 난다. 밖에서 한 번 구해 쓴다.
            float uvPerLocalX = UvPerLocalX(i);

            // [슬라이스] 가로로 긴 띠 단위로 좌우로 민다. 셀보다 크고 성긴 어긋남이라
            // '신호가 끊겨 줄이 밀렸다'는 인상을 만든다. 꺼져 있으면(_SliceAmount=0) 곱해서 0이 된다.
            float slice = floor(i.local.y / max(1.0, _SliceHeight));
            float sliceAlive = step(1.0 - _SliceAmount * g, hash21(float2(slice, t) + seed));
            float sliceShift = (hash21(float2(slice * 3.13, t * 1.7) + seed) - 0.5) * 2.0;
            uv.x += sliceShift * _SliceOffset * g * sliceAlive * uvPerLocalX;

            // [셀] 파편 격자. 글자가 아니라 화면(로컬 좌표)에 격자를 깔아야, 여러 글자에 걸쳐
            // 같은 크기의 조각으로 부서진다. 글자마다 격자가 달라지면 크기가 들쭉날쭉해 보인다.
            float2 cell = floor(i.local / max(1.0, _CellSize));

            float rAlive = hash21(cell + t * 0.137 + seed);
            float rOffX  = hash21(cell * 1.7 + t * 0.311 + seed);
            float rOffY  = hash21(cell * 2.3 + t * 0.577 + seed);

            // 살아 있는 셀 비율. 글리치 1에서 대부분이 꺼져 파편만 남고, 0으로 갈수록 전부 켜진다.
            // 이 감쇠가 곧 "파편이 모여 글자가 된다"의 정체다.
            float alive = step(_Scatter * g, rAlive);

            // 살아남은 조각도 제자리가 아니다. 어긋남이 줄어들면서 글자 모양이 맞아 들어간다.
            uv += (float2(rOffX, rOffY) - 0.5) * 2.0 * _CellOffset * g;

            // 색수차. 조각 단위로 세기를 달리해 균일하지 않게 만든다.
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

            // 글리치가 0이면 계산을 통째로 건너뛴다. 연출이 끝난 뒤에도 셰이더는 계속 붙어 있으므로
            // 꺼진 상태에서 비용을 남기지 않기 위함이다.
            if (g <= 0.001)
            {
                float a0 = SampleGlyph(i.uv);
                return fixed4(i.color.rgb, i.color.a * a0);
            }

            float3 a = GlitchAlpha(i, g, 0.0);

            float3 rgb = i.color.rgb * float3(a.y, a.x, a.z);
            float alpha = max(max(a.x, a.y), a.z) * i.color.a;

            // 주사선. 로컬 Y 기준이라 파편 격자와 어긋나 화면 신호처럼 겹쳐 보인다.
            float scan = 1.0 - _Scanline * g * step(0.5, frac(i.local.y * 0.25));
            rgb *= scan;

            return fixed4(rgb, alpha);
        }

        // ── 고스트 ────────────────────────────────────────────────────────
        // 가산 합성이라 알파는 쓰지 않는다(0을 돌려줘 대상 알파를 건드리지 않는다).
        // 글리치가 0이어도 _GhostIdle이 남아 있으면 계속 갈라진 채로 있는다.
        fixed4 fragGhost(v2f i, fixed4 tint, float seed)
        {
            // 꺼져 있으면 픽셀을 아예 버린다. Pass 자체를 건너뛸 수는 없으니 여기서 끊는다.
            if (_GhostOffset <= 0.0001 && _GhostJitter <= 0.0001) discard;

            float g = saturate(_Glitch);
            float a = (g <= 0.001) ? SampleGlyph(i.uv) : GlitchAlpha(i, g, seed).x;

            clip(a - 0.003);
            return fixed4(tint.rgb * tint.a * a * i.color.a, 0);
        }

        fixed4 fragGhostL (v2f i) : SV_Target { return fragGhost(i, _GhostColorL, 11.0); }
        fixed4 fragGhostR (v2f i) : SV_Target { return fragGhost(i, _GhostColorR, 29.0); }
        ENDCG

        // 순서가 곧 겹치는 순서다. 고스트를 먼저 깔고 그 위에 본체를 덮어야
        // 가운데는 원래 글자색이고 삐져나온 가장자리에만 빨강/시안이 남는다.
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
