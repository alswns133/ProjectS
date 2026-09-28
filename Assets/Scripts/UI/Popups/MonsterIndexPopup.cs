using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.Managers;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 몬스터 도감 창(MonsterIndex 오브젝트에 붙인다). 몬스터/보스 탭으로 <see cref="MonsterIndexTable"/> 행을
    /// 카드 그리드로 나열하고, 카드를 누르면 <b>탭과 카드 목록을 숨기고 MonsterDetail을 켜서</b> 그 몬스터의
    /// 상세 정보를 보여 준다. 상세의 뒤로가기 버튼은 다시 목록으로 돌아간다(스크롤 위치 유지).
    ///
    /// 탭 분류(보스 여부)는 도감 테이블이 아니라 <see cref="MonsterStatTable.IsBoss"/>를 읽는다.
    /// 목록은 도감 번호(No) 순으로 정렬한다.
    /// </summary>
    /// <remarks>
    /// UIManager로 여는 팝업(BasePopup)이지만, 작업 씬(HUD(TH) 2)처럼 UIManager 없이 오브젝트를 켜 두기만 해도
    /// 동작하도록 배선은 Awake, 목록 갱신은 OnEnable에서 한다(BasePopup.Show도 SetActive(true)로 OnEnable을 거친다).
    /// </remarks>
    public class MonsterIndexPopup : BasePopup
    {
        private enum Tab { Monster, Boss }

        [Header("목록 화면 (카드 클릭 시 숨김)")]
        [Tooltip("탭 묶음(MonsterIndex/Tab).")]
        [SerializeField] private GameObject tabRoot;
        [Tooltip("카드 스크롤 뷰(MonsterIndex/CardScrollView).")]
        [SerializeField] private GameObject cardListRoot;
        [SerializeField] private ScrollRect scrollRect;

        [Header("카드 그리드")]
        [Tooltip("카드가 생성될 부모(CardScrollView/Viewport/Content). 이미 배치된 카드는 풀로 재사용한다.")]
        [SerializeField] private Transform cardRoot;
        [SerializeField] private MonsterIndexCard cardPrefab;

        [Header("탭")]
        [SerializeField] private Button monsterTabButton;
        [SerializeField] private Button bossTabButton;
        [Tooltip("몬스터 탭 선택 표시(선택). 선택된 탭의 것만 켠다.")]
        [SerializeField] private GameObject monsterTabSelected;
        [Tooltip("보스 탭 선택 표시(선택). 선택된 탭의 것만 켠다.")]
        [SerializeField] private GameObject bossTabSelected;

        [Header("상세 화면 (카드 클릭 시 표시)")]
        [Tooltip("상세 화면 루트(MonsterIndex/MonsterDetail). 평소엔 꺼 둔다.")]
        [SerializeField] private MonsterIndexDetailView detailView;
        [Tooltip("상세 → 목록 복귀(MonsterDetail/.../BackButton).")]
        [SerializeField] private Button backButton;

        [Header("기타")]
        [SerializeField] private Button closeButton;

        // 카드 풀. 필요한 만큼 만들어 재사용하고, 남는 카드는 숨긴다(탭 전환마다 파괴·재생성하지 않음).
        private readonly List<MonsterIndexCard> cards = new();
        private readonly List<MonsterIndexTable> buffer = new();
        private Tab currentTab = Tab.Monster;
        private bool isWaitingForData;

        private void Awake()
        {
            // 인스펙터에선 Button 참조만 연결하면 되고 onClick은 코드가 건다.
            if (monsterTabButton != null) monsterTabButton.onClick.AddListener(() => SetTab(Tab.Monster));
            if (bossTabButton != null) bossTabButton.onClick.AddListener(() => SetTab(Tab.Boss));
            if (backButton != null) backButton.onClick.AddListener(ShowList);
            if (closeButton != null) closeButton.onClick.AddListener(Close);

            // 씬에 미리 놓인 카드(레이아웃 확인용 배치본)도 풀에 넣어 재사용한다 — 안 그러면 데이터와 무관한
            // 자리표시 카드가 목록 맨 앞에 그대로 남는다.
            if (cardRoot != null)
                cards.AddRange(cardRoot.GetComponentsInChildren<MonsterIndexCard>(true));
        }

        // 열릴 때마다 몬스터 탭 + 목록 화면으로 초기화한다(지난번 상세 화면이 남아 있지 않게).
        private void OnEnable()
        {
            currentTab = Tab.Monster;
            ShowList();
            UpdateTabVisual();
            RebuildWhenReady();
        }

        // 닫기: UIManager로 열린 경우 UIManager에 닫기를 요청하고(활성 목록 정리),
        // UIManager 없이 켜 둔 작업 씬에선 직접 끈다.
        private void Close()
        {
            if (IsVisible) RequestClose();
            else gameObject.SetActive(false);
        }

        private void SetTab(Tab tab)
        {
            if (currentTab == tab) return;

            currentTab = tab;
            UpdateTabVisual();
            Rebuild();
            ResetScroll();
        }

        private void UpdateTabVisual()
        {
            if (monsterTabSelected != null) monsterTabSelected.SetActive(currentTab == Tab.Monster);
            if (bossTabSelected != null) bossTabSelected.SetActive(currentTab == Tab.Boss);
        }

        // 부팅 직후 바로 켜진 경우 JsonManager가 아직 로딩 중일 수 있으므로, 끝날 때까지 기다렸다가 그린다.
        private async void RebuildWhenReady()
        {
            JsonManager json = JsonManager.Instance;
            if (json == null)
            {
                Debug.LogWarning("[MonsterIndexPopup] JsonManager가 없어 도감을 채울 수 없습니다(Bootstrap을 거쳐 실행하세요).");
                return;
            }

            if (!json.IsReady)
            {
                if (isWaitingForData) return;   // 이미 기다리는 중(연속 OnEnable)

                isWaitingForData = true;
                await json.ReadyTask;
                isWaitingForData = false;

                if (this == null || !isActiveAndEnabled) return;   // 기다리는 동안 닫힘
            }

            Rebuild();
            ResetScroll();
        }

        // 현재 탭에 해당하는 도감 행을 번호순으로 카드에 채운다.
        private void Rebuild()
        {
            JsonManager json = JsonManager.Instance;
            if (json == null || !json.IsReady) return;
            if (cardRoot == null || cardPrefab == null) return;

            buffer.Clear();
            foreach (MonsterIndexTable row in json.MonsterIndexDict.Values)
            {
                if (IsBoss(json, row) == (currentTab == Tab.Boss))
                    buffer.Add(row);
            }
            buffer.Sort((a, b) => a.No.CompareTo(b.No));

            for (int i = 0; i < buffer.Count; i++)
                GetCard(i).Bind(buffer[i], ResolveName(json, buffer[i]), OnCardClicked);

            // 남는 카드는 숨긴다(풀 재사용).
            for (int i = buffer.Count; i < cards.Count; i++)
                cards[i].gameObject.SetActive(false);
        }

        // 카드 클릭 → 탭·카드 목록을 숨기고 상세 화면을 켠 뒤 그 몬스터로 채운다.
        // SetActive를 먼저 해야 상세 View의 비동기 일러스트 로드가 꺼진 오브젝트에서 돌지 않는다.
        private void OnCardClicked(MonsterIndexTable row)
        {
            if (row == null || detailView == null) return;

            SetListVisible(false);
            detailView.gameObject.SetActive(true);
            detailView.Bind(row, ResolveName(JsonManager.Instance, row));
        }

        // 상세 → 목록 복귀. 목록을 다시 만들지 않으므로 보던 스크롤 위치가 그대로 유지된다.
        private void ShowList()
        {
            if (detailView != null) detailView.gameObject.SetActive(false);
            SetListVisible(true);
        }

        private void SetListVisible(bool visible)
        {
            if (tabRoot != null) tabRoot.SetActive(visible);
            if (cardListRoot != null) cardListRoot.SetActive(visible);
        }

        // 인덱스 위치의 카드를 얻는다(모자라면 생성). 활성화해 돌려준다(풀 재사용).
        private MonsterIndexCard GetCard(int index)
        {
            while (cards.Count <= index)
                cards.Add(Instantiate(cardPrefab, cardRoot));

            cards[index].gameObject.SetActive(true);
            return cards[index];
        }

        // 보스 여부는 스탯 테이블이 원천이다. 스탯 행이 없으면(오타 등) 일반 몬스터 탭에 둔다.
        private static bool IsBoss(JsonManager json, MonsterIndexTable row)
        {
            MonsterStatTable stat = json != null ? json.Get<MonsterStatTable>(row.MonsterId) : null;
            if (stat == null)
                Debug.LogWarning($"[MonsterIndexPopup] 도감 {row.MonsterId}: MonsterStatTable에 같은 ID가 없습니다(몬스터 탭에 표시).");

            return stat != null && stat.IsBoss;
        }

        // 표시 이름: 도감 Name → 스탯 테이블 Name → NameKey → ID 순으로 폴백한다.
        private static string ResolveName(JsonManager json, MonsterIndexTable row)
        {
            if (!string.IsNullOrEmpty(row.Name)) return row.Name;

            MonsterStatTable stat = json != null ? json.Get<MonsterStatTable>(row.MonsterId) : null;
            if (stat != null && !string.IsNullOrEmpty(stat.Name)) return stat.Name;
            if (stat != null && !string.IsNullOrEmpty(stat.NameKey)) return stat.NameKey;
            return $"#{row.MonsterId}";
        }

        // 탭 전환·재오픈처럼 목록이 통째로 바뀔 때 스크롤을 맨 위로 되돌린다.
        // ForceUpdateCanvases로 레이아웃이 새 콘텐츠 높이를 먼저 반영하게 한 뒤 위치를 잡아야 정확하다.
        private void ResetScroll()
        {
            if (scrollRect == null) return;
            scrollRect.StopMovement();

            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 1f;   // 1=맨 위
        }
    }
}
