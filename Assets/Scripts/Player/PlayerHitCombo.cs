using UnityEngine;
using ProjectS.Events;

namespace ProjectS.Players
{
    /// <summary>
    /// 히트 수 콤보(연속 유효타 카운터).
    /// "지금 공격 콤보 몇 단계냐"(PlayerCombat.comboStep)와는 다른 개념 —
    /// 여기서는 마지막 유효타 이후 <see cref="comboResetDelay"/>초 안에 적중이 이어지는 동안
    /// 누적 히트 수를 세고, 창이 끊기면 0으로 리셋한다.
    ///
    /// 적중 신호는 PlayerCombat을 직접 건드리지 않고, Player가 이미 구독 중인
    /// <see cref="PlayerCombat.TargetHit"/>(적중 1회당·마리당 1번 발행)을 재활용한다.
    /// 그래서 게이지 회복과 동일하게 "유효타만, 광역은 마리당" 규약이 자동으로 맞는다.
    /// </summary>
    public class PlayerHitCombo : MonoBehaviour
    {
        [Header("Hit Combo")]
        [SerializeField, Tooltip("마지막 유효타 후 이 시간(초)이 지나면 히트 카운트가 0으로 리셋된다.")]
        private float comboResetDelay = 5f;

        [SerializeField, Tooltip("표시·오버플로 방지용 최대 히트 수. 이 값에서 더 오르지 않는다.")]
        private int maxHitCount = 999;

        // 마지막 유효타 이후 경과 시간. 유효타가 들어올 때마다 0으로 되돌려 "마지막 타 기준 5초"(슬라이딩 창)를 만든다.
        private float decayTimer;

        /// <summary>현재 누적 히트 수. 0이면 콤보 없음.</summary>
        public int HitCount { get; private set; }

        private void Update()
        {
            // 카운트가 없으면 타이머를 굴리지 않는다(불필요한 감쇠 계산 방지).
            if (HitCount <= 0) return;

            decayTimer += Time.deltaTime;
            if (decayTimer >= comboResetDelay)
                ResetHitCombo();
        }

        /// <summary>
        /// 유효타 1회를 콤보에 더한다. PlayerCombat.TargetHit 구독 지점에서 호출된다.
        /// 광역으로 여러 마리를 맞히면 그만큼 여러 번 호출되어 마리당 +1로 쌓인다(게이지 회복과 동일 규약).
        /// </summary>
        public void AddHit()
        {
            HitCount = Mathf.Min(HitCount + 1, maxHitCount);

            // ★ 매 타격마다 타이머를 되돌려야 "마지막 타 기준 5초"가 된다.
            //   이 줄이 빠지면 첫 타 기준 고정 5초 창이 되어 연타 콤보가 중간에 끊긴다.
            decayTimer = 0f;

            PlayerEvents.FireHitComboChanged(HitCount);
        }

        /// <summary>
        /// 히트 콤보를 0으로 되돌린다. 시간 초과, 피격(PlayerHitState 진입 등), 사망/씬 전환에서 호출한다.
        /// </summary>
        public void ResetHitCombo()
        {
            if (HitCount == 0) return;   // 이미 0이면 중복 발행하지 않는다.

            HitCount = 0;
            decayTimer = 0f;
            PlayerEvents.FireHitComboChanged(0);
        }
    }
}
