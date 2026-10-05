using UnityEngine;
using ProjectS.Core;
using ProjectS.Managers;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 무력화(그로기) 상태. <see cref="EnemyGroggy"/>의 게이지가 0이 되면 <see cref="Enemy.EnterGroggy"/>를 통해 진입한다.
    /// 무력화 모션을 재생하고 <see cref="Enemy.GroggyDuration"/>초 동안 이동·AI·공격을 멈춘 채 가만히 있다가,
    /// 시간이 끝나면 그로기 게이지를 리필하고 <b>회복(End) 모션이 끝난 뒤</b> 다시 전투 흐름(Chase)으로 복귀한다.
    ///
    /// 데미지 적용 자체는 이 상태와 무관하다(무력화 중에도 EnemyStats.TakeDamage가 그대로 피해를 넣는다) —
    /// 이 상태는 "행동 정지 + 무방비" 연출만 담당한다. 보스는 보통 슈퍼아머(useHitStun=off)라 무력화 중 피격으로
    /// 끊기지 않고, <see cref="Enemy.OnDamaged"/>도 이 상태에서는 진입을 건너뛴다(약몹 오작동 방지 가드).
    /// </summary>
    /// <remarks>
    /// 무력화는 <b>2단</b>이다: ① 지속시간(Start→Loop 루프) → ② 회복(End 모션).
    /// 상태를 ②까지 붙잡아 두는 이유는, 지속시간이 끝나는 프레임에 바로 Chase로 넘기면 그 프레임에
    /// <see cref="EnemyAttackState"/>가 <c>animator.Play(공격)</c>로 <b>End 모션을 덮어써</b> 일어나는 동작이
    /// 통째로 잘리기 때문이다(그로기가 풀리자마자 공격이 튀어나오는 증상).
    /// 종료 판정은 클립 길이를 인스펙터에 적지 않고 애니메이터에서 읽는다 — Attack/Detect와 같은 규칙.
    /// ★ 계약: 애니메이터의 무력화 State 3개(Start·Loop·End)에 모두 <c>"Groggy"</c> 태그를 달고,
    ///   End→로코모션 자동 전이(Has Exit Time)를 둔다. 태그가 빠지면 ②가 즉시 통과되어 예전처럼 잘리고,
    ///   자동 전이가 없으면 아래 <see cref="MaxRecoveryTime"/> 안전장치까지 굳는다.
    /// </remarks>
    public class EnemyGroggyState : EnemyBaseState
    {
        // 애니메이터의 무력화 State들에 공통으로 다는 태그. 회복 모션 종료 판정의 유일한 근거다.
        private const string GroggyTag = "Groggy";

        // 회복(End) 모션 대기의 안전장치. 태그 누락·전이 누락으로 종료 판정이 오지 않아도
        // 보스가 영구히 굳지 않게 한다(EnemyAttackState.MaxAttackTime과 같은 목적).
        private const float MaxRecoveryTime = 5f;

        private float elapsed;

        // 지속시간이 끝나 회복(End) 모션을 재생하는 중인지. 이 구간에서도 상태는 유지된다.
        private bool recovering;

        /// <summary>무력화 상태를 만든다.</summary>
        /// <param name="enemy">상태가 조작할 몬스터 컨텍스트.</param>
        public EnemyGroggyState(Enemy enemy) : base(enemy) { }

        /// <summary>이동을 즉시 멈추고 무력화 모션을 재생하며, 진행 중이던 지속 이펙트를 걷어낸다.</summary>
        public override void Enter()
        {
            elapsed = 0f;
            recovering = false;

            // 이동·추격을 즉시 멈추고 무력화 모션으로 갈아탄다(Speed를 감쇠 없이 0으로 끊는다).
            enemy.Movement.Stop();
            enemy.Animation.SetSpeedImmediate(0f);
            enemy.Animation.PlayGroggy();

            // 진행 중이던 오라·장판 잔상을 걷어낸다(무방비 연출이 지속 이펙트에 묻히지 않게).
            enemy.Effects?.StopAll();

            // 그로기 돌입음. 무력화가 실제로 시작된 이 지점(0프레임)에서 그로기 이펙트와 함께 울린다.
            // 3D(PlaySFX3D)가 아니라 2D로 재생하는 이유: "이 순간을 알리는" 연출음이라 보스와의 거리에
            // 따라 작아지면 안 된다. 3D로 두면 기본 감쇠(Logarithmic/minDistance 1)에 걸려
            // 10m 거리에서 볼륨이 1/10로 떨어져 사실상 들리지 않는다.
            SoundManager.Instance?.PlaySFX(SoundID.SFX_BossGroggy);
        }

        /// <summary>지속시간이 끝나면 게이지를 리필하고 회복 모션을 시작하며, 회복 모션이 끝나 "Groggy" 태그를 벗어나면 전투로 복귀한다.</summary>
        public override void Update()
        {
            elapsed += Time.deltaTime;

            if (!recovering)
            {
                if (elapsed < enemy.GroggyDuration) return;

                // 무력화 종료: 게이지를 되돌리고(다시 그로기 가능) 회복 모션을 시작한다.
                // Refill이 그로기 변화 이벤트를 발행해 UI 바도 다시 차오른다.
                // isGroggy를 내리는 것만으로 Loop→End 전이가 열린다 — 상태 전환은 아직 하지 않는다.
                enemy.Groggy?.Refill();
                enemy.Animation.EndGroggy();

                recovering = true;
                elapsed = 0f;
                return;
            }

            // 회복 모션이 끝나 무력화 State들을 완전히 벗어났을 때만 전투로 복귀한다.
            // HasSettledOutsideTag를 쓰는 이유: 무력화는 Start→Loop→End 다단이라 IsPlaying의 단순 부정을
            // 쓰면 State 간 전이 순간(IsInTransition)에 종료로 오판해 End가 시작되기도 전에 빠져나간다.
            if (enemy.Animation.HasSettledOutsideTag(GroggyTag) || elapsed >= MaxRecoveryTime)
                enemy.StateMachine.ChangeState(enemy.AggroState);
        }

        /// <summary>
        /// 무력화에서 빠져나갈 때 isGroggy를 내려 애니메이터가 로코모션으로 복귀하게 한다
        /// (Groggy→로코모션 전이 조건). 정상 경로에서는 Update가 이미 내렸지만, 사망·페이즈 전환처럼
        /// 밖에서 상태를 갈아치우는 경로에서도 파라미터가 켜진 채 남지 않게 여기서 한 번 더 내린다.
        /// </summary>
        public override void Exit() => enemy.Animation.EndGroggy();
    }
}
