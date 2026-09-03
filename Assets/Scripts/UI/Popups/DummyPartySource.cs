using System;
using System.Collections;
using UnityEngine;

namespace ProjectS.UI
{
    /// <summary>
    /// 네트워크가 붙기 전까지 파티 상태를 흉내 내는 가짜 데이터원. 초대를 보내면 정해진 시간 뒤에
    /// 수락 또는 거절이 온 것처럼 행동한다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>즉시 수락시키지 않는 것이 핵심이다.</b> 초대는 실제로 왕복에 시간이 걸리고 거절당할 수 있어,
    /// UI에는 "초대 중…" 대기 상태와 실패 처리가 있어야 한다. 곧바로 파티가 생겨 버리면
    /// 그 두 화면을 영영 못 보고 넘어가, 나중에 네트워크를 붙이는 날 처음 마주치게 된다.
    /// </para>
    /// <para>
    /// <b>이 클래스는 나중에 통째로 지운다.</b> 네트워크 담당자가 <see cref="IPartySource"/>를 구현한
    /// 컴포넌트를 만들어 슬롯의 슬롯에 끼우면 UI 코드는 한 줄도 바뀌지 않는다.
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

        [Header("초대 흉내")]
        [Tooltip("초대를 보낸 뒤 응답이 오기까지 걸리는 시간(초). 0이면 즉시 응답한다.")]
        [SerializeField, Min(0f)] private float responseDelay = 1.5f;

        [Tooltip("끄면 상대가 거절한 것처럼 행동한다 — 실패 흐름을 확인할 때 쓴다.")]
        [SerializeField] private bool acceptInvite = true;

        /// <inheritdoc/>
        public PartyMemberInfo Self { get; private set; }

        /// <inheritdoc/>
        public PartyMemberInfo Partner { get; private set; }

        /// <inheritdoc/>
        /// <remarks>더미에서는 초대를 보낸 쪽이 항상 파티장이다.</remarks>
        public bool IsLeader { get; private set; } = true;

        /// <inheritdoc/>
        public bool IsInviting { get; private set; }

        private Coroutine pending;

        private void Awake()
        {
            Self = new PartyMemberInfo("self", selfNickname, selfLevel, selfCharacterType,
                                       isOnline: true, PartyInviteState.Invitable);
        }

        /// <inheritdoc/>
        public void RequestInvite(PartyMemberInfo target)
        {
            if (target == null || IsInviting || Partner != null) return;

            IsInviting = true;
            OnChanged?.Invoke();

            pending = StartCoroutine(RespondLater(target));
        }

        /// <inheritdoc/>
        public void RequestKick()
        {
            if (Partner == null) return;

            Partner = null;
            OnChanged?.Invoke();
        }

        /// <inheritdoc/>
        public void RequestLeave()
        {
            // 더미에서는 내가 나가든 상대를 내보내든 결과가 같다(2인 파티라 남는 사람이 없다).
            RequestKick();
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
            }
            else
            {
                Debug.Log($"[DummyPartySource] {target.Nickname}이 초대를 거절한 것으로 처리했다.", this);
            }

            OnChanged?.Invoke();
        }
    }
}
