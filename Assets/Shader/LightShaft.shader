Shader "Custom/LightShaft"
{
    // ===================================================================
    // 라이트 셰프트(빛줄기) 셰이더 (URP)
    //
    // 오브젝트를 흔드는 것이 아니라 표면의 밝기가 흘러가게 만든다.
    // 빛은 휘지 않는다. 실제 광선이 살아 보이는 이유는 줄기가 구부러져서가 아니라
    // 그 안을 떠도는 먼지 밀도가 계속 변하기 때문이다. 그래서 여기서는 지오메트리를
    // 건드리지 않고 표면 밝기만 노이즈로 출렁이게 한다.
    //
    // 구성 요소
    // - Flow    : 줄기를 따라 흘러내리는 노이즈. 두 겹을 반대 방향으로 겹쳐 루프를 숨긴다
    // - Falloff : 뿌리(광원 쪽)는 진하고 끝으로 갈수록 옅어지는 감쇠
    // - Thickness : 시선이 원통을 통과하는 길이. 이게 실루엣을 만든다
    //
    // 두께는 프레넬이 아니라 ndotv에 비례한다. 빛기둥은 속 빈 껍데기가 아니라 부피라서,
    // 정면(원통 한가운데)에서 통과 길이가 가장 길고 실루엣 가장자리에서 0으로 떨어진다.
    // 프레넬은 정반대로 가장자리를 밝게 만든다. 그걸 쓰면 가장자리에서 0이 되는 지점이
    // 없어져 부드럽게 사라지지 못하고 하드엣지 덩어리가 된다. (실제로 그렇게 만들었다가 고침)
    //
    // 색은 한 번만 쓰고 밝기는 전부 알파(밀도)로 몰아넣는다.
    // 밝기를 색에 더하거나 곱해서 채널이 1을 넘으면 청록이 흰색으로 잘린다.
    // 특히 이 씬은 기둥 8개가 서로 겹치고 Cull Off로 앞뒤면까지 더해지므로,
    // 색 채널은 반드시 1 이하로 두고 밝기는 _Opacity와 블룸에 맡긴다.
    //
    // 좌표계: UV가 아니라 오브젝트 공간 위치를 쓴다.
    //   Synty의 SM_LightRay_Round는 UV 배치를 신뢰할 수 없고, 대신 형상이 단순해서
    //   (오브젝트 공간 -X로 뻗은 원뿔, 단면 반지름 1) 위치에서 원통 좌표를 바로 만들 수 있다.
    //   둘레 방향은 atan2 대신 단면 방향벡터를 그대로 3D 노이즈에 넣는다. 각도를 쓰면
    //   ±180도 지점에 노이즈 이음매가 세로줄로 보이는데, 방향벡터는 원을 따라 도는 것이라
    //   이음매가 아예 생기지 않는다.
    //
    // 적용 대상: SM_LightRayRound_01 같은 원뿔형 빛줄기 메시
    // ===================================================================

    Properties
    {
        [Header(Main)]
        // 알파는 건드리지 마라. 진하기는 _Opacity로 조절한다.
        // 알파에 물려두면 색을 고를 때마다 진하기가 같이 흔들려 원인을 못 찾는다.
        _TintColor("Tint Color (빛 색)", Color) = (0.3165, 0.5189, 0.5005, 1)
        // 1을 넘기면 색 채널이 잘려 흰색으로 탄다. 더 밝게 하려면 _Opacity를 올려라.
        _Intensity("Intensity (색의 밝기. 1을 넘기면 흰색으로 탄다)", Range(0, 2)) = 1
        // 실효 알파는 이 값보다 훨씬 낮게 나온다. 원통을 옆에서 보면 정면을 향한 가운데 한 줄만
        // 두께가 최대고 나머지는 0으로 떨어지기 때문이다. 밝은 씬에서는 2 이상도 정상이다.
        _Opacity("Opacity (전체 진하기. 여기로 세기를 조절한다)", Range(0, 4)) = 1.5

        [Header(Shaft Shape)]
        _ShaftLength("Shaft Length (오브젝트 공간 길이. 뿌리와 끝이 뒤바뀌면 부호를 뒤집어라)", Float) = -1.83
        _Falloff("Falloff (끝으로 갈수록 옅어지는 정도. 클수록 빨리 사라짐)", Range(0.1, 8)) = 1.5

        [Header(Surface Flow)]
        _FlowSpeed("Flow Speed (표면이 흘러가는 속도. 0.05~0.15가 살짝 움직이는 정도)", Float) = 0.08
        _NoiseAmount("Noise Amount (표면 밝기가 출렁이는 폭. 0이면 완전히 균일)", Range(0, 1)) = 0.35
        _NoiseDensityU("Noise Density U (둘레 방향 무늬 밀도)", Float) = 2.5
        _NoiseDensityV("Noise Density V (길이 방향 무늬 밀도. 늘리면 가는 결이 생김)", Float) = 2
        _Layer2Scale("Layer2 Scale (두번째 겹의 밀도 배수)", Float) = 2.1
        _Layer2Speed("Layer2 Speed (두번째 겹의 속도 배수. 음수여야 반대로 흘러 루프가 숨는다)", Float) = -0.6

        [Header(Volume)]
        // 실루엣을 만드는 유일한 항. 올리면 가운데만 남아 가늘어지고, 내리면 가장자리까지 꽉 찬다.
        // 1보다 크게 두면 밝은 부분이 가운데 한 줄로 몰려 전체적으로 훨씬 흐려 보인다.
        _CorePower("Core Power (가장자리가 사라지는 빠르기. 클수록 가운데만 남음)", Range(0.3, 8)) = 0.8

        // 테두리 발광. 부피 표현에는 틀린 항이라 기본값 0이다.
        // 홀로그램처럼 껍데기 느낌을 일부러 낼 때만 살짝 올린다. 올릴수록 실루엣이 딱딱해진다.
        _FresnelPower("Fresnel Power (테두리 두께. 클수록 얇음)", Range(0.5, 8)) = 3
        _FresnelIntensity("Fresnel Intensity (테두리 발광. 올리면 가장자리가 딱딱해진다)", Range(0, 1)) = 0

        [Header(Options)]
        [Toggle] _UseVertexColor("Use Vertex Color (메시에 정점 색이 있을 때만 켜라)", Float) = 0

        // 진단용. 이 셰이더는 메시의 노멀과 오브젝트 공간 좌표 범위를 가정하고 만들어졌는데,
        // 그 가정이 깨지면 "그냥 안 보인다"로만 나타나 원인을 못 가린다. 각 중간값을 흑백으로
        // 뽑아 어디서 0이 되는지 눈으로 보라는 것이다.
        // 쓸 때는 Src Blend=One, Dst Blend=Zero로 바꿔야 씬에 안 섞이고 값이 그대로 보인다.
        [Enum(Off,0,Length T,1,NdotV,2,Thickness,3,Flow,4,Final Alpha,5)]
        _DebugMode("Debug Mode (진단용. 평소엔 Off)", Float) = 0

        // 기본값은 Additive(SrcAlpha One). 빛기둥은 보통 이쪽이 맞다.
        // 더 묵직한 안개 기둥처럼 만들고 싶으면 Dst를 OneMinusSrcAlpha로 바꿔 알파 블렌드로 쓴다.
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 1
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

            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            // 뒷면을 남겨야 원통 반대편이 겹쳐 보여 부피감이 생긴다. 끄면 껍데기 한 겹으로 보인다.
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TintColor;
                half _Intensity;
                half _Opacity;

                half _ShaftLength;
                half _Falloff;

                half _FlowSpeed;
                half _NoiseAmount;
                half _NoiseDensityU;
                half _NoiseDensityV;
                half _Layer2Scale;
                half _Layer2Speed;

                half _FresnelPower;
                half _FresnelIntensity;
                half _CorePower;

                half _UseVertexColor;
                half _DebugMode;

                // Blend [] 가 읽는 값이지만 SRP Batcher 호환을 위해 여기 함께 선언한다.
                // 머티리얼 프로퍼티가 하나라도 이 블록 밖에 있으면 배칭에서 통째로 빠진다.
                half _SrcBlend;
                half _DstBlend;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 viewDirWS  : TEXCOORD2;
                float4 color      : TEXCOORD3;
            };

            float Hash1(float3 p)
            {
                return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453);
            }

            // 3D 값 노이즈. 2D가 아니라 3D인 이유는 원통 둘레를 이음매 없이 감기 위해서다.
            // 둘레를 각도(스칼라)로 펴면 ±180도에서 값이 뚝 끊겨 세로줄이 보인다.
            float ValueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float3 u = f * f * (3.0 - 2.0 * f);

                float x00 = lerp(Hash1(i + float3(0, 0, 0)), Hash1(i + float3(1, 0, 0)), u.x);
                float x10 = lerp(Hash1(i + float3(0, 1, 0)), Hash1(i + float3(1, 1, 0)), u.x);
                float x01 = lerp(Hash1(i + float3(0, 0, 1)), Hash1(i + float3(1, 0, 1)), u.x);
                float x11 = lerp(Hash1(i + float3(0, 1, 1)), Hash1(i + float3(1, 1, 1)), u.x);

                return lerp(lerp(x00, x10, u.y), lerp(x01, x11, u.y), u.z);
            }

            // 3옥타브면 충분하다. 먼지 밀도는 큰 덩어리가 천천히 흐르는 것이라
            // 옥타브를 더 쌓으면 잔알갱이만 늘어 지지직거리는 노이즈로 보인다.
            float FBM(float3 p)
            {
                float total = 0;
                float amp = 0.5;

                [unroll]
                for (int i = 0; i < 3; i++)
                {
                    total += ValueNoise(p) * amp;
                    p *= 2.0;
                    amp *= 0.5;
                }

                // 진폭 합이 0.875라 그대로 두면 최대치가 1에 못 미친다. 0~1로 편다.
                return total / 0.875;
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = positionInputs.positionCS;
                output.positionOS = input.positionOS.xyz;
                output.normalWS = normalInputs.normalWS;
                output.viewDirWS = GetWorldSpaceViewDir(positionInputs.positionWS);
                output.color = input.color;

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 pos = input.positionOS;
                float time = _Time.y;

                // ---------- 원통 좌표 ----------
                // t: 뿌리(0) → 끝(1). _ShaftLength가 음수인 것은 이 메시가 -X로 뻗어 있기 때문이다.
                float t = saturate(pos.x / _ShaftLength);

                // 단면의 방향벡터. 정규화해서 원 위에 올려야 굵기가 변해도 무늬 밀도가 일정하다.
                // 축 위(pos.yz가 0)에서 normalize가 NaN이 되므로 아주 작은 값을 더해 막는다.
                float2 ring = normalize(float2(pos.y, pos.z) + 1e-5);

                // ---------- 흐르는 노이즈 두 겹 ----------
                // 시간을 t축에서 빼면 무늬가 뿌리에서 끝 방향으로 흘러내린다.
                float3 p1 = float3(ring * _NoiseDensityU,
                                   t * _NoiseDensityV - time * _FlowSpeed);

                // 두번째 겹은 밀도도 속도도 다르고 방향이 반대다(_Layer2Speed 기본값이 음수).
                // 한 겹만 쓰면 무늬가 통째로 같은 속도로 지나가 흐름이 눈에 띈다.
                float3 p2 = float3(ring * _NoiseDensityU * _Layer2Scale,
                                   t * _NoiseDensityV * _Layer2Scale - time * _FlowSpeed * _Layer2Speed) + 11.3;

                float flow = lerp(FBM(p1), FBM(p2), 0.5);

                // 1을 중심으로 오르내리게 만든다. _NoiseAmount가 0이면 정확히 1이 되어
                // 무늬가 완전히 사라지고 예전처럼 균일한 빛줄기가 된다.
                float shimmer = lerp(1.0 - _NoiseAmount, 1.0 + _NoiseAmount, flow);

                // ---------- 길이 감쇠 ----------
                float lengthFade = pow(saturate(1.0 - t), _Falloff);

                // ---------- 두께 근사 ----------
                // abs를 쓰는 이유는 Cull Off라 뒷면 노멀이 뒤집혀 들어오기 때문이다.
                // 이게 없으면 뒷면만 테두리가 사라져 좌우가 짝짝이로 보인다.
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(input.viewDirWS);
                float ndotv = saturate(abs(dot(normalWS, viewDirWS)));

                // 시선이 통과하는 두께. 정면에서 최대, 실루엣 가장자리에서 0이다.
                // 여기서 0으로 떨어지는 것이 핵심이다. 이게 없으면 기둥이 배경과
                // 딱 잘린 경계로 만나 빛이 아니라 흰 덩어리로 보인다.
                float thickness = pow(ndotv, _CorePower);

                // ---------- 합성 ----------
                // 밝기를 색에 더하거나 곱해 올리지 않고 전부 밀도(알파)로 몰아넣는다.
                // 색 채널이 1을 넘으면 초록·파랑이 먼저 잘려 청록이 흰색으로 빠지고,
                // 진하기를 올릴수록 색이 빠지는 조절 불가능한 셰이더가 된다.
                float density = thickness * lengthFade * shimmer * _Opacity;

                half3 col = saturate(_TintColor.rgb * _Intensity);
                float alpha = _TintColor.a * density;

                // 선택적 테두리 발광. 기본값 0이라 보통은 아무 일도 하지 않는다.
                alpha += _TintColor.a * pow(1.0 - ndotv, _FresnelPower) * _FresnelIntensity * lengthFade;

                if (_UseVertexColor > 0.5)
                {
                    col *= input.color.rgb;
                    alpha *= input.color.a;
                }

                alpha = saturate(alpha);

                // ---------- 진단 ----------
                // 어느 항이 0으로 죽는지 흑백으로 본다. 알파를 1로 강제하므로
                // Src Blend=One, Dst Blend=Zero로 두면 값이 그대로 화면에 찍힌다.
                if (_DebugMode > 0.5)
                {
                    float v = 0;
                    if (_DebugMode < 1.5)      v = t;           // 길이 좌표. 뿌리 0(검정) → 끝 1(흰색)
                    else if (_DebugMode < 2.5) v = ndotv;       // 정면 1(흰색) → 실루엣 0(검정)
                    else if (_DebugMode < 3.5) v = thickness;   // 두께. 전부 검으면 노멀이 깨진 것
                    else if (_DebugMode < 4.5) v = flow;        // 노이즈. 균일하면 좌표가 안 도는 것
                    else                       v = alpha;       // 최종 알파
                    return half4(v, v, v, 1);
                }

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
