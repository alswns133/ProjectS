using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace ProjectS.Tutorials
{
    /// <summary>
    /// 튜토리얼 조작 안내를 순서대로 띄우는 연출기. 안내마다 가이드 UI(키보드 이미지 묶음)를 띄우고,
    /// 그동안 <see cref="MonologueView"/>로 주인공의 독백을 출력한다. 출력할 대사도 여기서 안내별로 관리한다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>기존 방식(ZoneTrigger)을 대체한다.</b> 이전에는 바닥 영역마다 ZoneTrigger가 이미지 하나를 켜고 하나를 끄는
    /// 사슬이었다. 안내가 "이미지 + 독백 + 종료 조건"으로 늘어나면서 영역마다 흩어 두면 대사를 고칠 때 씬 곳곳을
    /// 찾아다녀야 해서, 안내 목록을 한 곳(이 컴포넌트)에 모았다. 영역 감지는 <see cref="PlayerZoneTrigger"/>를 쓴다
    /// (태그 문자열이 아니라 Player 컴포넌트로 판정해 오타를 피한다).
    /// </para>
    /// <para>
    /// <b>기본은 "행동을 마칠 때까지 유지"다(2026-09-17).</b> 조작 안내는 읽으라고 띄우는 게 아니라 해 보라고 띄우는 것이라,
    /// 시간이 지났다고 닫히면 아직 조작 중인 플레이어가 안내를 잃는다. 대사를 다 출력한 뒤 마지막 줄을 남겨 두고,
    /// 완료 신호(<see cref="GuideEntry.completeZone"/> 진입이나 <see cref="CompleteGuide(int)"/> 호출)가 오면 닫는다.
    /// 시간으로 닫는 안내가 필요하면 항목의 종료 방식을 <see cref="GuideEndMode.AfterDuration"/>으로 둔다.
    /// </para>
    /// <para>
    /// <b>입력을 직접 읽어 완료를 판정하지 않는다.</b> 게임플레이 입력은 PlayerInputHandler만 읽는 규칙이 있고,
    /// "키를 눌렀다"보다 "목적지에 닿았다·장벽을 통과했다" 같은 결과가 튜토리얼의 완료 기준으로 더 정확하다.
    /// </para>
    /// <para>
    /// <b>한 번에 안내 하나만 띄운다.</b> 안내가 떠 있는 중에 다른 안내가 요청되면 이전 안내를 즉시 닫고 새 안내를
    /// 시작한다. 두 안내가 겹치면 화면 상단의 같은 자리에 이미지가 포개지고, 독백 칸도 하나뿐이다.
    /// </para>
    /// <para>
    /// 키 점멸은 가이드 오브젝트에 붙은 <see cref="KeyGuideBlinker"/>가 켜질 때 스스로 시작하므로 여기서 따로 부르지 않는다.
    /// </para>
    /// <para>
    /// 붙이는 곳: 튜토리얼 씬의 관리자 오브젝트 하나. (2026-09-17 TH)
    /// </para>
    /// </remarks>
    public class TutorialGuideDirector : MonoBehaviour
    {
        /// <summary>안내를 언제 닫을지.</summary>
        /// <remarks>
        /// <b>UntilCompleted가 0이어야 한다.</b> 인스펙터에서 빈 목록에 [+]로 추가한 항목은 필드 기본값이 아니라 0으로
        /// 만들어지므로, 0이 기본 동작이 되도록 순서를 정했다.
        /// </remarks>
        public enum GuideEndMode
        {
            /// <summary>완료 신호가 올 때까지 유지한다. 대사가 끝나면 마지막 줄이 남는다.</summary>
            UntilCompleted = 0,

            /// <summary>유지 시간이 지나면 닫는다.</summary>
            AfterDuration = 1,
        }

        /// <summary>안내 하나. 가이드 UI · 독백 · 시작 조건 · 종료 조건을 묶는다.</summary>
        [Serializable]
        public class GuideEntry
        {
            [Tooltip("인스펙터에서 알아보기 위한 이름이자 PlayGuide(string)/CompleteGuide(string)의 키. 예: Move, Evasion")]
            public string id;

            [Tooltip("보여줄 가이드 UI(예: TutorialMoveGuide). 안내가 닫히면 다시 꺼진다.")]
            public GameObject guideRoot;

            [Tooltip("선택. 플레이어가 이 영역에 들어오면 안내를 시작한다. 비우면 코드나 UnityEvent로만 시작한다.")]
            public PlayerZoneTrigger trigger;

            [Header("독백")]
            [Tooltip("이 안내 동안 순서대로 출력할 독백. 마지막 줄은 안내가 닫힐 때까지 남는다.")]
            [TextArea(1, 3)] public string[] lines;

            [Tooltip("한 줄을 다 찍은 뒤 다음 줄이 나오기까지의 딜레이(초).")]
            [Min(0f)] public float lineInterval = 1.5f;

            [Header("종료")]
            [Tooltip("UntilCompleted: 완료 신호가 올 때까지 유지(기본). AfterDuration: 유지 시간이 지나면 닫는다.")]
            public GuideEndMode endMode = GuideEndMode.UntilCompleted;

            [Tooltip("선택. 플레이어가 이 영역에 들어오면 안내를 완료로 닫는다(예: 이동 안내의 목적지). " +
                     "비우면 다른 곳의 UnityEvent에서 CompleteGuide를 불러 닫는다.")]
            public PlayerZoneTrigger completeZone;

            [Tooltip("AfterDuration일 때 가이드 UI를 유지하는 시간(초).")]
            [Min(0f)] public float duration = 5f;

            [Tooltip("AfterDuration일 때, 켜면 유지 시간이 지나도 독백이 끝날 때까지 기다린다.")]
            public bool waitForMonologue = true;

            [Tooltip("한 번만 재생한다. 끄면 영역에 다시 들어올 때마다 재생된다.")]
            public bool playOnce = true;

            [Header("이벤트")]
            [Tooltip("안내가 시작될 때. 다음 목적지(초록 원) 켜기 같은 연출을 연결한다.")]
            public UnityEvent onStarted = new UnityEvent();

            [Tooltip("안내가 완료로 닫힐 때(완료 신호 또는 유지 시간 종료). 다른 안내에 끊기면 호출되지 않는다.")]
            public UnityEvent onFinished = new UnityEvent();
        }

        [Header("연결")]
        [Tooltip("독백을 출력할 뷰(TutorialMonologue). 모든 안내가 이 칸 하나를 함께 쓴다.")]
        [SerializeField] private MonologueView monologue;

        [Header("표시")]
        [Tooltip("안내가 닫힐 때 가이드와 독백 칸이 서서히 사라지는 시간(초). 0이면 즉시 사라진다.")]
        [SerializeField, Min(0f)] private float fadeOutDuration = 0.5f;

        [Tooltip("켜면 각 안내의 마지막 대사를 아래 색으로 표시한다. 행동 목표를 앞선 대사와 구분해 보이게 할 때 쓴다.")]
        [SerializeField] private bool tintLastLine = true;

        [Tooltip("마지막 대사의 글자 색.")]
        [SerializeField] private Color lastLineColor = new Color(1f, 0.85f, 0.35f, 1f);

        [Header("안내 목록")]
        [SerializeField] private List<GuideEntry> guides = new List<GuideEntry>();

        [Tooltip("씬 시작 시 자동으로 재생할 안내 번호(목록의 몇 번째 칸인지). -1이면 자동 재생하지 않는다.")]
        [SerializeField] private int playOnStartIndex = 0;

        /// <summary>지금 떠 있는 안내 번호. 없으면 -1.</summary>
        public int CurrentIndex { get; private set; } = -1;

        private Coroutine guideRoutine;

        // 이미 재생한 안내. playOnce 안내가 영역 재진입으로 다시 뜨지 않게 한다.
        private readonly HashSet<int> playedGuides = new HashSet<int>();

        // 영역 이벤트 구독 핸들. 람다는 같은 인스턴스를 들고 있어야 해제된다.
        private Action<bool>[] startHandlers;
        private Action<bool>[] completeHandlers;

        // 가이드별 투명도 그룹과 원래 투명도. 서서히 사라진 뒤 다음 표시를 위해 원래 값으로 되돌린다.
        private readonly Dictionary<GameObject, CanvasGroup> guideGroups = new Dictionary<GameObject, CanvasGroup>();
        private readonly Dictionary<GameObject, float> guideAlphas = new Dictionary<GameObject, float>();

        // 지금 사라지는 중인 가이드. 사라지는 도중 같은 가이드가 다시 뜨면 페이드를 끊어야 한다.
        private readonly Dictionary<GameObject, Coroutine> fadingGuides = new Dictionary<GameObject, Coroutine>();

        private void Awake()
        {
            if (monologue == null)
            {
                Debug.LogWarning($"[TutorialGuideDirector] {name}: monologue가 비어 있어 독백 없이 가이드만 표시된다.", this);
            }

            foreach (GuideEntry guide in guides)
            {
                GameObject root = guide?.guideRoot;
                if (root == null || guideGroups.ContainsKey(root)) continue;

                // 없으면 붙인다 — 가이드마다 CanvasGroup을 챙겨 달지 않아도 페이드가 동작하게.
                if (!root.TryGetComponent(out CanvasGroup group)) group = root.AddComponent<CanvasGroup>();
                guideGroups.Add(root, group);
                guideAlphas.Add(root, group.alpha);

                // 씬에 켜둔 채 저장돼 있어도 시작은 전부 숨김이다. 요청되기 전에 안내가 떠 있으면 순서를 오해한다.
                root.SetActive(false);
            }
        }

        private void OnEnable()
        {
            completeHandlers = new Action<bool>[guides.Count];
            startHandlers = new Action<bool>[guides.Count];

            // 완료 구독을 시작 구독보다 먼저 건다. 같은 영역이 "이전 안내의 목적지"이자 "다음 안내의 시작"일 때,
            // 이벤트는 구독 순서대로 오므로 이전 안내가 완료(onFinished)로 닫힌 뒤 다음 안내가 시작된다.
            // 순서가 뒤집히면 다음 안내가 이전 안내를 끊은 것으로 처리돼 onFinished가 불리지 않는다.
            for (int i = 0; i < guides.Count; i++)
            {
                if (guides[i]?.completeZone == null) continue;

                int index = i;   // 람다가 루프 변수를 공유하지 않도록 복사한다
                completeHandlers[i] = inside =>
                {
                    if (inside) CompleteGuide(index);
                };
                guides[i].completeZone.PlayerInsideChanged += completeHandlers[i];
            }

            for (int i = 0; i < guides.Count; i++)
            {
                if (guides[i]?.trigger == null) continue;

                int index = i;
                startHandlers[i] = inside =>
                {
                    if (inside) PlayGuide(index);
                };
                guides[i].trigger.PlayerInsideChanged += startHandlers[i];
            }
        }

        private void OnDisable()
        {
            for (int i = 0; i < guides.Count; i++)
            {
                if (startHandlers != null && i < startHandlers.Length && startHandlers[i] != null && guides[i]?.trigger != null)
                    guides[i].trigger.PlayerInsideChanged -= startHandlers[i];

                if (completeHandlers != null && i < completeHandlers.Length && completeHandlers[i] != null && guides[i]?.completeZone != null)
                    guides[i].completeZone.PlayerInsideChanged -= completeHandlers[i];
            }

            // 꺼진 오브젝트에서는 코루틴이 멈추고 새로 시작할 수도 없다. 사라지던 가이드와 떠 있는 안내를 즉시 정리한다.
            foreach (GameObject root in new List<GameObject>(fadingGuides.Keys))
            {
                HideGuideRoot(root);
            }

            EndCurrent(completed: false, instant: true);
        }

        private void Start()
        {
            if (playOnStartIndex >= 0) PlayGuide(playOnStartIndex);
        }

        /// <summary>
        /// 번호로 안내를 재생한다. UnityEvent에서 정수 인자로 연결할 수 있다
        /// (예: 이전 안내의 onFinished → 다음 안내).
        /// </summary>
        /// <param name="index">안내 목록의 번호</param>
        public void PlayGuide(int index)
        {
            if (index < 0 || index >= guides.Count || guides[index] == null)
            {
                Debug.LogWarning($"[TutorialGuideDirector] {name}: {index}번 안내가 없다.", this);
                return;
            }

            GuideEntry guide = guides[index];

            // 인스펙터에서 빈 목록에 [+]로 추가한 항목은 필드 기본값(5초 등)이 아니라 0으로 만들어진다.
            // 시간 종료 안내가 유지 시간 0이면 한 프레임 만에 닫혀 "안 뜬다"로 보이므로 알린다.
            if (guide.endMode == GuideEndMode.AfterDuration
                && guide.duration <= 0f
                && !(guide.waitForMonologue && monologue != null && guide.lines is { Length: > 0 }))
            {
                Debug.LogWarning($"[TutorialGuideDirector] {name}: {index}번 안내('{guide.id}')의 유지 시간이 0이라 " +
                                 "곧바로 닫힌다. Duration을 넣거나 Wait For Monologue를 켜고 대사를 넣으세요.", this);
            }

            if (guide.playOnce && playedGuides.Contains(index)) return;
            if (index == CurrentIndex) return;   // 같은 안내가 떠 있는 중에 영역을 드나들어도 처음부터 다시 시작하지 않는다

            if (!isActiveAndEnabled) return;     // 꺼진 오브젝트에서는 코루틴을 시작할 수 없다

            EndCurrent(completed: false);

            playedGuides.Add(index);
            CurrentIndex = index;
            guideRoutine = StartCoroutine(RunGuide(guide));
        }

        /// <summary>id로 안내를 재생한다. 목록 순서를 바꿔도 연결이 어긋나지 않게 할 때 쓴다.</summary>
        /// <param name="id">안내의 id</param>
        public void PlayGuide(string id)
        {
            int index = FindIndex(id);
            if (index < 0)
            {
                Debug.LogWarning($"[TutorialGuideDirector] {name}: id '{id}'인 안내가 없다.", this);
                return;
            }

            PlayGuide(index);
        }

        /// <summary>
        /// 안내를 완료로 닫는다(onFinished 호출). 플레이어가 안내된 행동을 마쳤을 때 부른다 —
        /// 예: RollThroughBarrier.onPassed → 회피 안내, TutorialRunTrial.onSuccess → 달리기 안내.
        /// 그 안내가 지금 떠 있지 않으면 아무 일도 하지 않는다.
        /// </summary>
        /// <param name="index">안내 목록의 번호</param>
        public void CompleteGuide(int index)
        {
            if (index < 0 || index != CurrentIndex) return;

            EndCurrent(completed: true);
        }

        /// <summary>id로 안내를 완료로 닫는다. 그 안내가 지금 떠 있지 않으면 아무 일도 하지 않는다.</summary>
        /// <param name="id">안내의 id</param>
        public void CompleteGuide(string id)
        {
            CompleteGuide(FindIndex(id));
        }

        /// <summary>지금 떠 있는 안내를 완료로 닫는다. 어떤 안내인지 신경 쓰지 않고 "행동 끝"만 알릴 때 쓴다.</summary>
        public void CompleteCurrent()
        {
            if (CurrentIndex >= 0) EndCurrent(completed: true);
        }

        /// <summary>떠 있는 안내를 즉시 닫는다. 그 안내의 onFinished는 호출되지 않는다.</summary>
        public void StopGuide()
        {
            EndCurrent(completed: false);
        }

        private int FindIndex(string id)
        {
            return guides.FindIndex(guide => guide != null && guide.id == id);
        }

        private IEnumerator RunGuide(GuideEntry guide)
        {
            if (guide.guideRoot != null)
            {
                // 같은 가이드가 사라지던 중이면 페이드를 끊고 원래 투명도로 되돌린 뒤 띄운다.
                StopGuideFade(guide.guideRoot);
                guide.guideRoot.SetActive(true);
            }

            guide.onStarted?.Invoke();

            // 독백 칸의 수명은 가이드가 쥔다. 대사가 먼저 끝나도 마지막 줄을 남겨 두고, 가이드가 닫힐 때 함께 닫는다
            // (MonologueView의 Hide On Finish 설정에 따라 칸만 먼저 사라지지 않게).
            bool hasMonologue = monologue != null && guide.lines != null && guide.lines.Length > 0;
            if (hasMonologue)
            {
                Color? lastColor = tintLastLine ? lastLineColor : (Color?)null;
                monologue.Play(guide.lines, hideWhenDone: false, guide.lineInterval, lastColor);
            }

            // 완료 신호를 기다리는 안내는 여기서 할 일이 없다. 대사는 MonologueView가 출력하고, 닫는 것은 CompleteGuide가 한다.
            if (guide.endMode == GuideEndMode.UntilCompleted)
            {
                guideRoutine = null;
                yield break;
            }

            // unscaled — 튜토리얼 연출로 timeScale을 바꿔도 안내가 읽힐 시간은 그대로여야 한다.
            float elapsed = 0f;
            while (elapsed < guide.duration || (guide.waitForMonologue && hasMonologue && monologue.IsPlaying))
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            guideRoutine = null;
            EndCurrent(completed: true);
        }

        // 떠 있는 안내를 닫는다. completed는 끝까지 마쳤는지 — 끊긴 안내에서 다음 단계가 이어지면 순서가 꼬인다.
        // instant는 페이드 없이 즉시 숨길지(오브젝트가 꺼지는 중이라 코루틴을 돌릴 수 없을 때).
        private void EndCurrent(bool completed, bool instant = false)
        {
            if (CurrentIndex < 0) return;

            if (guideRoutine != null) StopCoroutine(guideRoutine);
            guideRoutine = null;

            GuideEntry guide = CurrentIndex < guides.Count ? guides[CurrentIndex] : null;
            CurrentIndex = -1;

            bool fade = !instant && fadeOutDuration > 0f && isActiveAndEnabled;

            if (guide?.guideRoot != null)
            {
                if (fade && guide.guideRoot.activeInHierarchy) fadingGuides[guide.guideRoot] = StartCoroutine(FadeOutGuide(guide.guideRoot));
                else HideGuideRoot(guide.guideRoot);
            }

            if (monologue != null)
            {
                // 이 오브젝트가 꺼지는 중(OnDisable)에는 독백 칸을 SetActive로 끄지 않는다. 칸이 이 오브젝트의 자식이면
                // 부모가 비활성화되는 도중 자식을 토글하게 돼 Unity가 오류를 낸다 — 어차피 부모와 함께 꺼진다.
                if (fade) monologue.FadeOut(fadeOutDuration);
                else monologue.Stop(hide: gameObject.activeInHierarchy);
            }

            // 페이드를 기다리지 않고 바로 알린다. 다음 안내가 이전 안내가 사라지는 동안 시작돼야 흐름이 끊기지 않는다.
            if (completed) guide?.onFinished?.Invoke();
        }

        private IEnumerator FadeOutGuide(GameObject root)
        {
            guideGroups.TryGetValue(root, out CanvasGroup group);
            float startAlpha = group != null ? group.alpha : 1f;

            float elapsed = 0f;
            while (elapsed < fadeOutDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                if (group != null) group.alpha = Mathf.Lerp(startAlpha, 0f, elapsed / fadeOutDuration);
                yield return null;
            }

            fadingGuides.Remove(root);   // 자기 자신을 StopCoroutine하지 않도록 먼저 뺀다
            HideGuideRoot(root);
        }

        // 사라지던 중이면 페이드를 끊고, 원래 투명도로 되돌린다. 가이드를 다시 띄우기 직전에 부른다.
        private void StopGuideFade(GameObject root)
        {
            if (fadingGuides.TryGetValue(root, out Coroutine routine) && routine != null) StopCoroutine(routine);
            fadingGuides.Remove(root);

            RestoreGuideAlpha(root);
        }

        // 가이드를 끄고 투명도를 되돌린다. 흐린 채로 꺼두면 다음에 켜질 때 반투명으로 뜬다.
        private void HideGuideRoot(GameObject root)
        {
            if (fadingGuides.TryGetValue(root, out Coroutine routine) && routine != null) StopCoroutine(routine);
            fadingGuides.Remove(root);

            // 이 오브젝트가 꺼지는 중에 자식 가이드를 토글하면 Unity가 오류를 낸다. 자식이면 부모와 함께 꺼지므로 건너뛴다.
            bool isDeactivatingChild = !gameObject.activeInHierarchy && root.transform.IsChildOf(transform);
            if (!isDeactivatingChild) root.SetActive(false);

            RestoreGuideAlpha(root);
        }

        private void RestoreGuideAlpha(GameObject root)
        {
            if (guideGroups.TryGetValue(root, out CanvasGroup group) && guideAlphas.TryGetValue(root, out float alpha))
                group.alpha = alpha;
        }
    }
}
