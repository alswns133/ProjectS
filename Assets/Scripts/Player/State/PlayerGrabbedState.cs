using UnityEngine;
using ProjectS.Enemies;

namespace ProjectS.Players
{
    /// <summary>
    /// 보스에게 잡힌(구속) 상태. 보스 잡기 히트 프레임(<see cref="Boss.OnGrabConnect"/>)이 플레이어를
    /// 포착하면 <see cref="Player.OnGrabbed"/>가 이 상태로 전환한다. 조작을 완전히 차단하고
    /// CharacterController를 꺼, 위치를 보스의 잡기 앵커(grabAnchor)에 매 프레임 스냅한다
    /// (보스 애니메이션이 플레이어를 손/입 등에 붙여 끌고 다니게 하기 위함).
    ///
    /// 구속 중 데미지는 보스 클립의 Animation Event(<see cref="Boss.OnGrabDamage"/>)가 넣고,
    /// 이 상태는 경직(HitState)으로 새지 않는다 — <see cref="Player.OnDamaged"/>가 IsGrabbed일 때
    /// 전이를 건너뛰어, 데미지만 받고 잡힘이 유지된다.
    ///
    /// 탈출은 '고정 시간' 모델이다(2026-08 기준 — 추후 마시(연타) 탈출로 바뀔 수 있어 struggle 입력에 의존하지 않는다).
    /// 실제 종료 시점은 보스 잡기 클립의 마무리 Animation Event(<see cref="Boss.OnGrabThrow"/>/<see cref="Boss.OnGrabDrop"/>
    /// → <see cref="Player.ReleaseFromGrab"/>)가 소유하고, 이 상태는 그 신호를 '기다리기만' 한다. 다만 신호가 영영
    /// 안 올 실패에 대비해 두 겹의 안전장치를 둔다.
    ///  - 안전 타임아웃(<see cref="MaxGrabDuration"/>): 마무리 이벤트가 누락돼도 스스로 풀린다.
    ///  - 잡은 주체 실패 감지: 보스가 죽거나(Stats.IsDead) 비활성(씬 이탈 HaltForSceneExit 등)되면 즉시 스스로 해제한다.
    ///    → 잡힌 채 컨트롤러가 꺼진 채로 영구 고착되는 최악을 막는다(이 실패 복구가 잡기 기능의 최대 리스크 지점).
    /// </summary>
    public class PlayerGrabbedState : BaseState
    {
        // 마무리 이벤트를 못 받았을 때의 강제 해제 상한(초). 어떤 잡기 클립보다 넉넉히 길게 잡는다.
        private const float MaxGrabDuration = 6f;

        private Enemy grabber;      // 잡은 주체(보스). 생사·활성 감시와 바라볼 방향 산출에 쓴다.
        private Transform anchor;   // 잡힌 위치를 고정할 보스의 앵커 Transform(손/입 등). null이면 위치 고정 없음.
        private float elapsed;

        public PlayerGrabbedState(Player player) : base(player) { }

        /// <summary>
        /// 잡기 진입 정보를 주입한다. 상태 인스턴스는 Awake에서 1회 생성돼 생성자 인자를 매번 못 받으므로,
        /// <see cref="Player.OnGrabbed"/>가 ChangeState 직전에 이 메서드로 이번 잡기의 주체/앵커를 세팅한다.
        /// </summary>
        public void Setup(Enemy grabber, Transform anchor)
        {
            this.grabber = grabber;
            this.anchor = anchor;
        }

        public override void Enter()
        {
            elapsed = 0f;

            // 진행 중이던 동작을 전부 끊는다(피격 진입과 같은 정리 절차).
            player.Combat.CancelAction();
            player.Effect.AllStopEffect();
            player.UnlockMovement();       // 내부에서 SetHover(false)·시전 플래그 정리까지 함께 처리
            player.Movement.CancelJump();

            // 로코모션 bool을 내려 3단 로코모션 컨트롤러가 구속 모션 중 걷기 Loop로 새지 않게 한다.
            player.Animation.SetLocomotion(false, false);
            player.Animation.PlayGrabbed();

            // 위치를 보스가 끌고 다니도록 컨트롤러를 끄고 관성을 리셋한다(EndExternalControl과 짝).
            player.Movement.BeginExternalControl();

            SnapToAnchor();
        }

        public override void Update()
        {
            elapsed += Time.deltaTime;

            // 잡은 주체가 죽거나 사라지면(씬 이탈로 비활성 등) 즉시 스스로 해제한다.
            // 이 실패 복구가 없으면 보스가 잡은 채 죽었을 때 플레이어가 영구히 고착된다.
            if (grabber == null || grabber.Stats == null || grabber.Stats.IsDead || !grabber.isActiveAndEnabled)
            {
                player.ReleaseFromGrab(false);
                return;
            }

            // 마무리 이벤트 누락 대비 안전장치.
            if (elapsed >= MaxGrabDuration)
            {
                player.ReleaseFromGrab(false);
                return;
            }

            // 보스 앵커에 위치를 붙인다(보스 애니메이션이 위치를 소유). 컨트롤러가 꺼져 있어 되밀리지 않는다.
            SnapToAnchor();
        }

        public override void Exit()
        {
            // 컨트롤러를 되살리고(관성 리셋 포함) 래치된 구속 트리거를 정리한다.
            // 던지기 마무리는 이 Exit 뒤에 Player.ReleaseFromGrab이 ApplyThrow로 관성을 얹는다.
            player.Movement.EndExternalControl();
            player.Animation.ResetGrabbedTrigger();
            grabber = null;
            anchor = null;
        }

        // 위치를 앵커로 스냅하고 잡은 보스를 바라보게 한다(던질 때 -forward가 곧 '보스 반대쪽'이 되도록).
        private void SnapToAnchor()
        {
            if (anchor != null)
                player.transform.position = anchor.position;

            if (grabber != null)
            {
                Vector3 look = grabber.transform.position - player.transform.position;
                look.y = 0f;
                if (look.sqrMagnitude > 0.0001f)
                    player.transform.rotation = Quaternion.LookRotation(look);
            }
        }
    }
}
