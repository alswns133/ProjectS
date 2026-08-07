using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using ProjectS.Core;
using ProjectS.Players;

namespace ProjectS.Debugging
{
    /// <summary>
    /// 테스트 전용: 몬스터 없이 키 입력만으로 사망·부활 흐름을 확인한다.
    /// H = 약한 사망(doDie), L = 강한 사망(doDieLarge), R = 부활(Revive).
    /// 모션만 보는 게 아니라 실제 사망/부활 처리(HP·상태 전환·조작 잠금)까지 그대로 탄다.
    ///
    /// 2026-08-07 이전에는 H/L이 '피격 모션만' 확인하는 용도였다(데미지를 넣은 뒤 같은 프레임에 회복시켜
    /// 순 HP 변화를 0으로 만들었다). 사망·부활 애니메이션이 들어오면서 실제로 죽이고 되살리는 용도로 바꿨다.
    /// 피격 모션 확인이 다시 필요해지면 그때 별도 키로 되살린다.
    ///
    /// ★ 클래스·파일 이름을 하는 일에 맞춰 바꾸지 않은 이유: 이 스크립트는 Haru.prefab에 부착되어 있는데,
    ///   파일 이름을 바꾸면 .meta의 guid가 새로 생겨 프리팹의 컴포넌트 참조가 Missing이 된다.
    ///
    /// PlayerCombat.cs와 마찬가지로 실제 게임플레이 입력이 아니므로 PlayerInputHandler를 거치지 않는다.
    /// 같은 이유로 Revive 트리거는 Animator에 직접 세운다 — PlayerAnimation에는 사망 트리거(PlayDie)만
    /// 노출되어 있고 부활 모션용 진입점이 없으며, 그 파일은 수정 대상이 아니다.
    /// </summary>
    [RequireComponent(typeof(PlayerStats))]
    [RequireComponent(typeof(Player))]
    [RequireComponent(typeof(Animator))]
    public class DebugHitKey : MonoBehaviour
    {
        // 약한 사망(doDie)을 만들 때 쓰는 '마지막 타격'의 크기.
        // 사망 모션 분기(PlayerStats.LastHitWasStrong)는 "한 방에 최대 HP의 몇 %를 잃었는가"로 정해지는데,
        // 그 임계 비율(PlayerStats.strongHitHpRatio, 기본 0.25)이 private이라 여기서 읽을 수 없다.
        // 최대 HP의 25%보다 확실히 작아야 약한 사망으로 분류된다(MaxHp 100 기준 25 미만). 기본 1이면 안전.
        [SerializeField, Min(1)] private int weakHitAmount = 1;

        // 부활 모션 State 이름. 애니메이터에서 State 이름을 바꾸면 여기도 함께 바꾼다.
        [SerializeField] private string revivalStateName = "Revival";

        // Revival State의 시작/종료를 기다리는 한도(초). 정상 경로로는 도달하지 않아야 하는 backstop이라
        // 넉넉히 잡는다(PlayerHitState.MotionSafetyTimeout과 같은 방침).
        // ★ 특히 시작 대기가 길어야 한다: Die/Die_Large → Revival 전이는 Has Exit Time이 켜져 있어
        //   (각각 0.92/0.89) 사망 클립이 거의 끝나야 전이가 걸린다. 사망 직후 R을 누르면 그만큼 기다린다.
        private const float RevivalWaitTimeout = 5f;

        private static readonly int ReviveTrigger = Animator.StringToHash("Revive");

        private PlayerStats stats;
        private Player player;
        private Animator animator;

        // R 연타로 부활 코루틴이 겹쳐 도는 것을 막는다.
        private bool reviving;

        private void Awake()
        {
            stats = GetComponent<PlayerStats>();
            player = GetComponent<Player>();
            animator = GetComponent<Animator>();
        }

        private void OnDisable()
        {
            // 코루틴은 비활성화와 함께 멈추므로 플래그도 같이 풀어야 다시 켰을 때 R이 먹는다.
            reviving = false;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.hKey.wasPressedThisFrame) ForceDeath(false);
            if (keyboard.lKey.wasPressedThisFrame) ForceDeath(true);
            if (keyboard.rKey.wasPressedThisFrame) ForceRevive();
        }

        // 사망 모션은 '마지막으로 적용된 타격'의 강/약(PlayerStats.LastHitWasStrong)으로 갈리고,
        // 그 값은 TakeDamage 안에서만 정해진다. 원하는 모션을 보려면 마지막 타격 크기를 맞춰야 한다.
        //
        // 강한 사망: 최대 HP만큼 한 방. 임계 비율을 넘으면서 어떤 잔여 HP에서도 확실히 죽는다.
        // 약한 사망: 임계 비율을 넘지 않는 작은 타격으로 죽어야 하니 HP가 먼저 바닥이어야 한다.
        //   ① 풀피로 맞춘다 — 현재 HP를 읽을 수단이 없어 시작점을 고정해야 ②의 계산이 성립한다.
        //   ② 남은 HP가 weakHitAmount가 되도록 한 번에 깎는다.
        //   ③ weakHitAmount로 마무리한다.
        //   ②는 큰 타격이라 경직(HitState)이 한 번 끼어들지만, ③까지 같은 프레임에 끝나고
        //   HitState.Exit()이 피격 트리거를 지우므로(ResetHitTriggers) 애니메이터에는 doDie만 남는다.
        private void ForceDeath(bool strong)
        {
            if (stats.IsDead) return;

            if (strong)
            {
                ApplyDamage(stats.MaxHp);
                return;
            }

            stats.Heal(stats.MaxHp);                       // ①

            int chip = stats.MaxHp - weakHitAmount;
            if (chip > 0) ApplyDamage(chip);               // ②
            ApplyDamage(weakHitAmount);                    // ③
        }

        // 구르기 무적 중이어도 눌렀을 때 반드시 반응이 나와야 디버그 도구로서 예측 가능하므로
        // ignoreInvincibility를 true로 관통시킨다(즉사기와 같은 경로지만 목적은 다르다).
        private void ApplyDamage(int amount)
        {
            DamageResult debugDamage = new DamageResult { Amount = amount, IsCritical = false };
            stats.TakeDamage(in debugDamage, true);
        }

        private void ForceRevive()
        {
            if (!stats.IsDead || reviving) return;
            StartCoroutine(ReviveRoutine());
        }

        // Player.Revive()는 HP 충전과 함께 Animation.ResetDeath()(내부에서 Animator.Rebind)를 호출해
        // 애니메이터를 곧바로 기본 State로 되돌린다. 그래서 Revive 트리거 직후에 바로 부르면 이제 막
        // 재생을 시작한 부활 모션이 잘려나간다 → 모션이 끝난 뒤로 미룬다.
        //
        // 대기는 고정 시간이 아니라 Revival State의 시작·종료로 판정한다. 클립 길이를 손으로 적어두면
        // 클립을 교체할 때마다 값이 어긋나기 때문이다(적 상태가 클립 종료로 판정하는 것과 같은 방침).
        // 전이를 못 타는 상황에 영영 갇히지 않도록 양쪽 대기에 안전 타임아웃을 둔다.
        private IEnumerator ReviveRoutine()
        {
            reviving = true;

            animator.SetTrigger(ReviveTrigger);

            // Revival State에 진입할 때까지 (사망 클립의 Exit Time을 기다리는 구간)
            float deadline = Time.time + RevivalWaitTimeout;
            while (!IsRevivalPlaying() && Time.time < deadline) yield return null;

            // 부활 모션이 끝나고 로코모션으로 빠져나갈 때까지
            deadline = Time.time + RevivalWaitTimeout;
            while (IsRevivalPlaying() && Time.time < deadline) yield return null;

            // 전이를 못 탔다면 트리거가 래치된 채 남아 나중에 엉뚱한 시점에 부활 모션이 튄다.
            animator.ResetTrigger(ReviveTrigger);

            player.Revive();

            reviving = false;
        }

        // 전이 중에는 GetCurrentAnimatorStateInfo가 아직 이전 State를 가리키므로,
        // 진입 전이 구간을 놓치지 않도록 다음 State도 함께 본다.
        private bool IsRevivalPlaying()
        {
            if (animator.GetCurrentAnimatorStateInfo(0).IsName(revivalStateName)) return true;
            return animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName(revivalStateName);
        }
    }
}
