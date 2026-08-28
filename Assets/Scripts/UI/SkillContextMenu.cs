using UnityEngine;
using UnityEngine.UI;
using ProjectS.Skills;

namespace ProjectS.UI
{
    /// <summary>
    /// 액티브 스킬을 우클릭하면 커서 위치에 뜨는 재사용 단일 컨텍스트 메뉴. 단축키 슬롯 [1][2][3][4] 버튼으로
    /// 그 스킬을 해당 슬롯에 등록한다(<see cref="SkillState.SetSlot"/>). 인벤 <see cref="ItemContextMenu"/>의 스킬 버전이다.
    /// </summary>
    /// <remarks>
    /// 배치: 전체화면 루트(이 컴포넌트)에 두 자식 — ①메뉴 박스(menuRect, 커서로 이동) ②전체화면 블로커(바깥 클릭 시 닫힘).
    /// 루트는 OverlayCanvas 직속 자식으로 두고 Awake에서 자기 숨김. 의존 방향(화면→Framework)을 지키려
    /// Framework 슬롯은 이 메뉴를 직접 열지 않고 우클릭 콜백만 올리며, 호스트(SkillPopup)가 <see cref="Show"/>를 부른다.
    /// </remarks>
    public class SkillContextMenu : MonoBehaviour
    {
        /// <summary>전역 접근점.</summary>
        public static SkillContextMenu Instance { get; private set; }

        [Tooltip("커서 위치로 이동하는 메뉴 박스(전체화면 루트가 아니라 안쪽 박스)")]
        [SerializeField] private RectTransform menuRect;
        [Tooltip("단축키 1~4 등록 버튼(순서대로 연결). SlotCount와 길이를 맞춘다.")]
        [SerializeField] private Button[] slotButtons = new Button[SkillState.SlotCount];
        [Tooltip("메뉴 뒤 전체화면 블로커. 바깥을 클릭하면 닫힌다(선택)")]
        [SerializeField] private Button backgroundBlocker;

        private RectTransform parentRect;
        private Canvas canvas;
        private int currentSkillId;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (menuRect == null) menuRect = (RectTransform)transform;
            parentRect = menuRect.parent as RectTransform;
            canvas = GetComponentInParent<Canvas>();
            menuRect.anchorMin = menuRect.anchorMax = new Vector2(0.5f, 0.5f);

            if (slotButtons != null)
            {
                for (int i = 0; i < slotButtons.Length; i++)
                {
                    int slotNumber = i + 1;   // 버튼 인덱스 0 = 슬롯 1
                    if (slotButtons[i] != null)
                        slotButtons[i].onClick.AddListener(() => Register(slotNumber));
                }
            }

            if (backgroundBlocker != null) backgroundBlocker.onClick.AddListener(Hide);

            gameObject.SetActive(false);
        }

        /// <summary>이 스킬을 등록할 슬롯을 고르는 메뉴를 커서 위치에 연다.</summary>
        /// <param name="skillId">대상 스킬ID(액티브)</param>
        /// <param name="screenPos">커서 스크린 좌표</param>
        public void Show(int skillId, Vector2 screenPos)
        {
            if (skillId == 0) return;
            currentSkillId = skillId;

            gameObject.SetActive(true);
            Position(screenPos);
        }

        /// <summary>메뉴를 닫는다.</summary>
        public void Hide()
        {
            currentSkillId = 0;
            if (this != null) gameObject.SetActive(false);
        }

        private void Register(int slotNumber)
        {
            if (currentSkillId != 0) SkillState.SetSlot(slotNumber, currentSkillId);
            Hide();
        }

        // 커서가 있는 화면 구역에 맞춰 메뉴 박스를 커서 지점에 붙인다(ItemContextMenu와 동일 규칙).
        private void Position(Vector2 screenPos)
        {
            bool left = screenPos.x < Screen.width * 0.5f;
            bool top = screenPos.y > Screen.height * 0.5f;
            menuRect.pivot = new Vector2(left ? 0f : 1f, top ? 1f : 0f);

            if (parentRect != null)
            {
                Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    ? canvas.worldCamera
                    : null;

                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPos, cam, out Vector2 local))
                    menuRect.anchoredPosition = local;
            }

            menuRect.SetAsLastSibling();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;
    }
}
