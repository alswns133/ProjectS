using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using ProjectS.Managers;
using ProjectS.Scenes;

namespace ProjectS.UI
{
    /// <summary>
    /// 파티 창을 여는 유일한 통로(docs/PARTY_WINDOW_UI.md §7·§8).
    /// 마을에서 <c>Tab</c>으로 결성창을 열고 닫으며, 초대가 오면 수락 팝업을 스스로 띄운다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>파티 데이터원과 같은 오브젝트에 붙인다.</b> 창들은 닫혀 있는 동안 꺼져 있어 스스로 초대를
    /// 감시할 수 없다. 항상 살아 있는 이쪽이 <see cref="IPartySource.OnChanged"/>를 듣고 있어야
    /// 초대를 놓치지 않는다.
    /// </para>
    /// <para>
    /// <b>던전 안에서는 <c>Tab</c>이 먹지 않는다.</b> 이미 들어와 있어 결성창에 할 일이 없고,
    /// 전투 중 창이 열리는 것만 위험하다. 던전 안의 파티 정보는 파티현황창(HUD)이 담당한다.
    /// </para>
    /// <para>
    /// <b>글자를 입력하는 중에는 단축키를 먹지 않는다.</b> 채팅이나 검색창에 'Tab'이 들어가는 순간
    /// 창이 튀어나오면, 이름을 치다가 화면이 바뀌는 꼴이 된다.
    /// </para>
    /// </remarks>
    public class PartyWindowOpener : MonoBehaviour
    {
        [Header("데이터원")]
        [Tooltip("파티 상태를 물어볼 곳. 비우면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private MonoBehaviour partySourceBehaviour;

        [Header("단축키")]
        [Tooltip("결성창을 열고 닫을 키.")]
        [SerializeField] private Key toggleKey = Key.Tab;

        [Tooltip("끄면 던전 안에서도 단축키가 먹는다. 기본은 마을 전용.")]
        [SerializeField] private bool villageOnly = true;

        // 지금 구독 중인 소스(슬롯/같은 오브젝트 우선, 없으면 provider에서 온 것).
        private IPartySource source;

        // 인스펙터 슬롯 또는 같은 오브젝트에서 잡은 소스(Awake에서 1회 결정). provider보다 우선한다.
        private IPartySource explicitSource;

        // 같은 초대에 팝업을 두 번 띄우지 않기 위한 기억. 국면이 한 번 벗어나야 다시 열린다.
        private bool invitePopupShown;

        // 같은 출발에 결성창을 두 번 자동으로 띄우지 않기 위한 기억(초대의 invitePopupShown과 같은 취지).
        // Departing을 벗어나면 지워, 다음 출발에 다시 뜨게 한다.
        private bool departPromptShown;

        private void Awake()
        {
            // 슬롯이 있으면 그것, 없으면 같은 오브젝트에서. 여기서 못 찾아도 에러가 아니다 —
            // 실 소스(NetworkPartySource)의 provider 등록이 이 Awake보다 늦을 수 있어, OnEnable에서
            // provider를 통해 뒤늦게 붙는다(그래서 같은 오브젝트가 아니어도 동작한다).
            explicitSource = (partySourceBehaviour as IPartySource) ?? GetComponent<IPartySource>();
        }

        private void OnEnable()
        {
            // 소스가 나중에 등록/교체돼도 따라 붙게 provider 변경을 듣는다.
            PartySourceProvider.Changed += Rebind;
            Rebind();
        }

        private void OnDisable()
        {
            PartySourceProvider.Changed -= Rebind;
            BindTo(null);
        }

        // 지금 유효한 소스로 다시 붙는다. 슬롯/같은 오브젝트가 우선, 없으면 등록된 provider.
        private void Rebind()
        {
            IPartySource effective = explicitSource ?? PartySourceProvider.Current;
            if (ReferenceEquals(effective, source)) return;

            BindTo(effective);
            Debug.Log($"[진단][PartyWindowOpener] bind → partySource={(source as MonoBehaviour != null ? $"{((MonoBehaviour)source).name}#{((MonoBehaviour)source).GetInstanceID()}" : "null")}", this);

            // 붙는 순간 이미 초대가 와 있을 수 있으니(등록이 초대보다 늦었다면) 즉시 한 번 평가한다.
            if (source != null) OnPartyChanged();
        }

        // 구독 대상을 갈아 끼운다(옛 소스 해제 → 새 소스 구독). 짝을 맞춰 중복 구독을 막는다.
        private void BindTo(IPartySource next)
        {
            if (source != null) source.OnChanged -= OnPartyChanged;
            source = next;
            if (source != null) source.OnChanged += OnPartyChanged;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard[toggleKey].wasPressedThisFrame) return;
            if (IsTyping()) return;
            if (villageOnly && DungeonContext.CurrentDungeonId != 0) return;

            ToggleRoster();
        }

        /// <summary>결성창을 열거나 닫는다. 파티현황창(HUD) 클릭도 이 함수로 연결한다.</summary>
        public void ToggleRoster()
        {
            if (UIManager.Instance == null) return;

            if (UIManager.Instance.IsPopupOpen<PartyRosterPopup>())
            {
                UIManager.Instance.ClosePopup<PartyRosterPopup>();
                return;
            }

            UIManager.Instance.ShowPopup<PartyRosterPopup>();
        }

        private void OnPartyChanged()
        {
            if (source == null || UIManager.Instance == null)
            {
                Debug.LogWarning($"[진단][PartyWindowOpener] OnChanged 왔지만 무시 — source={(source == null ? "null" : "ok")}, UIManager={(UIManager.Instance == null ? "null" : "ok")}", this);
                return;
            }

            // ── 출발 자동 프롬프트 ──────────────────────────────────
            // 파티장이 출발을 걸면(Phase==Departing) 멤버에게 결성창을 자동으로 띄워 "던전 입장(확인)"을
            // 받게 한다(초대 자동 팝업과 같은 취지 — 멤버가 Tab을 몰라도 확인할 수 있게).
            // 파티장은 이미 결성창에서 출발을 걸었으므로 대상이 아니다(!IsLeader). 이미 열려 있으면 다시 열지 않는다.
            if (source.Phase == PartyPhase.Departing)
            {
                if (!departPromptShown && !source.IsLeader
                    && !UIManager.Instance.IsPopupOpen<PartyRosterPopup>())
                {
                    Debug.Log("[진단][PartyWindowOpener] Phase=Departing 감지(멤버) → 결성창 자동 오픈", this);
                    departPromptShown = true;
                    UIManager.Instance.ShowPopup<PartyRosterPopup>();
                }
            }
            else
            {
                // 출발 국면을 벗어나면 기억을 지운다. 다음 출발에 다시 띄운다.
                departPromptShown = false;
            }

            // ── 초대 자동 팝업 ──────────────────────────────────────
            if (source.Phase != PartyPhase.Invited)
            {
                // 국면을 벗어나면 기억을 지운다. 다음 초대는 다시 띄워야 한다.
                invitePopupShown = false;
                return;
            }

            if (invitePopupShown) return;

            Debug.Log("[진단][PartyWindowOpener] Phase=Invited 감지 → ShowPopup<PartyInviteAcceptPopup>", this);
            invitePopupShown = true;
            UIManager.Instance.ShowPopup<PartyInviteAcceptPopup>();
        }

        // 입력 필드에 포커스가 있으면 게임플레이 단축키를 삼킨다.
        private static bool IsTyping()
        {
            EventSystem events = EventSystem.current;
            GameObject selected = events != null ? events.currentSelectedGameObject : null;
            if (selected == null) return false;

            return selected.GetComponent<TMP_InputField>() != null
                || selected.GetComponent<UnityEngine.UI.InputField>() != null;
        }
    }
}
