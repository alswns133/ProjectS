using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 아이템 슬롯의 등급 표현. 슬롯에 담긴 아이템 등급에 따라 외곽 라인(<c>Frame</c>)과 내부 글로우(<c>Light</c>) 색을 바꾸고,
    /// 레어 이상 장비(무기·방어구)면 테두리 이펙트(<c>Special</c>)를 켜 등급별 색·머티리얼을 입힌다.
    /// 색·머티리얼 값은 슬롯마다 갖지 않고 공유 에셋(<see cref="ItemGradeStyle"/>)에서 읽는다.
    ///
    /// 아이템을 받는 길은 두 가지다.
    /// <list type="bullet">
    /// <item><see cref="InventoryItemSlot"/>과 같은 오브젝트에 붙이면 자동으로 따라간다. 슬롯은 "내용이 바뀌었다"는
    /// 이벤트를 내지 않으므로 슬롯 코드를 건드리지 않고 매 프레임 표시 중인 아이템 참조만 비교해 바뀐 순간에만
    /// 반영한다(참조 비교 1회라 슬롯 수십 개 규모에선 비용이 무시할 만하다).</item>
    /// <item>그 외 슬롯(보상 칸 등)은 채우는 쪽이 <see cref="SetItem"/>을 직접 부른다.</item>
    /// </list>
    /// </summary>
    public class ItemSlotGradeView : MonoBehaviour
    {
        [Tooltip("등급별 색·이펙트 설정 에셋. 모든 슬롯 프리팹이 같은 에셋을 가리키게 한다.")]
        [SerializeField] private ItemGradeStyle style;

        [Tooltip("슬롯 외곽 라인. 등급 색을 입히고, 빈칸이면 기본 슬롯 색으로 둔다.")]
        [FormerlySerializedAs("gradeColor")]
        [SerializeField] private Image frame;

        [Tooltip("슬롯 내부 글로우. 등급 색을 입히고, 빈칸이면 오브젝트를 끈다(없으면 비워 둔다).")]
        [SerializeField] private Image innerLight;

        [Tooltip("레어 이상에서만 켜지는 테두리 이펙트 이미지(없으면 비워 둔다).")]
        [SerializeField] private Image special;

        private InventoryItemSlot slot;
        private ItemData shownItem;
        private bool hasShown;   // 첫 프레임엔 빈칸(null)이어도 한 번은 반영해야 해서 참조 비교와 따로 둔다

        private void Awake()
        {
            slot = GetComponent<InventoryItemSlot>();

            if (style == null)
                Debug.LogWarning($"{name}: ItemGradeStyle 에셋이 연결되지 않아 등급 표시를 하지 않는다.", this);
        }

        // 창이 다시 열릴 때 꺼져 있던 동안의 변경을 놓치지 않게 다음 프레임에 무조건 다시 반영한다.
        private void OnEnable()
        {
            hasShown = false;
            if (style != null) style.OnChanged += Invalidate;
        }

        private void OnDisable()
        {
            if (style != null) style.OnChanged -= Invalidate;
        }

        private void LateUpdate()
        {
            // 인벤 슬롯이 없으면(보상 칸 등) SetItem으로 받은 아이템을 그대로 유지한다.
            ItemData item = slot != null ? slot.Equipment?.Item ?? slot.Stack?.Item : shownItem;
            if (hasShown && ReferenceEquals(item, shownItem)) return;

            SetItem(item);
        }

        /// <summary>
        /// 표시할 아이템을 직접 지정한다. 인벤 슬롯이 아닌 칸(보상 칸 등)을 채우는 쪽이 호출한다.
        /// 인벤 슬롯에 붙어 있을 때는 다음 프레임에 슬롯 내용으로 다시 덮이므로 부를 필요가 없다.
        /// </summary>
        /// <param name="item">표시할 아이템. 아이템이 아닌 칸(골드·경험치 등)이나 빈칸이면 null.</param>
        public void SetItem(ItemData item)
        {
            shownItem = item;
            hasShown = true;
            Apply(item);
        }

        // 설정 에셋 값이 바뀌었을 때(에디터에서 조정) 다음 프레임에 다시 그리게 한다.
        private void Invalidate()
        {
            hasShown = false;
        }

        // 등급 색은 외곽 라인(frame)과 내부 글로우(innerLight) 두 곳에 입힌다(색은 설정 에셋에서 각각 따로 정한다).
        // 빈칸이면 라인은 기본 슬롯 색으로 되돌리고 글로우·이펙트는 끈다. 레어 이상 장비만 이펙트를 켠다.
        // 설정 에셋이 없으면 칠할 색을 알 수 없으므로 라인은 프리팹 색 그대로 두고 글로우는 끈다.
        private void Apply(ItemData item)
        {
            bool hasGrade = item != null && style != null;

            if (frame != null && style != null)
                frame.color = hasGrade ? style.FrameColor(item.Grade) : style.DefaultColor;

            if (innerLight != null)
            {
                innerLight.gameObject.SetActive(hasGrade);
                if (hasGrade) innerLight.color = style.LightColor(item.Grade);
            }

            if (special == null) return;

            ItemGradeStyle.SpecialStyle specialStyle = default;
            // 이펙트는 장비(무기·방어구)에만 띄운다. 소비품·재료는 레어 이상이어도 배경색만 바뀐다.
            bool isEquipment = hasGrade && (item.Category == ItemCategory.Weapon || item.Category == ItemCategory.Armor);
            bool isSpecial = isEquipment && style.TryGetSpecial(item.Grade, out specialStyle);
            special.gameObject.SetActive(isSpecial);
            if (!isSpecial) return;

            special.color = specialStyle.color;
            if (specialStyle.material != null) special.material = specialStyle.material;
        }
    }
}
