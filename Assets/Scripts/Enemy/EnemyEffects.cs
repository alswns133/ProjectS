using System;
using UnityEngine;

/// <summary>
/// 몬스터 이펙트 브릿지. 상태/전투 코드는 어떤 파티클을 재생할지 모르고,
/// Detect/Attack/Hit/Death 같은 의미만 넘긴다. 실제 파티클 연결은 프리팹 인스펙터에서 한다.
/// </summary>
public class EnemyEffects : MonoBehaviour
{
    /// <summary>몬스터 상태 흐름에서 재생할 수 있는 이펙트 시점.</summary>
    public enum EffectCue
    {
        // 플레이어를 처음 발견했을 때의 경고/포효/먼지 이펙트.
        Detect,

        // 공격 시작 시의 예고/무기 궤적 시작 이펙트.
        Attack,

        // 피격 경직 진입 시의 피격/충격 이펙트.
        Hit,

        // 사망 상태 진입 시의 처치/소멸 이펙트.
        Death,
    }

    // cue와 ParticleSystem을 한 쌍으로 묶는다. 같은 cue를 여러 개 등록하면 동시에 재생된다.
    [Serializable]
    private class EffectSlot
    {
        public EffectCue cue;
        public ParticleSystem effect;
    }

    [SerializeField] private EffectSlot[] effects;

    /// <summary>지정한 cue에 해당하는 모든 파티클을 처음부터 다시 재생한다.</summary>
    public void Play(EffectCue cue)
    {
        if (effects == null) return;

        for (int i = 0; i < effects.Length; i++)
        {
            EffectSlot slot = effects[i];
            if (slot == null || slot.cue != cue || slot.effect == null) continue;

            // 이미 재생 중인 파티클을 그대로 두면 이전 재생의 잔상이 이어질 수 있어
            // 매 cue 호출마다 처음 프레임부터 다시 재생한다.
            slot.effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            slot.effect.Play(true);
        }
    }
}
