// 재질별로 안개(Fog)를 얼마나 받을지 조절할 수 있는 URP Lit 셰이더입니다.
//
// 참고 원본: Assets/ExternalAssets/Synty/PolygonSciFiWorlds/Materials/Alts/PolygonScifiWorlds_02_C.mat 이
// 쓰는 Synty의 Generic_Basic.shadergraph. 프로퍼티 이름(_Albedo_Map, _Normal_Map, _Emission_Map ...)을
// 그대로 맞춰뒀기 때문에, 머티리얼의 Shader만 이걸로 바꾸면 텍스처/색 연결이 유지됩니다.
//
// ShaderGraph로는 이 기능을 만들 수 없습니다. URP는 그래프 결과가 나온 "뒤"에 안개를 섞고,
// Lit 타깃에는 그걸 끄거나 줄이는 옵션이 없기 때문입니다. 그래서 손으로 쓴 셰이더로 만들고,
// 포워드 패스만 URP 원본을 복사해 안개 한 줄을 교체했습니다(FogControlForwardPass.hlsl).
Shader "ProjectS/Lit Fog Control"
{
    Properties
    {
        // ★ 기본값은 Generic_Basic.shadergraph의 프로퍼티 기본값과 반드시 일치시킵니다.
        //   새 머티리얼을 만들면 이 값이 그대로 들어가므로, 어긋나면 같은 텍스처를 넣어도
        //   Synty 셰이더와 다른 그림이 나옵니다(에미션이 통째로 켜지는 사고가 실제로 있었습니다).
        [MainTexture] _Albedo_Map("Albedo Map", 2D) = "white" {}
        // 알파가 0인 것은 그래프 기본값 그대로입니다. 알파는 클립에만 쓰이고
        // (불투명이라 출력 알파는 1로 고정) Alpha Clip Threshold도 0이라 아무것도 잘리지 않습니다.
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,0)

        [Normal] _Normal_Map("Normal Map", 2D) = "bump" {}
        _Normal_Amount("Normal Amount", Float) = 0.0

        _Emission_Map("Emission Map", 2D) = "white" {}
        [HDR] _Emission_Color("Emission Color", Color) = (0,0,0,0)
        [Toggle] _Enable_Emission("Enable Emission", Float) = 0.0

        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.2
        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0

        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha Clip", Float) = 1.0
        _Alpha_Clip_Threshold("Alpha Clip Threshold", Range(0.0, 1.0)) = 0.0

        [Header(Fog)]
        [Space]
        // 0 = 이 재질만 안개를 완전히 무시, 1 = 씬 Fog 설정 그대로. 그 사이는 비율.
        _FogStrength("Fog Strength", Range(0.0, 1.0)) = 1.0

        [Header(Rendering)]
        [Space]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2.0
        [ToggleOff(_RECEIVE_SHADOWS_OFF)] _ReceiveShadows("Receive Shadows", Float) = 1.0
        [ToggleOff(_SPECULARHIGHLIGHTS_OFF)] _SpecularHighlights("Specular Highlights", Float) = 1.0
        [ToggleOff(_ENVIRONMENTREFLECTIONS_OFF)] _EnvironmentReflections("Environment Reflections", Float) = 1.0

        // 불투명 전용입니다. URP 공용 코드가 _Surface를 참조하므로 값만 잡아둡니다.
        [HideInInspector] _Surface("__surface", Float) = 0.0

        [HideInInspector][NoScaleOffset]unity_Lightmaps("unity_Lightmaps", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset]unity_LightmapsInd("unity_LightmapsInd", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset]unity_ShadowMasks("unity_ShadowMasks", 2DArray) = "" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 300

        // ------------------------------------------------------------------
        //  안개 비율이 실제로 적용되는 패스. 나머지 패스는 URP 원본을 그대로 씁니다.
        Pass
        {
            Name "ForwardLit"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            ZWrite On
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex LitPassVertex
            #pragma fragment LitPassFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF

            // -------------------------------------
            // Universal Pipeline keywords (URP Lit.shader의 ForwardLit 패스와 동일하게 유지)
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fragment _ LIGHTMAP_BICUBIC_SAMPLING
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ USE_LEGACY_LIGHTMAPS
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            // 안개 키워드(FOG_LINEAR / FOG_EXP / FOG_EXP2)를 프로젝트 설정에 맞게 선언합니다.
            // 이게 빠지면 안개가 아예 계산되지 않아 _FogStrength도 의미가 없어집니다.
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            #include "FogControlLitInput.hlsl"
            #include "FogControlForwardPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags
            {
                "LightMode" = "ShadowCaster"
            }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #pragma shader_feature_local _ALPHATEST_ON

            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "FogControlLitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags
            {
                "LightMode" = "DepthOnly"
            }

            ZWrite On
            ColorMask R
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #pragma shader_feature_local _ALPHATEST_ON

            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing

            #include "FogControlLitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        // _CameraNormalsTexture(SSAO, 뎁스노말 기반 효과)용 패스입니다.
        Pass
        {
            Name "DepthNormals"
            Tags
            {
                "LightMode" = "DepthNormals"
            }

            ZWrite On
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            #pragma shader_feature_local _ALPHATEST_ON

            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #pragma multi_compile_instancing

            #include "FogControlLitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitDepthNormalsPass.hlsl"
            ENDHLSL
        }

        // 실제 렌더링에는 쓰이지 않고 라이트맵 베이킹에만 쓰입니다.
        // 마을 씬이 라이트맵을 굽고 있으므로 빠지면 이 재질의 GI 기여가 사라집니다.
        Pass
        {
            Name "Meta"
            Tags
            {
                "LightMode" = "Meta"
            }

            Cull Off

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex UniversalVertexMeta
            #pragma fragment UniversalFragmentMetaLit

            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature EDITOR_VISUALIZATION

            #include "FogControlLitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitMetaPass.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
