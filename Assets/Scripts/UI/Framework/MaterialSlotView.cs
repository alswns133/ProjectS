using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.Items;
using ProjectS.Managers;

namespace ProjectS.UI.Framework
{
    /// <summary>
    /// 재료 한 칸: 아이콘 + 이름 + 보유/필요 수량. 부족하면 수량 텍스트를 경고색으로 표시한다.
    /// 강화창 전용이 아니라 상점·인벤 상세창에서도 재활용할 수 있게 프레임워크 뷰로 둔다.
    /// 아이콘은 <see cref="ItemIconLoader"/>로 어드레서블에서 비동기 로드한다(인벤 슬롯과 같은 경로).
    /// 마우스를 올리면 인벤 슬롯과 같은 아이템 툴팁을 띄운다(<see cref="Set"/>로 받은 itemId를
    /// <see cref="JsonManager"/>로 조회 — 표시 DTO에 결합하지 않아 재사용성을 지킨다).
    /// 툴팁·hover가 뜨려면 이 오브젝트에 raycastTarget인 Graphic(배경/아이콘)이 있어야 한다.
    /// (2026-07-23 TH / 2026-08-25 hover 툴팁)
    /// </summary>
    public class MaterialSlotView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text countText;
        [SerializeField] private Color enoughColor = Color.white;
        [SerializeField] private Color lackColor = new Color(1f, 0.24f, 0.36f); // #FF3D5C

        // 비동기 아이콘 로드 중 이 슬롯이 다른 재료로 갱신되면 늦게 온 스프라이트를 버리는 기준.
        private string currentAddress;

        // hover 툴팁용. 재료 아이템 ID와 보유량을 들고 있다가 ShowStack에 넘긴다(0이면 빈 슬롯 → 툴팁 없음).
        private int itemId;
        private int owned;

        /// <summary>
        /// 재료 한 칸을 표시한다. 아이콘은 주소로 비동기 로드한다.
        /// </summary>
        /// <param name="iconAddress">재료 아이콘 어드레서블 주소(없으면 null)</param>
        /// <param name="materialName">재료 이름</param>
        /// <param name="owned">보유량</param>
        /// <param name="required">필요량</param>
        /// <param name="itemId">재료 아이템 ID(hover 툴팁 조회용, 0이면 툴팁 없음)</param>
        public void Set(string iconAddress, string materialName, int owned, int required, int itemId = 0)
        {
            gameObject.SetActive(true);
            this.itemId = itemId;
            this.owned = owned;
            if (nameText != null)
            {
                nameText.gameObject.SetActive(true);
                nameText.text = materialName;
            }

            if (countText != null)
            {
                countText.gameObject.SetActive(true);
                countText.text = $"{owned}/{required}";
                countText.color = owned >= required ? enoughColor : lackColor;
            }

            currentAddress = iconAddress;
            LoadIcon(iconAddress);
        }

        /// <summary>
        /// 고정 재료 칸을 비운 상태로 되돌린다. 배경 프레임은 유지하고 아이콘·이름·수량만 감춰
        /// 강화 대상을 선택하기 전의 3×2 빈 슬롯 디자인을 만든다.
        /// </summary>
        public void SetEmpty()
        {
            gameObject.SetActive(true);
            currentAddress = null;
            itemId = 0;   // 빈 슬롯은 hover 툴팁을 띄우지 않는다.
            owned = 0;

            // 이 슬롯이 띄운 툴팁이 아직 떠 있으면 닫는다(빈칸 위에 옛 정보가 남지 않게).
            ItemTooltip.Instance?.Hide(this);

            if (icon != null)
            {
                icon.sprite = null;
                icon.enabled = false;
            }

            if (nameText != null)
            {
                nameText.text = string.Empty;
                nameText.gameObject.SetActive(false);
            }

            if (countText != null)
            {
                countText.text = string.Empty;
                countText.gameObject.SetActive(false);
            }
        }

        // 아이콘을 어드레서블에서 비동기 로드한다. 로드 전엔 비워, 스프라이트 없는 Image가 흰 사각형으로
        // 보이는 팝인을 막는다. 대기 중 슬롯이 다른 재료로 바뀌면 늦게 온 스프라이트는 버린다.
        private async void LoadIcon(string address)
        {
            if (icon != null)
            {
                icon.sprite = null;
                icon.enabled = false;
            }

            Sprite sprite = await ItemIconLoader.LoadAsync(address);

            if (this == null || icon == null) return;
            if (!string.Equals(currentAddress, address)) return;   // 슬롯이 다른 재료로 교체됨

            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }

        /// <summary>마우스를 올리면 이 재료의 아이템 툴팁을 띄운다(빈 슬롯이면 무시). 인벤 슬롯과 같은 ShowStack 경로.</summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (itemId <= 0 || ItemTooltip.Instance == null || JsonManager.Instance == null) return;

            ItemData item = JsonManager.Instance.Get<ItemData>(itemId);
            if (item == null) return;

            // 소비품이면 소비품 행도 함께 넘겨 회복/쿨다운까지 뜨게 하고, 순수 재료면 null(재료 섹션 없음).
            ConsumableData consumable = JsonManager.Instance.Get<ConsumableData>(itemId);
            var stack = new ItemStack(item, consumable, Mathf.Max(owned, 1));

            // owner로 this를 넘겨, 강화창이 닫힐 때(OnDisable)만 이 툴팁이 닫히게 한다.
            ItemTooltip.Instance.ShowStack(stack, eventData.position, this);
        }

        /// <summary>마우스가 벗어나면 툴팁을 숨긴다.</summary>
        public void OnPointerExit(PointerEventData eventData) => ItemTooltip.Instance?.Hide();

        /// <summary>창이 닫혀 슬롯이 비활성되면 이 슬롯이 띄운 툴팁을 닫는다(마우스가 안 움직여 Exit가 안 와도).</summary>
        private void OnDisable() => ItemTooltip.Instance?.Hide(this);
    }
}
