using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using TMPro;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 씬 전환 로딩 화면. 왼쪽 위에 목적지(이름·구역 코드·흐르는 0/1 데이터), 아래쪽에 화면을 가로지르는
    /// 일자형 진행 바와 상태 문구·퍼센트·패킷 수, 맨 아래에 팁을 둔다. 로그인 화면과 같은 언어(단일 파랑·반투명·육각 타일)로 이어지게 했다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>진행률은 일자형 바 하나로만 보인다.</b> 원형 게이지(PerformanceGauge)는 결과 화면·로그인에서 이미 쓰고 있어
    /// 로딩까지 재활용하면 같은 물건이 반복돼 일자형으로 정했다(<see cref="LoadingBarView"/>).
    /// 예전 슬라이더(<see cref="loadingSlider"/>)는 씬에 남아 있어도 되도록 선택 참조로 두었다.
    /// </para>
    /// <para>
    /// <b>상태 문구는 실제 로딩 단계가 아니라 진행률 구간에 따라 바뀌는 연출</b>이다. 로딩 루프(<c>GameSceneManager</c>)는
    /// 단계 이름을 모르고 진행률만 알기 때문이다.
    /// </para>
    /// <para>
    /// <b>목적지</b>는 <c>GameSceneManager</c>가 로드할 씬 이름(= 씬 클래스 이름)을 <see cref="SetDestination"/>으로 넘기면
    /// <see cref="destinations"/> 표(인스펙터)에서 <b>한글 명칭</b>을 찾아 띄운다. 씬 이름(영문 클래스명)은 화면에 내보내지 않는다 —
    /// 제목 텍스트는 한글 폰트 하나로 찍는데, 영문이 섞이면 그 폰트로 표기가 어긋나기 때문이다.
    /// 표에 없는 씬이면 기본 한글 표기(<see cref="defaultTitle"/>)를 띄우고 경고를 남긴다.
    /// 기획 텍스트라 행이 늘어나면 JSON 테이블로 옮길 대상이다(데이터 운영 방침).
    /// </para>
    /// </remarks>
    public class LoadingPanel : BasePanel
    {
        /// <summary>씬 이름 → 로딩 화면에 띄울 한글 명칭.</summary>
        [Serializable]
        public struct Destination
        {
            [Tooltip("씬 클래스 이름(= 씬 이름). 예: VillageGather. 화면에는 나가지 않고 찾는 열쇠로만 쓴다.")]
            public string sceneName;

            [Tooltip("로딩 화면에 크게 보일 한글 명칭. 한글만 쓴다(영문 알파벳이 섞이면 인스펙터에 경고).")]
            [FormerlySerializedAs("title")]
            public string koreanName;

            [Tooltip("명칭 아래 작은 영문 구역 코드(선택). 예: SECTOR 07 · LOWER CITY")]
            public string code;

            [Tooltip("이 씬의 로딩 일러스트 후보(선택). 2장 이상이면 진입할 때마다 직전과 다른 것을 무작위로 고른다. 비우면 일러스트 자리를 숨긴다.")]
            public Sprite[] illustrations;
        }

        [Header("예전 구성 (선택)")]
        [Tooltip("예전 하단 진행 바. 새 구성에선 쓰지 않으며, 남아 있으면 같이 갱신만 한다.")]
        [SerializeField] private Slider loadingSlider;

        [Header("팁")]
        [SerializeField] private TMP_Text tip;

        // 아래 기본 문구는 '이 필드를 아직 한 번도 직렬화한 적 없는' 오브젝트에만 들어간다.
        // 씬에 저장된 뒤에는 여기를 고쳐도 반영되지 않으니, 운영 중 문구 수정은 인스펙터에서 한다.
        // "TIP" 머리말은 옆의 TipChip이 따로 그리므로 여기엔 본문만 적는다.
        [Tooltip("표시할 팁 본문. 켜질 때마다 직전과 다른 것을 무작위로 고른다.\n" +
                 "<color=#30BAD4>강조</color> 같은 TMP 태그를 쓸 수 있다(여는 태그의 닫는 '>'를 빠뜨리지 말 것).")]
        [SerializeField, TextArea(2, 4)] private string[] tips =
        {
            "마우스 좌클릭을 <color=#30BAD4>꾹 누르고 있으면 공격이 연속으로 이어집니다.</color> 연타하지 않아도 콤보가 끊기지 않습니다.",
            "<color=#30BAD4>우클릭 강공격은 스킬 게이지를 크게 채웁니다.</color> 평타만 치는 것보다 스킬이 훨씬 빨리 돌아옵니다.",
            "<color=#30BAD4>강공격과 스킬을 쓰는 동안은 웬만한 공격에 밀리지 않습니다.</color> 다만 강한 일격에는 자세가 무너지니 큰 기술은 피하세요.",
            "<color=#30BAD4>각성기는 시전하는 내내 무적</color>입니다. 피할 수 없는 공격이 날아온다면 각성기로 받아치세요.",
            "<color=#30BAD4>구르는 동안에는 무적</color>입니다. 적의 공격이 닿기 직전에 맞춰 굴러보세요.",
            "<color=#30BAD4>구르기는 이동 키를 누른 방향으로 굴러갑니다.</color> 방향 입력 없이는 구르지 않으니 피할 쪽을 함께 눌러주세요.",
            "구르기는 스태미나를 소모합니다. <color=#30BAD4>스태미나는 시간이 지나면 저절로 회복</color>되니 연속 회피 뒤에는 한 박자 쉬어가세요.",
            "공중에서 한 번 더 <color=#30BAD4>Shift를 누르면 공중 대시</color>로 거리를 벌릴 수 있습니다. 착지 전까지 한 번만 쓸 수 있습니다.",
            "이동 키를 <color=#30BAD4>두 번 연달아 누르면 달리기</color>로 바뀝니다. 이동을 완전히 멈추면 다시 걷기부터 시작합니다.",
            "스킬창에서 익힌 스킬을 <color=#30BAD4>HUD 슬롯으로 끌어다 놓으면 단축키로 등록</color>됩니다. 1~5번 키로 바로 발동하세요.",
            "물약은 <color=#30BAD4>Q·E 퀵슬롯에 등록</color>해두면 전투 중에도 곧바로 마실 수 있습니다. 던전에 들어가기 전에 채워두세요.",
            "<color=#30BAD4>F키로 NPC와 대화</color>해 퀘스트를 받고 보상을 수령할 수 있습니다.",
            "J키를 눌러 퀘스트 항목을 확인할 수 있습니다.",
            "파티는 던전 입구에서 결성 가능합니다.",
            "던전의 클리어가 어렵다면 강화를 이용해봅시다.",
        };

        [Header("목적지")]
        [SerializeField] private TMP_Text destinationTitle;
        [SerializeField] private TMP_Text destinationCode;

        [Tooltip("목적지 아래 흐르는 0/1 데이터(선택).")]
        [SerializeField] private TMP_Text dataStream;

        [Tooltip("목적지를 모를 때(부팅 직후, 표에 없는 씬) 띄울 한글 표기. 켜질 때마다 이걸로 초기화한 뒤 목적지가 오면 덮어쓴다.")]
        [SerializeField] private string defaultTitle = "시스템 동기화";
        [SerializeField] private string defaultCode = "BOOT SEQUENCE";

        // 한글 명칭은 세계관 설정집 기준 지역 이름이다(2026-09-26 확정). 마을=세컨드 노드, 레이드=퍼스트 노드는
        // 설정집·대화/퀘스트 텍스트에 이미 쓰이는 이름이고, 튜토리얼·던전1·던전2는 이때 새로 정했다.
        [Tooltip("씬별 한글 명칭 표. 새 씬을 로딩으로 이동시키려면 여기에 행을 추가한다.")]
        [SerializeField] private Destination[] destinations =
        {
            new Destination { sceneName = "Tutorial", koreanName = "귀환자 수용동", code = "SECTOR 01 · SIMULATION" },
            new Destination { sceneName = "VillageGather", koreanName = "세컨드 노드", code = "SECTOR 00 · SAFE ZONE" },
            new Destination { sceneName = "Dungeon1", koreanName = "침식 구역", code = "SECTOR 07" },
            new Destination { sceneName = "Dungeon2", koreanName = "어센션 코어", code = "SECTOR 08" },
            new Destination { sceneName = "Raid", koreanName = "퍼스트 노드", code = "SECTOR 13 · CORE FACILITY" },
        };

        [Header("일러스트")]
        [Tooltip("목적지 오른쪽 빈자리의 일러스트 틀. 목적지 표에 그림이 없는 씬(부팅 직후 포함)에서는 통째로 숨긴다.")]
        [SerializeField] private GameObject illustrationFrame;

        [Tooltip("일러스트를 그리는 Image. preserveAspect로 16:9 원본 비율을 지킨다.")]
        [SerializeField] private Image illustration;

        [Header("진행 바 · 상태")]
        [SerializeField] private LoadingBarView bar;
        [SerializeField] private TMP_Text statusText;

        [Tooltip("받은 패킷 수 표기(선택). 진행률 × 전체 개수.")]
        [SerializeField] private TMP_Text packetText;

        [SerializeField, Min(1)] private int packetTotal = 40;

        [Tooltip("진행률 구간별 상태 문구. 마지막 문구는 100%일 때만 나온다.")]
        [SerializeField] private string[] statusSteps =
        {
            "지형 데이터 수신 중",
            "개체 신호 동기화 중",
            "텍스처 복호화 중",
            "경로 좌표 검증 중",
            "구역 진입 준비 완료",
        };

        [Header("기타")]
        [SerializeField] private TMP_Text versionText;

        [Tooltip("0/1 데이터를 새로 쓰는 간격(초).")]
        [SerializeField, Min(0.02f)] private float dataRefreshInterval = 0.12f;

        private readonly List<int> scratchIndices = new();
        private float progress;
        private int shownStep = -1;
        private int shownPackets = -1;
        private float nextDataTime;

        // 직전에 보여준 팁. 로딩이 연달아 뜰 때 같은 문구가 반복되지 않게 한 칸만 기억한다.
        private int lastTipIndex = -1;

        // 씬별로 직전에 보여준 일러스트. 후보가 2장뿐이라 순수 랜덤이면 같은 그림이 연달아 나오는 게 체감상 잦다.
        private readonly Dictionary<string, int> lastIllustrationIndex = new();

        protected override void OnInit()
        {
            if (loadingSlider == null) loadingSlider = GetComponentInChildren<Slider>();
            if (versionText != null) versionText.text = $"v{Application.version}";
        }

        protected override void OnShow()
        {
            progress = 0f;
            shownStep = -1;
            shownPackets = -1;
            nextDataTime = 0f;

            if (tips != null && tips.Length > 0)
            {
                lastTipIndex = PickNextIndex(tips.Length, lastTipIndex);
                SetTIPText(tips[lastTipIndex]);
            }

            // 이전 로딩의 목적지가 남지 않게 초기화한다(부팅 직후처럼 목적지를 안 넘기는 경로가 있음).
            ShowDestination(defaultTitle, defaultCode);
            ShowIllustration(null, null);

            SetProgress(0f);
        }

        /// <summary>
        /// 직전에 고른 것을 피해 다음 인덱스를 고른다. 순수 랜덤이면 같은 것이 연달아 나오는 게 체감상 잦아서다.
        /// </summary>
        private static int PickNextIndex(int count, int last)
        {
            if (count <= 1) return 0;

            int index = UnityEngine.Random.Range(0, count);
            if (index == last) index = (index + 1) % count;
            return index;
        }

        private void Update()
        {
            // 로딩 중 timeScale과 무관하게 흐르도록 실제 시간 기준.
            if (dataStream == null || Time.unscaledTime < nextDataTime) return;

            nextDataTime = Time.unscaledTime + dataRefreshInterval;
            dataStream.text = HoloInfoText.BuildBody(28, 3, scratchIndices);
        }

        /// <summary>
        /// 진행률(0~1)을 게이지·상태 문구·패킷 수에 반영한다. 로딩 루프가 매 프레임 부른다.
        /// </summary>
        public void SetProgress(float ratio)
        {
            progress = Mathf.Clamp01(ratio);

            if (loadingSlider != null) loadingSlider.value = progress;
            if (bar != null) bar.SetProgress(progress);

            UpdateStatus();
            UpdatePackets();
        }

        /// <summary>
        /// 이동할 씬을 알린다. 목적지 표에서 한글 명칭·구역 코드를 찾아 띄운다.
        /// 표에 없는 씬이면 씬 이름(영문 클래스명)을 노출하지 않고 기본 한글 표기를 띄운 뒤 경고를 남긴다.
        /// </summary>
        /// <param name="sceneName">로드할 씬 이름(씬 클래스 이름)</param>
        public void SetDestination(string sceneName)
        {
            foreach (Destination d in destinations)
            {
                if (d.sceneName != sceneName) continue;
                ShowDestination(d.koreanName, d.code);
                ShowIllustration(d.sceneName, d.illustrations);
                return;
            }

            Debug.LogWarning($"[LoadingPanel] 목적지 표에 '{sceneName}'이 없어 기본 표기를 띄웁니다. " +
                             "LoadingPanel의 destinations에 한글 명칭 행을 추가하세요.", this);
            ShowDestination(defaultTitle, defaultCode);
            ShowIllustration(null, null);
        }

        /// <summary>
        /// 씬의 대표 일러스트(후보 첫 장)를 돌려준다. 던전 입장 화면(<see cref="DungeonEntryPopup"/>)이 미리보기 이미지로 쓴다 —
        /// 같은 지역 그림을 두 곳에 따로 등록하면 한쪽만 바뀌어 어긋나기 쉬워, 로딩 화면의 목적지 표를 원천으로 삼는다.
        /// </summary>
        /// <remarks>
        /// 무작위가 아니라 첫 장으로 고정한다. 입장 화면에서 에피소드를 오갈 때마다 그림이 바뀌면 깜빡거려 보이기 때문이다.
        /// 로딩 화면 자체의 "직전과 다른 그림" 기록(<see cref="lastIllustrationIndex"/>)도 건드리지 않는다.
        /// </remarks>
        /// <param name="sceneName">씬 이름(씬 클래스 이름)</param>
        /// <returns>대표 일러스트. 표에 없거나 그림이 없으면 null</returns>
        public Sprite GetIllustration(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName) || destinations == null) return null;

            foreach (Destination d in destinations)
            {
                if (d.sceneName != sceneName) continue;
                return d.illustrations != null && d.illustrations.Length > 0 ? d.illustrations[0] : null;
            }
            return null;
        }

        /// <summary>팁 본문을 바꾼다.</summary>
        public void SetTIPText(string tipText)
        {
            if (tip != null) tip.text = tipText;
        }

        private void ShowDestination(string koreanName, string code)
        {
            if (destinationTitle != null) destinationTitle.text = koreanName;
            if (destinationCode != null) destinationCode.text = code;
        }

        /// <summary>
        /// 씬의 일러스트 후보 중 직전과 다른 한 장을 띄운다. 후보가 없으면 틀째 숨긴다 —
        /// 빈 틀만 남으면 그림이 로드되다 만 것처럼 보이기 때문이다.
        /// </summary>
        private void ShowIllustration(string sceneName, Sprite[] candidates)
        {
            bool has = illustration != null && candidates != null && candidates.Length > 0;
            if (illustrationFrame != null) illustrationFrame.SetActive(has);
            if (!has) return;

            int last = lastIllustrationIndex.TryGetValue(sceneName, out int shown) ? shown : -1;
            int index = PickNextIndex(candidates.Length, last);
            lastIllustrationIndex[sceneName] = index;
            illustration.sprite = candidates[index];
        }

#if UNITY_EDITOR
        // 명칭 칸에 영문 알파벳이 섞이면 바로 알린다. 제목은 한글 폰트 하나로 찍어서, 영문이 섞이면 표기가 어긋난다.
        private void OnValidate()
        {
            if (ContainsLatinLetter(defaultTitle))
                Debug.LogWarning($"[LoadingPanel] 기본 표기 '{defaultTitle}'에 영문이 있습니다. 한글로만 적어 주세요.", this);

            if (destinations == null) return;
            foreach (Destination d in destinations)
            {
                if (ContainsLatinLetter(d.koreanName))
                    Debug.LogWarning($"[LoadingPanel] '{d.sceneName}'의 명칭 '{d.koreanName}'에 영문이 있습니다. 한글로만 적어 주세요.", this);
            }
        }

        private static bool ContainsLatinLetter(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            foreach (char c in value)
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) return true;
            return false;
        }
#endif

        // 100% 전까지는 마지막 문구("준비 완료")를 아껴 둔다 — 99%에서 완료라고 적혀 있으면 거짓말이 된다.
        private void UpdateStatus()
        {
            if (statusText == null || statusSteps == null || statusSteps.Length == 0) return;

            int last = statusSteps.Length - 1;
            int step = progress >= 1f ? last : Mathf.Min(Mathf.Max(0, last - 1), Mathf.FloorToInt(progress * last));
            if (step == shownStep) return;

            shownStep = step;
            // 진행 중에만 끝에 커서가 깜빡이는 느낌을 준다(완료 문구엔 없음).
            statusText.text = step == last ? statusSteps[step] : statusSteps[step] + "_";
        }

        private void UpdatePackets()
        {
            if (packetText == null) return;

            int packets = Mathf.FloorToInt(progress * packetTotal);
            if (packets == shownPackets) return;

            shownPackets = packets;
            packetText.text = $"{packets} / {packetTotal} PKT";
        }
    }
}
