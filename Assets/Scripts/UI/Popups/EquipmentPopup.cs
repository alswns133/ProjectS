using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.Events;
using ProjectS.Items;
using ProjectS.Managers;
using ProjectS.Players;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 장비창(순수 View). 5부위 착용 슬롯 + 캐릭터 스탯 12개 + 레벨/닉네임 + 직업 심볼 + 캐릭터 일러스트를 표시한다.
    /// 착용/해제는 슬롯(<see cref="EquipSlotView"/>)이 <see cref="InventoryManager"/>로 처리하고, 이 창은
    /// <see cref="PlayerStats"/> 최종 getter를 읽어 스탯을 그린다. 인벤과 공존하는 이동식 팝업이다.
    /// </summary>
    public class EquipmentPopup : BasePopup
    {
        [Header("착용 슬롯 (무기·헬멧·상의·하의·신발)")]
        [SerializeField] private EquipSlotView[] slots;

        [Header("헤더")]
        [SerializeField] private TMP_Text levelNameText;
        [SerializeField] private Image classSymbol;
        [SerializeField] private Image characterIllust;
        [SerializeField] private Button closeButton;

        [Header("스탯 - 왼쪽")]
        [SerializeField] private TMP_Text attackText;
        [SerializeField] private TMP_Text critRateText;
        [SerializeField] private TMP_Text critDamageText;
        [SerializeField] private TMP_Text bossDamageText;
        [SerializeField] private TMP_Text defensePenText;
        [SerializeField] private TMP_Text damageIncreaseText;

        [Header("스탯 - 오른쪽")]
        [SerializeField] private TMP_Text healthText;
        [SerializeField] private TMP_Text defenseText;
        [SerializeField] private TMP_Text sgText;
        [SerializeField] private TMP_Text sgRegenText;
        [SerializeField] private TMP_Text staminaText;
        [SerializeField] private TMP_Text staminaRegenText;

        private int loadedCharacterId = -1;   // 심볼/일러스트 중복 로드 방지

        protected override void OnInit()
        {
            if (closeButton != null) closeButton.onClick.AddListener(() => RequestClose());

            // 이동식 창 위치 저장 키를 코드로 주입(장비창은 유일).
            if (TryGetComponent(out DraggableWindow window))
                window.SetWindowId(WindowIds.Equipment);
        }

        protected override void OnShow()
        {
            InventoryEvents.OnItemEquipped += HandleEquipChanged;
            InventoryEvents.OnItemUnequipped += HandleEquipChanged;
            PlayerEvents.OnCombatStatsChanged += RefreshStats;

            RefreshAll();
        }

        protected override void OnHide()
        {
            InventoryEvents.OnItemEquipped -= HandleEquipChanged;
            InventoryEvents.OnItemUnequipped -= HandleEquipChanged;
            PlayerEvents.OnCombatStatsChanged -= RefreshStats;
        }

        private void HandleEquipChanged(ItemData _) => RefreshAll();

        private void RefreshAll()
        {
            RefreshHeader();
            RefreshSlots();
            RefreshStats();
        }

        private void RefreshSlots()
        {
            if (slots == null) return;
            foreach (EquipSlotView slot in slots)
                if (slot != null) slot.Refresh();
        }

        private void RefreshHeader()
        {
            PlayerStats stats = Stats();
            CharacterSaveData save = GameSession.SelectedCharacter;

            int level = stats != null ? stats.Level : (save?.level ?? 1);
            string name = save != null ? save.name : string.Empty;
            if (levelNameText != null) levelNameText.text = $"Lv. {level:00}  {name}";

            // 직업 심볼·일러스트는 캐릭터 타입으로 어드레서블 로드(캐릭터별 이미지 필요).
            int charId = stats != null ? stats.CharacterId : (save?.characterType ?? 0);
            if (charId != loadedCharacterId)
            {
                loadedCharacterId = charId;
                LoadCharacterArt(classSymbol, $"Char_Symbol_{charId}");
                LoadCharacterArt(characterIllust, $"Char_Illust_{charId}");
            }
        }

        private void RefreshStats()
        {
            PlayerStats s = Stats();
            if (s == null) return;

            // 왼쪽
            Set(attackText, Mathf.RoundToInt(s.AttackPower).ToString());
            Set(critRateText, Percent(s.CritChance));
            Set(critDamageText, Percent(s.CritDamage));
            Set(bossDamageText, Percent(s.BossDamage));
            Set(defensePenText, Percent(s.DefensePenetration));
            Set(damageIncreaseText, Percent(s.DamageIncrease));

            // 오른쪽
            Set(healthText, s.MaxHp.ToString());
            Set(defenseText, Mathf.RoundToInt(s.Defense).ToString());
            Set(sgText, Mathf.RoundToInt(s.MaxSkillGauge).ToString());
            Set(sgRegenText, Fmt(s.SkillGaugeRegen));
            Set(staminaText, Mathf.RoundToInt(s.MaxStamina).ToString());
            Set(staminaRegenText, Fmt(s.StaminaRegen));
        }

        private async void LoadCharacterArt(Image target, string address)
        {
            if (target == null) return;

            Sprite sprite = await ItemIconLoader.LoadAsync(address);
            if (this == null || target == null) return;

            target.sprite = sprite;
            target.enabled = sprite != null;
        }

        private static PlayerStats Stats()
            => PlayerManager.Instance != null && PlayerManager.Instance.Player != null
                ? PlayerManager.Instance.Player.Stats
                : null;

        private static void Set(TMP_Text text, string value)
        {
            if (text != null) text.text = value;
        }

        // 비율(0.05)을 퍼센트 문자열("5%")로. 옵션 단위가 기획에서 확정되면 여기만 손보면 된다.
        private static string Percent(float ratio) => $"{ratio * 100f:0.#}%";

        private static string Fmt(float value) => value.ToString("0.#");
    }
}
