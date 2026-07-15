using System;
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
    [Serializable]
    private class EffectSlot
    {
        public string key;
        public ParticleSystem particle;

        // 시전 위치에 남아야 하는 이펙트(땅의 검흔 등)만 켠다.
        // 재생 순간 플레이어에서 분리해 월드에 고정하므로 이동해도 따라오지 않는다.
        public bool anchorToWorld;

        // 분리했다가 다음 재생 때 제자리로 복귀시키기 위한 원래 부모/로컬 포즈.
        [NonSerialized] public Transform originalParent;
        [NonSerialized] public Vector3 originalLocalPosition;
        [NonSerialized] public Quaternion originalLocalRotation;
    }

    [SerializeField] private EffectSlot[] effects;

    // 매 이벤트마다 배열을 뒤지지 않도록 Awake에서 1회 구축하는 조회용 사전.
    private readonly Dictionary<string, EffectSlot> effectMap = new Dictionary<string, EffectSlot>();

    // 구르기·피격·사망 중 뒤늦게 도착한 이펙트 이벤트를 무시하기 위해 상태를 조회할 중앙 컨텍스트.
    private Player player;

    private void Awake()
    {
        player = GetComponent<Player>();

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

            // 월드 고정 슬롯은 분리 후 복귀할 원래 자리를 기억해 둔다.
            if (slot.anchorToWorld && slot.particle != null)
            {
                Transform tr = slot.particle.transform;
                slot.originalParent = tr.parent;
                slot.originalLocalPosition = tr.localPosition;
                slot.originalLocalRotation = tr.localRotation;
            }

            effectMap.Add(slot.key, slot);
        }
    }

    /// <summary>
    /// 공격/스킬 클립의 Animation Event가 호출한다. 인자는 인스펙터에 등록한 슬롯 키.
    /// 월드 고정 슬롯이면 현재 시전 위치에서 분리해 그 자리에 남긴다.
    /// </summary>
    public void OnEffect(string key)
    {
        // 구르기·피격·사망으로 동작이 중단됐으면, 블렌드 아웃 중 뒤늦게 도착한 이펙트를 무시한다.
        // 입력을 막는 조건과 동일(Player.IsActionInterrupted) → 이벤트도 같은 기준으로 게이트.
        if (player.IsActionInterrupted) return;

        if (!TryGetSlot(key, out EffectSlot slot)) return;

        if (slot.anchorToWorld)
        {
            // ① 원래 자리(플레이어 자식)로 복귀시켜 이번 시전 위치에 맞춘 뒤
            // ② 그 월드 포즈 그대로 분리한다 → 재생 중 플레이어가 움직여도 따라오지 않는다.
            // 끝난 뒤 되붙이지 않는 이유: 다음 재생 때 ①이 다시 제자리로 데려오므로 콜백이 필요 없다.
            Transform tr = slot.particle.transform;
            tr.SetParent(slot.originalParent, false);
            tr.localPosition = slot.originalLocalPosition;
            tr.localRotation = slot.originalLocalRotation;
            tr.SetParent(null, true);
        }

        slot.particle.Play();
    }

    /// <summary>
    /// 해당 이펙트의 방출을 멈춘다(이미 나온 파티클은 수명대로 자연 소멸).
    /// 차징·오라 같은 루프 이펙트를 꺼야 하는 프레임에 Animation Event로 호출한다.
    /// </summary>
    public void OffEffect(string key)
    {
        if (!TryGetSlot(key, out EffectSlot slot)) return;
        slot.particle.Stop();
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
            if (slot != null && slot.particle != null && !slot.anchorToWorld)
                slot.particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private bool TryGetSlot(string key, out EffectSlot slot)
    {
        // OnHitFrame과 같은 방침: 이벤트 인자 실수(오타)는 경고만 남기고 플레이는 계속한다.
        if (string.IsNullOrEmpty(key) || !effectMap.TryGetValue(key, out slot) || slot.particle == null)
        {
            Debug.LogWarning($"Effect key not found or empty ('{key}'). Check the Animation Event string.", this);
            slot = null;
            return false;
        }

        return true;
    }
}
