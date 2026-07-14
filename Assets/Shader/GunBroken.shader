// 망가진 총 표현용 URP 셰이더.
// SwordBroken과 같은 계열로, Synty Generic_Basic 셰이더그래프와 같은 프로퍼티 이름을 사용해
// 기존 무기 머티리얼(PolygonMech_01_A 등)의 텍스처를 그대로 옮겨 쓸 수 있습니다.
//
// 칼처럼 한 곳을 크게 부러뜨리는 대신, 총 전체 표면에 파손 레이어를 겹칩니다.
//  1. 균열   : 오브젝트 공간 노이즈로 검은 균열 라인 + 주변 그을음(AO 느낌)을 넣습니다.
//  2. 녹     : 노이즈 마스크로 녹 색을 블렌딩하고, 균열 주변으로 번지게 합니다.
//  3. 뜯김   : 작은 노이즈 스팟을 클리핑해 표면이 군데군데 뜯겨 나간 실루엣을 만들고,
//              뚫린 안쪽 면은 어두운 속살로 렌더링합니다.
//  4. 약한 빛: 발광부(Emission Map이 밝은 곳)가 어두워지고 불규칙하게 깜빡입니다.
// 모든 노이즈는 UV가 아닌 오브젝트 공간 좌표로 계산합니다.
// (Synty 아틀라스 UV는 부위별로 한 점에 몰려 있어 노이즈 좌표로 쓸 수 없습니다.)
Shader "ProjectS/GunBroken"
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

        [Header(Damage)]
        _DamageAmount("Damage Amount", Range(0, 1)) = 1
        _BrokenLightStrength("Broken Light Strength", Range(0, 1)) = 0.2
        _FlickerSpeed("Flicker Speed", Float) = 6
        _FlickerStrength("Flicker Strength", Range(0, 1)) = 0.7

        [Header(Cracks)]
        _CrackScale("Crack Scale", Float) = 6
        _CrackWidth("Crack Width", Range(0.005, 0.3)) = 0.05
        _CrackDarkness("Crack Darkness", Range(0, 1)) = 0.85

        [Header(Rust)]
        _RustColor("Rust Color", Color) = (0.45, 0.2, 0.07, 1)
        _RustScale("Rust Scale", Float) = 5
        _RustAmount("Rust Amount", Range(0, 1)) = 0.45

        [Header(Chips)]
        _ChipScale("Chip Scale", Float) = 12
        _ChipAmount("Chip Amount", Range(0, 1)) = 0.5
        _ChipEdgeWidth("Chip Edge Width", Range(0.005, 0.2)) = 0.05
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
        half4 _RustColor;
        half _Normal_Amount;
        half _Metallic;
        half _Smoothness;
        half _Alpha_Clip_Threshold;
        half _Enable_Emission;
        half _DamageAmount;
        half _BrokenLightStrength;
        float _FlickerSpeed;
        half _FlickerStrength;
        float _CrackScale;
        half _CrackWidth;
        half _CrackDarkness;
        float _RustScale;
        half _RustAmount;
        float _ChipScale;
        half _ChipAmount;
        half _ChipEdgeWidth;
        CBUFFER_END

        TEXTURE2D(_Albedo_Map);   SAMPLER(sampler_Albedo_Map);
        TEXTURE2D(_Normal_Map);   SAMPLER(sampler_Normal_Map);
        TEXTURE2D(_Emission_Map); SAMPLER(sampler_Emission_Map);

        float Hash31(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.zyx + 31.32);
            return frac((p.x + p.y) * p.z);
        }

        float ValueNoise3D(float3 p)
        {
            float3 i = floor(p);
            float3 f = frac(p);
            float3 u = f * f * (3.0 - 2.0 * f);

            float n000 = Hash31(i);
            float n100 = Hash31(i + float3(1, 0, 0));
            float n010 = Hash31(i + float3(0, 1, 0));
            float n110 = Hash31(i + float3(1, 1, 0));
            float n001 = Hash31(i + float3(0, 0, 1));
            float n101 = Hash31(i + float3(1, 0, 1));
            float n011 = Hash31(i + float3(0, 1, 1));
            float n111 = Hash31(i + float3(1, 1, 1));

            float nx00 = lerp(n000, n100, u.x);
            float nx10 = lerp(n010, n110, u.x);
            float nx01 = lerp(n001, n101, u.x);
            float nx11 = lerp(n011, n111, u.x);
            float nxy0 = lerp(nx00, nx10, u.y);
            float nxy1 = lerp(nx01, nx11, u.y);
            return lerp(nxy0, nxy1, u.z);
        }

        float Fbm3(float3 p)
        {
            return ValueNoise3D(p) * 0.65 + ValueNoise3D(p * 2.37 + 13.7) * 0.35;
        }

        // 균열 마스크. core는 균열 라인 자체(1 = 균열 중심),
        // halo는 균열 주변 그을음/때가 낀 영역입니다.
        void CrackMasks(float3 positionOS, out half core, out half halo)
        {
            float n = Fbm3(positionOS * _CrackScale);
            float d = abs(n - 0.5);
            core = 1.0 - smoothstep(0.0, _CrackWidth, d);
            halo = 1.0 - smoothstep(0.0, _CrackWidth * 3.5, d);
        }

        // 뜯김 부호 거리. 양수면 뜯겨 나간 영역(클리핑 대상)입니다.
        float ChipSignedDistance(float3 positionOS)
        {
            float threshold = 1.0 - _ChipAmount * 0.35;
            return ValueNoise3D(positionOS * _ChipScale + 51.3) - threshold;
        }

        // 모든 패스(포워드/그림자/깊이)에서 동일하게 호출해야
        // 뜯긴 부분이 그림자와 깊이 버퍼에서도 함께 사라집니다.
        void ApplyChipClip(float3 positionOS)
        {
            if (_DamageAmount > 0.001)
            {
                clip(-ChipSignedDistance(positionOS));
            }
        }

        // 힘을 잃은 발광부의 불규칙한 깜빡임. 느린 맥동 + 가끔 훅 꺼지는 드롭아웃 조합입니다.
        float BrokenFlicker()
        {
            float t = _Time.y * _FlickerSpeed;
            float slow = ValueNoise3D(float3(t * 0.45, 3.7, 0));
            float fast = ValueNoise3D(float3(t * 2.1, 9.3, 0));
            float pulse = 0.55 + 0.45 * slow;
            float dropout = lerp(0.15, 1.0, smoothstep(0.1, 0.3, fast));
            return lerp(1.0, saturate(pulse * dropout), _FlickerStrength);
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

                half damage = _DamageAmount;
                bool isDamaged = damage > 0.001;

                float chipDistance = ChipSignedDistance(input.positionOS);
                if (isDamaged)
                {
                    clip(-chipDistance);
                }

                half crackCore, crackHalo;
                CrackMasks(input.positionOS, crackCore, crackHalo);
                crackCore *= damage;
                crackHalo *= damage;

                // 녹: 자체 노이즈 패치 + 균열 주변으로 번지는 성분
                float rustNoise = Fbm3(input.positionOS * _RustScale + 7.31);
                half rust = smoothstep(1.0 - _RustAmount, 1.0 - _RustAmount + 0.25, rustNoise);
                rust = max(rust, crackHalo * 0.5);
                rust *= damage;
                half rustTone = 0.6 + 0.4 * ValueNoise3D(input.positionOS * _RustScale * 3.7);

                // 뜯긴 구멍 가장자리의 그을린 테두리
                half chipRim = 0;
                if (isDamaged)
                {
                    chipRim = 1.0 - smoothstep(0.0, _ChipEdgeWidth, -chipDistance);
                }

                // 표면 합성: 그을음 -> 녹 -> 균열 라인 -> 뜯김 테두리 순서로 겹칩니다.
                albedo.rgb *= 1.0 - crackHalo * 0.35;
                albedo.rgb = lerp(albedo.rgb, _RustColor.rgb * rustTone, rust);
                albedo.rgb *= 1.0 - crackCore * 0.85;
                albedo.rgb *= 1.0 - chipRim * 0.6 * damage;

                half4 emissionSample = SAMPLE_TEXTURE2D(_Emission_Map, sampler_Emission_Map, input.uv);
                half flicker = BrokenFlicker();

                half3 emission = half3(0, 0, 0);
                if (_Enable_Emission > 0.5)
                {
                    emission = emissionSample.rgb * _Emission_Color.rgb;
                    emission *= lerp(1.0, _BrokenLightStrength * flicker, damage);
                    emission *= 1.0 - crackCore * _CrackDarkness;
                    emission *= 1.0 - rust * 0.5;
                }

                half4 normalSample = SAMPLE_TEXTURE2D(_Normal_Map, sampler_Normal_Map, input.uv);
                half3 normalTS = UnpackNormalScale(normalSample, _Normal_Amount);

                float3 bitangentWS = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                half3x3 tangentToWorld = half3x3(
                    (half3)input.tangentWS.xyz, (half3)bitangentWS, (half3)input.normalWS);
                float3 normalWS = normalize(TransformTangentToWorld(normalTS, tangentToWorld));

                // 클리핑으로 뚫린 구멍에서 보이는 안쪽 면은 어두운 속살로 처리합니다.
                bool isFront = IS_FRONT_VFACE(frontFace, true, false);
                if (!isFront)
                {
                    normalWS = -normalWS;
                    albedo.rgb *= 0.22;
                    emission *= 0.1;
                }

                // 녹슬고 그을린 표면은 광택도 함께 죽입니다.
                half wear = max(rust, crackCore);
                half smoothness = _Smoothness * (1.0 - wear * 0.7);

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
                surfaceData.smoothness = smoothness;
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
                ApplyChipClip(input.positionOS);
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
                ApplyChipClip(input.positionOS);
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
                ApplyChipClip(input.positionOS);
                return half4(normalize(input.normalWS) * 0.5 + 0.5, 0);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
