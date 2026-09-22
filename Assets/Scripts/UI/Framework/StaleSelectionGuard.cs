using UnityEngine;
using UnityEngine.EventSystems;

namespace ProjectS.UI.Framework
{
    /// <summary>
    /// <b>비활성화된 오브젝트를 가리키는 EventSystem 선택을 매 프레임 끝에 해제하는 감시자.</b>
    /// 첫 씬 로드 후 1회 자동 생성되어 씬을 넘어 살아남는다 — 씬이나 창마다 배치할 필요가 없다.
    ///
    /// <b>없으면 생기는 문제</b> — 창을 닫았다 다시 열면 <b>직전에 눌렀던 버튼이 Selected 색으로 남는다.</b>
    /// 원인은 버튼이 아니라 EventSystem에 있다:
    ///   1. 마우스로 버튼을 누르면 EventSystem의 선택 대상이 그 버튼이 된다.
    ///   2. 창을 닫으면 <c>Selectable.OnDisable</c>이 <c>InstantClearState()</c>로 <b>자기 틴트만</b> 지운다.
    ///   3. 그런데 <b>EventSystem은 선택 대상이 비활성화돼도 그 참조를 비우지 않는다</b>
    ///      (uGUI의 EventSystem에는 activeInHierarchy 검사도, 자동 해제도 없다 — 의도된 동작이라
    ///      우리가 정리해 주지 않으면 참조가 그대로 남는다).
    ///   4. 창을 다시 열면 <c>Selectable.OnEnable</c>이 <c>EventSystem.current.currentSelectedGameObject == gameObject</c>를
    ///      보고 <c>hasSelection</c>을 되살려 <c>currentSelectionState</c>가 다시 Selected가 된다.
    /// 즉 상한 상태는 버튼이 아니라 <b>EventSystem이 들고 있는 죽은 참조</b>이므로, 그것이 생기는 순간
    /// (= 선택 대상이 비활성이 되는 순간) 끊는 것이 이 클래스의 역할이다.
    ///
    /// <b>왜 창마다 고치지 않는가</b> — 창은 BasePanel 4종·BasePopup 16종·NPC 뷰(<c>NpcScreenViewBase</c>)에
    /// 더해 코드가 직접 SetActive로 토글하는 것들까지 있어, 여는 쪽을 하나씩 고치면 빠뜨리는 곳이 생기고
    /// 앞으로 만들 창마다 같은 처리를 기억해야 한다. 여기서 끊으면 종류와 무관하게 한 번에 끝난다.
    ///
    /// <b>왜 '열 때 해제'가 아닌가</b> — 열 때 무조건 선택을 비우면, 활성화 직후 일부러 선택을 거는 화면이 깨진다.
    /// 실제로 <c>NpcQuestListView</c>는 현재 퀘스트 행에 EventSystem 선택을 걸어 Selected 색으로 하이라이트하고,
    /// 그것 때문에 <c>NpcScreenViewBase.OnShown</c>(활성화 뒤 훅)이 따로 있다. 이 감시자는 <b>비활성 대상만</b>
    /// 건드리므로 그런 의도된 선택은 그대로 둔다.
    ///
    /// 검사는 <c>LateUpdate</c>에서 한다. 그 프레임의 모든 UI 토글이 끝난 뒤라야 "지금 꺼져 있는가"가 확정된다.
    /// </summary>
    public class StaleSelectionGuard : MonoBehaviour
    {
        /// <summary>전역 인스턴스. 중복 생성을 막기 위한 것이라 외부에서 쓸 일은 없다.</summary>
        public static StaleSelectionGuard Instance { get; private set; }

        // 첫 씬 로드 후 1회 자동 생성. 씬마다 배치할 필요가 없다(누락 방지).
        // AfterSceneLoad라야 DontDestroyOnLoad 이관이 안전하다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;

            GameObject go = new GameObject("[StaleSelectionGuard]");
            go.AddComponent<StaleSelectionGuard>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            // 부트스트랩 경로가 아닌 씬 배치로 중복 생성돼도 하나만 남긴다.
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void LateUpdate()
        {
            EventSystem events = EventSystem.current;
            if (events == null) return;

            GameObject selected = events.currentSelectedGameObject;

            // 살아 있고 켜져 있는 선택은 누군가 의도해서 건 것이다(키보드 내비게이션·퀘스트 행 하이라이트). 그대로 둔다.
            if (selected == null || selected.activeInHierarchy) return;

            events.SetSelectedGameObject(null);
        }
    }
}
