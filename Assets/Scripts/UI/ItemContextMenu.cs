using UnityEngine;
using UnityEngine.UI;
using ProjectS.Items;
using ProjectS.Managers;

namespace ProjectS.UI
{
    /// <summary>
    /// 인벤 소비품을 우클릭하면 커서 위치에 뜨는 재사용 단일 컨텍스트 메뉴(등록1/등록2/사용).
    /// 버튼은 <see cref="InventoryManager"/>의 퀵슬롯 등록·사용을 호출한다. 소비품 전용이며 재료·장비엔 뜨지 않는다.
    ///
    /// 배치: 전체화면 루트(이 컴포넌트)에 두 자식 — ①메뉴 박스(menuRect, 커서 위치로 이동) ②전체화면 블로커
    /// (backgroundBlocker, 바깥 클릭 시 닫힘). 루트는 캔버스 직속 자식으로 두고 Awake에서 자기 숨김.
    /// </summary>
    public class ItemContextMenu : MonoBehaviour
    {
        /// <summary>전역 접근점. 슬롯 우클릭 처리부가 타입 참조 없이 연다.</summary>
        public static ItemContextMenu Instance { get; private set; }

        [Tooltip("커서 위치로 이동하는 메뉴 박스(전체화면 루트가 아니라 안쪽 박스)")]
        [SerializeField] private RectTransform menuRect;
        [SerializeField] private Button register1Button;
        [SerializeField] private Button register2Button;
        [SerializeField] private Button useButton;
        [Tooltip("메뉴 뒤 전체화면 블로커. 바깥을 클릭하면 닫힌다(선택)")]
        [SerializeField] private Button backgroundBlocker;

        private RectTransform parentRect;
        private Canvas canvas;
        private ItemStack currentStack;

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

            if (register1Button != null) register1Button.onClick.AddListener(() => Register(0));
            if (register2Button != null) register2Button.onClick.AddListener(() => Register(1));
            if (useButton != null) useButton.onClick.AddListener(Use);
            if (backgroundBlocker != null) backgroundBlocker.onClick.AddListener(Hide);

            gameObject.SetActive(false);
        }

        /// <summary>소비품 우클릭 시 커서 위치에 메뉴를 연다(소비품이 아니면 무시).</summary>
        /// <param name="stack">대상 스택</param>
        /// <param name="screenPos">커서 스크린 좌표</param>
        public void Show(ItemStack stack, Vector2 screenPos)
        {
            if (stack == null || !stack.IsConsumable) return;
            currentStack = stack;

            gameObject.SetActive(true);
            Position(screenPos);
        }

        /// <summary>메뉴를 닫는다.</summary>
        public void Hide()
        {
            currentStack = null;
            if (this != null) gameObject.SetActive(false);
        }

        private void Register(int slotIndex)
        {
            if (currentStack?.Item != null)
                InventoryManager.Instance?.RegisterQuickSlot(slotIndex, currentStack.Item.Index);
            Hide();
        }

        private void Use()
        {
            if (currentStack != null) InventoryManager.Instance?.UseConsumable(currentStack);
            Hide();
        }

        // 커서가 있는 화면 구역에 맞춰 메뉴 박스를 커서 지점에 붙인다(툴팁과 동일 규칙 — 화면 밖으로 안 나가게).
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
