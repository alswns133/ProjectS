using System;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Data;

namespace ProjectS.UI
{
    /// <summary>구매 상점 상단 카테고리(아이콘 순서와 같다).</summary>
    public enum ShopCategory
    {
        Sword = 0,
        Gun,
        Helmet,
        Top,
        Bottom,
        Shoes,
        /// <summary>잡화(포션 아이콘) — 소비품 + 재료(강화석 등).</summary>
        Supplies,
        All,    // 장비 상점 전체 목록, 잡화 상점 전체 목록 - 전체 목록 분류는 코드로 함.
    }

    /// <summary>
    /// 구매 상점 상단의 카테고리 아이콘 바. 상점 종류(<see cref="ShopType"/>)에 따라
    /// 장비상점은 잡화 탭을, 잡화상점은 장비 탭을 <b>오브젝트째 끈다</b>(전체 탭은 항상 보임).
    /// 어떤 아이템이 어느 카테고리인지(<see cref="Matches"/>)도 여기서 판정해, 필터 규칙이 한 곳에만 있게 한다.
    /// </summary>
    public class ShopCategoryBar : MonoBehaviour
    {
        [Serializable]
        private class Entry
        {
            public ShopCategory category;
            public Button button;
            [Tooltip("선택 표시용 이미지(없으면 버튼 이미지).")]
            public Image image;
        }

        [Tooltip("카테고리별 버튼. 아이콘 순서대로 등록.")]
        [SerializeField] private Entry[] entries;

        [SerializeField] private Color normalColor = new Color(0.4f, 0.42f, 0.5f, 1f);
        [SerializeField] private Color selectedColor = Color.white;

        /// <summary>카테고리를 고르면 발행. ShopPopup이 받아 카드 목록을 다시 그린다.</summary>
        public event Action<ShopCategory> OnCategorySelected;

        /// <summary>지금 선택된 카테고리.</summary>
        public ShopCategory Current { get; private set; }

        /// <summary>지금 열린 상점 종류(<see cref="Setup"/>에서 받음). All 카테고리의 분류 기준이 된다.</summary>
        public ShopType ShopType { get; private set; }

        private void Awake()
        {
            // 리스너는 한 번만 건다(Setup은 상점 열 때마다 불린다).
            foreach (Entry entry in entries)
            {
                if (entry.button == null) continue;
                ShopCategory category = entry.category;   // 클로저 캡처용 지역 복사
                entry.button.onClick.AddListener(() => Select(category));
            }
        }

        /// <summary>
        /// 상점을 열 때 호출. 상점 종류에 맞지 않는 카테고리 버튼은 오브젝트째 끄고 기본 카테고리를 고른다.
        /// </summary>
        /// <param name="shopType">열린 상점의 종류.</param>
        public void Setup(ShopType shopType)
        {
            ShopType = shopType;

            foreach (Entry entry in entries)
            {
                if (entry.button == null) continue;

                // 버튼이 탭 오브젝트의 루트라, 버튼 오브젝트를 끄면 아이콘·배경까지 통째로 사라진다.
                // 장비상점: 잡화 탭 끔 / 잡화상점: 장비 탭 전부 끔 / All: 두 상점 모두 켬(분류는 Matches가 상점 종류로).
                // 회색(interactable=false) 대신 끄는 이유: 못 누르는 탭이 보이면 "왜 안 눌리지?"를 유저가 따로 알아내야 한다.
                // 상점을 오갈 때 이전 상점에서 끈 탭이 남지 않도록, 매번 켜고 끄기를 둘 다 명시적으로 정한다.
                entry.button.gameObject.SetActive(IsShownIn(entry.category, shopType));
            }

            // 상점을 열면 항상 전체 목록부터 보여준다(어떤 상점이든 처음엔 뭘 파는지 다 보이게).
            Select(ShopCategory.All);
        }

        /// <summary>카테고리를 선택하고 표시를 갱신한 뒤 이벤트를 발행한다.</summary>
        public void Select(ShopCategory category)
        {
            Current = category;
            UpdateVisual();
            OnCategorySelected?.Invoke(category);
        }

        /// <summary>
        /// 아이템이 해당 카테고리에 속하는지 판정한다(구매 목록 필터).
        /// </summary>
        /// <param name="category">검사할 카테고리.</param>
        /// <param name="item">아이템 정의.</param>
        /// <param name="equipment">장비 부가 정의(장비가 아니면 null).</param>
        /// <param name="shopType">열린 상점 종류. All이 "이 상점 종류의 전체"를 고르는 데 쓴다.</param>
        public static bool Matches(ShopCategory category, ItemData item, EquipmentData equipment, ShopType shopType)
        {
            if (item == null) return false;

            bool isWeapon = item.Category == ItemCategory.Weapon;
            bool isArmor = item.Category == ItemCategory.Armor;
            bool isSupplies = item.Category == ItemCategory.Consumable || item.Category == ItemCategory.Material;

            switch (category)
            {
                // 전체: 장비상점이면 장비 전부, 잡화상점이면 잡화 전부.
                // 판매목록(JSON)을 그대로 다 보여주지 않고 상점 종류로 거르는 이유: 데이터 실수로 장비상점에
                // 포션이 섞여도, 숨긴 잡화 탭 대신 전체 탭으로 새어 나오지 않게 하기 위함.
                case ShopCategory.All:
                    return shopType == ShopType.Equipment ? isWeapon || isArmor : isSupplies;

                case ShopCategory.Supplies:
                    return isSupplies;

                // 장비 부가 정의(EquipmentData)가 없으면 부위를 알 수 없으므로 어느 장비 탭에도 넣지 않는다.
                case ShopCategory.Sword:
                    return isWeapon && equipment != null && equipment.WeaponType == WeaponType.Sword;
                case ShopCategory.Gun:
                    return isWeapon && equipment != null && equipment.WeaponType == WeaponType.Gun;
                case ShopCategory.Helmet:
                    return isArmor && equipment != null && equipment.EquipSlot == EquipSlot.Helmet;
                case ShopCategory.Top:
                    return isArmor && equipment != null && equipment.EquipSlot == EquipSlot.Top;
                case ShopCategory.Bottom:
                    return isArmor && equipment != null && equipment.EquipSlot == EquipSlot.Bottom;
                case ShopCategory.Shoes:
                    return isArmor && equipment != null && equipment.EquipSlot == EquipSlot.Shoes;

                default:
                    return false;
            }
        }

        // 이 상점 종류에서 보여 줄 탭인지. All은 항상, 잡화 탭은 잡화상점에만, 장비 탭은 장비상점에만.
        private static bool IsShownIn(ShopCategory category, ShopType shopType)
        {
            if (category == ShopCategory.All) return true;
            if (category == ShopCategory.Supplies) return shopType == ShopType.General;
            return shopType == ShopType.Equipment;   // 나머지는 전부 장비 탭
        }

        private void UpdateVisual()
        {
            foreach (Entry entry in entries)
            {
                Image target = entry.image != null ? entry.image : entry.button != null ? entry.button.image : null;
                if (target != null) target.color = entry.category == Current ? selectedColor : normalColor;
            }
        }
    }
}
