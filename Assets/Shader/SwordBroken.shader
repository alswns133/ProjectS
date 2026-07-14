// 망가진(부러진) 검 표현용 URP 셰이더.
// Synty Generic_Basic 셰이더그래프와 같은 프로퍼티 이름(_Albedo_Map, _Emission_Map, _Emission_Color 등)을
// 사용하므로 기존 Sword 머티리얼의 텍스처를 그대로 옮겨 쓸 수 있습니다.
//
// 망가짐 표현 3요소:
//  1. 약해진 빛  : _DamageAmount에 따라 발광이 _BrokenLightStrength 수준까지 줄고 불규칙하게 깜빡입니다.
//  2. 부러진 칼날: 오브젝트 공간 _BreakAxis 방향으로 _BreakHeight 위쪽을 노이즈 톱니 모양으로 클리핑하고,
//                  단면 가장자리에 _BreakEdgeColor 발광 테두리를 넣습니다.
//  3. 균열       : 발광 영역(Emission Map이 밝은 곳)에만 균열 노이즈를 넣어 빛이 갈라져 보이게 합니다.
//                  발광이 없는 칼자루는 균열/발광 영향을 받지 않아 검은색이 유지됩니다.
Shader "ProjectS/SwordBroken"
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
        [HDR] _Emission_Color("Emission Color", Color) = (0.5, 4, 8, 1)

        [Header(Damage)]
        _DamageAmount("Damage Amount", Range(0, 1)) = 1
        _BrokenLightStrength("Broken Light Strength", Range(0, 1)) = 0.15
        _FlickerSpeed("Flicker Speed", Float) = 6
        _FlickerStrength("Flicker Strength", Range(0, 1)) = 0.7

        [Header(Blade Break)]
        _BreakAxis("Break Axis (Object Space)", Vector) = (0, 1, 0, 0)
        _BreakHeight("Break Height", Float) = 0.55
        _BreakJagScale("Break Jag Scale", Float) = 40
        _BreakJagAmount("Break Jag Amount", Float) = 0.035
        _BreakEdgeWidth("Break Edge Width", Float) = 0.02
        [HDR] _BreakEdgeColor("Break Edge Color", Color) = (0.2, 1.2, 2.4, 1)

        [Header(Cracks)]
        _CrackScale("Crack Scale", Float) = 9
        _CrackWidth("Crack Width", Range(0.01, 0.5)) = 0.08
        _CrackDarkness("Crack Darkness", Range(0, 1)) = 0.9
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

        CBUFFER_START(UnityPerMaterial)
        float4 _Albedo_Map_ST;
        float4 _Normal_Map_ST;
        float4 _Emission_Map_ST;
        half4 _BaseColor;
        half4 _Emission_Color;
        half4 _BreakEdgeColor;
        float4 _BreakAxis;
        half _Normal_Amount;
        half _Metallic;
        half _Smoothness;
        half _Alpha_Clip_Threshold;
        half _Enable_Emission;
        half _DamageAmount;
        half _BrokenLightStrength;
        float _FlickerSpeed;
        half _FlickerStrength;
        float _BreakHeight;
        float _BreakJagScale;
        float _BreakJagAmount;
        float _BreakEdgeWidth;
        float _CrackScale;
        half _CrackWidth;
        half _CrackDarkness;
        CBUFFER_END

        TEXTURE2D(_Albedo_Map);   SAMPLER(sampler_Albedo_Map);
        TEXTURE2D(_Normal_Map);   SAMPLER(sampler_Normal_Map);
        TEXTURE2D(_Emission_Map); SAMPLER(sampler_Emission_Map);

        float Hash21(float2 p)
        {
            p = frac(p * float2(127.09, 311.7));
            p += dot(p, p + 34.45);
            return frac(p.x * p.y);
        }

        float ValueNoise(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            float2 u = f * f * (3.0 - 2.0 * f);
            float a = Hash21(i);
            float b = Hash21(i + float2(1, 0));
            float c = Hash21(i + float2(0, 1));
            float d = Hash21(i + float2(1, 1));
            return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
        }

        // _BreakAxis 기준의 직교 좌표계. 파단선 노이즈와 균열 좌표를
        // UV 대신 오브젝트 공간에서 계산하기 위해 사용합니다.
        // (Synty 아틀라스 UV는 부위별로 한 점에 몰려 있어 노이즈 좌표로 쓸 수 없습니다.)
        void GetBreakBasis(out float3 axis, out float3 tangent, out float3 bitangent)
        {
            axis = normalize(_BreakAxis.xyz);
            float3 helper = abs(axis.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0);
            tangent = normalize(cross(axis, helper));
            bitangent = cross(axis, tangent);
        }

        // 파단선 기준 부호 거리. 양수면 잘려 나간 쪽(클리핑 대상)입니다.
        float BreakSignedDistance(float3 positionOS)
        {
            float3 axis, tangent, bitangent;
            GetBreakBasis(axis, tangent, bitangent);

            float alongAxis = dot(positionOS, axis);
            float2 crossSection = float2(dot(positionOS, tangent), dot(positionOS, bitangent));
            float jag = ValueNoise(crossSection * _BreakJagScale) * 2.0 - 1.0;
            return alongAxis - (_BreakHeight + jag * _BreakJagAmount);
        }

        // 모든 패스(포워드/그림자/깊이)에서 동일하게 호출해야
        // 부러진 부분이 그림자와 깊이 버퍼에서도 함께 사라집니다.
        void ApplyBreakClip(float3 positionOS)
        {
            if (_DamageAmount > 0.001)
            {
                clip(-BreakSignedDistance(positionOS));
            }
        }

        // 힘을 잃은 검의 불규칙한 깜빡임. 느린 맥동 + 가끔 훅 꺼지는 드롭아웃 조합입니다.
        float BrokenFlicker()
        {
            float t = _Time.y * _FlickerSpeed;
            float slow = ValueNoise(float2(t * 0.45, 3.7));
            float fast = ValueNoise(float2(t * 2.1, 9.3));
            float pulse = 0.55 + 0.45 * slow;
            float dropout = lerp(0.15, 1.0, smoothstep(0.1, 0.3, fast));
            return lerp(1.0, saturate(pulse * dropout), _FlickerStrength);
        }

        // 칼날 발광부에 넣을 균열 마스크(1 = 균열 중심).
        float CrackMask(float3 positionOS)
        {
            float3 axis, tangent, bitangent;
            GetBreakBasis(axis, tangent, bitangent);

            float2 p = float2(
                dot(positionOS, axis),
                dot(positionOS, tangent) + dot(positionOS, bitangent) * 0.7);
            p *= _CrackScale;

            float n = ValueNoise(p) * 0.65 + ValueNoise(p * 2.31 + 17.19) * 0.35;
            return 1.0 - smoothstep(0.0, _CrackWidth, abs(n - 0.5));
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull Off

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
                float3 positionOS : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
                float4 tangentWS : TEXCOORD4;
                half fogFactor : TEXCOORD5;
#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord : TEXCOORD6;
#endif
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 7);
#if defined(USE_APV_PROBE_OCCLUSION)
                float4 probeOcclusion : TEXCOORD8;
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
                output.positionOS = input.positionOS.xyz;
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

            half4 LitFragment(Varyings input, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 albedo = SAMPLE_TEXTURE2D(_Albedo_Map, sampler_Albedo_Map, input.uv) * _BaseColor;
                clip(albedo.a - _Alpha_Clip_Threshold);

                float breakDistance = BreakSignedDistance(input.positionOS);
                bool isDamaged = _DamageAmount > 0.001;
                if (isDamaged)
                {
                    clip(-breakDistance);
                }

                half4 emissionSample = SAMPLE_TEXTURE2D(_Emission_Map, sampler_Emission_Map, input.uv);

                // 칼자루처럼 발광이 없는 부위는 균열/발광 연출에서 제외해 원래 색(검은색)을 유지합니다.
                half bladeMask = saturate(max(emissionSample.r, max(emissionSample.g, emissionSample.b)));

                half crack = CrackMask(input.positionOS) * _DamageAmount * bladeMask;
                half flicker = BrokenFlicker();

                // 균열 자리는 발광층이 떨어져 나간 것처럼 표면도 어둡게
                albedo.rgb *= 1.0 - crack * 0.65;

                half3 bladeEmission = half3(0, 0, 0);
                if (_Enable_Emission > 0.5)
                {
                    bladeEmission = emissionSample.rgb * _Emission_Color.rgb;
                    bladeEmission *= lerp(1.0, _BrokenLightStrength * flicker, _DamageAmount);
                    bladeEmission *= 1.0 - crack * _CrackDarkness;
                }

                half3 edgeEmission = half3(0, 0, 0);
                if (isDamaged)
                {
                    half edge = 1.0 - smoothstep(0.0, _BreakEdgeWidth, -breakDistance);
                    edgeEmission = _BreakEdgeColor.rgb * edge * lerp(0.6, 1.0, flicker) * _DamageAmount;
                }

                half4 normalSample = SAMPLE_TEXTURE2D(_Normal_Map, sampler_Normal_Map, input.uv);
                half3 normalTS = UnpackNormalScale(normalSample, _Normal_Amount);

                float3 bitangentWS = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                half3x3 tangentToWorld = half3x3(
                    (half3)input.tangentWS.xyz, (half3)bitangentWS, (half3)input.normalWS);
                float3 normalWS = normalize(TransformTangentToWorld(normalTS, tangentToWorld));

                // 클리핑으로 뚫린 단면에서 보이는 안쪽 면은 어두운 속살로 처리합니다.
                bool isFront = IS_FRONT_VFACE(frontFace, true, false);
                if (!isFront)
                {
                    normalWS = -normalWS;
                    albedo.rgb *= 0.22;
                    bladeEmission *= 0.1;
                }

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

                // 프로브 볼륨(APV)을 쓰는 씬에서는 SampleSH만으로는 주변광을 받지 못해
                // 실내에서 거의 검게 나옵니다. URP Lit과 동일한 경로로 GI를 샘플링합니다.
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
                surfaceData.emission = bladeEmission + edgeEmission;
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
            Cull Off

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
                float3 positionOS : TEXCOORD1;
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
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half4 ShadowFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half alpha = SAMPLE_TEXTURE2D(_Albedo_Map, sampler_Albedo_Map, input.uv).a * _BaseColor.a;
                clip(alpha - _Alpha_Clip_Threshold);
                ApplyBreakClip(input.positionOS);
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
            Cull Off

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
                float3 positionOS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthOnlyVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _Albedo_Map);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half4 DepthOnlyFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half alpha = SAMPLE_TEXTURE2D(_Albedo_Map, sampler_Albedo_Map, input.uv).a * _BaseColor.a;
                clip(alpha - _Alpha_Clip_Threshold);
                ApplyBreakClip(input.positionOS);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Off

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
                float3 positionOS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _Albedo_Map);
                output.positionOS = input.positionOS.xyz;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half alpha = SAMPLE_TEXTURE2D(_Albedo_Map, sampler_Albedo_Map, input.uv).a * _BaseColor.a;
                clip(alpha - _Alpha_Clip_Threshold);
                ApplyBreakClip(input.positionOS);
                return half4(normalize(input.normalWS) * 0.5 + 0.5, 0);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
