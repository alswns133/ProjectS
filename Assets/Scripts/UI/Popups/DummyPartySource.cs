using System;
using System.Collections;
using UnityEngine;

namespace ProjectS.UI
{
    /// <summary>
    /// 네트워크가 붙기 전까지 파티 상태를 흉내 내는 가짜 데이터원.
    /// 초대·수락·출발 카운트다운까지 실제 흐름과 같은 순서로 흘려, UI를 끝까지 만들 수 있게 한다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>즉시 수락시키지 않는 것이 핵심이다.</b> 초대는 실제로 왕복에 시간이 걸리고 거절당할 수 있어,
    /// UI에는 "초대 중…" 대기 상태와 실패 처리가 있어야 한다. 곧바로 파티가 생겨 버리면
    /// 그 두 화면을 영영 못 보고 넘어가, 나중에 네트워크를 붙이는 날 처음 마주치게 된다.
    /// </para>
    /// <para>
    /// <b>받는 쪽 화면은 인스펙터 컨텍스트 메뉴로 만든다.</b> 혼자서는 초대를 받을 수 없어
    /// <see cref="PartyPhase.Invited"/> 화면을 볼 방법이 없다. ⋮ 메뉴의 "초대 받은 것처럼"으로 띄운다.
    /// </para>
    /// <para>
    /// <b>이 컴포넌트는 던전 입장 창 밖에 둔다.</b> 창이 닫히면 꺼져서 코루틴이 끊기고 파티가 사라진다.
    /// UIManager 아래처럼 씬을 넘어 사는 오브젝트에 붙이고, 슬롯 바와 결성창이 같은 것을 참조한다.
    /// </para>
    /// <para>
    /// <b>이 클래스는 나중에 통째로 지운다.</b> 네트워크 담당자가 <see cref="IPartySource"/>를 구현한
    /// 컴포넌트를 만들어 같은 자리에 끼우면 UI 코드는 한 줄도 바뀌지 않는다.
    /// </para>
    /// </remarks>
    public class DummyPartySource : MonoBehaviour, IPartySource
    {
        /// <inheritdoc/>
        public event Action OnChanged;

        [Header("나")]
        [SerializeField] private string selfNickname = "하루";
        [SerializeField, Min(1)] private int selfLevel = 24;
        [SerializeField, Min(0)] private int selfCharacterType;

        [Header("가려는 던전(표시 전용)")]
        [SerializeField] private string dungeonName = "폐기된 연구시설 1-1";
        [SerializeField] private string difficultyLabel = "노말";

        [Header("초대 흉내")]
        [Tooltip("초대를 보낸 뒤 응답이 오기까지 걸리는 시간(초). 0이면 즉시 응답한다.")]
        [SerializeField, Min(0f)] private float responseDelay = 1.5f;

        [Tooltip("끄면 상대가 거절한 것처럼 행동한다 — 실패 흐름을 확인할 때 쓴다.")]
        [SerializeField] private bool acceptInvite = true;

        [Header("타이머(초)")]
        [Tooltip("초대를 받은 쪽이 수락/거절을 고를 시간.")]
        [SerializeField, Min(1f)] private float inviteTimeout = 20f;

        [Tooltip("파티장이 출발을 건 뒤 파티원이 확인할 시간.")]
        [SerializeField, Min(1f)] private float departTimeout = 30f;

        /// <inheritdoc/>
        public PartyPhase Phase { get; private set; } = PartyPhase.None;

        /// <inheritdoc/>
        public PartyMemberInfo Self { get; private set; }

        /// <inheritdoc/>
        public PartyMemberInfo Partner { get; private set; }

        /// <inheritdoc/>
        public PartyMemberInfo Inviter { get; private set; }

        /// <inheritdoc/>
        /// <remarks>더미에서는 초대를 보낸 쪽이 파티장, 받아서 수락한 쪽이 파티원이 된다.</remarks>
        public bool IsLeader { get; private set; } = true;

        /// <inheritdoc/>
        public bool IsInviting { get; private set; }

        /// <inheritdoc/>
        public string DungeonName => dungeonName;

        /// <inheritdoc/>
        public string DifficultyLabel => difficultyLabel;

        /// <inheritdoc/>
        public float RemainingSeconds => Mathf.Max(0f, deadline - Time.unscaledTime);

        /// <inheritdoc/>
        public float PhaseDuration { get; private set; }

        // 타이머는 남은 시간을 매 프레임 계산해 쓴다. 코루틴으로 감소시키면 값을 두 곳에서 들고 있게 돼
        // 일시정지·재개에서 어긋난다. 기준 시각 하나만 두고 필요할 때 빼서 쓴다.
        private float deadline;
        private Coroutine pending;

        private void Awake()
        {
            Self = new PartyMemberInfo("self", selfNickname, selfLevel, selfCharacterType,
                                       isOnline: true, PartyInviteState.Invitable);
        }

        private void Update()
        {
            // 타이머가 도는 국면에서만 만료를 본다. 만료 처리는 국면을 바꾸므로 OnChanged를 한 번 쏜다.
            if (Phase != PartyPhase.Invited && Phase != PartyPhase.Departing) return;
            if (RemainingSeconds > 0f) return;

            if (Phase == PartyPhase.Invited)
            {
                // 응답하지 않으면 초대는 사라진다.
                Inviter = null;
                SetPhase(PartyPhase.None);
                return;
            }

            // 30초가 지나도 파티는 유지되고 출발만 취소된다(2026-09-07 확정).
            Debug.Log("[DummyPartySource] 출발이 취소되었다 — 파티는 유지된다.", this);
            SetPhase(PartyPhase.Formed);
        }

        // ── 초대 ────────────────────────────────────────────────

        /// <inheritdoc/>
        public void RequestInvite(PartyMemberInfo target)
        {
            if (target == null || IsInviting || Partner != null) return;

            IsInviting = true;
            OnChanged?.Invoke();

            pending = StartCoroutine(RespondLater(target));
        }

        /// <inheritdoc/>
        public void AcceptInvite()
        {
            if (Phase != PartyPhase.Invited) return;

            Partner = Inviter;
            Inviter = null;
            IsLeader = false;      // 부른 쪽이 파티장이다
            SetPhase(PartyPhase.Formed);
        }

        /// <inheritdoc/>
        public void DeclineInvite()
        {
            if (Phase != PartyPhase.Invited) return;

            Inviter = null;
            SetPhase(PartyPhase.None);
        }

        // ── 파티 유지 ───────────────────────────────────────────

        /// <inheritdoc/>
        public void RequestKick()
        {
            if (Partner == null) return;

            Partner = null;
            IsLeader = true;
            SetPhase(PartyPhase.None);
        }

        /// <inheritdoc/>
        public void RequestLeave()
        {
            // 더미에서는 내가 나가든 상대를 내보내든 결과가 같다(2인 파티라 남는 사람이 없다).
            RequestKick();
        }

        // ── 출발 ────────────────────────────────────────────────

        /// <inheritdoc/>
        public void RequestDepart()
        {
            if (Phase != PartyPhase.Formed || !IsLeader) return;

            SetPhase(PartyPhase.Departing);
        }

        /// <inheritdoc/>
        public void CancelDepart()
        {
            if (Phase != PartyPhase.Departing) return;

            // 카운터만 멈추고 파티는 그대로 둔다.
            SetPhase(PartyPhase.Formed);
        }

        /// <inheritdoc/>
        public void ConfirmDepart()
        {
            if (Phase != PartyPhase.Departing) return;

            Debug.Log("[DummyPartySource] 두 명 다 던전에 입장한 것으로 처리했다.", this);
            SetPhase(PartyPhase.Formed);
        }

        // ── 테스트용 ────────────────────────────────────────────

        /// <summary>
        /// 초대를 받은 것처럼 만들어 결성창의 '초대 대기' 화면을 띄운다.
        /// 혼자 플레이하면서는 이 화면을 볼 방법이 없어 둔 통로다.
        /// </summary>
        [ContextMenu("초대 받은 것처럼")]
        public void SimulateIncomingInvite()
        {
            if (Partner != null) return;

            Inviter = new PartyMemberInfo("inviter", "시온", 31, 0, isOnline: true, PartyInviteState.Invitable);
            SetPhase(PartyPhase.Invited);
        }

        private void OnDisable()
        {
            // 대기 중 코루틴이 끊기면 IsInviting이 켜진 채 굳어 빈 칸이 영영 잠긴다.
            if (pending == null) return;

            StopCoroutine(pending);
            pending = null;
            IsInviting = false;
        }

        private IEnumerator RespondLater(PartyMemberInfo target)
        {
            if (responseDelay > 0f) yield return new WaitForSeconds(responseDelay);

            IsInviting = false;
            pending = null;

            if (acceptInvite)
            {
                Partner = target;
                IsLeader = true;
                SetPhase(PartyPhase.Formed);
                yield break;
            }

            Debug.Log($"[DummyPartySource] {target.Nickname}이 초대를 거절한 것으로 처리했다.", this);
            OnChanged?.Invoke();
        }

        // 국면을 바꾸며 그 국면의 타이머를 건다. 타이머가 없는 국면이면 0으로 눕힌다.
        private void SetPhase(PartyPhase phase)
        {
            Phase = phase;

            PhaseDuration = phase switch
            {
                PartyPhase.Invited   => inviteTimeout,
                PartyPhase.Departing => departTimeout,
                _                    => 0f,
            };

            deadline = PhaseDuration > 0f ? Time.unscaledTime + PhaseDuration : 0f;
            OnChanged?.Invoke();
        }
    }
}
