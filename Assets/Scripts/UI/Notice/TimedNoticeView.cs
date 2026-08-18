using System.Collections;
using UnityEngine;

namespace ProjectS.UI
{
    /// <summary>
    /// 떴다가 정해진 시간 뒤 스스로 사라지는 화면 알림의 공통 뼈대.
    /// 페이드 인 → 유지(<see cref="HoldSeconds"/>) → 페이드 아웃 → 자동 비활성 순으로 한 번 재생된다.
    /// 표시할 내용은 파생 클래스가 채우고, 이 클래스는 <b>타이밍과 보이기/숨기기만</b> 맡는다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>BasePopup을 상속하지 않는 이유 (2026.08.12 태하).</b>
    /// <see cref="ProjectS.Managers.UIManager.Back"/>은 뒤로가기(ESC)를 받으면 <b>열려 있는 팝업부터</b> 닫는다.
    /// 이 알림을 팝업으로 등록하면 레벨업 직후 5초 동안 ESC가 알림에게 먹혀 메뉴가 열리지 않는다.
    /// <c>CanCloseByBack = false</c>로 막으면 더 나쁘다 — <c>Back()</c>이 그 자리에서 반환해 버려
    /// 그 5초간 ESC가 <b>아무 일도 하지 않는다</b>.
    /// 기획서(4장)의 레벨업·스킬 해금 알림에는 버튼이 없다 = 입력을 받지 않는다 = 패널 스택·포커스 관리
    /// 대상이 아니다. 그래서 <c>QuestTrackerHud</c>·<c>HpVignette</c>처럼 HUD에 얹는 평범한 뷰로 둔다.
    /// </para>
    /// <para>
    /// <b>시간을 unscaled로 세는 이유</b>: 레벨업은 처치 연출·컷신처럼 <c>timeScale</c>이 낮아진 중에도 뜬다.
    /// 스케일을 따라가면 기획서가 정한 "5초 뒤 자동 소멸"이 체감상 한참 늘어진다.
    /// </para>
    /// <para>
    /// <b>이 오브젝트는 씬에서 켜 둬도 되고 꺼 둬도 된다.</b> 켜 두면 <see cref="Awake"/>가 알파를 0으로
    /// 내려 안 보이는 상태로 시작하고, 꺼 뒀으면 첫 <see cref="Play"/>가 켜면서 <c>Awake</c>가 돈다.
    /// 다만 <b>알파로만 숨기지 말고 반드시 <see cref="Play"/>를 통해 재생</b>해야 한다 —
    /// 꺼진 오브젝트에서는 코루틴이 시작되지 않으므로 <see cref="Play"/>가 켜기를 먼저 한다.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class TimedNoticeView : MonoBehaviour
    {
        [Header("타이밍")]
        [Tooltip("나타나는 데 걸리는 시간(초). 0이면 즉시 나타난다.")]
        [SerializeField, Min(0f)] private float fadeInSeconds = 0.25f;

        [Tooltip("완전히 보인 채 머무는 시간(초). 기획서 4장 기준 5초.")]
        [SerializeField, Min(0f)] private float holdSeconds = 4f;

        [Tooltip("사라지는 데 걸리는 시간(초). 0이면 즉시 사라진다.")]
        [SerializeField, Min(0f)] private float fadeOutSeconds = 0.5f;

        private CanvasGroup group;
        private Coroutine routine;

        /// <summary>지금 재생 중인지. 큐를 돌리는 파생 클래스가 "이어서 틀어도 되는가"를 판단하는 데 쓴다.</summary>
        public bool IsPlaying => routine != null;

        /// <summary>인스펙터에 설정된 유지 시간(초).</summary>
        protected float HoldSeconds => holdSeconds;

        /// <summary>
        /// 이번 재생에 쓸 유지 시간(초). 재생을 시작할 때 한 번만 평가한다.
        /// 기본은 인스펙터 값 그대로이고, 대기열을 쓰는 파생 클래스가 상황에 따라 줄이려고 재정의한다.
        /// </summary>
        /// <remarks>
        /// 재생 도중 값이 바뀌어도 이미 시작된 알림에는 반영되지 않는다 — 유지 시간이 재생 중에
        /// 늘었다 줄었다 하면 언제 사라질지 예측할 수 없어진다.
        /// </remarks>
        /// <returns>이번 재생에서 완전히 보인 채 머물 시간(초)</returns>
        protected virtual float ResolveHoldSeconds() => holdSeconds;

        /// <remarks>
        /// 알림은 입력을 받지 않는다. <c>blocksRaycasts</c>를 켜 두면 화면을 덮는 알림이 그 5초 동안
        /// 아래 HUD 버튼(스킬 슬롯·퀵슬롯) 클릭을 삼킨다.
        /// </remarks>
        protected virtual void Awake()
        {
            group = GetComponent<CanvasGroup>();

            group.interactable = false;
            group.blocksRaycasts = false;
            group.alpha = 0f;
        }

        /// <remarks>
        /// HUD가 통째로 꺼지면(씬 전환·패널 교체) 코루틴은 조용히 죽는데 <see cref="routine"/> 참조는 남는다.
        /// 그대로 두면 <see cref="IsPlaying"/>이 영원히 true가 되어, 큐를 쓰는 파생 클래스의 대기열이 멈춘다.
        /// </remarks>
        protected virtual void OnDisable()
        {
            routine = null;
            if (group != null) group.alpha = 0f;
        }

        /// <summary>
        /// 알림을 처음부터 재생한다. 이미 재생 중이면 <b>중단하고 다시 시작</b>한다
        /// (연속 레벨업처럼 같은 알림이 겹쳐 들어올 때 최신 내용을 보여주기 위함).
        /// 표시 내용은 이 호출 <b>전에</b> 파생 클래스가 채워 둔다.
        /// </summary>
        protected void Play()
        {
            // 꺼져 있으면 켜야 코루틴이 돈다. SetActive(true)는 Awake를 그 자리에서 동기 실행하므로
            // 아래 group 참조는 항상 준비된 상태다(첫 재생이 곧 Awake인 경우 포함).
            if (!gameObject.activeSelf) gameObject.SetActive(true);

            // 컴포넌트 자체가 꺼져 있으면 코루틴을 시작할 수 없다. 조용히 사라지면 원인을 찾기 어려워 알린다.
            if (!isActiveAndEnabled)
            {
                Debug.LogWarning($"{name}: 컴포넌트가 비활성이라 알림을 재생하지 못했다.", this);
                return;
            }

            if (group == null) group = GetComponent<CanvasGroup>();

            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(PlayRoutine());
        }

        /// <summary>
        /// 재생 중인 알림을 즉시 끝낸다(연출 없이). 씬 전환·사망처럼 화면이 통째로 바뀔 때 호출한다.
        /// </summary>
        /// <remarks>완료 훅(<see cref="OnNoticeFinished"/>)은 부르지 않는다 — 중단이지 정상 종료가 아니다.</remarks>
        public void Dismiss()
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;

            if (group != null) group.alpha = 0f;
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 한 번의 재생이 정상적으로 끝났을 때 불린다. 대기 중인 다음 알림을 이어 재생하는 데 쓴다.
        /// </summary>
        protected virtual void OnNoticeFinished() { }

        private IEnumerator PlayRoutine()
        {
            yield return Fade(0f, 1f, fadeInSeconds);

            // 페이드 인이 끝난 시점에 한 번만 평가한다. 대기열이 남았는지에 따라 파생 클래스가 줄일 수 있다.
            float remain = ResolveHoldSeconds();
            while (remain > 0f)
            {
                remain -= Time.unscaledDeltaTime;
                yield return null;
            }

            yield return Fade(1f, 0f, fadeOutSeconds);

            Finish();
        }

        // 끄기를 완료 훅보다 먼저 하는 이유: 훅에서 다음 알림을 이어 재생하면 Play()가 오브젝트를 다시 켜는데,
        // 끄기가 나중에 오면 방금 켠 것을 도로 꺼 버린다.
        private void Finish()
        {
            routine = null;

            group.alpha = 0f;
            gameObject.SetActive(false);

            OnNoticeFinished();
        }

        private IEnumerator Fade(float from, float to, float duration)
        {
            if (duration <= 0f)
            {
                group.alpha = to;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(from, to, elapsed / duration);
                yield return null;
            }

            group.alpha = to;
        }
    }
}
