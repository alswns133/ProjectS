// 몬스터 발밑 접지 표시(그라운드 링).
// 3D 액션에서 "저 몬스터가 바닥 어디에 서 있나"는 색이 아니라 접지로 읽힌다.
// 몬스터 자식으로 붙인 Quad에 이 머티리얼을 올려 쓴다. 라이팅을 받지 않는 가산 합성이라
// 어두운 던전 바닥에서도 일정하게 보이고, 라이트 개수/렌더링 경로와 무관하다.
Shader "ProjectS/Monster Ground Blob"
{
    Properties
    {
        [HDR] _Color("Color", Color) = (1.6, 0.32, 0.03, 1)
        // UV 기준 반지름이라 0.5가 Quad 가장자리. 실제 크기는 Quad의 Scale로 맞춘다.
        _Radius("Ring Radius", Range(0.05, 0.5)) = 0.42
        _RingWidth("Ring Width", Range(0.005, 0.3)) = 0.06
        // 링 안쪽을 옅게 채워 접지 지점을 덩어리로 인식시킨다. 0이면 링만 남는다.
        _FillOpacity("Inner Fill", Range(0.0, 1.0)) = 0.12
        _Opacity("Opacity", Range(0.0, 2.0)) = 1.0

        [Space(8)][Header(Segments)][Space(4)]
        // 링을 몇 조각으로 자를지. 4면 90도씩 네 조각.
        _SegmentCount("Segment Count", Range(1, 8)) = 4
        // 각 조각이 비워내는 비율. 0이면 끊김 없는 원, 0.2면 조각마다 20%가 간격.
        _SegmentGap("Segment Gap", Range(0.0, 0.6)) = 0.18
        // 조각 끝을 부드럽게 잘라 계단현상을 없앤다.
        _SegmentSoftness("Segment Softness", Range(0.001, 0.2)) = 0.04
        // 초당 회전수. 0.06이면 한 바퀴에 약 17초.
        _RotationSpeed("Rotation Speed", Range(-1.0, 1.0)) = 0.06

        [Space(8)][Header(Pulse)][Space(4)]
        // 평상시엔 0. 공격 예고 같은 연출에서만 스크립트로 올려 쓰는 용도.
        _PulseSpeed("Pulse Speed", Range(0.0, 8.0)) = 0.0
        _PulseAmount("Pulse Amount", Range(0.0, 0.5)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "GroundBlob"
            Tags { "LightMode" = "UniversalForward" }

            // 가산 합성. 바닥 타일 위에 겹쳐도 바닥 무늬를 지우지 않고 빛처럼 얹힌다.
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off
            // 바닥과의 Z 파이팅 방지. 폴리곤 오프셋으로 깊이값을 카메라 쪽으로 당긴다.
            // ZWrite는 꺼져 있지만 ZTest에 쓰이는 깊이에는 오프셋이 적용되므로 효과가 있다.
            // 값을 키운 이유: Quad가 바닥과 거의 같은 높이일 때 -1,-1로는 부족해 깜빡였다.
            Offset -2, -4

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex BlobVertex
            #pragma fragment BlobFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Radius;
                half _RingWidth;
                half _FillOpacity;
                half _Opacity;
                half _SegmentCount;
                half _SegmentGap;
                half _SegmentSoftness;
                half _RotationSpeed;
                half _PulseSpeed;
                half _PulseAmount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv         : TEXCOORD0;
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings BlobVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 BlobFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 p = input.uv - 0.5;
                float d = length(p);

                float pulse = 1.0 + _PulseAmount * sin(_Time.y * _PulseSpeed * 6.2831853);
                float radius = max(_Radius * pulse, 1e-4);

                // 링: 반지름에서 멀어질수록 감쇠. smoothstep으로 가장자리를 부드럽게.
                float ring = saturate(1.0 - abs(d - radius) / max(_RingWidth, 1e-4));
                ring = ring * ring * (3.0 - 2.0 * ring);

                // 각도를 0~1로 정규화한 뒤 시간만큼 밀어 회전시킨다.
                // 회전은 UV 자체가 아니라 각도값에만 적용하므로 링 두께나 반지름은 영향받지 않는다.
                float angle = frac(atan2(p.y, p.x) * 0.15915494 + 0.5 + _Time.y * _RotationSpeed);

                // 조각 내부 좌표(0~1). 양 끝에서 _SegmentGap의 절반씩 잘라내면 조각 사이에 간격이 생긴다.
                float seg = frac(angle * max(_SegmentCount, 1.0));
                float halfGap = _SegmentGap * 0.5;
                float soft = max(_SegmentSoftness, 1e-4);
                float segMask = smoothstep(halfGap - soft, halfGap + soft, seg)
                              * (1.0 - smoothstep(1.0 - halfGap - soft, 1.0 - halfGap + soft, seg));

                // 조각 마스크는 링에만 건다. 안쪽 채움까지 자르면 접지 지점이 깜빡이는 것처럼 보인다.
                ring *= segMask;

                // 안쪽 채움: 중심이 진하고 링 쪽으로 갈수록 옅어진다.
                float fill = saturate(1.0 - d / radius);
                fill = fill * fill * _FillOpacity;

                float alpha = saturate(max(ring, fill)) * _Opacity;
                // Quad 밖 사각 모서리가 보이지 않도록 원 밖을 잘라낸다.
                alpha *= step(d, 0.5);

                return half4(_Color.rgb, alpha * _Color.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
