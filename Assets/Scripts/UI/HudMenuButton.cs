using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// HUD 하단 메뉴바 아이콘 버튼 하나. 클릭하면 <see cref="target"/>이 가리키는 창을 열고/닫는다(토글).
    /// 키보드 핫키(I/P/K 등)와 <b>같은 토글 경로</b>(<see cref="PopupToggle"/> · <see cref="QuestTrackerHud"/>)를
    /// 재사용해, 아이콘과 단축키가 똑같이 동작하게 한다.
    /// <para>
    /// 접힘/펼침(애로우)은 이 컴포넌트가 아니라 <see cref="Framework.FoldableToolbar"/>가 따로 담당한다.
    /// 각 아이콘 오브젝트(Button)에 이 컴포넌트를 붙이고 target만 고르면 된다(Quest는 트래커 참조도 연결).
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class HudMenuButton : MonoBehaviour
    {
        /// <summary>이 아이콘이 여닫는 창의 종류.</summary>
        public enum Target
        {
            None,       // 미정 슬롯: 배선만 하고 눌러도 아무 동작 안 함
            Inventory,  // 인벤토리(I)
            Equipment,  // 장비창(P)
            Skill,      // 스킬창(K)
            Quest,      // 퀘스트 트래커(J) — 팝업이 아니라 트래커 펼침+마우스모드 토글
        }

        [Tooltip("이 아이콘이 여는 창. None이면 눌러도 아무 동작 안 함(미정 슬롯).")]
        [SerializeField] private Target target = Target.None;

        [Tooltip("target이 Quest일 때 토글할 퀘스트 트래커. 같은 HUD 씬에 있으므로 인스펙터로 직접 연결한다.")]
        [SerializeField] private QuestTrackerHud questTracker;

        private Button button;

        private void Awake() => button = GetComponent<Button>();

        // 클릭 구독은 켜질 때만 걸고 꺼질 때 뗀다(중복 구독·파괴 후 호출 방지).
        private void OnEnable() => button.onClick.AddListener(OnClicked);
        private void OnDisable() => button.onClick.RemoveListener(OnClicked);

        private void OnClicked()
        {
            switch (target)
            {
                // 팝업 3종은 핫키와 같은 매핑(강화창 예외 포함)을 공유하도록 PopupToggle에 위임한다.
                case Target.Inventory: PopupToggle.Toggle(PopupToggle.PopupKind.Inventory); break;
                case Target.Equipment: PopupToggle.Toggle(PopupToggle.PopupKind.Equipment); break;
                case Target.Skill:     PopupToggle.Toggle(PopupToggle.PopupKind.Skill); break;

                // 퀘스트는 팝업이 아니라 트래커 펼침+마우스모드 토글이라 별도 경로를 탄다.
                case Target.Quest:
                    if (questTracker != null) questTracker.ToggleWithCursor();
                    else Debug.LogWarning("[HudMenuButton] Quest 대상인데 questTracker가 연결되지 않음", this);
                    break;
                case Target.None: break;   // 미정 슬롯: 무동작
            }
        }
    }
}
