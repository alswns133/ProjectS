using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

namespace ProjectS.Debugging
{
    /// <summary>
    /// 화면 구석에 실시간 FPS를 텍스트로 표시한다. 에디터가 아닌 <b>빌드에서도</b> 프레임을 눈으로
    /// 확인하기 위한 도구다(에디터 Stats 창은 빌드에 없다).
    /// <para>
    /// 씬에 배치하거나 프리팹을 참조할 필요가 없다 — 첫 씬 로드 후 자기 오브젝트를 만들어 붙고(부트스트랩),
    /// 자기만의 오버레이 Canvas와 TMP 텍스트까지 코드로 생성한다. 그래서 어느 씬에서 시작하든 항상 뜬다.
    /// <see cref="AutoSaveTicker"/>·<see cref="DebugTimeScaleKey"/>와 같은 자동 생성 패턴이지만,
    /// 빌드에서도 필요하므로 <c>#if UNITY_EDITOR</c>로 감싸지 않는다.
    /// </para>
    /// <para>
    /// F1로 표시를 켜고 끈다. 기본값은 <see cref="startVisible"/>로 정한다.
    /// </para>
    /// </summary>
    public class FpsCounter : MonoBehaviour
    {
        /// <summary>중복 생성 방지 및 외부 토글용 싱글톤 참조.</summary>
        public static FpsCounter Instance { get; private set; }

        // FPS 값을 갱신하는 주기(초). 매 프레임 숫자가 널뛰면 읽기 어려우므로 짧게 평균 내어 보여준다.
        // Time.unscaledDeltaTime을 쓰기 때문에 DebugTimeScaleKey로 배속을 바꿔도 실제 프레임률이 나온다.
        private const float UpdateInterval = 0.5f;

        // 처음부터 보일지. 개발 빌드에선 켜두고 확인하다가, 필요 없으면 F1로 끈다.
        private const bool startVisible = true;

        private TextMeshProUGUI label;
        private Canvas canvas;

        // UpdateInterval 동안 누적한 프레임 수와 경과 시간. 나눠서 평균 FPS를 낸다.
        private int accumulatedFrames;
        private float accumulatedTime;

        private bool visible = startVisible;

        // 첫 씬 로드 후 1회 자동 생성. 씬마다 배치할 필요가 없다(누락 방지).
        // AfterSceneLoad라야 DontDestroyOnLoad 이관이 안전하다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;

            GameObject go = new GameObject("[FpsCounter]");
            go.AddComponent<FpsCounter>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            // 부트스트랩 경로가 아닌 씬 배치로 중복 생성돼도 하나만 남긴다.
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            BuildUI();
            canvas.enabled = visible;
        }

        private void Update()
        {
            // 배속 디버그(timeScale)에 흔들리지 않게 unscaled 기준으로 실제 프레임률을 잰다.
            accumulatedFrames++;
            accumulatedTime += Time.unscaledDeltaTime;

            if (accumulatedTime < UpdateInterval) return;

            float fps = accumulatedFrames / accumulatedTime;
            float ms = accumulatedTime / accumulatedFrames * 1000f;
            accumulatedFrames = 0;
            accumulatedTime = 0f;

            if (visible)
            {
                label.text = $"{fps:0} FPS ({ms:0.0} ms)";
                label.color = FpsColor(fps);
            }
        }

        // F1로 표시 토글. 빌드에서도 켜고 끌 수 있게 게임플레이 키(Input System)로 읽는다.
        private void LateUpdate()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame)
            {
                SetVisible(!visible);
            }
        }

        /// <summary>표시를 켜고 끈다. 다른 디버그 UI에서 코드로 제어할 수 있게 공개한다.</summary>
        public void SetVisible(bool value)
        {
            visible = value;
            if (canvas != null) canvas.enabled = value;
        }

        // 프레임률에 따라 색을 바꿔 한눈에 상태를 읽게 한다(초록=쾌적, 노랑=주의, 빨강=끊김).
        private static Color FpsColor(float fps)
        {
            if (fps >= 50f) return Color.green;
            if (fps >= 30f) return Color.yellow;
            return Color.red;
        }

        // 자기만의 오버레이 Canvas와 텍스트를 코드로 구성한다. 씬의 다른 Canvas와 섞이지 않도록
        // sortingOrder를 크게 줘 항상 맨 위에 그린다. Raycaster는 붙이지 않아 입력을 가로채지 않는다.
        private void BuildUI()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;

            // 해상도가 달라도 글자 크기가 일정하게 보이도록 스케일러를 붙인다.
            var scaler = gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject textGo = new GameObject("Label");
            textGo.transform.SetParent(transform, false);

            label = textGo.AddComponent<TextMeshProUGUI>();
            label.fontSize = 28f;
            label.color = Color.green;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.raycastTarget = false;
            label.text = "-- FPS";

            // 좌상단에 고정하고 살짝 여백을 준다.
            RectTransform rect = label.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(16f, -16f);
            rect.sizeDelta = new Vector2(400f, 60f);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
