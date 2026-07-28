Shader "Custom/ElectricMembrane"
{
    // ===================================================================
    // 전기 아크가 지지직거리는 에너지 막 셰이더 (URP)
    //
    // TV 노이즈(잔알갱이)가 아니라, 가늘고 밝은 전기 줄기가 표면을 기어다니는 방식.
    // Ridged Noise(능선 노이즈)로 얇은 실선을 만들고, 도메인 워핑으로 꿈틀거리게 함.
    //
    // 구성 요소
    // - Arc        : 전기 줄기. 두 겹으로 겹쳐서 복잡한 방전 패턴을 만듦
    // - Surge      : 불규칙하게 확 밝아지는 방전 순간
    // - Polar Mode : 켜면 아크가 중심에서 바깥으로 뻗음 (원형 포탈에 적합)
    // - Fresnel    : 가장자리 테두리 발광
    //
    // 적용 대상: 게이트 링 안쪽 Quad 또는 원판 메시
    // ===================================================================

    Properties
    {
        [Header(Main)]
        _BaseColor("Base Color (막 바탕색)", Color) = (0.05, 0.1, 0.3, 1)
        _ArcColor("Arc Color (전기 줄기 색)", Color) = (0.4, 0.9, 1, 1)
        _BaseAlpha("Base Alpha (바탕 투명도)", Range(0, 1)) = 0.25
        _Intensity("Intensity (전체 밝기)", Range(0, 10)) = 3

        [Header(Electric Arc)]
        _ArcScale("Arc Scale (줄기 밀도)", Float) = 4
        _ArcSharpness("Arc Sharpness (줄기 가늘기, 클수록 가늠)", Range(1, 40)) = 14
        _ArcSpeed("Arc Speed (꿈틀거리는 속도)", Float) = 1.5
        _ArcWarp("Arc Warp (줄기 구부러짐 정도)", Range(0, 2)) = 0.6
        _ArcLayer2Scale("Arc Layer2 Scale (두번째 겹 밀도)", Float) = 9
        _ArcLayer2Weight("Arc Layer2 Weight (두번째 겹 비중)", Range(0, 1)) = 0.5

        [Header(Surge Flash)]
        _SurgeSpeed("Surge Speed (방전 빈도)", Float) = 7
        _SurgeThreshold("Surge Threshold (높을수록 드물게)", Range(0, 1)) = 0.82
        _SurgeBoost("Surge Boost (방전시 밝기 증폭)", Range(1, 8)) = 3

        [Header(Polar Mode)]
        [Toggle] _PolarMode("Polar Mode (중심에서 방사형으로)", Float) = 1
        _PolarSpin("Polar Spin (회전 속도)", Float) = 0.15

        [Header(Edge Glow)]
        _FresnelPower("Fresnel Power (테두리 두께)", Range(0.5, 8)) = 2
        _FresnelIntensity("Fresnel Intensity (테두리 밝기)", Range(0, 5)) = 1.5
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Transparent"
        }
        LOD 100

        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha One      // Additive. 은은하게 하려면 SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _ArcColor;
                half _BaseAlpha;
                half _Intensity;

                half _ArcScale;
                half _ArcSharpness;
                half _ArcSpeed;
                half _ArcWarp;
                half _ArcLayer2Scale;
                half _ArcLayer2Weight;

                half _SurgeSpeed;
                half _SurgeThreshold;
                half _SurgeBoost;

                half _PolarMode;
                half _PolarSpin;

                half _FresnelPower;
                half _FresnelIntensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 viewDirWS  : TEXCOORD2;
            };

            float Hash1(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = Hash1(i);
                float b = Hash1(i + float2(1, 0));
                float c = Hash1(i + float2(0, 1));
                float d = Hash1(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
            }

            float FBM(float2 p)
            {
                float total = 0;
                float amp = 0.5;
                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    total += ValueNoise(p) * amp;
                    p *= 2.0;
                    amp *= 0.5;
                }
                return total;
            }

            // 능선(Ridged) 노이즈: 노이즈의 0.5 지점을 밝은 선으로 뽑아낸다.
            // abs로 접어서 골짜기를 만들고, 뒤집은 뒤 pow로 날카롭게 깎으면
            // 잔알갱이가 아니라 "가는 실선"이 남는다 = 전기 줄기
            float RidgedArc(float2 p, float sharpness)
            {
                float n = FBM(p);
                float ridge = 1.0 - abs(n * 2.0 - 1.0);
                return pow(saturate(ridge), sharpness);
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = positionInputs.positionCS;
                output.uv = input.uv;
                output.normalWS = normalInputs.normalWS;
                output.viewDirWS = GetWorldSpaceViewDir(positionInputs.positionWS);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float time = _Time.y;

                // ---------- 좌표계 선택 ----------
                // Polar Mode: UV를 극좌표로 바꾸면 아크가 중심에서 바깥으로 뻗는다.
                // 원형 포탈에서는 이쪽이 훨씬 자연스럽다.
                float2 p;
                float radius = 0;
                if (_PolarMode > 0.5)
                {
                    float2 centered = uv - 0.5;
                    radius = length(centered) * 2.0;
                    float angle = atan2(centered.y, centered.x);
                    // x축 = 각도(회전), y축 = 반지름(중심에서의 거리)
                    p = float2(angle / 3.14159 + time * _PolarSpin, radius);
                }
                else
                {
                    p = uv;
                }

                // ---------- 도메인 워핑: 좌표 자체를 노이즈로 밀어서 줄기를 구부린다 ----------
                float2 warp;
                warp.x = FBM(p * 3.0 + float2(time * _ArcSpeed * 0.4, 0));
                warp.y = FBM(p * 3.0 + float2(0, time * _ArcSpeed * 0.4 + 5.2));
                float2 warpedP = p + (warp - 0.5) * _ArcWarp;

                // ---------- 전기 줄기: 두 겹을 겹쳐서 복잡한 방전 패턴 ----------
                float arc1 = RidgedArc(warpedP * _ArcScale + float2(0, time * _ArcSpeed), _ArcSharpness);
                float arc2 = RidgedArc(warpedP * _ArcLayer2Scale - float2(time * _ArcSpeed * 0.7, 0), _ArcSharpness * 1.5);
                float arc = max(arc1, arc2 * _ArcLayer2Weight);

                // ---------- 방전 순간: 불규칙하게 확 밝아짐 ----------
                // 시간을 계단으로 끊고 랜덤값이 임계치를 넘을 때만 증폭
                float surgeRandom = Hash1(float2(floor(time * _SurgeSpeed), 3.7));
                float surge = step(_SurgeThreshold, surgeRandom);
                float surgeMul = lerp(1.0, _SurgeBoost, surge);

                // ---------- 색 합성 ----------
                half3 col = _BaseColor.rgb;
                col += _ArcColor.rgb * arc * surgeMul;

                // 아크 중심부는 흰색으로 타들어가게 (실제 전기 아크의 코어처럼)
                col += half3(1, 1, 1) * pow(arc, 3.0) * 0.6 * surgeMul;

                // ---------- 프레넬 테두리 ----------
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(input.viewDirWS);
                float fresnel = pow(1.0 - saturate(abs(dot(normalWS, viewDirWS))), _FresnelPower);
                col += _ArcColor.rgb * fresnel * _FresnelIntensity;

                col *= _Intensity;

                // ---------- 알파 ----------
                float alpha = _BaseAlpha;
                alpha += arc * 0.8 * surgeMul;   // 줄기가 지나가는 곳은 진하게
                alpha += fresnel * 0.5;

                // Polar Mode일 때 원 바깥은 잘라내서 깔끔한 원판이 되게 함
                if (_PolarMode > 0.5)
                    alpha *= 1.0 - smoothstep(0.9, 1.0, radius);

                alpha = saturate(alpha);

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
