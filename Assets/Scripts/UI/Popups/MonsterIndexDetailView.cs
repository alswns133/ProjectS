using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.Items;

namespace ProjectS.UI
{
    /// <summary>
    /// 몬스터 도감의 상세 화면(MonsterDetail 오브젝트에 붙인다). 도감 행 하나를 받아 이름·분류·출몰 지역·설명·
    /// 일러스트를 채운다. 목록↔상세 전환과 뒤로가기는 호스트(<see cref="MonsterIndexPopup"/>)가 맡고,
    /// 이 View는 표시만 한다.
    /// </summary>
    public class MonsterIndexDetailView : MonoBehaviour
    {
        [Header("텍스트 (Monsters/Illust/Text 하위)")]
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text categoryText;
        [SerializeField] private TMP_Text areaText;
        [SerializeField] private TMP_Text descriptionText;

        [Header("표시 형식 ({0} = 테이블 값)")]
        [Tooltip("분류 칸 형식. 예: \"분류 : {0}\"")]
        [SerializeField] private string categoryFormat = "{0}";
        [Tooltip("출몰 지역 칸 형식. 씬의 자리표시 문구 \"출몰 지역 : \"에 맞춘 기본값.")]
        [SerializeField] private string areaFormat = "출몰 지역 : {0}";

        [Header("일러스트")]
        [Tooltip("일러스트를 띄울 Image(Monsters/Illust).")]
        [SerializeField] private Image illustImage;
        [Tooltip("일러스트가 없을 때 대신 켜 둘 자리표시(선택, 예: Illust/Name '몬스터 일러스트' 문구). 로드되면 끈다.")]
        [SerializeField] private GameObject illustPlaceholder;

        // 일러스트가 없을 때 되돌릴 원래 스프라이트(씬에 배치된 기본 이미지).
        private Sprite defaultIllust;
        private bool hasCachedDefault;
        private MonsterIndexTable currentRow;

        /// <summary>도감 행 하나로 상세 화면을 채운다(호스트가 카드 클릭 시 호출).</summary>
        /// <param name="row">표시할 도감 행</param>
        /// <param name="displayName">표시 이름(행의 Name이 비었을 때 스탯 테이블 이름으로 폴백한 결과)</param>
        public void Bind(MonsterIndexTable row, string displayName)
        {
            currentRow = row;

            if (nameText != null) nameText.text = displayName ?? string.Empty;
            if (categoryText != null) categoryText.text = Format(categoryFormat, row?.Category);
            if (areaText != null) areaText.text = Format(areaFormat, row?.Area);
            if (descriptionText != null) descriptionText.text = row?.Description ?? string.Empty;

            LoadIllust(row);
        }

        // 형식 문자열이 비었거나 {0}이 없어도 값이 사라지지 않게 방어한다(인스펙터 오타 대비).
        private static string Format(string format, string value)
        {
            value ??= string.Empty;
            if (string.IsNullOrEmpty(format) || !format.Contains("{0}")) return value;
            return string.Format(format, value);
        }

        // 일러스트를 비동기 로드한다. 로드 중 다른 몬스터로 넘어가면 늦게 온 스프라이트는 버린다.
        // 주소가 없거나 로드에 실패하면 씬에 깔려 있던 기본 이미지 + 자리표시로 되돌린다.
        private async void LoadIllust(MonsterIndexTable row)
        {
            if (illustImage != null && !hasCachedDefault)
            {
                defaultIllust = illustImage.sprite;
                hasCachedDefault = true;
            }

            ShowIllust(null);
            if (row == null || string.IsNullOrEmpty(row.IllustAddress)) return;

            Sprite sprite = await ItemIconLoader.LoadAsync(row.IllustAddress);

            if (this == null) return;
            if (!ReferenceEquals(currentRow, row)) return;   // 다른 몬스터로 교체됨

            ShowIllust(sprite);
        }

        private void ShowIllust(Sprite sprite)
        {
            if (illustImage != null) illustImage.sprite = sprite != null ? sprite : defaultIllust;
            if (illustPlaceholder != null) illustPlaceholder.SetActive(sprite == null);
        }
    }
}
