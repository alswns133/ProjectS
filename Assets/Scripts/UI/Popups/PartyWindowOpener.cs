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

        private IPartySource source;

        // 같은 초대에 팝업을 두 번 띄우지 않기 위한 기억. 국면이 한 번 벗어나야 다시 열린다.
        private bool invitePopupShown;

        private void Awake()
        {
            source = partySourceBehaviour as IPartySource;
            if (source == null) source = GetComponent<IPartySource>();

            if (source == null)
            {
                Debug.LogError("[PartyWindowOpener] IPartySource를 찾지 못했다 — 초대가 와도 팝업이 뜨지 않는다.", this);
            }
        }

        private void OnEnable()
        {
            if (source != null) source.OnChanged += OnPartyChanged;
        }

        private void OnDisable()
        {
            if (source != null) source.OnChanged -= OnPartyChanged;
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
