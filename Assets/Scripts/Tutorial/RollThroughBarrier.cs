using UnityEngine;
using UnityEngine.Events;
using ProjectS.Events;
using ProjectS.Players;

namespace ProjectS.Tutorials
{
    /// <summary>
    /// 걸어서는 못 지나가고 구르기(또는 공중 대시)로만 통과할 수 있는 장벽. 튜토리얼의 레이저에 쓴다.
    ///
    /// <b>체력을 깎지 않는다.</b> 대신 화면 흔들림·피격 이펙트로 "아프다"는 느낌만 준다.
    /// 이유: 튜토리얼에서 죽으면 안 되는데, 죽지 않을 만큼 적은 피해는 어차피 플레이어가 체감하지 못한다.
    /// 보이지 않는 피해는 없는 피해와 같으므로, 숫자를 깎는 대신 연출에 예산을 쓴다.
    /// (피해를 정말 넣어야 하면 PlayerStats.TakeDamage에 '데미지 텍스트 숨김' 옵션을 추가해야 한다 —
    ///  공용 코드 변경이라 지금은 하지 않았다.)
    ///
    /// 구성: 막는 콜라이더(트리거 아님)와 감지 영역(<see cref="PlayerZoneTrigger"/>)을 분리한다.
    /// 감지 영역을 막는 콜라이더보다 <b>넉넉히 크게</b> 잡아야, 구르기로 달려들 때 콜라이더가
    /// 미리 열려 한 프레임 튕기는 일이 없다.
    ///
    /// 통과 판정 축은 이 오브젝트의 <b>Z축(파란 화살표)</b>이다. 장벽을 뚫고 지나가는 방향으로 맞춰 둔다.
    /// </summary>
    public class RollThroughBarrier : MonoBehaviour
    {
        [Header("연결")]
        [Tooltip("실제로 길을 막는 콜라이더. Is Trigger는 꺼야 한다(레이저 본체에 붙인다).")]
        [SerializeField] private Collider blockCollider;

        [Tooltip("감지 영역. 막는 콜라이더보다 앞뒤로 넉넉히 크게 잡는다.")]
        [SerializeField] private PlayerZoneTrigger zone;

        [Header("통과 조건")]
        [SerializeField] private bool allowRoll = true;
        [SerializeField] private bool allowJumpDash = true;

        [Header("막혔을 때 연출")]
        [Tooltip("카메라 흔들림 방향(로컬). 좌우로 지직거리게 하려면 X를 크게 준다.")]
        [SerializeField] private Vector3 shakeDirection = new Vector3(1f, 0.3f, 0f);

        [SerializeField, Min(0f)] private float shakeForce = 0.25f;

        [Tooltip("흔들림 지속(초). 감전 느낌이라 짧고 날카로운 편이 낫다.")]
        [SerializeField, Min(0f)] private float shakeDuration = 0.15f;

        [Tooltip("플레이어 몸에 피격 이펙트를 띄운다(CombatEvents.OnEnemyHitLanded 구독자가 처리).")]
        [SerializeField] private bool spawnHitEffect = true;

        [Tooltip("피격 경직 모션을 재생한다. 체력은 깎지 않고 '맞은 것처럼' 보이게만 한다.")]
        [SerializeField] private bool playHitMotion = true;

        [Tooltip("연출 재생 최소 간격(초). 장벽에 붙어 있을 때 연출이 도배되는 것을 막는다.")]
        [SerializeField, Min(0f)] private float feedbackCooldown = 0.5f;

        [Header("이벤트")]
        [Tooltip("막혔을 때. 사운드·파티클을 인스펙터에서 연결한다.")]
        [SerializeField] private UnityEvent onBlocked = new UnityEvent();

        [Tooltip("반대편으로 통과에 성공했을 때 1회. 튜토리얼 진행·연출 연결용.")]
        [SerializeField] private UnityEvent onPassed = new UnityEvent();

        private Player player;
        private PlayerCameraEffects cameraEffects;

        // 이번 체류 중에 콜라이더를 연 적이 있는지. 통과 성공 판정과 '끼임 방지'에 함께 쓴다.
        private bool openedDuringStay;

        // 감지 영역에 들어온 순간 플레이어가 있던 쪽(+1 / -1). 반대쪽으로 나가야 통과로 친다.
        private int enteredSide;

        private float lastFeedbackTime = -999f;

        private void OnEnable()
        {
            if (zone != null) zone.PlayerInsideChanged += OnZoneChanged;
        }

        private void OnDisable()
        {
            if (zone != null) zone.PlayerInsideChanged -= OnZoneChanged;

            // 꺼진 채로 남으면 다음에 켰을 때 길이 뚫려 있다.
            if (blockCollider != null) blockCollider.enabled = true;
            openedDuringStay = false;
        }

        private void Update()
        {
            if (zone == null || blockCollider == null) return;
            if (!zone.IsPlayerInside) return;

            // 한 번 연 뒤에는 영역을 벗어날 때까지 다시 닫지 않는다.
            // 구르기가 장벽 한가운데서 끝났을 때 콜라이더가 되살아나면
            // CharacterController가 콜라이더 안에 낀 채로 밀려나 엉뚱한 곳으로 튕긴다.
            if (openedDuringStay) return;

            if (!CanPass()) return;

            blockCollider.enabled = false;
            openedDuringStay = true;
        }

        private void OnZoneChanged(bool inside)
        {
            if (blockCollider == null) return;

            if (inside)
            {
                enteredSide = SideOf(PlayerPosition());
                openedDuringStay = false;

                // 진입 프레임에 바로 판정한다. Update를 기다리면 구르기 초입에 한 프레임 막힌다.
                if (CanPass())
                {
                    blockCollider.enabled = false;
                    openedDuringStay = true;
                }
                else
                {
                    PlayBlockedFeedback();
                }

                return;
            }

            // 완전히 벗어났다 → 길을 다시 막는다. 이 시점엔 플레이어가 콜라이더 밖이라 끼지 않는다.
            blockCollider.enabled = true;

            // 열린 채로 반대편으로 빠져나갔을 때만 통과 성공이다.
            // (굴러 들어왔다가 왔던 쪽으로 되돌아 나가면 성공이 아니다.)
            if (openedDuringStay && SideOf(PlayerPosition()) != enteredSide)
                onPassed?.Invoke();

            openedDuringStay = false;
        }

        // 구르기·공중 대시 중인지. 둘 다 '무적 회피' 동작이라 통과 허용의 기준으로 삼는다.
        private bool CanPass()
        {
            Player p = ResolvePlayer();
            if (p == null) return false;

            return (allowRoll && p.IsRolling) || (allowJumpDash && p.IsJumpDashing);
        }

        // 장벽의 Z축 기준으로 플레이어가 어느 쪽에 있는지. 0은 나오지 않도록 부호만 취한다.
        private int SideOf(Vector3 worldPos)
        {
            return Vector3.Dot(worldPos - transform.position, transform.forward) >= 0f ? 1 : -1;
        }

        private Vector3 PlayerPosition()
        {
            Player p = ResolvePlayer();
            return p != null ? p.transform.position : transform.position;
        }

        private void PlayBlockedFeedback()
        {
            if (Time.time - lastFeedbackTime < feedbackCooldown) return;
            lastFeedbackTime = Time.time;

            if (cameraEffects != null && shakeForce > 0f)
                cameraEffects.ShakeFor(shakeDirection, shakeForce, shakeDuration);

            // 플레이어 몸에 붙는 피격 이펙트. OnPlayerHitLanded(=플레이어의 공격이 적중)가 아니라
            // OnEnemyHitLanded(=플레이어가 맞음)를 써야 연출 종류가 맞는다.
            if (spawnHitEffect)
                CombatEvents.FireEnemyHitLanded(PlayerPosition() + Vector3.up);

            if (playHitMotion) PlayHitMotion();

            onBlocked?.Invoke();
        }

        // 데미지 없이 피격 경직만 재생한다. Player.ChangeState/HitState가 public이라 가능하다.
        //
        // 가드는 Player.OnDamaged(실제 피격 경로)와 같은 기준을 그대로 따른다. 여기서만 빠뜨리면
        // 죽은 뒤에 경직이 걸리거나, 회피 무적으로 지나가는 중에 경직이 끼어들어 구르기가 끊긴다.
        // (스킬 슈퍼아머는 일부러 뺐다 — 레이저는 '데미지 없는 연출'이라 스킬을 끊지 않는 편이 낫고,
        //  강피격 개념도 없어서 슈퍼아머를 뚫을 근거가 없다.)
        private void PlayHitMotion()
        {
            Player p = ResolvePlayer();
            if (p == null) return;

            if (p.Stats.IsDead) return;
            if (p.IsRolling || p.IsJumpDashing) return;
            if (p.Combat.IsSuperArmorMove) return;

            p.ChangeState(p.HitState);
        }

        // 플레이어는 씬 중간에 생성·교체될 수 있어(PlayerManager) 필요할 때 한 번만 찾아 캐싱한다.
        private Player ResolvePlayer()
        {
            if (player != null) return player;

            player = FindAnyObjectByType<Player>();
            if (player != null) cameraEffects = player.GetComponent<PlayerCameraEffects>();

            return player;
        }

#if UNITY_EDITOR
        // 통과 축(Z)을 씬 뷰에 그려 둔다. 장벽을 뚫고 지나가는 방향과 맞는지 눈으로 확인하기 위함이다.
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.9f);
            Vector3 from = transform.position - transform.forward * 1.5f;
            Vector3 to = transform.position + transform.forward * 1.5f;

            Gizmos.DrawLine(from, to);
            Gizmos.DrawSphere(to, 0.12f);
        }
#endif
    }
}
