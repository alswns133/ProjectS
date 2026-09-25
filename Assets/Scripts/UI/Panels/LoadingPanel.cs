using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    public class LoadingPanel : BasePanel
    {
        /// <summary>
        /// 씬 하나에 대응하는 로딩 일러스트 묶음.
        /// <c>sceneName</c>은 <c>BaseScene</c> 파생 클래스 이름(= 씬 파일명 = RequestSceneChange의 T)과
        /// 같아야 조회된다. 이름이 어긋나면 그림이 조용히 기본값으로 떨어지므로 오타 시 경고를 남긴다.
        /// </summary>
        [Serializable]
        private class SceneLoadingArt
        {
            [Tooltip("씬 클래스명과 동일하게: VillageGather / Dungeon1 / Dungeon2 / Raid / Tutorial")]
            public string sceneName;

            [Tooltip("이 씬의 로딩 일러스트 후보. 2장 이상이면 진입할 때마다 그 안에서 랜덤으로 고른다.")]
            public Sprite[] images;

            [Tooltip("로딩 화면 상단에 띄울 지역 이름(예: 세컨드 노드). 비우면 코드의 기본 이름을 쓴다.")]
            public string regionName;
        }

        // TIP 머리말과 본문 크기 태그. 문구마다 이 래퍼를 되풀이해 적으면 닫는 '>'를 빠뜨리는 식의
        // 태그 오타가 나기 쉽고(그러면 태그가 글자 그대로 화면에 찍힌다), 표기를 바꿀 때 전부 손봐야 한다.
        // 그래서 래퍼는 코드가 한곳에서 씌우고, 인스펙터의 tips에는 본문만 적는다.
        private const string TipPrefix = "<size=125%>TIP</size> <size=75%>";
        private const string TipSuffix = "</size>";

        // 씬별 지역 이름의 기본값(세계관 설정집 기준, 2026-09-26 확정).
        // 마을=세컨드 노드, 레이드=퍼스트 노드는 설정집과 대화/퀘스트 텍스트에 이미 쓰이는 이름이고,
        // 나머지 셋은 설정집에 개별 지명이 없어 이번에 정한 것이다.
        // 인스펙터의 Region Name을 채우면 그쪽이 이긴다 — 여기는 인스펙터가 빈 칸일 때의 폴백이다.
        private static readonly Dictionary<string, string> DefaultRegionNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Tutorial",       "귀환자 수용동" },
            { "VillageGather",  "세컨드 노드" },
            { "Dungeon1",       "침식 구역" },
            { "Dungeon2",       "어센션 코어" },
            { "Raid",           "퍼스트 노드" },
        };

        [Header("References")]
        [SerializeField] private Slider loadingSlider;
        [SerializeField] private TMP_Text tip;

        // ★ Bootstrap 씬에서는 화면을 실제로 덮는 일러스트 자리가 'Background'가 아니라 'Im'이다
        //   ('Background'는 그 뒤에 깔린 검은 판). UGUI는 자식 순서대로 그리는데 Im이 Background보다
        //   뒤에 있어, Background에 그림을 넣으면 Im의 옛 이미지에 가려진다 → 반드시 Im을 연결할 것.
        [Tooltip("로딩 화면을 덮는 일러스트 이미지. Bootstrap 씬에서는 'Im'을 연결한다.\n" +
                 "비워두면 자식 'Background'를 찾지만, 그쪽은 Im에 가려 보이지 않는다.")]
        [SerializeField] private Image background;

        [Tooltip("로딩 화면 상단의 지역 이름 텍스트. 비워두면 자식 'MapText'를 자동으로 찾는다.")]
        [SerializeField] private TMP_Text mapText;

        [Header("Scene Art")]
        [Tooltip("씬별 로딩 일러스트. 씬 이름으로 조회한다.")]
        [SerializeField] private SceneLoadingArt[] sceneArts;

        [Tooltip("씬 이름을 못 찾았을 때(부트스트랩 최초 로딩, 목록에 없는 씬) 쓸 그림. 비워두면 검은 화면.")]
        [SerializeField] private Sprite defaultImage;

        [Header("Tips")]
        // 아래 기본 문구는 '이 필드를 아직 한 번도 직렬화한 적 없는' 오브젝트에만 들어간다.
        // 인스펙터에서 한 번이라도 손대면 그 값이 씬에 저장되므로, 이후 여기를 고쳐도 반영되지 않는다.
        // → 운영 중 문구 수정은 인스펙터에서 한다.
        [Tooltip("로딩 중 표시할 팁 본문. 'TIP' 머리말과 크기 태그는 코드가 붙이므로 본문만 적는다.\n" +
                 "<color=#30BAD4>강조</color> 같은 TMP 태그는 그대로 쓸 수 있다(여는 태그의 닫는 '>'를 빠뜨리지 말 것).")]
        [SerializeField, TextArea(2, 4)]
        private string[] tips =
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
        };

        // 씬별로 직전에 고른 인덱스. 후보가 2장뿐이라 순수 랜덤이면 같은 그림이 연달아 나오는 게 체감상 잦아,
        // 한 칸만 기억해 두고 직전과 다른 쪽을 고른다.
        private readonly Dictionary<string, int> lastPickedIndex = new Dictionary<string, int>();

        // 직전에 보여준 팁. 로딩이 연달아 뜰 때 같은 문구가 반복되지 않게 한 칸만 기억한다.
        private int lastTipIndex = -1;

        protected override void OnInit() 
        {
            loadingSlider = GetComponentInChildren<Slider>();
            //tip = transform.Find("Im/BottomBarIm").GetComponentInChildren<TMP_Text>();

            EnsureBackground();
        }

        /// <summary>
        /// 인스펙터 연결을 깜빡해도 굴러가게 자식 'Background'/'MapText'를 찾아둔다
        /// (계층 이름 규약: LoadingPanel > Background, LoadingPanel > MapText).
        /// OnInit(최초 Show)보다 SetSceneArt가 먼저 오는 경우가 있어 양쪽에서 호출한다.
        /// </summary>
        private void EnsureBackground()
        {
            if (background == null)
            {
                Transform found = transform.Find("Background");
                if (found != null) background = found.GetComponent<Image>();
            }

            if (mapText == null)
            {
                Transform found = transform.Find("MapText");
                if (found != null) mapText = found.GetComponent<TMP_Text>();
            }
        }

        protected override void OnShow()
        {
            SetTIPText(BuildTipText());
        }

        /// <summary>
        /// 이번 로딩에 띄울 팁 한 줄을 만든다. 후보 중 직전과 다른 것을 골라 TIP 래퍼를 씌운다.
        /// 후보가 비어 있으면 머리말만 덩그러니 남지 않도록 빈 문자열을 돌려준다.
        /// </summary>
        private string BuildTipText()
        {
            if (tips == null || tips.Length == 0) return string.Empty;

            lastTipIndex = PickNextIndex(tips.Length, lastTipIndex);

            string body = tips[lastTipIndex];
            if (string.IsNullOrWhiteSpace(body)) return string.Empty;

            return TipPrefix + body + TipSuffix;
        }

        /// <summary>
        /// 직전에 고른 것을 피해 다음 인덱스를 고른다.
        /// 후보가 두셋뿐일 때 순수 랜덤이면 같은 것이 연달아 나오는 게 체감상 잦아서, 한 칸을 기억해 걸러낸다.
        /// </summary>
        /// <param name="count">후보 개수</param>
        /// <param name="last">직전에 고른 인덱스(없으면 -1)</param>
        private static int PickNextIndex(int count, int last)
        {
            if (count <= 1) return 0;

            int index = UnityEngine.Random.Range(0, count);
            if (index == last) index = (index + 1) % count;
            return index;
        }

        public void SetProgress(float ratio)
            => loadingSlider.value = ratio;

        public void SetTIPText(string tipText) => tip.text = tipText;

        /// <summary>
        /// 다음에 들어갈 씬에 맞는 로딩 일러스트로 배경을 갈아끼운다.
        /// <see cref="ProjectS.Managers.UIManager.ShowLoading(string)"/>이 패널을 켜기 직전에 호출한다 —
        /// Show 이후에 바꾸면 이전 씬의 그림이 한 프레임 비친다.
        /// </summary>
        /// <param name="sceneName">진입할 씬 이름(BaseScene 파생 클래스 이름). 비어 있으면 기본 그림.</param>
        public void SetSceneArt(string sceneName)
        {
            EnsureBackground();

            if (background != null)
            {
                Sprite picked = PickSprite(sceneName);
                background.sprite = picked;

                // 배경 Image의 기본 색이 검정이라, 흰색으로 돌리지 않으면 스프라이트가 곱해져 까맣게 나온다.
                background.color = (picked != null) ? Color.white : Color.black;
            }

            ApplyRegionName(sceneName);
        }

        /// <summary>
        /// 로딩 화면 상단에 이번 씬의 지역 이름을 띄운다.
        /// 이름을 못 찾으면(목적지가 아직 없는 부팅 직후 등) 비운다 — 직전 씬 이름이 남아 있는 것보다 낫다.
        /// </summary>
        private void ApplyRegionName(string sceneName)
        {
            if (mapText == null) return;

            mapText.text = ResolveRegionName(sceneName) ?? string.Empty;
        }

        /// <summary>
        /// 이 씬의 지역 이름을 정한다. 인스펙터의 Region Name이 먼저이고, 비어 있으면
        /// <see cref="DefaultRegionNames"/>의 기본 이름을 쓴다. 둘 다 없으면 null.
        /// </summary>
        private string ResolveRegionName(string sceneName)
        {
            SceneLoadingArt art = FindArt(sceneName);
            if (art != null && !string.IsNullOrWhiteSpace(art.regionName)) return art.regionName.Trim();

            if (!string.IsNullOrEmpty(sceneName) && DefaultRegionNames.TryGetValue(sceneName, out string fallback))
                return fallback;

            return null;
        }

        /// <summary>
        /// 씬 이름에 해당하는 등록 항목을 찾는다. 없으면 null.
        /// 인스펙터에 손으로 치는 문자열이라 앞뒤 공백·대소문자는 흡수한다(오타로 통째로 안 나오는 걸 줄임).
        /// </summary>
        private SceneLoadingArt FindArt(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName) || sceneArts == null) return null;

            for (int i = 0; i < sceneArts.Length; i++)
            {
                SceneLoadingArt art = sceneArts[i];
                if (art != null && string.Equals(art.sceneName?.Trim(), sceneName, StringComparison.OrdinalIgnoreCase))
                    return art;
            }

            return null;
        }

        /// <summary>
        /// 씬 이름으로 후보를 찾아 한 장 고른다. 후보가 여러 장이면 직전과 다른 것을 우선한다.
        /// </summary>
        private Sprite PickSprite(string sceneName)
        {
            SceneLoadingArt art = FindArt(sceneName);
            if (art == null)
            {
                // 씬 이름 오타/미등록은 여기로 떨어진다. 화면이 검게만 나오면 이 경고부터 확인할 것.
                // (부팅 직후처럼 목적지 이름이 아예 없는 호출은 경고 없이 기본 이미지로 간다.)
                if (!string.IsNullOrEmpty(sceneName))
                    Debug.LogWarning($"[LoadingPanel] 씬 '{sceneName}'에 등록된 로딩 이미지가 없다. Scene Arts 목록의 Scene Name 표기를 확인할 것.");

                return defaultImage;
            }

            Sprite[] images = art.images;
            if (images == null || images.Length == 0)
            {
                Debug.LogWarning($"[LoadingPanel] '{sceneName}'의 로딩 이미지가 비어 있어 기본 이미지를 쓴다.");
                return defaultImage;
            }

            int last = lastPickedIndex.TryGetValue(sceneName, out int stored) ? stored : -1;
            int index = PickNextIndex(images.Length, last);

            lastPickedIndex[sceneName] = index;
            return images[index];
        }
    }
}
