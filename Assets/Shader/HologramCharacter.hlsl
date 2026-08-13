#ifndef PROJECTS_HOLOGRAM_CHARACTER_INCLUDED
#define PROJECTS_HOLOGRAM_CHARACTER_INCLUDED

// HologramCharacter.shader의 두 패스(깊이 프리패스 / 컬러 패스)가 공유하는 코드.
//
// 왜 파일로 뺐나: 두 패스는 반드시 '같은' 정점 변위를 계산해야 한다. 글리치 계산이 조금이라도
// 어긋나면 프리패스가 써둔 깊이와 컬러 패스의 위치가 달라져, 글리치가 터지는 순간 캐릭터가
// 잘려 보인다. ShaderLab은 패스 간 HLSL 블록 공유가 안 되므로 include로 한 곳에 모은다.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D(_MainTex);
SAMPLER(sampler_MainTex);

// SRP Batcher 호환을 위해 텍스처를 뺀 모든 머티리얼 프로퍼티를 이 블록에 넣는다.
CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST;
    float4 _HoloColor;
    float4 _RimColor;
    float  _HoloIntensity;
    float  _EmissionBoost;
    float  _Opacity;
    float  _RimPower;
    float  _RimStrength;
    float  _ScanLineCount;
    float  _ScanLineWidth;
    float  _ScanLineSoft;
    float  _ScanStrength;
    float  _ScanRollSpeed;
    float  _GlitchInterval;
    float  _GlitchDuration;
    float  _GlitchSpeed;
    float  _GlitchStrength;
    float  _GlitchThreshold;
    float  _FlickerSpeed;
    float  _FlickerStrength;
    float  _Cull;
CBUFFER_END

// 의사난수. Hologram7.shader와 같은 식을 쓴다(연출 톤을 맞추기 위함).
float Rand(float2 co)
{
    return frac(sin(dot(co, float2(12.9898, 78.233))) * 43758.5453);
}

// 지금이 글리치 버스트 구간인지. 시간을 _GlitchInterval 슬롯으로 나누고
// 슬롯마다 랜덤한 지점에서 _GlitchDuration 만큼만 발동한다.
// 매 프레임 흔들리는 대신 "가만히 있다가 가끔 툭" 터지게 하려는 구조다.
float InGlitchBurst()
{
    float interval   = max(_GlitchInterval, 0.01);
    float slot       = floor(_Time.y / interval);
    float slotRand   = Rand(float2(slot, 91.7));
    float localTime  = _Time.y - slot * interval;
    float burstStart = slotRand * max(interval - _GlitchDuration, 0.0);

    return step(burstStart, localTime) * step(localTime, burstStart + _GlitchDuration);
}

// 높이를 30개 행으로 끊고, 임계값을 넘는 행만 옆으로 민다.
// UV가 아니라 정점을 미는 이유: 이 캐릭터의 컬러맵은 아주 작은 팔레트 아틀라스라
// UV를 조금만 밀어도 전혀 다른 색 칸이 샘플된다.
float3 ApplyGlitch(float3 posOS)
{
    if (InGlitchBurst() < 0.5) return posOS;

    float timeStep   = floor(_Time.y * _GlitchSpeed) / _GlitchSpeed;
    float rowSeed    = floor(posOS.y * 30.0);
    float glitchRand = Rand(float2(rowSeed, timeStep));

    if (glitchRand > _GlitchThreshold)
    {
        float amount = (glitchRand - _GlitchThreshold)
                     / max(1.0 - _GlitchThreshold, 0.001)
                     * _GlitchStrength;

        amount *= (Rand(float2(rowSeed + 1.0, timeStep)) > 0.5) ? 1.0 : -1.0;
        posOS.x += amount;
    }

    return posOS;
}

#endif
