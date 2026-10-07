using UnityEngine;
using UnityEngine.EventSystems;

namespace ProjectS.UI
{
    /// <summary>
    /// 채팅 채널 라벨을 마우스로 눌러 전체↔파티를 바꾼다(Tab과 같은 <see cref="ChatWindow.ToggleChannel"/> 경로).
    /// 라벨(TMP_Text, Raycast Target 켬)과 같은 오브젝트에 붙인다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Click이 아니라 PointerDown에서 처리한다.</b> 입력창 바깥을 누르면 누르는 순간 EventSystem이 입력창 선택을 풀고,
    /// 그러면 ChatWindow가 포커스 해제로 보고 <c>CanvasGroup.blocksRaycasts</c>를 꺼 버린다. 손을 뗄 때 판정하는
    /// Click은 그 사이 레이캐스트가 막혀 오지 않는다. 누르는 순간엔 아직 레이캐스트가 살아 있어 확실히 받는다.
    /// </para>
    /// <para>
    /// <b>이 오브젝트를 입력창(TMP_InputField) 오브젝트의 자식으로 두는 것을 권장한다.</b> EventSystem은 누른 대상의
    /// 부모에서 선택 가능한 것을 찾으므로, 입력창 자식을 누르면 선택이 그대로 유지돼 포커스가 한 프레임도 끊기지 않는다.
    /// 바깥에 두어도 <see cref="ChatWindow.ToggleChannel"/>이 포커스를 다시 걸어 동작은 같다(한 프레임 깜빡임만 있음).
    /// 단 입력창의 Text Area(RectMask2D) 아래에 넣으면 마스크에 잘리니, 입력창 루트의 직속 자식으로 둔다.
    /// </para>
    /// <para>
    /// 채팅 창은 포커스 중에만 레이캐스트를 받으므로, 이 클릭도 채팅 입력 중(그리고 커서가 보이는 마우스 모드)에만 먹는다.
    /// </para>
    /// </remarks>
    public class ChatChannelToggle : MonoBehaviour, IPointerDownHandler
    {
        [Tooltip("채널을 바꿀 채팅 창. 비우면 부모에서 찾는다(라벨은 보통 ChatWindow 아래에 있다).")]
        [SerializeField] private ChatWindow chatWindow;

        private void Awake()
        {
            if (chatWindow == null) chatWindow = GetComponentInParent<ChatWindow>(true);
        }

        /// <summary>좌클릭으로 누르는 순간 채널을 바꾼다(EventSystem이 호출).</summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (chatWindow != null) chatWindow.ToggleChannel();
        }
    }
}
