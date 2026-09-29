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
        [Tooltip("일러스트를 띄울 Image(Monsters/Illust/Empty). Illust 자체는 액자(배경)라 그 안쪽 전용 Image에 그린다. " +
                 "여기 넣어 둔 스프라이트는 '일러스트 없음' 기본 이미지로 쓰이므로, 미리보기용 몬스터 그림을 남겨 두지 않는다.")]
        [SerializeField] private Image illustImage;
        [Tooltip("일러스트가 없을 때 대신 켜 둘 자리표시(선택, 예: Illust/Name '몬스터 일러스트' 문구). 로드되면 끈다.")]
        [SerializeField] private GameObject illustPlaceholder;

        [Header("페이즈 전환 (2페이즈 일러스트가 있는 몬스터만 표시)")]
        [Tooltip("페이즈 버튼 묶음. Phase2IllustAddress가 빈 몬스터에선 통째로 숨긴다.")]
        [SerializeField] private GameObject phaseToggleRoot;
        [SerializeField] private Button phase1Button;
        [SerializeField] private Button phase2Button;
        [Tooltip("1페이즈 선택 표시(선택). 선택된 페이즈의 것만 켠다.")]
        [SerializeField] private GameObject phase1Selected;
        [Tooltip("2페이즈 선택 표시(선택). 선택된 페이즈의 것만 켠다.")]
        [SerializeField] private GameObject phase2Selected;

        // 일러스트가 없을 때 되돌릴 원래 스프라이트(씬에 배치된 기본 이미지).
        private Sprite defaultIllust;
        private bool hasCachedDefault;
        private MonsterIndexTable currentRow;
        private int currentPhase = 1;

        // 일러스트 요청 번호. 같은 몬스터 안에서도 페이즈를 빠르게 오가면 늦게 온 이전 페이즈 그림이
        // 새 그림을 덮을 수 있어, 행 비교 대신 "마지막 요청인가"로 늦은 결과를 버린다.
        private int illustRequestId;

        // 자리표시가 일러스트 Image 자신이나 그 조상이면, 로드 성공 시 끄는 순간 일러스트(와 그 아래 텍스트·뒤로가기)까지
        // 통째로 사라진다. 실제로 Illust 오브젝트를 자리표시로 잘못 연결해 "그림 있는 몬스터만 상세가 빈 화면"이 된 적이 있어,
        // 이런 연결은 경고 후 무시한다.
        private void Awake()
        {
            if (illustPlaceholder != null && illustImage != null
                && illustImage.transform.IsChildOf(illustPlaceholder.transform))
            {
                Debug.LogWarning($"[MonsterIndexDetailView] illustPlaceholder({illustPlaceholder.name})가 일러스트 Image 자신이거나 그 부모입니다. " +
                                 "로드 성공 시 일러스트까지 꺼지므로 무시합니다 — 별도 자식 오브젝트를 연결하세요.", this);
                illustPlaceholder = null;
            }

            // 인스펙터에선 Button 참조만 연결하면 되고 onClick은 코드가 건다(MonsterIndexPopup의 탭 버튼과 같은 결).
            if (phase1Button != null) phase1Button.onClick.AddListener(() => SetPhase(1));
            if (phase2Button != null) phase2Button.onClick.AddListener(() => SetPhase(2));
        }

        /// <summary>도감 행 하나로 상세 화면을 채운다(호스트가 카드 클릭 시 호출). 항상 1페이즈부터 보여 준다.</summary>
        /// <param name="row">표시할 도감 행</param>
        /// <param name="displayName">표시 이름(행의 Name이 비었을 때 스탯 테이블 이름으로 폴백한 결과)</param>
        public void Bind(MonsterIndexTable row, string displayName)
        {
            currentRow = row;

            if (nameText != null) nameText.text = displayName ?? string.Empty;
            if (categoryText != null) categoryText.text = Format(categoryFormat, row?.Category);
            if (areaText != null) areaText.text = Format(areaFormat, row?.Area);
            if (descriptionText != null) descriptionText.text = row?.Description ?? string.Empty;

            if (phaseToggleRoot != null) phaseToggleRoot.SetActive(row != null && row.HasPhase2);
            currentPhase = 0;   // SetPhase의 같은 페이즈 무시에 걸리지 않게 초기화
            SetPhase(1);
        }

        // 페이즈를 바꾸고 그 페이즈의 일러스트를 로드한다. 2페이즈가 없는 몬스터에선 1페이즈로 고정한다.
        private void SetPhase(int phase)
        {
            if (currentRow == null || !currentRow.HasPhase2) phase = 1;
            if (currentPhase == phase) return;

            currentPhase = phase;
            if (phase1Selected != null) phase1Selected.SetActive(phase == 1);
            if (phase2Selected != null) phase2Selected.SetActive(phase == 2);

            LoadIllust(phase == 2 ? currentRow.Phase2IllustAddress : currentRow?.IllustAddress);
        }

        // 형식 문자열이 비었거나 {0}이 없어도 값이 사라지지 않게 방어한다(인스펙터 오타 대비).
        private static string Format(string format, string value)
        {
            value ??= string.Empty;
            if (string.IsNullOrEmpty(format) || !format.Contains("{0}")) return value;
            return string.Format(format, value);
        }

        // 일러스트를 비동기 로드한다. 로드 중 다른 몬스터·페이즈로 넘어가면 늦게 온 스프라이트는 버린다.
        // 주소가 없거나 로드에 실패하면 씬에 깔려 있던 기본 이미지 + 자리표시로 되돌린다.
        private async void LoadIllust(string address)
        {
            int requestId = ++illustRequestId;

            if (illustImage != null && !hasCachedDefault)
            {
                defaultIllust = illustImage.sprite;
                hasCachedDefault = true;
            }

            ShowIllust(null);
            if (string.IsNullOrEmpty(address)) return;

            Sprite sprite = await ItemIconLoader.LoadAsync(address);

            if (this == null) return;
            if (requestId != illustRequestId) return;   // 그사이 다른 몬스터·페이즈를 요청함

            ShowIllust(sprite);
        }

        private void ShowIllust(Sprite sprite)
        {
            if (illustImage != null)
            {
                Sprite shown = sprite != null ? sprite : defaultIllust;
                illustImage.sprite = shown;
                // 기본 이미지도 없으면 Image를 끈다 — 스프라이트 없는 Image는 흰 사각형으로 그려져 로드 중에 번쩍인다.
                illustImage.enabled = shown != null;
            }
            if (illustPlaceholder != null) illustPlaceholder.SetActive(sprite == null);
        }
    }
}
