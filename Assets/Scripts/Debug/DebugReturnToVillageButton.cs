using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using TMPro;
using ProjectS.Managers;
using ProjectS.Scenes;

namespace ProjectS.Debugging
{
    /// <summary>
    /// 화면에 "마을복귀" 버튼을 띄워 던전 등 어디서든 마을(<see cref="VillageGather"/>) 씬으로 즉시 복귀한다.
    /// <see cref="DebugReturnToVillageKey"/>(V키)의 <b>빌드용</b> 짝이다 — 키는 에디터 전용이라 빌드/모바일에선
    /// 못 쓰므로, 손가락/마우스로 누를 수 있는 온스크린 버튼으로 같은 동작을 낸다.
    /// <para>
    /// 씬 배치·프리팹 참조가 필요 없다 — 첫 씬 로드 후 자기 오브젝트를 만들어 붙고, 자기만의 오버레이 Canvas와
    /// 버튼까지 코드로 생성한다(<see cref="FpsCounter"/>와 같은 자동 생성 패턴). 정식 복귀 동선(마을 게이트 등)이
    /// 생기면 이 파일을 지우면 된다.
    /// </para>
    /// </summary>
    public class DebugReturnToVillageButton : MonoBehaviour
    {
        /// <summary>중복 생성 방지 및 외부 제어용 싱글톤 참조.</summary>
        public static DebugReturnToVillageButton Instance { get; private set; }

        private Canvas canvas;

        // 첫 씬 로드 후 1회 자동 생성. 씬마다 배치할 필요가 없다(누락 방지).
        // 빌드에서도 테스트하려는 도구라 #if UNITY_EDITOR로 감싸지 않는다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;

            GameObject go = new GameObject("[DebugReturnToVillageButton]");
            go.AddComponent<DebugReturnToVillageButton>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            // 씬 배치 등으로 중복 생성돼도 하나만 남긴다.
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            EnsureEventSystem();
            BuildUI();
        }

        /// <summary>버튼 표시를 켜고 끈다.</summary>
        public void SetVisible(bool value)
        {
            if (canvas != null) canvas.enabled = value;
        }

        // 버튼 클릭을 처리하려면 씬에 EventSystem이 있어야 한다. 대개 UI가 있는 씬엔 이미 있지만,
        // 없는 씬(테스트 씬 등)에서 눌러도 반응하도록 없으면 만든다. 이 프로젝트는 Input System을
        // 쓰므로 구형 StandaloneInputModule이 아니라 InputSystemUIInputModule을 붙여야 클릭이 처리된다.
        private void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;

            GameObject es = new GameObject("[EventSystem(Debug)]");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
            DontDestroyOnLoad(es);
        }

        // 자기만의 오버레이 Canvas와 버튼을 코드로 구성한다. 다른 Canvas 위에 그리도록 sortingOrder를 크게 준다.
        private void BuildUI()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue - 1; // FpsCounter(=MaxValue)보다 한 단계 아래.
            gameObject.AddComponent<GraphicRaycaster>();

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // 버튼 배경(Image + Button). 우상단에 고정해 FPS 카운터(좌상단)와 겹치지 않게 둔다.
            GameObject btnGo = new GameObject("ReturnButton");
            btnGo.transform.SetParent(transform, false);

            Image image = btnGo.AddComponent<Image>();
            image.color = new Color(0.15f, 0.15f, 0.18f, 0.85f);

            Button button = btnGo.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(ReturnToVillage);

            RectTransform rect = btnGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-24f, -24f);
            rect.sizeDelta = new Vector2(220f, 84f);

            // 라벨.
            GameObject labelGo = new GameObject("Label");
            labelGo.transform.SetParent(btnGo.transform, false);

            TextMeshProUGUI label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = "마을복귀";
            label.font = ResolveKoreanFont();   // 기본 폰트는 한글 글리프가 없어 □로 깨진다.
            label.fontSize = 32f;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;

            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
        }

        // 빌드에서 쓸 한글 TMP 폰트를 로드한다. 'Paperlogy-5Medium SDF'는 TextMesh Pro/Resources 아래에 있어
        // Resources.Load로 런타임 로드가 된다(에디터 전용 AssetDatabase는 빌드에서 못 쓴다). Resources 경로는
        // Resources 폴더 기준 상대경로·확장자 없음이라 "Fonts & Materials/Paperlogy-5Medium SDF"가 된다.
        // 못 찾으면 기본 폰트로 폴백하지만, 그 경우 한글이 깨지므로 이름/위치를 확인하라는 경고를 남긴다.
        private static TMP_FontAsset ResolveKoreanFont()
        {
            const string resourcePath = "Fonts & Materials/Paperlogy-5Medium SDF";
            TMP_FontAsset font = Resources.Load<TMP_FontAsset>(resourcePath);
            if (font != null) return font;

            UnityEngine.Debug.LogWarning($"[DebugReturnToVillageButton] '{resourcePath}' 폰트를 못 찾아 기본 폰트를 씁니다(한글 깨짐).");
            return TMP_Settings.defaultFontAsset;
        }

        // 실제 워프·활성화·마을 모드 전환은 VillageGather.Enter가 처리하므로 여기는 씬 전환만 요청한다.
        // 이미 마을이거나 로딩 중이면 RequestSceneChange 쪽에서 걸러진다.
        private void ReturnToVillage()
        {
            if (GameSceneManager.Instance == null) return;

            DevLog.Log("[Debug] 마을로 복귀(버튼)");
            GameSceneManager.Instance.RequestSceneChange<VillageGather>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
