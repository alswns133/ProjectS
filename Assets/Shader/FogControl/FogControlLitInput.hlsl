#ifndef PROJECTS_FOG_CONTROL_LIT_INPUT_INCLUDED
#define PROJECTS_FOG_CONTROL_LIT_INPUT_INCLUDED

// Synty Generic_Basic.shadergraph(= PolygonScifiWorlds_02_C.mat 이 쓰는 셰이더)와
// 프로퍼티 이름을 똑같이 맞추기 위한 별칭입니다.
// 이름을 맞춰두면 머티리얼의 셰이더만 이 셰이더로 바꿔도 텍스처/색 연결이 그대로 살아 있고,
// URP가 제공하는 공용 패스(ShadowCaster / DepthOnly / DepthNormals / Meta)는
// _BaseMap, _BumpMap 같은 고정 이름을 참조하므로 별칭만으로 그대로 재사용할 수 있습니다.
// (별칭을 빼면 그림자·깊이 패스를 전부 직접 다시 써야 합니다.)
#define _BaseMap                _Albedo_Map
#define sampler_BaseMap         sampler_Albedo_Map
#define _BaseMap_ST             _Albedo_Map_ST
#define _BumpMap                _Normal_Map
#define sampler_BumpMap         sampler_Normal_Map
#define _EmissionMap            _Emission_Map
#define sampler_EmissionMap     sampler_Emission_Map
#define _BumpScale              _Normal_Amount
#define _Cutoff                 _Alpha_Clip_Threshold

// Synty 원본 그래프는 노말맵을 항상 샘플링합니다. 키워드로 가르면 셰이더를 갈아끼운
// 머티리얼에 _NORMALMAP 키워드가 없어 노말이 통째로 빠지므로, 여기서는 항상 켭니다.
#define _NORMALMAP 1

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"

// SRP Batcher가 동작하려면 HLSL에서 읽는 머티리얼 값이 전부 이 버퍼에 있어야 합니다
// (_Cull, _AlphaClip처럼 렌더 스테이트/키워드로만 쓰이는 값은 넣지 않습니다 — URP Lit과 동일).
// 변형(variant)마다 레이아웃이 달라지면 배칭이 깨지므로 #ifdef로 감싸지 않습니다.
CBUFFER_START(UnityPerMaterial)
    float4 _Albedo_Map_ST;
    half4 _BaseColor;
    half4 _Emission_Color;
    half _Enable_Emission;
    half _Normal_Amount;
    half _Smoothness;
    half _Metallic;
    half _Alpha_Clip_Threshold;
    half _FogStrength;
    half _Surface;
    UNITY_TEXTURE_STREAMING_DEBUG_VARS;
CBUFFER_END

/// 이 셰이더의 핵심. 씬 안개를 그대로 먹인 색과 원래 색을 _FogStrength로 섞습니다.
/// 0 = 이 재질만 안개를 완전히 무시, 1 = 씬 설정 그대로, 그 사이는 비율.
/// MixFog를 직접 호출해 결과만 보간하므로 FOG_LINEAR/EXP/EXP2 키워드나 안개 OFF 상태,
/// URP 내부의 안개 계산 방식을 우리가 다시 구현할 필요가 없습니다(버전이 올라가도 안전).
half3 MixFogStrength(half3 color, half fogCoord)
{
    half3 fogged = MixFog(color, fogCoord);
    return lerp(color, fogged, saturate(_FogStrength));
}

/// URP 공용 패스들이 문자열이 아니라 이 이름으로 서피스 값을 가져갑니다
/// (LitMetaPass 등이 그대로 호출하므로 시그니처를 바꾸면 안 됩니다).
inline void InitializeStandardLitSurfaceData(float2 uv, out SurfaceData outSurfaceData)
{
    half4 albedoAlpha = SampleAlbedoAlpha(uv, TEXTURE2D_ARGS(_Albedo_Map, sampler_Albedo_Map));

    // Alpha()는 _ALPHATEST_ON이 켜져 있을 때만 임계값으로 clip 합니다.
    outSurfaceData.alpha = Alpha(albedoAlpha.a, _BaseColor, _Alpha_Clip_Threshold);
    outSurfaceData.albedo = albedoAlpha.rgb * _BaseColor.rgb;

    outSurfaceData.metallic = _Metallic;
    outSurfaceData.specular = half3(0.0h, 0.0h, 0.0h);
    outSurfaceData.smoothness = _Smoothness;
    outSurfaceData.normalTS = SampleNormal(uv, TEXTURE2D_ARGS(_Normal_Map, sampler_Normal_Map), _Normal_Amount);
    outSurfaceData.occlusion = 1.0h;

    // Synty 그래프의 Enable Emission(Branch)과 같은 동작을 0/1 곱으로 대신합니다.
    // 키워드(_EMISSION)로 가르지 않는 이유는 위 _NORMALMAP과 같습니다.
    outSurfaceData.emission = SAMPLE_TEXTURE2D(_Emission_Map, sampler_Emission_Map, uv).rgb
                              * _Emission_Color.rgb * _Enable_Emission;

    outSurfaceData.clearCoatMask = 0.0h;
    outSurfaceData.clearCoatSmoothness = 0.0h;
}

#endif // PROJECTS_FOG_CONTROL_LIT_INPUT_INCLUDED
