using UnityEngine;
using UnityEngine.InputSystem;
using ProjectS.Core;
using ProjectS.Players;

namespace ProjectS.Debugging
{
    /// <summary>
    /// 테스트 전용: H 키는 약피격, L 키는 강피격을 강제로 발동한다.
    /// 지상/공중 구분은 이 스크립트가 하지 않는다 — PlayerStats.TakeDamage로 데미지를 흘려보내면
    /// Player가 평소처럼 PlayerHitState로 들어가고, 실제 지상/공중 모션 분기는 Animator가
    /// 이미 매 프레임 받고 있는 isGrounded 값으로 알아서 가른다(점프해서 뜬 채로 눌러도 그대로 반영됨).
    /// PlayerCombat.cs와 마찬가지로 실제 게임플레이 입력이 아니므로 PlayerInputHandler를 거치지 않는다.
    /// </summary>
    [RequireComponent(typeof(PlayerStats))]
    public class DebugHitKey : MonoBehaviour
    {
        // PlayerStats.strongHitHpRatio(기본 0.25, private)를 여기서 직접 읽을 수 없어
        // 이 값을 따로 들고 있는다. 그쪽 밸런스가 바뀌면 이 값도 그에 맞춰 조정할 것 —
        // 실수치 기준보다 넉넉히 높게 잡아 두면(기본 0.3) 웬만한 조정에는 안전하다.
        [SerializeField, Range(0f, 1f)] private float strongHitRatioGuess = 0.3f;

        // 약피격 데미지는 절대값 1이면 충분하다(어떤 MaxHp에서도 25% 미만이 보장되는 극단적 저체력이
        // 아닌 한 약피격으로 분류된다).
        [SerializeField, Min(1)] private int weakHitAmount = 1;

        private PlayerStats stats;

        private void Awake()
        {
            stats = GetComponent<PlayerStats>();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.hKey.wasPressedThisFrame) ForceHit(weakHitAmount);
            if (keyboard.lKey.wasPressedThisFrame) ForceHit(Mathf.CeilToInt(stats.MaxHp * strongHitRatioGuess));
        }

        // 구르기 무적 중이어도 눌렀을 때 반드시 반응이 나와야 디버그 도구로서 예측 가능하므로
        // ignoreInvincibility를 true로 관통시킨다(즉사기와 같은 경로지만 목적은 다르다).
        //
        // ★ HP는 실제로 깎이면 안 된다(이 도구의 목적은 모션·경직 반응 확인이지 HP 테스트가 아님).
        //   PlayerStats.TakeDamage는 "데미지 반영"과 "피격 반응 발행"이 한 메서드에 묶여 있어 분리할
        //   수 없고, 그 파일은 수정 대상이 아니다 → 데미지를 넣은 그 프레임에 즉시 같은 양만큼
        //   회복시켜 순 HP 변화를 0으로 만든다. LastHitWasStrong·Damaged 이벤트·Animator 트리거는
        //   TakeDamage 시점에 이미 확정되어 있으므로 뒤이은 Heal은 그 판정에 영향을 주지 않는다.
        private void ForceHit(int amount)
        {
            DamageResult debugDamage = new DamageResult { Amount = amount, IsCritical = false };
            bool applied = stats.TakeDamage(in debugDamage, true);

            // 데미지가 사망으로 이어졌으면(HP가 amount보다 낮았던 경우) 되살릴 수단이 없어 그대로 둔다.
            if (applied && !stats.IsDead) stats.Heal(amount);
        }
    }
}
