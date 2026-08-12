// 스킨드 메시(캐릭터)용 홀로그램 셰이더. URP 전용, Unlit + 알파 블렌드.
//
// 왜 새로 만들었나:
//  - Assets/Shader/Hologram*.shader는 전부 Shader "UI/..." + Stencil + UnityUI.cginc 기반이라
//    Canvas 이미지 전용이다. SkinnedMeshRenderer에는 붙지 않는다.
//  - Synty의 SyntyStudios_Hologram_01은 알베도 경로가 Branch(택일)라
//    "원본 텍스처 색을 유지하면서 홀로그램 색으로 물들이기"가 구조적으로 불가능하다.
//    여기서는 _HoloColor를 텍스처에 곱해 섞으므로 _HoloIntensity로 0~100% 사이를 자유롭게 고른다.
//
// UI판에서 옮기며 바꾼 것 (그대로 옮기면 이 캐릭터에서 깨진다):
//  - 스캔라인 기준을 uv.y가 아니라 오브젝트 공간 Y로 바꿨다. Sidekick 캐릭터의 컬러맵은
//    아주 작은 팔레트 아틀라스라 UV를 조금만 밀어도 전혀 다른 색 칸이 샘플된다.
//  - 같은 이유로 글리치를 UV 이동이 아니라 정점 변위로 처리한다. 가로로 몸이 밀려 찢어지는 연출.
//  - RGB 색수차는 아예 뺐다. 아틀라스 UV에서는 색이 튀기만 하고 색수차로 보이지 않는다.
//  - 대신 3D에서만 가능한 프레넬 림 글로우를 넣었다. 홀로그램 외곽선 느낌의 핵심이다.
//
// ★ 패스가 둘인 이유(깊이 프리패스):
//    반투명은 보통 ZWrite Off로 그리는데, 그러면 겹쳐 있는 자기 몸이 전부 따로 합성된다.
//    특히 머리카락은 얇은 판이 여러 겹이라 층이 누적되면서 알파가 1에 도달하고 색까지 쌓여
//    불투명한 색 덩어리가 된다. 그래서 색을 그리기 전에 깊이만 먼저 채워두고(1패스),
//    컬러 패스는 ZTest Equal로 '가장 앞면 한 겹'만 통과시킨다(2패스).
//    결과적으로 캐릭터 전체가 한 겹짜리 반투명으로 그려진다.
//
// ShadowCaster 패스가 없다 = 그림자를 만들지 않는다(홀로그램이 사람 그림자를 드리우면 안 되므로 의도).
// Fallback도 두지 않았다. 렌더러의 Cast Shadows 설정과 무관하게 그림자가 안 나온다.
Shader "ProjectS/Hologram Character"
{
    Properties
    {
        _MainTex ("Base Map", 2D) = "white" {}

        // ── 홀로그램 색 ─────────────────────────────────
        [HDR] _HoloColor ("Holo Color", Color) = (0.3, 0.85, 1.0, 1.0)
        // 0 = 원본 텍스처 색 그대로, 1 = 완전히 홀로그램 색으로 물듦. 중간값이 목적.
        _HoloIntensity ("Holo Intensity", Range(0, 1)) = 0.45
        // 원본 색에만 곱해지는 밝기 배수. Bloom threshold(이 씬은 0.4)를 넘겨야 빛나 보인다.
        // 1.0을 크게 넘기면 알베도가 흰색으로 클립되어 원본 색이 사라진다. 발광은 림에 맡기는 편이 낫다.
        _EmissionBoost ("Emission Boost", Range(0, 5)) = 1.0
        _Opacity ("Opacity", Range(0, 1)) = 0.8

        // ── 프레넬 림 글로우 (외곽이 빛나는 홀로그램 특유의 느낌) ──
        [HDR] _RimColor ("Rim Color", Color) = (0.4, 0.95, 1.0, 1.0)
        // 클수록 테두리에만 얇게 붙는다. 작을수록 몸 전체로 번진다.
        // 캐릭터는 팔다리가 원통형이라 낮은 값에서 몸 전체가 림에 잠긴다. 3 이하로 내리지 말 것.
        _RimPower ("Rim Falloff", Range(0.5, 8)) = 4.0
        _RimStrength ("Rim Strength", Range(0, 5)) = 1.2

        // ── 스캔라인 (오브젝트 Y 기준의 가로줄) ──────────
        // Count는 "캐릭터 키 1유닛당 줄 개수". 키 1.8짜리 캐릭터면 60이 약 108줄.
        _ScanLineCount ("Scan Line Count", Range(1, 400)) = 60
        _ScanLineWidth ("Scan Line Width", Range(0, 1)) = 0.5
        _ScanLineSoft ("Scan Line Soft", Range(0, 0.5)) = 0.05
        _ScanStrength ("Scan Strength", Range(0, 1)) = 0.45
        _ScanRollSpeed ("Scan Roll Speed", Range(-5, 5)) = 0.6

        // ── 글리치 (몇 초에 한 번 '툭' 터지는 버스트) ────
        _GlitchInterval ("Glitch Interval (sec)", Range(0.2, 10)) = 3.0
        _GlitchDuration ("Glitch Duration (sec)", Range(0.02, 1)) = 0.12
        _GlitchSpeed ("Glitch Tear Speed", Range(1, 60)) = 25.0
        // 오브젝트 공간 단위(≈미터)로 몸이 옆으로 밀리는 양.
        _GlitchStrength ("Glitch Strength", Range(0, 0.3)) = 0.03
        _GlitchThreshold ("Glitch Threshold", Range(0, 1)) = 0.7

        // ── 깜빡임 ──────────────────────────────────────
        _FlickerSpeed ("Flicker Speed", Range(0, 30)) = 10.0
        _FlickerStrength ("Flicker Strength", Range(0, 1)) = 0.15

        // 깊이 프리패스가 있어 뒷면이 비치는 문제는 해결됐다. 그래도 "속이 비쳐 보이는"
        // 연출을 원하면 Off로 바꾼다(대신 겹침 정렬이 다시 지저분해진다).
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        // 1패스: 색은 안 쓰고 깊이만 채운다. 이게 있어야 아래 컬러 패스가
        // 겹친 면 중 가장 앞 한 겹만 그린다(머리카락이 덩어리지는 문제의 해결책).
        Pass
        {
            Name "HologramDepthPrepass"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "HologramCharacter.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                // 컬러 패스와 완전히 같은 변위를 써야 깊이가 어긋나지 않는다.
                float3 posOS = ApplyGlitch(IN.positionOS.xyz);
                OUT.positionCS = GetVertexPositionInputs(posOS).positionCS;

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // 2패스: 실제 색. 프리패스가 써둔 깊이와 같은 면만 통과시킨다.
        Pass
        {
            Name "HologramForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Equal
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "HologramCharacter.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionOS : TEXCOORD1;   // 스캔라인 기준(UV가 아니라 오브젝트 Y를 쓴다)
                float3 normalWS   : TEXCOORD2;
                float3 viewDirWS  : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 posOS = ApplyGlitch(IN.positionOS.xyz);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(posOS);
                VertexNormalInputs   normalInputs   = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = positionInputs.positionCS;
                OUT.uv         = TRANSFORM_TEX(IN.uv, _MainTex);
                // 스캔라인은 밀리기 전 위치를 기준으로 삼는다. 글리치로 몸이 밀려도
                // 줄무늬가 같이 흔들리지 않아야 "화면이 찢어진" 느낌이 산다.
                OUT.positionOS = IN.positionOS.xyz;
                OUT.normalWS   = normalInputs.normalWS;
                OUT.viewDirWS  = _WorldSpaceCameraPos - positionInputs.positionWS;

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half4 baseMap = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);

                // 핵심: 원본 색과 "원본 x 홀로그램 색" 사이를 섞는다.
                // Synty 셰이더가 Branch(택일)라 못 하던 부분이 바로 이 lerp다.
                half3 tinted = lerp(baseMap.rgb, baseMap.rgb * _HoloColor.rgb, _HoloIntensity);

                // 스캔라인: 오브젝트 Y를 셀로 쪼개고 셀 중앙(0.5)에 어두운 줄을 박는다.
                float coord    = IN.positionOS.y * _ScanLineCount + _Time.y * _ScanRollSpeed;
                float cell     = frac(coord);
                float dist     = abs(cell - 0.5);
                float halfW    = _ScanLineWidth * 0.5;
                float scanLine = 1.0 - smoothstep(halfW, halfW + _ScanLineSoft, dist);

                tinted *= 1.0 - scanLine * _ScanStrength;

                // 프레넬 림: 시선과 법선이 수직에 가까울수록(=외곽) 1에 가깝다.
                float3 normalWS  = normalize(IN.normalWS);
                float3 viewDirWS = normalize(IN.viewDirWS);
                float  fresnel   = pow(saturate(1.0 - saturate(dot(normalWS, viewDirWS))), _RimPower);
                float  rim       = fresnel * _RimStrength;

                // ★ 순서가 중요하다. 부스트는 '원본 색'에만 곱하고, 림은 그 위에 따로 얹는다.
                // 예전처럼 (원본 + 림) x 부스트로 묶으면 림까지 증폭돼 1.0을 넘겨 흰색으로 클립되고,
                // 캐릭터는 팔다리가 원통형이라 프레넬이 외곽선뿐 아니라 몸 전체에 넓게 걸리기 때문에
                // 결국 알베도가 통째로 흰빛에 묻힌다(부스트를 올려도 내려도 원본이 안 살아나던 이유).
                // Bloom(이 씬은 threshold 0.4 / intensity 2)이 물어야 발광으로 읽힌다.
                half3 color = tinted * _EmissionBoost;

                color += _RimColor.rgb * rim;

                // 깜빡임: 대부분 안정적이고 가끔만 확 튀도록 step으로 문턱을 둔다.
                float flickerTime = floor(_Time.y * _FlickerSpeed);
                float flicker     = 1.0 - _FlickerStrength * step(0.85, Rand(float2(flickerTime, 0.0)));

                // 외곽(rim)은 더 불투명하게 해야 실루엣이 배경에 묻히지 않는다.
                // 이 씬처럼 배경이 흰 방일 때 형태를 잡아주는 것이 이 항이다.
                half alpha = saturate(_Opacity + rim * 0.5) * baseMap.a * flicker;

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
