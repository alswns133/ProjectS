using UnityEngine;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.Enhance;
using ProjectS.Items;
using ProjectS.Managers;

namespace ProjectS.UI
{
    /// <summary>
    /// 인벤 아이템을 우클릭하면 커서 위치에 뜨는 재사용 단일 컨텍스트 메뉴.
    /// 아이템 종류로 버튼을 가른다 — <b>장비=[장착][파괴]</b>, <b>소비품=[등록1][등록2][사용][파괴]</b>.
    /// 재료(비소비 스택)엔 뜨지 않는다. 버튼은 <see cref="InventoryManager"/>의 장착·퀵슬롯 등록·사용·파괴를 호출한다.
    ///
    /// 파괴는 되돌릴 수 없어 <see cref="ConfirmDialog"/>로 한 번 더 확인한 뒤에만 실행한다(확인창이 없으면 파괴 취소).
    ///
    /// 배치: 전체화면 루트(이 컴포넌트)에 두 자식 — ①메뉴 박스(menuRect, 커서 위치로 이동. VerticalLayoutGroup+
    /// ContentSizeFitter라 켜진 버튼 수에 맞춰 높이가 줄고 늘어난다) ②전체화면 블로커(바깥 클릭 시 닫힘).
    /// 루트는 OverlayCanvas 직속 자식으로 두고 Awake에서 자기 숨김. 계층·배선은 ItemContextMenuGenerator가 만든다.
    /// </summary>
    public class ItemContextMenu : MonoBehaviour
    {
        /// <summary>전역 접근점. 슬롯 우클릭 처리부가 타입 참조 없이 연다.</summary>
        public static ItemContextMenu Instance { get; private set; }

        [Tooltip("커서 위치로 이동하는 메뉴 박스(전체화면 루트가 아니라 안쪽 박스)")]
        [SerializeField] private RectTransform menuRect;
        [Tooltip("장비 전용: 착용")]
        [SerializeField] private Button equipButton;
        [SerializeField] private Button register1Button;
        [SerializeField] private Button register2Button;
        [SerializeField] private Button useButton;
        [Tooltip("장비·소비품 공통: 파괴(버리기)")]
        [SerializeField] private Button discardButton;
        [Tooltip("메뉴 뒤 전체화면 블로커. 바깥을 클릭하면 닫힌다(선택)")]
        [SerializeField] private Button backgroundBlocker;

        private RectTransform parentRect;
        private Canvas canvas;

        // 현재 대상. 장비를 열면 currentEquipment만, 소비품을 열면 currentStack만 채워진다(다른 쪽은 null).
        private ItemStack currentStack;
        private EquipmentInstance currentEquipment;

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

            if (equipButton != null) equipButton.onClick.AddListener(Equip);
            if (register1Button != null) register1Button.onClick.AddListener(() => Register(0));
            if (register2Button != null) register2Button.onClick.AddListener(() => Register(1));
            if (useButton != null) useButton.onClick.AddListener(Use);
            if (discardButton != null) discardButton.onClick.AddListener(Discard);
            if (backgroundBlocker != null) backgroundBlocker.onClick.AddListener(Hide);

            gameObject.SetActive(false);
        }

        /// <summary>소비품 우클릭 시 [등록1][등록2][사용][파괴] 메뉴를 커서 위치에 연다(소비품이 아니면 무시).</summary>
        /// <param name="stack">대상 스택</param>
        /// <param name="screenPos">커서 스크린 좌표</param>
        public void Show(ItemStack stack, Vector2 screenPos)
        {
            if (stack == null || !stack.IsConsumable) return;
            currentStack = stack;
            currentEquipment = null;

            SetButton(equipButton, false);
            SetButton(register1Button, true);
            SetButton(register2Button, true);
            SetButton(useButton, true);
            SetButton(discardButton, true);

            gameObject.SetActive(true);
            Position(screenPos);
        }

        /// <summary>장비 우클릭 시 [장착][파괴] 메뉴를 커서 위치에 연다(빈 인스턴스면 무시).</summary>
        /// <param name="equipment">대상 장비 인스턴스</param>
        /// <param name="screenPos">커서 스크린 좌표</param>
        public void Show(EquipmentInstance equipment, Vector2 screenPos)
        {
            if (equipment?.Item == null) return;
            currentEquipment = equipment;
            currentStack = null;

            SetButton(equipButton, true);
            SetButton(register1Button, false);
            SetButton(register2Button, false);
            SetButton(useButton, false);
            SetButton(discardButton, true);

            gameObject.SetActive(true);
            Position(screenPos);
        }

        /// <summary>메뉴를 닫는다.</summary>
        public void Hide()
        {
            currentStack = null;
            currentEquipment = null;
            if (this != null) gameObject.SetActive(false);
        }

        // 버튼 GameObject를 켜고 끈다. 레이아웃 그룹이 꺼진 버튼을 건너뛰어 박스 높이가 자동으로 맞는다.
        private static void SetButton(Button button, bool on)
        {
            if (button != null) button.gameObject.SetActive(on);
        }

        private void Equip()
        {
            if (currentEquipment != null) InventoryManager.Instance?.Equip(currentEquipment);
            Hide();
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

        // 파괴는 비가역이라 ConfirmDialog로 한 번 더 확인한다. 확인창이 비동기라, Hide가 필드를 지우기 전에
        // 대상을 지역 변수로 캡처해 콜백이 그 값을 쓰게 한다(콜백 시점엔 currentEquipment/currentStack이 이미 null).
        private void Discard()
        {
            EquipmentInstance eq = currentEquipment;
            ItemStack st = currentStack;
            ItemData item = eq?.Item ?? st?.Item;

            Hide();
            if (item == null) return;

            // "확인 팝업 필수" 기획 결정 — 확인창이 씬에 없으면 실수 파괴를 막기 위해 파괴하지 않고 경고만 남긴다.
            if (ConfirmDialog.Instance == null)
            {
                Debug.LogWarning("[ItemContextMenu] ConfirmDialog가 씬에 없어 파괴를 취소했습니다. " +
                    "Tools ▸ ProjectS ▸ Create Item Context Menu로 생성하세요.");
                return;
            }

            string message = $"'{item.Name}'을(를) 파괴하시겠습니까?\n파괴한 아이템은 되돌릴 수 없습니다.";
            ConfirmDialog.Instance.Show(message, () =>
            {
                if (eq != null) InventoryManager.Instance?.DiscardEquipment(eq);
                else if (st != null) InventoryManager.Instance?.DiscardStack(st);
            });
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
