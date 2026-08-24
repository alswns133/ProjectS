using ProjectS.Core;
using ProjectS.Events;
using ProjectS.Players;
using UnityEngine;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 보스 몬스터. <see cref="Enemy"/>를 상속해 스폰·컴포넌트·상태 머신·전투 인프라를 그대로 물려받고,
    /// 잡몹에 없는 보스 전용 패턴(현재: 플레이어 잡기)만 얹는다.
    ///
    /// ★★ 이 클래스는 Unity 메시지 메서드(Awake/Start/Update/OnEnable/OnDisable/FixedUpdate 등)를 절대 선언하지 않는다. ★★
    ///   Enemy가 이들을 private으로 갖고 있고(상태 머신 구동·PlayerEvents 구독/해제가 그 안에 있다), 같은 이름을
    ///   여기서 선언하면 Unity는 파생 클래스 것만 호출하고 base의 것은 실행하지 않는다 → 상태 머신 Update가 멈추고
    ///   이벤트 구독이 새는 등 보스가 통째로 죽는다. 이것이 "Enemy 생성과 충돌하는가"의 실제 지점이며, 그래서
    ///   여기는 <b>직렬화 참조 + 공개 메서드만</b>으로 짠다. 보스 전용 런타임 초기화가 필요해지면 그때 Enemy의 해당
    ///   메서드를 protected virtual로 열어 override(+base 호출)해야 한다(지금 잡기는 그럴 필요가 없다).
    ///   (에디터 전용 <see cref="OnDrawGizmosSelected"/>만 예외로 base에서 protected virtual로 열어 override한다 —
    ///    런타임에 영향이 없어 안전하고, 잡기 히트박스를 눈으로 배치하기 위함이다.)
    ///
    /// 경직/런처 면역("보스는 경직 자체가 안 걸림")은 코드가 아니라 프리팹 설정으로 처리한다:
    ///   Enemy의 useHitStun을 끄면 Enemy.OnDamaged와 Enemy.Launch가 둘 다 진입부 <c>!useHitStun</c> 가드로
    ///   조용히 무시된다(경직도 런처도 안 걸림). 데미지 자체는 EnemyStats.TakeDamage가 useHitStun과 무관하게 넣는다.
    ///
    /// 잡기 흐름(보스 잡기 클립의 Animation Event가 아래 메서드들을 프레임 단위로 호출한다):
    ///   1) <see cref="OnGrabConnect"/>: 잡기 히트 프레임. grabHitbox로 플레이어를 포착해 구속 상태로 밀어넣는다.
    ///      포착에 실패하면(범위에 없거나, 있어도 회피/무적/사망으로 거부) 애니메이터를 헛잡기 모션으로 분기시킨다
    ///      (<see cref="EnemyAnimation.PlayGrabFail"/>) — 허공을 잡고 던지는 뒤 프레임이 그대로 재생되지 않게 한다.
    ///   2) <see cref="OnGrabDamage"/>: 구속 중 데미지 프레임(여러 번 찍어도 된다).
    ///   3) <see cref="OnGrabThrow"/>/<see cref="OnGrabDrop"/>: 마무리. 던지기(넉백) 또는 제자리 해제. 클립에 맞는 쪽 하나를 찍는다.
    /// 잡기 슬롯의 선택·클립 재생은 EnemyCombat의 일반 공격 룰렛/AttackIndex가 그대로 담당한다
    ///   (잡기 클립엔 OnAttackHit 대신 위 잡기 이벤트만 찍어, EnemyCombat의 박스 판정과 겹치지 않게 한다).
    /// </summary>
    public class Boss : Enemy
    {
        [Header("잡기 - 포착")]
        // 잡기 히트 프레임에 플레이어를 포착하는 판정 박스. 이 Transform의 위치/회전/스케일이 곧 박스다
        // (EnemyCombat 히트박스와 같은 규약). 보스 손이 뻗는 위치에 맞춘 자식 오브젝트를 연결한다.
        [SerializeField] private Transform grabHitbox;

        // 포착 대상 레이어(플레이어 피격 콜라이더). 보통 EnemyCombat의 targetMask와 같은 값을 넣는다.
        [SerializeField] private LayerMask grabTargetMask;

        [Header("잡기 - 구속")]
        // 잡힌 플레이어를 붙여 둘 앵커. 보스가 애니메이션으로 플레이어를 끌고 다니게, 손/입 등의 자식 Transform을 연결한다.
        // 비워 두면 포착만 하고 위치는 고정하지 않는다(플레이어는 잡힌 자리에 멈춘 채 구속 모션만 재생).
        [SerializeField] private Transform grabAnchor;

        // 구속 중 데미지 프레임(OnGrabDamage)의 계수. 피해 = Stats.AttackPower × 이 값.
        [SerializeField] private float grabDamageCoef = 1f;

        [Header("잡기 - 던지기 마무리")]
        // OnGrabThrow가 플레이어에게 실어 보내는 넉백 세기(수평/수직). OnGrabDrop(제자리 해제)에는 쓰이지 않는다.
        // 실제 넉백 적용은 플레이어 쪽(PlayerMovement.ApplyThrow)이 하고, 세기만 여기서 넘긴다.
        [SerializeField] private float throwHorizontalSpeed = 6f;
        [SerializeField] private float throwUpSpeed = 6f;

        // 매 포착마다 할당이 생기지 않도록 NonAlloc 버퍼를 재사용한다(EnemyCombat과 같은 방침).
        private readonly Collider[] grabBuffer = new Collider[8];

        // 현재 잡고 있는 플레이어. 포착 성공 시 채워지고, 마무리(던지기/해제)에서 비운다.
        // 데미지/마무리 이벤트가 이 참조를 기준으로 동작한다.
        private Player grabbedPlayer;

        protected override void Start()
        {
            base.Start(); // Enemy.Start(Target 획득 + 상태머신 시작) 먼저
            BossEvents.FireBossAppeared(this);
        }

        /// <summary>
        /// 잡기 히트 프레임. 보스 잡기 클립의 Animation Event로 연결한다(문자열 참조 — 메서드명 변경 주의).
        /// grabHitbox 범위에서 플레이어를 찾아 구속 상태로 밀어넣는다. 포착에 실패하면 — 범위에 플레이어가
        /// 없거나, 있어도 회피/무적/사망으로 거부해(<see cref="Player.OnGrabbed"/>가 false) — 헛잡기로 처리하고
        /// (<see cref="NotifyGrabFailed"/>) 애니메이터를 실패 모션으로 분기시킨다.
        /// </summary>
        public void OnGrabConnect()
        {
            if (Stats.IsDead || grabHitbox == null) return;

            int count = Physics.OverlapBoxNonAlloc(
                grabHitbox.position,
                grabHitbox.lossyScale * 0.5f,
                grabBuffer,
                grabHitbox.rotation,
                grabTargetMask);

            for (int i = 0; i < count; i++)
            {
                // 현재 프로젝트 규칙: 피격 레이어 콜라이더와 Player는 같은 루트 GameObject에 둔다.
                if (grabBuffer[i].TryGetComponent<Player>(out var player))
                {
                    // 플레이어는 하나뿐 — 처음 찾은 대상에서 성공/실패를 판정하고 끝낸다.
                    if (player.OnGrabbed(this, grabAnchor))
                    {
                        grabbedPlayer = player;
                        return;                 // 잡기 성공
                    }

                    NotifyGrabFailed();         // 범위엔 있었으나 회피/무적/사망으로 거부(헛잡기)
                    return;
                }
            }

            NotifyGrabFailed();                 // 범위에 플레이어가 없어 헛손질(헛잡기)
        }

        // 잡기 실패(헛잡기) 공통 처리. 잡은 대상이 없음을 명확히 하고, 애니메이터를 실패/회복 모션으로 분기시킨다.
        // grabbedPlayer가 null이면 뒤이어 오는 데미지/마무리 이벤트는 모두 조용히 무시되므로(각 메서드의 null 가드),
        // 실패 모션으로 전이하지 못한 경우에도 허공에 데미지가 들어가거나 던지기가 발동하지는 않는다.
        private void NotifyGrabFailed()
        {
            grabbedPlayer = null;
            Animation.PlayGrabFail();
        }

        /// <summary>
        /// 구속 중 데미지 프레임. 잡고 있는 플레이어에게 Stats.AttackPower × grabDamageCoef 피해를 넣는다.
        /// 잡기 클립에 이벤트를 여러 번 찍으면 여러 번 들어간다. 플레이어는 이 데미지로 경직되지 않는다
        /// (Player.OnDamaged가 잡힘 중 전이를 건너뜀 — 데미지만 받는다).
        /// </summary>
        public void OnGrabDamage()
        {
            if (grabbedPlayer == null || grabbedPlayer.Stats == null || grabbedPlayer.Stats.IsDead) return;

            AttackContext context = BuildGrabContext();
            IDamageable target = grabbedPlayer.Stats;
            DamageResult result = DamageCalculator.Calculate(in context, target.Defense, target.IsBoss);
            target.TakeDamage(in result);
        }

        /// <summary>
        /// 잡기 마무리 — 던지기(넉백). 잡고 있던 플레이어를 뒤로 날려보내며 구속을 푼다.
        /// 데미지가 필요하면 이 이벤트 앞 프레임에 <see cref="OnGrabDamage"/>를 함께 찍는다.
        /// </summary>
        public void OnGrabThrow() => ReleaseGrabbed(true);

        /// <summary>
        /// 잡기 마무리 — 제자리 해제. 넉백 없이 잡고 있던 플레이어를 그 자리에서 놓아준다.
        /// </summary>
        public void OnGrabDrop() => ReleaseGrabbed(false);

        // 잡기 마무리 공통 처리. 플레이어를 구속에서 풀고(넉백 여부 전달) 참조를 비운다.
        // 던지기 넉백 세기(throwHorizontalSpeed/throwUpSpeed)는 플레이어가 소유하지 않고 보스가 넘긴다.
        private void ReleaseGrabbed(bool asThrow)
        {
            if (grabbedPlayer == null) return;

            grabbedPlayer.ReleaseFromGrab(asThrow, throwHorizontalSpeed, throwUpSpeed);
            grabbedPlayer = null;
        }

        // 잡기 데미지용 AttackContext. EnemyCombat.BuildAttackContext와 같은 규약(편차 없음·치명타 없음).
        private AttackContext BuildGrabContext()
        {
            return new AttackContext
            {
                AttackPower = Stats.AttackPower,
                Coef = grabDamageCoef,
                RandomMin = 1f,
                RandomMax = 1f,
                CritChance = 0f,
                CritDamage = 1f,
                Penetration = 0f,
                DamageBonus = 0f,
                BossBonus = 0f,
            };
        }

        // 잡기 포착 박스·앵커 미리보기. base(감지 반경·군중 제어 등)를 먼저 그린 뒤 잡기 전용을 덧그린다.
        protected override void OnDrawGizmosSelected()
        {
            base.OnDrawGizmosSelected();

            if (grabHitbox != null)
            {
                // 보라색 — EnemyCombat 근접 히트박스(빨강)와 구분한다.
                Gizmos.color = new Color(0.7f, 0.2f, 1f, 1f);
                Gizmos.matrix = Matrix4x4.TRS(grabHitbox.position, grabHitbox.rotation, grabHitbox.lossyScale);
                Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
                Gizmos.matrix = Matrix4x4.identity;
            }

            if (grabAnchor != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(grabAnchor.position, 0.15f);
            }
        }
    }
}
