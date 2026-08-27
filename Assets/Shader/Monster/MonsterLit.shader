// 몬스터 전용 URP Lit 변형.
// 배경(시안/보라 네온)에 몬스터가 묻히는 문제를 해결하기 위한 셰이더로, 표준 Lit에 두 가지를 더한다.
//  1) Emission Recolor: Synty 아틀라스의 원색(청록/마젠타) 이미시브를 휘도 마스크로 바꿔 앰버로 재염색.
//     텍스처를 수정하지 않고 몬스터 발광색만 통일하기 위함.
//  2) Rim Light: 프레넬 기반 얇은 윤곽선. 어두운 바닥 위에서 실루엣이 읽히게 하는 핵심 장치.
// Forward / Forward+ 양쪽 대응(_CLUSTER_LIGHT_LOOP). 렌더링 경로를 바꿔도 이 셰이더는 그대로 동작한다.
Shader "ProjectS/Monster Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Body Tint", Color) = (0.5706, 0.4771, 0.42, 1)
        // 캐릭터는 대부분 불투명이라 기본은 꺼둔다. 알파 채널이 없는 아틀라스에서 이걸 켜면
        // 컷오프 판정이 엉켜 모델이 통째로 사라질 수 있다.
        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha Clip", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5

        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Scale", Range(0.0, 2.0)) = 1.0

        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.3

        [Space(8)][Header(Body Color Remap)][Space(4)]
        // 알베도에 박힌 색을 바꾸기 위한 HSV 제어. Body Tint(곱셈)로는 색을 어둡게만 할 수 있고
        // 마젠타를 앰버로 옮기거나 탈색시키는 것이 불가능해서 필요하다.
        // 0.5 = 정반대 색상. 마젠타(약 0.87) -> 앰버(약 0.08)는 대략 +0.21.
        _HueShift("Hue Shift", Range(-0.5, 0.5)) = 0.0
        // 0 = 완전 무채색, 1 = 원본, 1 초과 = 과채도.
        _Saturation("Saturation", Range(0.0, 2.0)) = 1.0

        [Space(8)][Header(Emission)][Space(4)]
        _EmissionMap("Emission Mask", 2D) = "black" {}
        [HDR] _EmissionTint("Emission Tint", Color) = (1.0, 0.1442, 0.013, 1)
        _EmissionStrength("Emission Strength", Range(0.0, 8.0)) = 2.0
        // 0 = 텍스처 원래 색 유지, 1 = 휘도만 남기고 Tint 색으로 완전 교체.
        _EmissionRecolor("Recolor Amount", Range(0.0, 1.0)) = 1.0

        [Space(8)][Header(Rim Light)][Space(4)]
        [HDR] _RimColor("Rim Color", Color) = (1.0, 0.3608, 0.0243, 1)
        // 값이 클수록 테두리가 얇아진다. 6~8이 "얇은 한 줄" 구간.
        _RimPower("Rim Thinness", Range(0.5, 16.0)) = 6.0
        _RimStrength("Rim Strength", Range(0.0, 4.0)) = 1.2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
        }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

        // SRP Batcher가 물리려면 모든 패스가 같은 CBUFFER 레이아웃을 공유해야 한다.
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _AlphaClip;
            half _Cutoff;
            half _BumpScale;
            half _Metallic;
            half _Smoothness;
            half _HueShift;
            half _Saturation;
            half4 _EmissionTint;
            half _EmissionStrength;
            half _EmissionRecolor;
            half4 _RimColor;
            half _RimPower;
            half _RimStrength;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex MonsterForwardVertex
            #pragma fragment MonsterForwardFragment

            #pragma shader_feature_local_fragment _ALPHATEST_ON

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            // Forward+ (클러스터 라이트 루프). 이게 빠지면 Forward+에서 추가 라이트가 안 들어온다.
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fragment _ LIGHTMAP_BICUBIC_SAMPLING
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #pragma multi_compile _ USE_LEGACY_LIGHTMAPS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS       : POSITION;
                float3 normalOS         : NORMAL;
                float4 tangentOS        : TANGENT;
                float2 texcoord         : TEXCOORD0;
                float2 staticLightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv           : TEXCOORD0;
                float3 positionWS   : TEXCOORD1;
                half3 normalWS      : TEXCOORD2;
                half4 tangentWS     : TEXCOORD3;
                half4 fogFactorAndVertexLight : TEXCOORD4;
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 5);
            #ifdef USE_APV_PROBE_OCCLUSION
                float4 probeOcclusion : TEXCOORD6;
            #endif
                float4 positionCS   : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings MonsterForwardVertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
                output.positionWS = vertexInput.positionWS;
                output.positionCS = vertexInput.positionCS;
                output.normalWS = normalInput.normalWS;

                real sign = input.tangentOS.w * GetOddNegativeScale();
                output.tangentWS = half4(normalInput.tangentWS.xyz, sign);

                half3 vertexLight = VertexLighting(vertexInput.positionWS, normalInput.normalWS);
                half fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
                output.fogFactorAndVertexLight = half4(fogFactor, vertexLight);

                OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
                OUTPUT_SH4(vertexInput.positionWS, output.normalWS.xyz,
                    GetWorldSpaceNormalizeViewDir(vertexInput.positionWS), output.vertexSH, output.probeOcclusion);

                return output;
            }

            half4 MonsterForwardFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 albedoSample = SampleAlbedoAlpha(input.uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
            #ifdef _ALPHATEST_ON
                clip(albedoSample.a * _BaseColor.a - _Cutoff);
            #endif

                // 색상 리맵은 Tint 곱셈보다 먼저 한다. Tint가 먼저 들어가면
                // 채도를 뺀 뒤 다시 색이 얹히는 순서가 되어 의도한 색이 안 나온다.
                half3 hsv = RgbToHsv(albedoSample.rgb);
                hsv.x = frac(hsv.x + _HueShift);
                hsv.y = saturate(hsv.y * _Saturation);
                half4 albedo = half4(HsvToRgb(hsv), albedoSample.a) * _BaseColor;

                // 이미시브 재염색: 원본 색의 휘도를 마스크로 써서 Tint 색으로 갈아끼운다.
                // 이렇게 해야 Synty 아틀라스를 공유하는 다른 캐릭터를 건드리지 않고 몬스터만 색을 바꿀 수 있다.
                half3 emissionSrc = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb;
                half emissionMask = dot(emissionSrc, half3(0.2126h, 0.7152h, 0.0722h));
                half3 emission = lerp(emissionSrc, emissionMask * _EmissionTint.rgb, _EmissionRecolor) * _EmissionStrength;

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo.rgb;
                surfaceData.alpha = 1.0h;
                surfaceData.metallic = _Metallic;
                surfaceData.specular = half3(0.0h, 0.0h, 0.0h);
                surfaceData.smoothness = _Smoothness;
                surfaceData.occlusion = 1.0h;
                surfaceData.emission = emission;
                // SurfaceInput.hlsl의 SampleNormal은 _NORMALMAP 키워드가 없으면 평면 노멀을 돌려준다.
                // 이 셰이더는 노멀맵을 항상 쓰므로 키워드 의존 없이 직접 언팩한다.
                half4 normalSample = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv);
                surfaceData.normalTS = UnpackNormalScale(normalSample, _BumpScale);
                surfaceData.clearCoatMask = 0.0h;
                surfaceData.clearCoatSmoothness = 0.0h;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
            #if defined(DEBUG_DISPLAY)
                inputData.positionCS = input.positionCS;
            #endif

                half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float sgn = input.tangentWS.w;
                float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
                half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent.xyz, input.normalWS.xyz);
                inputData.tangentToWorld = tangentToWorld;
                inputData.normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(surfaceData.normalTS, tangentToWorld));
                inputData.viewDirectionWS = viewDirWS;

            #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
            #else
                inputData.shadowCoord = float4(0, 0, 0, 0);
            #endif

                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactorAndVertexLight.x);
                inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
                inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);

                // 얇은 림라이트. 조명과 무관하게 더해지므로, 라이트가 하나도 안 닿는 각도에서도 윤곽이 남는다.
                // 어두운 바닥 위 몬스터 실루엣이 읽히게 하는 것이 목적이라 의도적으로 가산 합성.
                half fresnel = 1.0h - saturate(dot(inputData.normalWS, viewDirWS));
                half rim = pow(fresnel, _RimPower) * _RimStrength;
                color.rgb += rim * _RimColor.rgb;

                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0h;
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
            #pragma target 3.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
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
            #pragma target 3.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        // SSAO와 뎁스 기반 효과가 몬스터를 인식하려면 필요하다. 빠지면 몬스터만 AO에서 빠진다.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
