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
        }

        [Header("예전 구성 (선택)")]
        [Tooltip("예전 하단 진행 바. 새 구성에선 쓰지 않으며, 남아 있으면 같이 갱신만 한다.")]
        [SerializeField] private Slider loadingSlider;

        [Header("팁")]
        [SerializeField] private TMP_Text tip;

        [Tooltip("표시할 팁 문구. 켜질 때마다 하나를 무작위로 고른다.")]
        [SerializeField, TextArea] private string[] tips =
        {
            "전투 지역에서는 장비 착용 중 브로치와 소켓 아이템을 바꿀 수 없습니다. 안전 지역에서 바꿔 주세요.",
            "강공격과 스킬 시전 중에는 약한 공격에 경직되지 않습니다.",
        };

        [Header("목적지")]
        [SerializeField] private TMP_Text destinationTitle;
        [SerializeField] private TMP_Text destinationCode;

        [Tooltip("목적지 아래 흐르는 0/1 데이터(선택).")]
        [SerializeField] private TMP_Text dataStream;

        [Tooltip("목적지를 모를 때(부팅 직후, 표에 없는 씬) 띄울 한글 표기. 켜질 때마다 이걸로 초기화한 뒤 목적지가 오면 덮어쓴다.")]
        [SerializeField] private string defaultTitle = "시스템 동기화";
        [SerializeField] private string defaultCode = "BOOT SEQUENCE";

        [Tooltip("씬별 한글 명칭 표. 새 씬을 로딩으로 이동시키려면 여기에 행을 추가한다.")]
        [SerializeField] private Destination[] destinations =
        {
            new Destination { sceneName = "VillageGather", koreanName = "마을", code = "SECTOR 00 · SAFE ZONE" },
            new Destination { sceneName = "DungeonGather", koreanName = "던전", code = "SECTOR 07 · LOWER CITY" },
            new Destination { sceneName = "RaidGather", koreanName = "레이드", code = "SECTOR 13 · CORE FACILITY" },
            new Destination { sceneName = "Tutorial", koreanName = "훈련 구역", code = "SECTOR 01 · SIMULATION" },
        };

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

            if (tips != null && tips.Length > 0) SetTIPText(tips[UnityEngine.Random.Range(0, tips.Length)]);

            // 이전 로딩의 목적지가 남지 않게 초기화한다(부팅 직후처럼 목적지를 안 넘기는 경로가 있음).
            ShowDestination(defaultTitle, defaultCode);

            SetProgress(0f);
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
                return;
            }

            Debug.LogWarning($"[LoadingPanel] 목적지 표에 '{sceneName}'이 없어 기본 표기를 띄웁니다. " +
                             "LoadingPanel의 destinations에 한글 명칭 행을 추가하세요.", this);
            ShowDestination(defaultTitle, defaultCode);
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
