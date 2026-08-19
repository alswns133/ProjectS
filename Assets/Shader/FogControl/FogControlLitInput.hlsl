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
    // 높이 그라디언트. 월드 좌표를 비교하는 값이라 Bottom/Top Y는 half가 아니라 float입니다
    // (맵이 넓어지면 half 정밀도로는 계단이 보입니다).
    half _HeightGradientStrength;
    float _HeightGradientBottomY;
    float _HeightGradientTopY;
    half _HeightGradientPower;
    half4 _HeightGradientTint;
    half4 _HeightGradientEmission;
    half _HeightGradientWallMask;
    float _HeightGradientNoiseAmount;
    float _HeightGradientNoiseScale;
    half _HeightGradientDither;
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

// 값 노이즈. 그라디언트 경계선이 완벽한 수평선으로 보이지 않게 높이를 흔드는 데만 씁니다.
// 텍스처를 추가로 물리지 않으려고 해시 기반으로 계산합니다(벽 한 면당 몇 번 안 불리는 비용).
float HeightGradientHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float HeightGradientValueNoise(float2 uv)
{
    float2 i = floor(uv);
    float2 f = frac(uv);
    f = f * f * (3.0 - 2.0 * f);

    float a = HeightGradientHash(i);
    float b = HeightGradientHash(i + float2(1.0, 0.0));
    float c = HeightGradientHash(i + float2(0.0, 1.0));
    float d = HeightGradientHash(i + float2(1.0, 1.0));

    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

/// 바닥에 가까울수록 밝아지는 높이 그라디언트를 서피스 값에 얹습니다.
/// 월드 Y를 [Top -> Bottom] 구간으로 정규화해 바닥에서 1, 윗단에서 0인 h를 만든 뒤
/// 알베도에 Tint를 섞어 곱하고 Emission을 더합니다.
///
/// 램프는 선형이 아니라 smoothstep입니다. 선형(또는 saturate된 pow)은 Top Y 지점에서
/// 기울기가 뚝 꺾여 벽에 가로줄 하나가 그대로 보입니다. smoothstep은 양 끝 기울기가 0이라
/// 어디서 시작하고 끝나는지 눈에 잡히지 않습니다. Power는 그 위에서 밝은 구간의 두께만 조절합니다.
///
/// 알베도 곱은 조명을 받는 값이라 그늘진 벽에서는 잘 드러나지 않습니다. 어두운 던전에서
/// 확실히 밝히려면 Bottom Emission 쪽을 올리세요.
/// _HeightGradientStrength가 0이면 h가 0이 되어 아무 일도 하지 않습니다
/// (이 셰이더를 이미 쓰고 있는 머티리얼이 영향을 받지 않게 하기 위한 기본값).
/// 월드 좌표가 필요해서 InitializeStandardLitSurfaceData(uv만 받음) 안이 아니라
/// 프래그먼트에서 따로 호출합니다. 그래서 Meta 패스(라이트맵 굽기)에는 반영되지 않습니다 —
/// 연출용 보정이지 GI에 섞일 값이 아니므로 의도된 동작입니다.
///
/// <param name="normalWS">노말맵이 아니라 정점 노말을 넘깁니다. 벽 마스크는 면의 방향을
/// 보려는 것이라, 노말맵 요철까지 섞이면 마스크가 지저분하게 얼룩집니다.</param>
/// <param name="positionSS">디더용 픽셀 좌표(SV_POSITION의 xy).</param>
void ApplyHeightGradient(inout SurfaceData surfaceData, float3 positionWS, float3 normalWS, float2 positionSS)
{
    // 경계가 자로 그은 수평선이 되지 않게 월드 XZ 노이즈로 높이를 흔듭니다.
    float sampleY = positionWS.y;
    if (_HeightGradientNoiseAmount > 0.0)
    {
        float n = HeightGradientValueNoise(positionWS.xz * _HeightGradientNoiseScale);
        sampleY += (n - 0.5) * _HeightGradientNoiseAmount;
    }

    float range = max(_HeightGradientTopY - _HeightGradientBottomY, 1e-4);
    half h = saturate((_HeightGradientTopY - sampleY) / range);
    h = smoothstep(0.0h, 1.0h, h);
    h = pow(h, _HeightGradientPower);

    // 수직면일수록 1. 바닥·천장(normal.y = ±1)은 0이 되어 그라디언트에서 빠집니다.
    half wallness = saturate(1.0h - abs(normalWS.y));
    h *= lerp(1.0h, wallness, _HeightGradientWallMask);

    h *= _HeightGradientStrength;

    // 8bit 출력에서 생기는 띠를 픽셀 단위 노이즈로 흩습니다(interleaved gradient noise).
    // 곱하기 전 h에 더해야 밝기와 무관하게 일정한 세기로 먹습니다.
    // 픽셀 좌표는 1920까지 가는 값이라 half로 계산하면 정밀도가 뭉개져 패턴이 무너집니다. float 고정.
    float ign = frac(52.9829189 * frac(dot(positionSS, float2(0.06711056, 0.00583715))));
    h = saturate(h + (half)(ign - 0.5) * _HeightGradientDither);

    surfaceData.albedo *= lerp(half3(1.0h, 1.0h, 1.0h), _HeightGradientTint.rgb, h);
    surfaceData.emission += _HeightGradientEmission.rgb * h;
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
