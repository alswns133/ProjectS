using UnityEngine;

namespace ProjectS.Players
{
    /// <summary>
    /// 피격 경직 상태. 데미지가 실제로 적용됐을 때(PlayerStats.Damaged) 진입한다.
    /// 진행 중이던 공격/스킬을 캔슬하고 피격 모션을 재생하며, 경직 동안 이동·공격 입력을 막는다.
    /// 강한 피격(LastHitWasStrong)은 별도 모션과 더 긴 경직이 적용된다.
    /// 구르기는 경직을 캔슬할 수 있다(회피 최우선 철학 — Player.TryRoll이 경직을 확인하지 않음).
    /// 연타를 맞아도 경직이 리셋되지 않는다(상태 머신의 같은 상태 재진입 방지).
    /// </summary>
    public class PlayerHitState : BaseState
    {
        // 태그 캐릭터가 Hit 모션을 못 잡거나(태그 누락) 블렌드가 비정상적으로 길어져도
        // 경직에 영영 갇히지 않게 하는 backstop. 정상 경로(모션 종료)로는 도달하지 않아야 하는 값이라
        // 어떤 Hit 클립 길이보다 넉넉히 길게 잡는다. 적 상태의 안전 타임아웃과 같은 방침.
        private const float MotionSafetyTimeout = 2f;

        private float elapsed;

        // 태그 캐릭터 전용: 실제로 Hit 모션에 한 번 진입했는지. 진입 직후 애니메이터가 아직 Hit State로
        // 전이하기 전(태그가 안 올라온 구간)에 "모션이 끝났다"고 오판해 곧바로 탈출하는 것을 막는다.
        private bool enteredHitMotion;

        public PlayerHitState(Player player) : base(player) { }

        public override void Enter()
        {
            elapsed = 0f;
            enteredHitMotion = false;

            // 피격은 진행 중이던 동작을 강제로 끊는다. 구르기 캔슬과 같은 정리 절차:
            // 콤보/버퍼/트리거 정리 + 이펙트 제거 + 이동 잠금·호버링 해제.
            player.Combat.CancelAction();
            player.Effect.AllStopEffect();
            player.UnlockMovement();   // 내부에서 SetHover(false)도 함께 처리(점프 공격 중 피격 대비)
            player.Movement.CancelJump();          // 상승 중이던 점프 속도 제거(피격했는데 계속 떠오르는 것 방지)

            player.Animation.PlayHit(player.Stats.LastHitWasStrong);

            // 경직 동안 수평 이동이 멈추므로 로코모션 bool도 함께 내린다. 켜둔 채로 두면
            // 3단 로코모션 컨트롤러가 피격 모션 도중 걷기 Loop로 새어나간다.
            // Enter에서 1회면 충분하다: 경직 중에는 이 값을 다시 켜는 상태가 돌지 않는다.
            player.Animation.SetLocomotion(false, false);
        }

        public override void Update()
        {
            elapsed += Time.deltaTime;

            // 경직 중 수평 이동은 없고 중력·접지만 유지한다(RollState와 같은 zero 입력 호출).
            player.Movement.Move(Vector2.zero);

            // 태그 캐릭터: 이동 잠금 해제를 고정 타이머가 아니라 실제 Hit 모션 종료에 맞춘다.
            // 공격 입력 게이트(Player.IsHitBlocked)가 이미 Hit 태그 기준이라, 타이머로 탈출하면
            // 클립이 타이머보다 짧을 때 "애니메이션은 끝났는데 이동만 막히는" 죽은 구간이 생긴다
            // (특히 강 피격 0.8초). 모션이 끝나는 순간(태그 해제) 탈출해 이동/공격 해제 시점을 일치시킨다.
            if (player.UsesMotionTags)
            {
                if (player.IsPlayingHitMotion) enteredHitMotion = true;

                bool motionFinished = enteredHitMotion && !player.IsPlayingHitMotion;
                if (motionFinished || elapsed >= MotionSafetyTimeout)
                    player.ChangeState(player.FreeState);
                return;
            }

            // 기존 Z 블렌드 캐릭터: 태그가 없어 클립 기준을 못 쓰므로 경직 타이머를 그대로 쓴다(기존 동작 유지).
            if (elapsed >= player.Stats.CurrentStaggerDuration)
                player.ChangeState(player.FreeState);
        }

        public override void Exit()
        {
            // 경직 중 사망/구르기로 끊겨도 래치된 피격 트리거가 남지 않게 정리한다.
            player.Animation.ResetHitTriggers();
        }
    }
}
