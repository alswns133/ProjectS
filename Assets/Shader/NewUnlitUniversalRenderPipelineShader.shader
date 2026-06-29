Shader "GAPH Custom Shader/Distortion Effect/Distortion Texture/Distortion Effect(WithMask) URP"
{
    Properties
    {
        _TintColor      ("Tint Color", Color) = (1,1,1,1)
        _Mask           ("Mask", 2D) = "black" {}
        _NormalMap      ("Normalmap", 2D) = "bump" {}
        _DistortFactor  ("Distortion", Float) = 10
        _InvFade        ("Soft Particles Factor", Range(0,10)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "Queue"           = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        Lighting Off
        ZWrite Off

        Pass
        {
            Name "BASE"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            // Soft particles 키워드(SOFTPARTICLES_ON) + fog
            #pragma multi_compile_particles
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // GrabPass 대체: 카메라 Opaque Texture (_CameraOpaqueTexture, SampleSceneColor)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            // Soft particles 용 Depth Texture (_CameraDepthTexture, SampleSceneDepth)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 vertex   : POSITION;
                float2 texcoord : TEXCOORD0;
                half4  color    : COLOR;
            };

            struct Varyings
            {
                float4 vertex    : SV_POSITION;
                float4 uvgrab    : TEXCOORD0;
                float2 uvnormal  : TEXCOORD1;
                float2 uvmask    : TEXCOORD2;
                half4  color     : COLOR;
                float  fogFactor : TEXCOORD4;
                #ifdef SOFTPARTICLES_ON
                    float4 projPos : TEXCOORD3;
                #endif
            };

            TEXTURE2D(_Mask);       SAMPLER(sampler_Mask);
            TEXTURE2D(_NormalMap);  SAMPLER(sampler_NormalMap);

            // 글로벌(텍셀 사이즈는 Unity가 자동 주입)
            // float4 _CameraOpaqueTexture_TexelSize;

            // SRP Batcher 호환용 머티리얼 상수 버퍼
            CBUFFER_START(UnityPerMaterial)
                half4  _TintColor;
                float4 _NormalMap_ST;
                float4 _Mask_ST;
                float  _DistortFactor;
                float  _InvFade;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;

                VertexPositionInputs posInputs = GetVertexPositionInputs(v.vertex.xyz);
                o.vertex = posInputs.positionCS;

                // ComputeScreenPos 가 (xy + w) * 0.5 / UV 상하반전(_ProjectionParams.x) 처리.
                // 결과 .zw = positionCS.zw 라 원본의 uvgrab.zw 와 동일.
                o.uvgrab = ComputeScreenPos(o.vertex);

                #ifdef SOFTPARTICLES_ON
                    o.projPos    = ComputeScreenPos(o.vertex);
                    o.projPos.z  = -posInputs.positionVS.z; // eye depth (COMPUTE_EYEDEPTH 대체)
                #endif

                o.color    = v.color;
                o.uvnormal = TRANSFORM_TEX(v.texcoord, _NormalMap);
                o.uvmask   = TRANSFORM_TEX(v.texcoord, _Mask);
                o.fogFactor = ComputeFogFactor(o.vertex.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                #ifdef SOFTPARTICLES_ON
                    float2 screenUV = i.projPos.xy / i.projPos.w;
                    float  sceneZ   = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                    float  partZ    = i.projPos.z;
                    float  fade     = saturate(_InvFade * (sceneZ - partZ));
                    i.color.a *= fade;
                #endif

                // 노멀맵에서 왜곡 방향 추출
                half2 normal = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, i.uvnormal)).rg;

                // 왜곡 오프셋 = 노멀 * 강도 * 텍셀 사이즈
                half2 distortValue = normal * _DistortFactor * _CameraOpaqueTexture_TexelSize.xy;

                // DX11 / Metal 은 오프셋 스케일 보정 (원본 동작 유지)
                #if defined(SHADER_API_D3D11) || defined(SHADER_API_METAL)
                    distortValue *= 10;
                #endif

                // 원본 grab UV 에 왜곡 더하기
                i.uvgrab.xy = (distortValue * i.uvgrab.z) + i.uvgrab.xy;

                // tex2Dproj 대체: w 로 나눈 정규화 스크린 UV 로 Opaque Texture 샘플
                float2 grabUV  = i.uvgrab.xy / i.uvgrab.w;
                half4  distort = half4(SampleSceneColor(grabUV), 1.0h);

                half4 mask = SAMPLE_TEXTURE2D(_Mask, sampler_Mask, i.uvmask);

                half4 res = distort;
                res.a = _TintColor.a * i.color.a * mask.a;

                res.rgb = MixFog(res.rgb, i.fogFactor);
                return res;
            }
            ENDHLSL
        }
    }

    Fallback Off
}

