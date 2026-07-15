// Synty Generic_Basic 호환 색상 치환 셰이더.
// 아틀라스 텍스처에 박혀 있는 특정 색(예: 얼굴 디스플레이의 파랑)을
// 다른 색(예: 보라)으로 바꾸고 싶을 때 사용합니다.
//
// Generic_Basic과 같은 프로퍼티 이름을 쓰므로, 기존 머티리얼을 복제한 뒤
// 셰이더만 이걸로 바꾸면 텍스처 연결이 그대로 유지됩니다.
//
// 동작 방식: 알베도/이미션 픽셀을 HSV로 변환해 색상(Hue)이 _RecolorSource와
// _HueTolerance 안에서 비슷하고 채도가 _SaturationMin 이상인 픽셀만
// _RecolorTarget의 색상으로 회전시킵니다. 명도는 원본을 유지하므로
// 디스플레이의 밝기 패턴은 그대로 남고 색만 바뀝니다.
// 무채색(검정 몸체, 회색 부품)과 색상이 다른 발광부(빨강/주황)는 영향을 받지 않습니다.
Shader "ProjectS/GenericRecolor"
{
    Properties
    {
        [Header(Base)]
        _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _Albedo_Map("Albedo Map", 2D) = "white" {}
        [Normal] _Normal_Map("Normal Map", 2D) = "bump" {}
        _Normal_Amount("Normal Amount", Range(0, 2)) = 1
        _Metallic("Metallic", Range(0, 1)) = 0
        _Smoothness("Smoothness", Range(0, 1)) = 0.2
        _Alpha_Clip_Threshold("Alpha Clip Threshold", Range(0, 1)) = 0.5

        [Header(Emission)]
        [Toggle] _Enable_Emission("Enable Emission", Float) = 1
        _Emission_Map("Emission Map", 2D) = "white" {}
        [HDR] _Emission_Color("Emission Color", Color) = (1, 1, 1, 1)

        [Header(Recolor)]
        _RecolorSource("Recolor Source (바꿀 색)", Color) = (0, 0.7, 1, 1)
        _RecolorTarget("Recolor Target (새 색)", Color) = (0.55, 0.15, 1, 1)
        _HueTolerance("Hue Tolerance", Range(0.01, 0.5)) = 0.12
        _SaturationMin("Saturation Min", Range(0, 1)) = 0.25
        _RecolorAlbedo("Recolor Albedo", Range(0, 1)) = 1
        _RecolorEmission("Recolor Emission", Range(0, 1)) = 1

        [Header(UV Mask)]
        [Toggle] _UseUVMask("Use UV Mask", Float) = 0
        _RecolorUVRect("Recolor UV Rect (xMin yMin xMax yMax)", Vector) = (0, 0, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        // RgbToHsv/HsvToRgb 제공. ForwardLit 외의 패스(DepthNormals 등)에서는
        // Lighting.hlsl이 없어서 자동으로 딸려 오지 않으므로 명시적으로 포함합니다.
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

        CBUFFER_START(UnityPerMaterial)
        float4 _Albedo_Map_ST;
        float4 _Normal_Map_ST;
        float4 _Emission_Map_ST;
        half4 _BaseColor;
        half4 _Emission_Color;
        half4 _RecolorSource;
        half4 _RecolorTarget;
        half _Normal_Amount;
        half _Metallic;
        half _Smoothness;
        half _Alpha_Clip_Threshold;
        half _Enable_Emission;
        half _HueTolerance;
        half _SaturationMin;
        half _RecolorAlbedo;
        half _RecolorEmission;
        half _UseUVMask;
        float4 _RecolorUVRect;
        CBUFFER_END

        TEXTURE2D(_Albedo_Map);   SAMPLER(sampler_Albedo_Map);
        TEXTURE2D(_Normal_Map);   SAMPLER(sampler_Normal_Map);
        TEXTURE2D(_Emission_Map); SAMPLER(sampler_Emission_Map);

        // RgbToHsv / HsvToRgb는 Core.hlsl이 포함하는 Color.hlsl의 것을 사용합니다.
        // (직접 정의하면 재정의 에러가 납니다.)

        // 색상환에서 두 Hue 사이의 최단 거리(0~0.5).
        float HueDistance(float a, float b)
        {
            float d = abs(a - b);
            return min(d, 1.0 - d);
        }

        // _RecolorSource 계열 색만 _RecolorTarget 색상으로 치환합니다.
        // strength 0이면 원본 그대로, 1이면 마스크 영역을 완전히 치환합니다.
        // UV 마스크를 켜면 아틀라스의 _RecolorUVRect 사각형 안쪽만 대상이 됩니다.
        // (몸 전체가 한 머티리얼일 때 얼굴 디스플레이 영역만 바꾸기 위한 옵션)
        float3 ApplyRecolor(float3 color, half strength, float2 uv)
        {
            if (strength <= 0.001) return color;

            if (_UseUVMask > 0.5)
            {
                float2 inRect = step(_RecolorUVRect.xy, uv) * step(uv, _RecolorUVRect.zw);
                strength *= inRect.x * inRect.y;
                if (strength <= 0.001) return color;
            }

            float3 hsv = RgbToHsv(color);
            float3 srcHsv = RgbToHsv(_RecolorSource.rgb);
            float3 tgtHsv = RgbToHsv(_RecolorTarget.rgb);

            // 색상이 기준색과 가깝고, 어느 정도 채도가 있는 픽셀만 대상
            float hueMask = 1.0 - smoothstep(_HueTolerance * 0.5, _HueTolerance, HueDistance(hsv.x, srcHsv.x));
            float satMask = smoothstep(_SaturationMin * 0.5, _SaturationMin, hsv.y);
            float mask = hueMask * satMask * strength;

            float3 shifted = hsv;
            shifted.x = frac(hsv.x + (tgtHsv.x - srcHsv.x));
            shifted.y = saturate(hsv.y * (tgtHsv.y / max(srcHsv.y, 0.001)));
            return lerp(color, HsvToRgb(shifted), mask);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex LitVertex
            #pragma fragment LitFragment

            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _FORWARD_PLUS _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3;
                half fogFactor : TEXCOORD4;
#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord : TEXCOORD5;
#endif
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 6);
#if defined(USE_APV_PROBE_OCCLUSION)
                float4 probeOcclusion : TEXCOORD7;
#endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings LitVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.uv = TRANSFORM_TEX(input.uv, _Albedo_Map);
                output.normalWS = normalInputs.normalWS;

                real tangentSign = input.tangentOS.w * GetOddNegativeScale();
                output.tangentWS = float4(normalInputs.tangentWS, tangentSign);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                output.shadowCoord = GetShadowCoord(positionInputs);
#endif
                OUTPUT_SH4(positionInputs.positionWS, output.normalWS,
                    GetWorldSpaceNormalizeViewDir(positionInputs.positionWS),
                    output.vertexSH, output.probeOcclusion);
                return output;
            }

            half4 LitFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 albedo = SAMPLE_TEXTURE2D(_Albedo_Map, sampler_Albedo_Map, input.uv) * _BaseColor;
                clip(albedo.a - _Alpha_Clip_Threshold);

                albedo.rgb = ApplyRecolor(albedo.rgb, _RecolorAlbedo, input.uv);

                half3 emission = half3(0, 0, 0);
                if (_Enable_Emission > 0.5)
                {
                    half3 emissionSample = SAMPLE_TEXTURE2D(_Emission_Map, sampler_Emission_Map, input.uv).rgb;
                    emissionSample = ApplyRecolor(emissionSample, _RecolorEmission, input.uv);
                    emission = emissionSample * _Emission_Color.rgb;
                }

                half4 normalSample = SAMPLE_TEXTURE2D(_Normal_Map, sampler_Normal_Map, input.uv);
                half3 normalTS = UnpackNormalScale(normalSample, _Normal_Amount);

                float3 bitangentWS = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                half3x3 tangentToWorld = half3x3(
                    (half3)input.tangentWS.xyz, (half3)bitangentWS, (half3)input.normalWS);
                float3 normalWS = normalize(TransformTangentToWorld(normalTS, tangentToWorld));

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                inputData.shadowCoord = input.shadowCoord;
#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
#else
                inputData.shadowCoord = float4(0, 0, 0, 0);
#endif
                inputData.fogCoord = input.fogFactor;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                // 프로브 볼륨(APV)을 쓰는 씬에서도 주변광을 제대로 받도록
                // URP Lit과 동일한 경로로 GI를 샘플링합니다.
#if !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
                inputData.bakedGI = SAMPLE_GI(input.vertexSH,
                    GetAbsolutePositionWS(inputData.positionWS),
                    inputData.normalWS,
                    inputData.viewDirectionWS,
                    input.positionCS.xy,
                    input.probeOcclusion,
                    inputData.shadowMask);
#else
                inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
                inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
#endif

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo.rgb;
                surfaceData.alpha = 1;
                surfaceData.metallic = _Metallic;
                surfaceData.smoothness = _Smoothness;
                surfaceData.normalTS = normalTS;
                surfaceData.emission = emission;
                surfaceData.occlusion = 1;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1;
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings ShadowVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
                float3 lightDirectionWS = _LightDirection;
#endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
#if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#endif
                output.positionCS = positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _Albedo_Map);
                return output;
            }

            half4 ShadowFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half alpha = SAMPLE_TEXTURE2D(_Albedo_Map, sampler_Albedo_Map, input.uv).a * _BaseColor.a;
                clip(alpha - _Alpha_Clip_Threshold);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthOnlyVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _Albedo_Map);
                return output;
            }

            half4 DepthOnlyFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half alpha = SAMPLE_TEXTURE2D(_Albedo_Map, sampler_Albedo_Map, input.uv).a * _BaseColor.a;
                clip(alpha - _Alpha_Clip_Threshold);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _Albedo_Map);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half alpha = SAMPLE_TEXTURE2D(_Albedo_Map, sampler_Albedo_Map, input.uv).a * _BaseColor.a;
                clip(alpha - _Alpha_Clip_Threshold);
                return half4(normalize(input.normalWS) * 0.5 + 0.5, 0);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
