using System.Collections;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace ProjectS.UI
{
    /// <summary>
    /// 진입 흐름(로그인 → 캐릭터 선택)을 덮는 전체화면 베일. 배경 + 로고 + 스피너 + 문구를 얹은
    /// CanvasGroup 하나이고, 로그인 씬과 캐릭터 선택 씬에 각각 한 개씩 둔다.
    ///
    /// 목적은 "연출로 가리기"가 아니라 <b>판정이 끝나기 전 화면을 아예 보여주지 않는 것</b>이다.
    /// 자동 로그인은 Firebase 초기화(ReadyTask)를 기다려야 결과를 알 수 있어서, 그전까지 로그인 폼이
    /// 한 번 번쩍였다가 씬이 넘어갔다. 그래서 alpha를 인스펙터 값에 맡기지 않고 <see cref="Awake"/>에서
    /// 1로 못박는다 — Start는 이미 한 프레임 늦어 그 프레임이 그려질 수 있다.
    ///
    /// ★ 두 씬의 베일은 <b>배경색·로고·문구 위치를 같게</b> 맞춘다. 씬이 바뀌는 순간 한쪽이 사라지고
    /// 다른 쪽이 덮으며 교대하므로, 모양이 다르면 그 한 프레임에 차이가 튄다(퍼시스턴트 오버레이 없이
    /// 두 장을 이어 붙이는 이 방식의 유일한 약점이라 여기서만 관리한다).
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class EntryVeil : MonoBehaviour
    {
        [Header("표시")]
        [Tooltip("진행 단계 문구(접속 중 · 캐릭터 정보를 불러오는 중). 없어도 동작한다")]
        [SerializeField] private TMP_Text messageText;

        [Tooltip("덮고 있는 동안 계속 회전할 스피너. 없어도 동작한다")]
        [SerializeField] private Transform spinner;

        [Tooltip("스피너 회전 속도(도/초). 음수면 시계 방향")]
        [SerializeField] private float spinnerSpeed = -200f;

        [Header("페이드")]
        [Tooltip("걷을 때 걸리는 시간(초)")]
        [SerializeField, Min(0f)] private float fadeOutDuration = 0.35f;

        [Tooltip("다시 덮을 때 걸리는 시간(초). 수동 로그인 성공 → 씬 전환에 쓰인다")]
        [SerializeField, Min(0f)] private float fadeInDuration = 0.2f;

        private CanvasGroup group;
        private Coroutine fadeRoutine;
        private TaskCompletionSource<bool> fadeCompletion;

        /// <summary>지금 화면을 덮고 있는지. 페이드 중에도 덮는 방향이면 true다.</summary>
        public bool IsCovered { get; private set; }

        // 씬에서 이 오브젝트를 꺼 둔 채로 저장하면 Awake가 돌지 않는다(에디터에서 화면이 가려져
        // 잠시 꺼 두는 일이 실제로 잦다). 그 상태로 호출부가 HideAsync를 부르면 캐시가 비어
        // NullReferenceException이 나므로, 필요한 순간에 채우는 방식으로 둔다.
        private CanvasGroup Group => group != null ? group : (group = GetComponent<CanvasGroup>());

        private void Awake() => CoverImmediate();

        private void Update()
        {
            // 로딩 중에는 timeScale이 0일 수도 있으므로 unscaled로 돌린다(멈춘 스피너는 프리즈로 보인다).
            if (spinner != null) spinner.Rotate(0f, 0f, spinnerSpeed * Time.unscaledDeltaTime);
        }

        // 파괴되는 순간에도 기다리던 쪽(await)이 영원히 멈추지 않게 Task를 닫아 준다.
        // 씬 전환으로 베일이 사라지는 동안 HideAsync를 await하고 있으면 그 이후 코드가 통째로 죽는다.
        private void OnDestroy() => CompleteFade();

        /// <summary>문구를 갈아 끼운다. 단계가 바뀔 때마다 불러 "무엇을 기다리는지"를 보여준다.</summary>
        /// <param name="message">표시할 문구. null이면 건드리지 않는다</param>
        public void SetMessage(string message)
        {
            if (message != null && messageText != null) messageText.text = message;
        }

        /// <summary>
        /// 페이드 없이 즉시 덮는다. 씬을 떠나기 직전처럼 한 프레임의 틈도 주면 안 될 때 쓴다.
        /// </summary>
        public void CoverImmediate()
        {
            StopFade();

            // 씬에서 꺼진 채 저장돼 있어도 여기서 되살린다. 꺼져 있으면 이 컴포넌트의 Awake가 아예 돌지 않아
            // 아무것도 덮지 못하는데, 에러도 예외도 없이 "예전처럼 폼이 번쩍이는" 상태로 조용히 돌아가
            // 원인을 코드에서 찾게 된다. 켜 두는 것을 사람이 기억해야 하는 구조를 만들지 않는다.
            // (여기서 켜면 Awake가 그 자리에서 실행되며 이 메서드를 한 번 더 부르지만, 같은 값을 다시
            //  쓸 뿐이라 안전하다.)
            if (!gameObject.activeSelf) gameObject.SetActive(true);

            IsCovered = true;
            Group.alpha = 1f;
            Group.blocksRaycasts = true;

            // 부모가 꺼져 있으면 자기만 켜서는 화면에 나오지 않는다. 남의 오브젝트를 마음대로 켜면
            // 그쪽 의도를 깨뜨리므로 고치지 않고 알리기만 한다.
            if (!gameObject.activeInHierarchy)
                Debug.LogWarning("[EntryVeil] 부모 오브젝트가 꺼져 있어 베일이 화면에 나오지 않습니다. 부모를 켜 주세요.", this);
        }

        /// <summary>
        /// 서서히 덮는다(수동 로그인 성공 → 캐릭터 선택 전환 등). 페이드가 끝나면 Task가 완료된다.
        /// 입력 차단은 페이드 <b>시작</b>과 함께 걸어, 전환되는 사이 버튼이 다시 눌리는 것을 막는다.
        /// </summary>
        /// <param name="message">함께 갈아 끼울 문구(선택)</param>
        public Task CoverAsync(string message = null)
        {
            SetMessage(message);
            gameObject.SetActive(true);
            IsCovered = true;
            Group.blocksRaycasts = true;
            return RunFade(1f, fadeInDuration);
        }

        /// <summary>
        /// 서서히 걷는다. 다 걷히면 입력 차단을 풀고 오브젝트를 끈다(스피너 Update까지 멈추게).
        /// </summary>
        public Task HideAsync()
        {
            IsCovered = false;
            return RunFade(0f, fadeOutDuration);
        }

        private Task RunFade(float targetAlpha, float duration)
        {
            StopFade();

            // 꺼져 있으면 코루틴을 못 돌린다(StartCoroutine이 예외). 값만 맞추고 즉시 끝낸다.
            if (!gameObject.activeInHierarchy || duration <= 0f)
            {
                ApplyEnd(targetAlpha);
                return Task.CompletedTask;
            }

            fadeCompletion = new TaskCompletionSource<bool>();
            fadeRoutine = StartCoroutine(FadeRoutine(targetAlpha, duration));
            return fadeCompletion.Task;
        }

        private IEnumerator FadeRoutine(float targetAlpha, float duration)
        {
            float from = Group.alpha;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                Group.alpha = Mathf.Lerp(from, targetAlpha, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            fadeRoutine = null;
            ApplyEnd(targetAlpha);
            CompleteFade();
        }

        // 페이드 끝 상태를 확정한다. 걷힌 경우에만 입력 차단을 풀고 오브젝트를 끈다.
        private void ApplyEnd(float targetAlpha)
        {
            Group.alpha = targetAlpha;

            if (targetAlpha <= 0f)
            {
                Group.blocksRaycasts = false;
                gameObject.SetActive(false);
            }
        }

        private void StopFade()
        {
            if (fadeRoutine != null)
            {
                StopCoroutine(fadeRoutine);
                fadeRoutine = null;
            }

            CompleteFade();   // 중간에 끊긴 페이드를 기다리던 쪽도 풀어 준다
        }

        private void CompleteFade()
        {
            if (fadeCompletion == null) return;

            TaskCompletionSource<bool> completion = fadeCompletion;
            fadeCompletion = null;
            completion.TrySetResult(true);
        }
    }
}
