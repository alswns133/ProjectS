using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.Items;

namespace ProjectS.UI
{
    /// <summary>
    /// 몬스터 도감 그리드의 카드 한 장(MonsterCard 프리팹 루트에 붙인다). 도감 번호·이름(·초상)을 표시하고,
    /// 클릭하면 호스트(<see cref="MonsterIndexPopup"/>)에 자기 행을 넘겨 상세 화면으로 넘어가게 한다.
    /// 화면 전환은 호스트가 맡고, 카드는 표시와 클릭 전달만 한다(ShopItemCard와 같은 결).
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class MonsterIndexCard : MonoBehaviour
    {
        [Tooltip("도감 번호(MonsterCard/Num). 3자리로 채워 표시한다(예: 012).")]
        [SerializeField] private TMP_Text numberText;
        [Tooltip("몬스터 이름(MonsterCard/Text).")]
        [SerializeField] private TMP_Text nameText;

        [Tooltip("초상 이미지(선택). 넣으면 IllustAddress의 스프라이트를 로드해 표시한다.")]
        [SerializeField] private Image portrait;

        private Button button;
        private Action<MonsterIndexTable> onClick;

        /// <summary>이 카드가 표시 중인 도감 행. 풀 재사용 중 늦게 온 초상을 버리는 기준이기도 하다.</summary>
        public MonsterIndexTable Row { get; private set; }

        // 리스너는 여기서 한 번만 건다. 카드는 풀에서 재사용되며 Bind가 여러 번 불리므로,
        // Bind에서 걸면 클릭 한 번에 상세 열기가 여러 번 호출된다.
        private void Awake()
        {
            button = GetComponent<Button>();
            button.onClick.AddListener(() => onClick?.Invoke(Row));
        }

        /// <summary>카드에 도감 행을 채우고 클릭 콜백을 건다(호스트가 카드 재사용마다 호출).</summary>
        /// <param name="row">표시할 도감 행</param>
        /// <param name="displayName">표시 이름(행의 Name이 비었을 때 스탯 테이블 이름으로 폴백한 결과)</param>
        /// <param name="clickHandler">카드 클릭 시 호출할 콜백</param>
        public void Bind(MonsterIndexTable row, string displayName, Action<MonsterIndexTable> clickHandler)
        {
            Row = row;
            onClick = clickHandler;

            if (numberText != null) numberText.text = row != null ? row.No.ToString("000") : string.Empty;
            if (nameText != null) nameText.text = displayName ?? string.Empty;

            LoadPortrait(row);
        }

        // 초상을 비동기 로드한다. 대기 중 카드가 다른 행으로 재바인딩되면 늦게 온 스프라이트는 버린다.
        private async void LoadPortrait(MonsterIndexTable row)
        {
            if (portrait == null) return;

            // 로드 전엔 비운다 — 스프라이트 없는 Image의 흰 사각형 팝인을 막는다(완료 시 다시 켠다).
            portrait.sprite = null;
            portrait.enabled = false;
            if (row == null || string.IsNullOrEmpty(row.IllustAddress)) return;

            Sprite sprite = await ItemIconLoader.LoadAsync(row.IllustAddress);

            if (this == null || portrait == null) return;
            if (!ReferenceEquals(Row, row)) return;   // 카드가 다른 몬스터로 교체됨

            portrait.sprite = sprite;
            portrait.enabled = sprite != null;
        }
    }
}
