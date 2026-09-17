using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ProjectS.Tutorials
{
    /// <summary>
    /// 화면 상단 독백 칸에 주인공의 독백을 한 글자씩 찍어 보여주는 뷰. 튜토리얼에서 플레이어의 목적을
    /// 대화창 없이 알려 줄 때 쓴다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>문자열을 한 글자씩 이어 붙이지 않고 <see cref="TMP_Text.maxVisibleCharacters"/>를 늘린다.</b>
    /// 독백 칸은 Content Size Fitter로 글 길이를 따라가는데, 이어 붙이면 글자가 늘 때마다 칸이 매 프레임
    /// 커지며 떨린다. 전체 문장을 먼저 넣으면 크기는 처음부터 최종 크기로 잡히고 보이는 글자 수만 는다.
    /// 리치 텍스트 태그(&lt;color&gt; 등)도 중간에 잘린 채 노출되지 않는다.
    /// </para>
    /// <para>
    /// <b>입력을 직접 읽지 않는다.</b> 튜토리얼 독백은 플레이어가 WASD로 움직이는 동안 뜨므로 넘기기 키를
    /// 두면 조작과 겹친다. 넘기기가 필요하면 바깥에서 <see cref="RevealAll"/>/<see cref="Stop"/>을 부른다
    /// (게임플레이 입력은 PlayerInputHandler만 읽는다는 규칙과도 맞춘다).
    /// </para>
    /// <para>
    /// <b>시간은 unscaled로 센다.</b> 튜토리얼 연출 중 timeScale을 늦추거나 멈춰도 독백은 읽혀야 한다.
    /// </para>
    /// <para>
    /// 붙이는 곳: 항상 켜져 있는 UI 오브젝트에 붙이고 <see cref="root"/>에 독백 칸을 연결하는 것을 권장한다.
    /// root를 비워 자기 자신을 켜고 끄게 해도 동작하지만, 그 경우 <see cref="Hide"/>가 재생 코루틴까지 끊는다.
    /// (2026-09-17 TH)
    /// </para>
    /// </remarks>
    public class MonologueView : MonoBehaviour
    {
        [Header("UI")]
        [Tooltip("켜고 끌 독백 칸. 비우면 이 오브젝트 자신을 켜고 끈다.")]
        [SerializeField] private GameObject root;

        [SerializeField] private TMP_Text monologueText;

        [Tooltip("선택. Content Size Fitter가 붙은 독백 칸. 연결하면 문장을 넣는 즉시 크기를 다시 계산해 " +
                 "이전 문장 크기로 한 프레임 보이는 것을 막는다.")]
        [SerializeField] private RectTransform box;

        [Header("타이핑")]
        [Tooltip("초당 찍히는 글자 수.")]
        [SerializeField, Min(1f)] private float charactersPerSecond = 20f;

        [Tooltip("이 문자를 찍은 뒤 잠깐 멈춘다. 독백의 호흡을 만드는 연출값이다.")]
        [SerializeField] private string pauseCharacters = ",.!?…";

        [Tooltip("위 문자 뒤에 추가로 멈추는 시간(초).")]
        [SerializeField, Min(0f)] private float punctuationPause = 0.25f;

        [Header("진행")]
        [Tooltip("한 줄을 다 찍은 뒤 다음 줄로 넘어가기 전까지 머무는 시간(초). 마지막 줄 뒤에는 칸을 숨길 때만 기다린다.")]
        [SerializeField, Min(0f)] private float holdDuration = 2f;

        [Tooltip("모든 줄이 끝나면 독백 칸을 숨긴다. 끄면 마지막 줄이 남아 있다(목표를 계속 띄워 둘 때).")]
        [SerializeField] private bool hideOnFinish = true;

        [Tooltip("PlayConfigured로 재생할 줄들. 트리거의 UnityEvent에서 인자 없이 부를 때 쓴다.")]
        [SerializeField, TextArea(1, 3)] private string[] lines;

        [Header("이벤트")]
        [Tooltip("모든 줄이 끝났을 때 1회. Stop으로 끊기면 호출되지 않는다.")]
        [SerializeField] private UnityEvent onFinished = new UnityEvent();

        /// <summary>모든 줄을 끝까지 재생했을 때 발행된다. <see cref="Stop"/>으로 끊기면 발행되지 않는다.</summary>
        public event Action Finished;

        /// <summary>독백을 재생 중인지(타이핑 중이거나 줄 사이에 머무는 중).</summary>
        public bool IsPlaying => playRoutine != null;

        /// <summary>지금 줄을 한 글자씩 찍는 중인지.</summary>
        public bool IsTyping { get; private set; }

        private Coroutine playRoutine;
        private Coroutine fadeRoutine;

        // RevealAll 요청. 타이핑 루프가 다음 프레임에 보고 남은 글자를 한 번에 연다.
        private bool revealRequested;

        // 서서히 사라질 때 투명도를 조절할 그룹. 칸 전체(배경·테두리·글자)를 한 번에 흐리게 하려고 root에 둔다.
        private CanvasGroup rootGroup;

        // 텍스트의 원래 색. 마지막 줄만 색을 바꾼 뒤 다음 재생에서 되돌리는 기준이다.
        private Color defaultTextColor = Color.white;

        private void Awake()
        {
            if (root == null) root = gameObject;

            // 없으면 붙인다 — 프리팹마다 CanvasGroup을 챙겨 달지 않아도 페이드가 동작하게.
            if (!root.TryGetComponent(out rootGroup)) rootGroup = root.AddComponent<CanvasGroup>();

            if (monologueText == null)
            {
                Debug.LogWarning($"[MonologueView] {name}: monologueText가 비어 있어 독백을 표시할 수 없다.", this);
            }
            else
            {
                defaultTextColor = monologueText.color;
            }

            // 씬에 켜둔 채 저장돼 있어도 시작은 숨김이다. 독백이 요청되기 전에 빈 칸이 떠 있으면 안 된다.
            Hide();
        }

        private void OnDisable()
        {
            // 오브젝트가 꺼지면 코루틴도 멈춘다. 핸들을 비워 두지 않으면 IsPlaying이 영영 true로 남는다.
            playRoutine = null;
            IsTyping = false;

            // 페이드 도중 꺼지면 반투명으로 굳는다. 다음에 켜질 때 흐린 칸이 뜨지 않게 되돌린다.
            fadeRoutine = null;
            if (rootGroup != null) rootGroup.alpha = 1f;
        }

        /// <summary>한 줄짜리 독백을 재생한다. UnityEvent에서 문자열 인자로 바로 연결할 수 있다.</summary>
        /// <param name="line">보여줄 문장. 리치 텍스트 태그를 써도 된다</param>
        public void Play(string line)
        {
            Play(new[] { line });
        }

        /// <summary>
        /// 여러 줄을 순서대로 재생한다. 재생 중에 부르면 이전 독백을 끊고 새로 시작한다
        /// (이전 독백의 완료 이벤트는 발행되지 않는다).
        /// </summary>
        /// <param name="sequence">순서대로 보여줄 문장들</param>
        public void Play(string[] sequence)
        {
            Play(sequence, hideOnFinish);
        }

        /// <summary>
        /// 여러 줄을 재생하되, 다 끝난 뒤 칸을 숨길지를 이번 재생에 한해 정한다. 인스펙터의 Hide On Finish를 무시한다.
        /// </summary>
        /// <remarks>
        /// 독백 칸을 다른 UI(튜토리얼 가이드 등)와 함께 닫아야 하는 쪽이 부른다. 칸의 수명을 그쪽이 쥐는데
        /// 인스펙터 설정에 따라 대사만 먼저 사라지면, 가이드는 남아 있는데 독백 칸만 비는 어긋남이 생긴다.
        /// </remarks>
        /// <param name="sequence">순서대로 보여줄 문장들</param>
        /// <param name="hideWhenDone">false면 마지막 줄을 남겨 두고, 숨기는 것은 호출한 쪽이 <see cref="Stop"/>으로 한다</param>
        /// <param name="lineInterval">줄을 다 찍은 뒤 다음 줄까지 기다리는 시간(초). 음수면 인스펙터의 Hold Duration을 쓴다</param>
        /// <param name="lastLineColor">마지막 줄에만 쓸 글자 색. null이면 모든 줄이 원래 색이다</param>
        public void Play(string[] sequence, bool hideWhenDone, float lineInterval = -1f, Color? lastLineColor = null)
        {
            if (monologueText == null || sequence == null || sequence.Length == 0) return;

            Stop(hide: false);

            // 꺼진 오브젝트에서는 코루틴을 시작할 수 없으므로 먼저 켠다.
            if (root != null) root.SetActive(true);
            if (!isActiveAndEnabled)
            {
                Debug.LogWarning($"[MonologueView] {name}: 이 컴포넌트가 비활성이라 독백을 재생할 수 없다.", this);
                return;
            }

            float hold = lineInterval >= 0f ? lineInterval : holdDuration;
            playRoutine = StartCoroutine(PlayRoutine(sequence, hideWhenDone, hold, lastLineColor));
        }

        /// <summary>
        /// 독백 칸을 서서히 흐리게 한 뒤 숨긴다. 타이핑 중이면 그 자리에서 멈춘 채 흐려진다.
        /// 완료 이벤트는 발행하지 않는다(<see cref="Stop"/>과 같은 취급).
        /// </summary>
        /// <param name="duration">흐려지는 시간(초). 0 이하면 즉시 숨긴다</param>
        public void FadeOut(float duration)
        {
            Stop(hide: false);

            if (duration <= 0f || !isActiveAndEnabled || root == null || !root.activeInHierarchy)
            {
                Hide();
                return;
            }

            fadeRoutine = StartCoroutine(FadeOutRoutine(duration));
        }

        /// <summary>인스펙터의 <see cref="lines"/>를 재생한다. 트리거의 인자 없는 UnityEvent에 연결할 때 쓴다.</summary>
        public void PlayConfigured()
        {
            Play(lines);
        }

        /// <summary>
        /// 지금 찍는 줄의 남은 글자를 한 번에 보여준다. 줄 사이 대기는 그대로 이어진다.
        /// 타이핑 중이 아니면 아무 일도 하지 않는다.
        /// </summary>
        public void RevealAll()
        {
            if (IsTyping) revealRequested = true;
        }

        /// <summary>재생을 끊는다. 완료 이벤트는 발행하지 않는다.</summary>
        /// <param name="hide">독백 칸까지 숨길지</param>
        public void Stop(bool hide = true)
        {
            if (playRoutine != null) StopCoroutine(playRoutine);
            if (fadeRoutine != null) StopCoroutine(fadeRoutine);

            playRoutine = null;
            fadeRoutine = null;
            IsTyping = false;
            revealRequested = false;

            // 페이드 중에 멈추거나 새로 재생하면 흐린 상태로 남지 않게 되돌린다.
            if (rootGroup != null) rootGroup.alpha = 1f;

            if (hide) Hide();
        }

        /// <summary>독백 칸을 숨긴다. 재생 중이면 root 구성에 따라 재생도 함께 멈출 수 있다.</summary>
        public void Hide()
        {
            if (root != null) root.SetActive(false);
        }

        private IEnumerator PlayRoutine(string[] sequence, bool hideWhenDone, float hold, Color? lastLineColor)
        {
            for (int i = 0; i < sequence.Length; i++)
            {
                bool isLast = i == sequence.Length - 1;

                // 마지막 줄만 지정 색으로 찍는다. 다른 줄은 원래 색 — 이전 재생의 마지막 줄 색이 남아 있을 수 있어 매번 정한다.
                monologueText.color = isLast && lastLineColor.HasValue ? lastLineColor.Value : defaultTextColor;

                yield return TypeLine(sequence[i]);

                // 마지막 줄 뒤의 대기는 칸을 숨길 때만 의미가 있다(읽을 시간). 남겨 두는 경우엔 기다릴 이유가 없고,
                // 기다리면 완료 판정(IsPlaying/Finished)만 늦어진다.
                if (isLast && !hideWhenDone) break;

                float held = 0f;
                while (held < hold)
                {
                    held += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            playRoutine = null;
            if (hideWhenDone) Hide();

            onFinished?.Invoke();
            Finished?.Invoke();
        }

        private IEnumerator FadeOutRoutine(float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                rootGroup.alpha = 1f - Mathf.Clamp01(elapsed / duration);
                yield return null;
            }

            fadeRoutine = null;
            Hide();

            // 숨긴 뒤 원래대로 돌려 둔다. 다음 재생은 불투명하게 시작해야 한다.
            rootGroup.alpha = 1f;
        }

        private IEnumerator TypeLine(string line)
        {
            IsTyping = true;
            revealRequested = false;

            // 전체 문장을 먼저 넣어 칸 크기를 최종 크기로 확정하고, 보이는 글자만 0부터 늘린다.
            monologueText.text = line ?? string.Empty;
            monologueText.maxVisibleCharacters = 0;

            // textInfo는 메시 갱신 전까지 이전 문장 기준이다. 글자 수(태그 제외)를 읽기 전에 강제로 갱신한다.
            monologueText.ForceMeshUpdate();
            if (box != null) LayoutRebuilder.ForceRebuildLayoutImmediate(box);

            TMP_TextInfo info = monologueText.textInfo;
            int total = info.characterCount;
            float interval = 1f / charactersPerSecond;

            int visible = 0;
            float timer = 0f;

            // 프레임당 한 글자가 아니라 시간 누적으로 연다. 초당 글자 수가 프레임률보다 높아도 속도가 지켜진다.
            while (visible < total)
            {
                if (revealRequested) break;

                timer += Time.unscaledDeltaTime;
                while (timer >= interval && visible < total)
                {
                    timer -= interval;
                    visible++;

                    // 쉼표·마침표 뒤에는 타이머를 음수로 끌어내려 자연스럽게 멈춘다. 마지막 글자 뒤는 holdDuration이 맡는다.
                    if (visible < total && IsPauseCharacter(info.characterInfo[visible - 1].character))
                    {
                        timer -= punctuationPause;
                    }
                }

                monologueText.maxVisibleCharacters = visible;
                yield return null;
            }

            monologueText.maxVisibleCharacters = total;
            revealRequested = false;
            IsTyping = false;
        }

        private bool IsPauseCharacter(char character)
        {
            return !string.IsNullOrEmpty(pauseCharacters) && pauseCharacters.IndexOf(character) >= 0;
        }
    }
}
