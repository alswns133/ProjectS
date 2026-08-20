using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectS.Players; // AnimationEventKey (슬롯 키 정규화). 히트박스·투사체·플레이어 이펙트와 같은 규약을 공유한다.

namespace ProjectS.Enemies
{
    /// <summary>
    /// 몬스터 연출용 파티클을 재생·중지하는 유일한 통로. <c>PlayerEffects</c>와 같은 규약
    /// (슬롯 키 문자열 + <see cref="AnimationEventKey.Normalize"/> + Play/Stop만, 런타임 Instantiate 없음)을 따르는
    /// 병렬 컴포넌트다. 클립 저작 흐름을 플레이어와 동일화해, 잡몹의 단순 이펙트부터 보스·레이드보스의
    /// 여러 이펙트 겹침까지 슬롯만 늘리면 개수 제약 없이 커버한다.
    ///
    /// 재생 시점은 각 클립의 Animation Event가 슬롯 키를 넘겨 정한다(발견/공격/피격/사망 모두 클립 주도).
    /// 예전 cue 기반(<c>EffectCue</c> enum + State 진입 시 <c>Play(cue)</c>)은 State 단위라 타이밍이 거칠고
    /// cue당 하나뿐이라 보스에 안 맞아 폐기했다 — 자세한 배경은 리팩토링 메모 참고.
    ///
    /// <c>PlayerEffects</c>와의 유일한 차이: 플레이어의 <c>IsActionInterrupted</c> 중단 게이트가 없다.
    /// Enemy엔 아직 그 개념이 없어서다. 나중에 Enemy에 중단 게이트를 붙이면, 사망 클립 이벤트가
    /// 스스로 막히지 않도록 게이트 우회용 <c>OnDeathEffect</c>를 그때 함께 판다(현재는 불필요).
    ///
    /// ★ 반드시 Animator와 같은 GameObject(몬스터 루트)에 붙일 것.
    ///   Animation Event는 Animator가 붙은 오브젝트의 컴포넌트에서만 메서드를 찾는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class EnemyEffects : MonoBehaviour
    {
        // 키는 클립 Animation Event의 string 인자와 맞춘다.
        // 예: "Attack1", "Hit", "Death", "Slam", "Shockwave" — 한 클립에 타이밍이 다른
        // 이펙트가 여러 개 붙어도 슬롯만 추가하면 된다(개수 제약 없음).
        [Serializable]
        private class EffectSlot
        {
            public string key;
            public ParticleSystem particle;

            // 시전 위치에 남아야 하는 이펙트(보스 장판·바닥 균열 등)만 켠다.
            // 재생 순간 몬스터에서 분리해 월드에 고정하므로 이동해도 따라오지 않는다.
            public bool anchorToWorld;

            // 월드 고정 이펙트를 강제 정리(StopAll — 피격/사망 진입)에도 함께 멈출지 여부.
            // anchorToWorld는 기본적으로 "동작이 끊겨도 그대로 남는다"라 StopAll에서 제외된다.
            // 하지만 설치형 지속 이펙트(장판·오라)는 캐릭터를 따라오지 않게 월드 고정을 켜면서도,
            // 동작이 끊기면 같이 걷어야 할 때가 있다. 이 값을 켜면 월드 고정 슬롯이라도 StopAll에 포함된다.
            // ★ anchorToWorld가 false면 의미 없다(그때는 어차피 항상 정리 대상).
            public bool stopOnInterrupt;

            // 분리했다가 다음 재생 때 제자리로 복귀시키기 위한 원래 부모/로컬 포즈.
            [NonSerialized] public Transform originalParent;
            [NonSerialized] public Vector3 originalLocalPosition;
            [NonSerialized] public Quaternion originalLocalRotation;
        }

        [SerializeField] private EffectSlot[] effects;

        // 매 이벤트마다 배열을 뒤지지 않도록 Awake에서 1회 구축하는 조회용 사전.
        private readonly Dictionary<string, EffectSlot> effectMap = new Dictionary<string, EffectSlot>();

        private void Awake()
        {
            if (effects == null) return;

            foreach (var slot in effects)
            {
                if (slot == null || string.IsNullOrEmpty(slot.key)) continue;

                // 히트박스·투사체·플레이어 이펙트와 같은 방침: 언더바 표기 차이를 흡수하려 정규화한 키로
                // 등록·조회한다(AnimationEventKey 참조). 클립이 "Attack_1", 인스펙터가 "Attack1"이어도 맞물린다.
                string normKey = AnimationEventKey.Normalize(slot.key);

                // 키 중복을 조용히 덮어쓰면 한쪽 이펙트가 영영 안 나와 원인 찾기 어렵다 → 경고.
                if (effectMap.ContainsKey(normKey))
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

                effectMap.Add(normKey, slot);
            }
        }

        /// <summary>
        /// 공격/발견/피격/사망 등 클립의 Animation Event가 호출한다. 인자는 인스펙터에 등록한 슬롯 키.
        /// 월드 고정 슬롯이면 현재 위치에서 분리해 그 자리에 남긴다.
        /// </summary>
        /// <param name="key">슬롯 키. 언더바 표기 차이는 무시된다(예: "Attack_1" == "Attack1").</param>
        public void OnEffect(string key) => PlayByKey(key);

        // 재생 본체. 지금은 게이트가 없어 OnEffect가 곧장 부르지만, 나중에 중단 게이트를 붙일 때
        // OnEffect(게이트 통과)와 OnDeathEffect(게이트 우회)가 공유할 수 있도록 분리해 둔다.
        private void PlayByKey(string key)
        {
            if (!TryGetSlot(key, out EffectSlot slot)) return;

            if (slot.anchorToWorld)
            {
                // ① 원래 자리(몬스터 자식)로 복귀시켜 이번 재생 위치에 맞춘 뒤
                // ② 그 월드 포즈 그대로 분리한다 → 재생 중 몬스터가 움직여도 따라오지 않는다.
                // 끝난 뒤 되붙이지 않는 이유: 다음 재생 때 ①이 다시 제자리로 데려오므로 콜백이 필요 없다.
                Transform tr = slot.particle.transform;
                tr.SetParent(slot.originalParent, false);
                tr.localPosition = slot.originalLocalPosition;
                tr.localRotation = slot.originalLocalRotation;
                tr.SetParent(null, true);
            }

            // 이미 재생 중인 파티클을 그대로 두면 이전 재생의 잔상이 이어질 수 있어(연사·연속 피격)
            // 매 호출마다 처음 프레임부터 다시 재생한다.
            slot.particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            slot.particle.Play(true);
        }

        /// <summary>
        /// 해당 이펙트를 즉시 제거한다(방출 중지 + 이미 나온 파티클까지 삭제).
        /// 차징·오라 같은 루프 이펙트를 꺼야 하는 프레임에 Animation Event로 호출한다.
        /// 서서히 사그라드는 연출이 필요하면 여기서 처리하지 말고, OffEffect를 늦게 찍거나 파티클 수명을 줄인다.
        /// </summary>
        /// <param name="key">슬롯 키.</param>
        public void OffEffect(string key)
        {
            if (!TryGetSlot(key, out EffectSlot slot)) return;
            slot.particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        /// <summary>
        /// 모든 이펙트를 즉시 제거한다. 피격·사망처럼 동작이 강제로 끊길 때 보스의 오라·장판 잔상이
        /// 남지 않게 상태 진입부(<c>EnemyHitState</c>/<c>EnemyDeadState</c>의 Enter)에서 호출한다.
        /// 잡몹은 지속 이펙트가 없어 무해하다.
        /// </summary>
        public void StopAll()
        {
            if (effects == null) return;

            foreach (var slot in effects)
            {
                // 아직 안 채운 슬롯이 있어도 순회가 끊기지 않게 건너뛴다.
                // 월드 고정 슬롯은 원칙적으로 남기되(바닥 균열 등), stopOnInterrupt가 켜진
                // 설치형 지속 이펙트는 월드 고정이어도 함께 걷는다.
                if (slot == null || slot.particle == null) continue;
                if (slot.anchorToWorld && !slot.stopOnInterrupt) continue;

                slot.particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private bool TryGetSlot(string key, out EffectSlot slot)
        {
            // 이벤트 인자 실수(오타)는 경고만 남기고 플레이는 계속한다(히트박스·플레이어 이펙트와 같은 방침).
            // 조회는 정규화 키로 한다 → 언더바 표기 차이(Skill_1_1 vs Skill1_1)를 흡수.
            if (string.IsNullOrEmpty(key) || !effectMap.TryGetValue(AnimationEventKey.Normalize(key), out slot) || slot.particle == null)
            {
                Debug.LogWarning($"Effect key not found or empty ('{key}'). Check the Animation Event string.", this);
                slot = null;
                return false;
            }

            return true;
        }
    }
}
