using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 공격/스킬 연출용 파티클을 재생·중지하는 유일한 통로.
/// Animation Event가 슬롯 키(string)를 넘겨 호출하며, 파티클은 미리 배치해 두고
/// Play/Stop만 하므로 런타임 Instantiate가 발생하지 않는다.
/// 한 시점에 같이 터지는 이펙트 묶음은 부모 파티클 하나로 등록한다
/// (Play/Stop이 자식 파티클까지 함께 적용되는 Unity 기본 동작 활용).
/// </summary>
public class PlayerEffects : MonoBehaviour
{
    // 키는 클립 Animation Event의 string 인자와 맞춘다.
    // 예: "Attack1", "Skill2_Cast", "Skill2_Blast" — 스킬 하나에 타이밍이 다른
    // 이펙트가 여러 개 붙어도 슬롯만 추가하면 된다(개수 제약 없음).
    [System.Serializable]
    private class EffectSlot
    {
        public string key;
        public ParticleSystem particle;
    }

    [SerializeField] private EffectSlot[] effects;

    // 매 이벤트마다 배열을 뒤지지 않도록 Awake에서 1회 구축하는 조회용 사전.
    private readonly Dictionary<string, ParticleSystem> effectMap = new Dictionary<string, ParticleSystem>();

    private void Awake()
    {
        if (effects == null) return;

        foreach (var slot in effects)
        {
            if (slot == null || string.IsNullOrEmpty(slot.key)) continue;

            // 키 중복을 조용히 덮어쓰면 한쪽 이펙트가 영영 안 나와 원인 찾기 어렵다 → 경고.
            if (effectMap.ContainsKey(slot.key))
            {
                Debug.LogWarning($"Duplicate effect key '{slot.key}'. Only the first slot is used.", this);
                continue;
            }

            effectMap.Add(slot.key, slot.particle);
        }
    }

    /// <summary>
    /// 공격/스킬 클립의 Animation Event가 호출한다. 인자는 인스펙터에 등록한 슬롯 키.
    /// </summary>
    public void OnEffect(string key)
    {
        if (!TryGetEffect(key, out ParticleSystem fx)) return;
        fx.Play();
    }

    /// <summary>
    /// 해당 이펙트의 방출을 멈춘다(이미 나온 파티클은 수명대로 자연 소멸).
    /// 차징·오라 같은 루프 이펙트를 꺼야 하는 프레임에 Animation Event로 호출한다.
    /// </summary>
    public void OffEffect(string key)
    {
        if (!TryGetEffect(key, out ParticleSystem fx)) return;
        fx.Stop();
    }

    /// <summary>
    /// 모든 이펙트를 즉시 제거한다. 구르기 캔슬·사망처럼 동작이 강제로 끊길 때
    /// 잔상이 화면에 남지 않게 상태 진입부(RollState/DeadState)에서 호출한다.
    /// </summary>
    public void AllStopEffect()
    {
        if (effects == null) return;

        foreach (var slot in effects)
        {
            // 아직 안 채운 슬롯이 있어도 순회가 끊기지 않게 건너뛴다.
            if (slot != null && slot.particle != null)
                slot.particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private bool TryGetEffect(string key, out ParticleSystem fx)
    {
        // OnHitFrame과 같은 방침: 이벤트 인자 실수(오타)는 경고만 남기고 플레이는 계속한다.
        if (!effectMap.TryGetValue(key, out fx) || fx == null)
        {
            Debug.LogWarning($"Effect key not found or empty ('{key}'). Check the Animation Event string.", this);
            return false;
        }

        return true;
    }
}
