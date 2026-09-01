// Synty 로우폴리 프롭을 "삭은" 상태로 보이게 하는 URP Lit 셰이더.
//
// 왜 트라이플래너인가:
//   Synty는 2048 아틀라스에 여러 오브젝트의 UV가 뭉쳐 있어서, UV 기준으로 디테일 맵을 깔면
//   오브젝트마다 타일링 스케일이 제각각이 되고 아틀라스 옆칸 색이 새어 들어온다.
//   그래서 때/녹/스크래치는 전부 UV가 아니라 오브젝트 공간 좌표에 투영한다.
//   오브젝트 공간을 쓰는 이유는 포탈이 이동/회전해도 때가 같이 따라가야 하기 때문
//   (월드 공간이면 오브젝트를 옮길 때 때만 제자리에 남아 흘러내린다).
//
// 왜 발광부를 보호하는가:
//   때 레이어를 전면에 곱하면 청록 발광 라인 위에도 녹이 얹혀 빛이 탁해진다.
//   Emissive 맵 밝기로 마스크를 만들어 그 부분만 때를 빼준다.
//
// 원본 머티리얼(PolygonScifiSpace_SpaceStation_01)은 건드리지 않는다.
// 이 셰이더용 머티리얼을 따로 만들어 대상 오브젝트에만 끼운다.
Shader "ProjectS/Environment/SyntyWeathering"
{
    Properties
    {
        [Header(Base)]
        [MainTexture] _Texture_Map ("Albedo (Synty Atlas)", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Tint", Color) = (1, 1, 1, 1)
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.526

        [Header(Emissive)]
        // 베이스와 같은 UV를 쓰므로 자체 Tiling/Offset은 의미가 없다.
        [NoScaleOffset] _Emissive_Map ("Emissive Map", 2D) = "black" {}
        [HDR] _Emissive_Color ("Emissive Color", Color) = (1, 1, 1, 1)
        // ★ 기본값 0. Synty 이미시브 아틀라스 하단에는 흰/주황/파랑/초록/빨강/청록/마젠타
        //   "이미시브 팔레트 띠"가 있고, WarpGate 링 패널의 UV가 하필 그 띠에 꽂힌다.
        //   그래서 이걸 올리면 링이 노랑/마젠타로 번쩍인다. 원본 Synty 머티리얼도
        //   Emissive Color 알파를 0으로 두어 이 모델의 이미시브를 꺼놨다.
        //   게이트의 청록 글로우는 이 맵이 아니라 별도 메시(SM_Veh_WarpGate_Glow_01)가 낸다.
        //   다른 Synty 프롭(모니터/간판 등)에 이 셰이더를 쓸 때만 올린다.
        _Emissive_Intensity ("Emissive Intensity", Range(0, 20)) = 0
        // 발광부 주변을 때에서 얼마나 지켜줄지. 0이면 발광 라인에도 녹이 얹힌다.
        _Emissive_Protect ("Emissive Protect", Range(0, 1)) = 0.9

        [Header(Grunge Source)]
        // Synty 팩에 딸려오는 Dirt_01 을 넣으면 된다. 비워두면(흰색) 절차적 노이즈만으로 동작.
        // 트라이플래너로 투영하므로 Tiling/Offset 대신 아래 _GrungeTiling 을 쓴다.
        [NoScaleOffset] _GrungeMap ("Grunge Map (Triplanar)", 2D) = "white" {}
        _GrungeTiling ("Grunge Tiling", Range(0.01, 2)) = 0.35
        // 세로로 늘여 흘러내린 자국을 만든다. 1이면 늘이지 않음.
        _StreakStretch ("Streak Stretch", Range(1, 8)) = 3

        [Header(Rust)]
        _RustColor ("Rust Color", Color) = (0.29, 0.15, 0.08, 1)
        _RustAmount ("Rust Amount", Range(0, 1)) = 0.55
        // 오브젝트 공간 Y 기준. 이 높이보다 아래쪽이 더 심하게 삭는다.
        _RustHeightBase ("Rust Height Base", Float) = 0
        _RustHeightFalloff ("Rust Height Falloff", Range(0, 2)) = 0.35
        // 빨간 도장면이 먼저 삭는 느낌. 아틀라스 색으로 판별하므로 마스크 텍스처가 필요 없다.
        _RustOnPaint ("Rust On Red Paint", Range(0, 1)) = 0.7

        [Header(Dust)]
        _DirtColor ("Dust Color", Color) = (0.32, 0.29, 0.25, 1)
        _DirtAmount ("Dust Amount", Range(0, 1)) = 0.4
        // 위를 보는 면에만 쌓이게 하는 정도. 낮추면 전면에 균일하게 낀다.
        _DirtUpBias ("Dust Up Bias", Range(0.01, 8)) = 2.5

        [Header(Surface Detail)]
        // 미세 요철/스크래치. 노멀맵이 없으므로 노이즈 기울기로 굴곡을 만든다.
        _ScratchAmount ("Scratch Amount", Range(0, 1)) = 0.35
        _ScratchTiling ("Scratch Tiling", Range(1, 60)) = 18
        _NormalStrength ("Normal Strength", Range(0, 2)) = 0.6

        [Header(Tone)]
        // 전체 채도/밝기를 눌러 원본의 쨍한 팔레트를 가라앉힌다.
        _Desaturation ("Desaturation", Range(0, 1)) = 0.25
        _Darken ("Darken", Range(0, 1)) = 0.15
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _Texture_Map_ST;
            half4 _BaseColor;
            half _Metallic;
            half _Smoothness;

            half4 _Emissive_Color;
            half _Emissive_Intensity;
            half _Emissive_Protect;

            float _GrungeTiling;
            float _StreakStretch;

            half4 _RustColor;
            half _RustAmount;
            float _RustHeightBase;
            float _RustHeightFalloff;
            half _RustOnPaint;

            half4 _DirtColor;
            half _DirtAmount;
            half _DirtUpBias;

            half _ScratchAmount;
            float _ScratchTiling;
            half _NormalStrength;

            half _Desaturation;
            half _Darken;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            // 프로젝트 렌더러가 Forward+ 라서 이 키워드가 없으면 추가 조명이 클러스터에서 안 잡힌다.
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_Texture_Map);    SAMPLER(sampler_Texture_Map);
            TEXTURE2D(_Emissive_Map);   SAMPLER(sampler_Emissive_Map);
            TEXTURE2D(_GrungeMap);      SAMPLER(sampler_GrungeMap);

            // ---------------------------------------------------------------
            // 절차적 노이즈: 그런지 맵보다 잔 요철이 필요할 때 쓴다.
            // ---------------------------------------------------------------
            float Hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float ValueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = Hash13(i + float3(0, 0, 0));
                float n100 = Hash13(i + float3(1, 0, 0));
                float n010 = Hash13(i + float3(0, 1, 0));
                float n110 = Hash13(i + float3(1, 1, 0));
                float n001 = Hash13(i + float3(0, 0, 1));
                float n101 = Hash13(i + float3(1, 0, 1));
                float n011 = Hash13(i + float3(0, 1, 1));
                float n111 = Hash13(i + float3(1, 1, 1));

                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);
                float nxy0 = lerp(nx00, nx10, f.y);
                float nxy1 = lerp(nx01, nx11, f.y);
                return lerp(nxy0, nxy1, f.z);
            }

            // 3 옥타브면 잔 요철 표현에 충분하다. 더 늘리면 프롭 하나 치고는 비싸진다.
            float Fbm(float3 p)
            {
                float v = 0.0;
                float a = 0.5;
                [unroll]
                for (int i = 0; i < 3; i++)
                {
                    v += a * ValueNoise(p);
                    p *= 2.03;
                    a *= 0.5;
                }
                return v;
            }

            // 오브젝트 공간 트라이플래너. UV를 쓰지 않으므로 아틀라스 옆칸이 새지 않는다.
            float SampleTriplanarGrunge(float3 posOS, float3 normOS, float tiling, float stretch)
            {
                float3 blend = pow(abs(normOS), 4.0);
                blend /= max(dot(blend, float3(1, 1, 1)), 1e-4);

                // 옆면(X/Z 투영)만 Y를 늘여 흘러내린 자국을 만든다. 윗면(Y 투영)은 늘이지 않는다.
                float2 uvX = float2(posOS.z, posOS.y / stretch) * tiling;
                float2 uvY = posOS.xz * tiling;
                float2 uvZ = float2(posOS.x, posOS.y / stretch) * tiling;

                float x = SAMPLE_TEXTURE2D(_GrungeMap, sampler_GrungeMap, uvX).r;
                float y = SAMPLE_TEXTURE2D(_GrungeMap, sampler_GrungeMap, uvY).r;
                float z = SAMPLE_TEXTURE2D(_GrungeMap, sampler_GrungeMap, uvZ).r;

                return x * blend.x + y * blend.y + z * blend.z;
            }

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float2 uv           : TEXCOORD0;
                float2 lightmapUV   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float3 positionWS   : TEXCOORD1;
                float3 normalWS     : TEXCOORD2;
                float3 positionOS   : TEXCOORD3;
                float3 normalOS     : TEXCOORD4;
                float4 fogAndVertexLight : TEXCOORD5;
                // 매크로 정의에 세미콜론이 없다. 빼면 뒤 멤버와 붙어 구문 에러가 난다.
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 6);
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(input.normalOS);

                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = nrm.normalWS;
                output.positionOS = input.positionOS.xyz;
                output.normalOS = input.normalOS;
                output.uv = TRANSFORM_TEX(input.uv, _Texture_Map);

                half3 vertexLight = VertexLighting(pos.positionWS, nrm.normalWS);
                output.fogAndVertexLight = float4(ComputeFogFactor(pos.positionCS.z), vertexLight);

                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
                OUTPUT_SH(nrm.normalWS, output.vertexSH);

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 baseTex = SAMPLE_TEXTURE2D(_Texture_Map, sampler_Texture_Map, input.uv);
                half3 albedo = baseTex.rgb * _BaseColor.rgb;

                half3 emissiveTex = SAMPLE_TEXTURE2D(_Emissive_Map, sampler_Emissive_Map, input.uv).rgb;
                half emissiveMask = saturate(Luminance(emissiveTex) * 4.0);

                float3 normOS = normalize(input.normalOS);
                float3 normWS = normalize(input.normalWS);

                // --- 마스크 ---
                float grunge = SampleTriplanarGrunge(input.positionOS, normOS, _GrungeTiling, _StreakStretch);
                float fine = Fbm(input.positionOS * _ScratchTiling);

                // 아래쪽일수록 녹이 심하다. 물이 고이고 흙이 튀는 쪽이라는 관찰에서 온 값.
                float lowness = saturate(1.0 - (input.positionOS.y - _RustHeightBase) * _RustHeightFalloff);

                // 팔레트 아틀라스라 색 종류가 몇 개 안 된다. 그래서 색 비교만으로 "빨간 도장면"이 갈린다.
                float redness = saturate((baseTex.r - max(baseTex.g, baseTex.b)) * 4.0);

                float upFacing = pow(saturate(normWS.y), _DirtUpBias);

                // 발광부 보호: 청록 라인 위에 녹이 얹히면 빛이 탁해진다.
                float protect = 1.0 - emissiveMask * _Emissive_Protect;

                float rustMask = saturate(grunge * (lowness + redness * _RustOnPaint)) * _RustAmount * protect;
                float dirtMask = saturate(grunge * upFacing) * _DirtAmount * protect;

                // --- 알베도 합성 ---
                albedo = lerp(albedo, _RustColor.rgb, rustMask);
                albedo = lerp(albedo, _DirtColor.rgb, dirtMask);
                albedo *= 1.0 - fine * _ScratchAmount * 0.5;

                half lum = Luminance(albedo);
                albedo = lerp(albedo, half3(lum, lum, lum), _Desaturation);
                albedo *= 1.0 - _Darken;

                // --- 표면 물성 ---
                // 녹슨 곳은 거칠고 금속감이 죽는다. 이게 없으면 삭은 색인데 반짝여서 어색하다.
                half smoothness = _Smoothness * (1.0 - rustMask * 0.9) * (1.0 - dirtMask * 0.7);
                half metallic = _Metallic * (1.0 - rustMask);

                // --- 노멀 섭동 ---
                // 노멀맵이 없으므로 노이즈 기울기를 직접 재서 미세 굴곡을 만든다.
                // 녹슨 곳(rustMask)일수록 더 우둘투둘하게 한다.
                float e = 0.35 / max(_ScratchTiling, 0.001);
                float nx = Fbm((input.positionOS + float3(e, 0, 0)) * _ScratchTiling) - fine;
                float ny = Fbm((input.positionOS + float3(0, e, 0)) * _ScratchTiling) - fine;
                float nz = Fbm((input.positionOS + float3(0, 0, e)) * _ScratchTiling) - fine;
                float3 bumpOS = float3(nx, ny, nz) * _NormalStrength * (0.5 + rustMask);
                float3 bumpWS = TransformObjectToWorldDir(bumpOS, false);
                normWS = normalize(normWS + bumpWS);

                // --- 라이팅 ---
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                // Forward+ 클러스터 조회가 이 값을 쓴다. 빠지면 추가 조명이 엉뚱하게 잡힌다.
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normWS;
                inputData.viewDirectionWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogAndVertexLight.x;
                inputData.vertexLighting = input.fogAndVertexLight.yzw;
                inputData.bakedGI = SAMPLE_GI(input.lightmapUV, input.vertexSH, normWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = SAMPLE_SHADOWMASK(input.lightmapUV);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.metallic = metallic;
                surfaceData.specular = half3(0, 0, 0);
                surfaceData.smoothness = smoothness;
                surfaceData.normalTS = half3(0, 0, 1);
                surfaceData.emission = emissiveTex * _Emissive_Color.rgb * _Emissive_Intensity;
                surfaceData.occlusion = 1.0;
                surfaceData.alpha = 1.0;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
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
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
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
                return output;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
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
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthFrag(DepthVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // SSAO(Screen Space Ambient Occlusion)가 이 오브젝트를 인식하려면 이 패스가 필요하다.
        // 빠지면 패널 틈에 그늘이 안 껴서 "삭은 느낌"의 절반이 날아간다.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DNAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DNVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DNVaryings DepthNormalsVert(DNAttributes input)
            {
                DNVaryings output = (DNVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFrag(DNVaryings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
