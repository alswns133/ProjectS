using TMPro;
using UnityEngine;

namespace ProjectS.UI
{
    /// <summary>
    /// 레이드 등장 연출 전 <b>파티원 대기 화면</b>. 전체를 덮는 검은 배경 가운데에 원형 로딩과 준비 현황("1/2")을 띄운다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 있나.</b> 로딩 화면이 닫히고 씬이 뜨는 사이, 파티원이 아직 도착하지 않아 연출이 시작되기 전까지 화면이
    /// 붕 뜬 채로 보인다. 그 틈을 가리고 "누구를 기다리는 중인지"를 숫자로 알려 준다.
    /// </para>
    /// <para>
    /// <b>표시/숨김은 GameObject가 아니라 알파로 한다.</b> 오브젝트를 끄면 같은 오브젝트의 Presenter가 구독을 해제해
    /// 다음 대기 신호를 못 받는다(<c>ToastNotice</c>에 기록된 같은 함정). 그래서 루트는 항상 켜 두고 알파와 레이캐스트만 바꾼다.
    /// </para>
    /// <para>
    /// <b>★ 사라질 때 페이드하지 않고 즉시 알파 0으로 만든다(2026-09-17).</b> 연출이 시작되는 같은 프레임에
    /// <c>BossIntroDirector</c>가 UIManager 전체를 꺼 버려, 페이드가 알파 1 근처에서 멈춘 채 얼어붙는다. 그러면 연출이
    /// 끝나 UIManager가 다시 켜질 때 <b>검은 대기 화면이 다시 드러난다.</b> 기획도 "연출 시작 = 대기 화면 즉시 사라짐"이다.
    /// </para>
    /// <para>
    /// <b>정렬 순서.</b> 로딩 화면(100)보다 아래, 나머지 UI보다 위(생성 툴 기본 90). 로딩 중에 미리 깔려 있다가
    /// 로딩이 걷히는 순간 이미 덮고 있어야 빈 프레임이 안 보인다. 생성: Tools ▸ ProjectS ▸ Create Raid Wait Screen.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(CanvasGroup))]
    public class RaidWaitView : MonoBehaviour
    {
        [Tooltip("회전시킬 원형 로딩 호(arc). 트랙·구멍은 돌리지 않는다.")]
        [SerializeField] private RectTransform spinnerArc;

        [Tooltip("대기 안내 문구(무엇을 기다리는지). 없어도 동작한다.")]
        [SerializeField] private TMP_Text messageText;

        [Tooltip("준비 현황 텍스트(준비 수/총원).")]
        [SerializeField] private TMP_Text countText;

        [Header("문구")]
        [Tooltip("대기 중 보여 줄 안내. 숫자만 있으면 무엇을 기다리는지 읽히지 않아 한 줄을 함께 띄운다.")]
        [SerializeField, TextArea] private string waitingMessage = "파티원이 모두 도착하기를 기다리는 중입니다";

        [Tooltip("현황 표기 형식. {0}=준비된 수, {1}=총원.")]
        [SerializeField] private string countFormat = "{0} / {1} 준비 완료";

        [Tooltip("로딩 호의 회전 속도(도/초).")]
        [SerializeField, Min(0f)] private float spinDegreesPerSecond = 300f;

        private CanvasGroup group;

        // 지연 표시 예약 시각(unscaled). 음수면 예약 없음. 싱글처럼 곧바로 끝날 수 있는 대기를 번쩍이지 않게 한다.
        private float showAt = -1f;
        private bool visible;

        private void Awake()
        {
            group = GetComponent<CanvasGroup>();
            ApplyHidden();
        }

        private void OnEnable()
        {
            // 부모(UIManager)가 꺼졌다 켜진 경우의 보강: 대기 중이 아니면 어떤 알파로 남아 있었든 확실히 숨긴다.
            if (group != null && !visible) ApplyHidden();
        }

        /// <summary>대기를 시작한다. <paramref name="showDelay"/>가 지나기 전에 <see cref="EndWait"/>가 오면 화면을 띄우지 않는다.</summary>
        /// <param name="showDelay">화면을 띄우기까지의 지연(초). 0이면 즉시.</param>
        public void BeginWait(float showDelay)
        {
            if (showDelay <= 0f) ShowNow();
            else if (!visible) showAt = Time.unscaledTime + showDelay;
        }

        /// <summary>준비 현황을 갱신한다.</summary>
        /// <param name="ready">준비된 파티원 수.</param>
        /// <param name="total">접속 중인 파티원 수.</param>
        public void SetCount(int ready, int total)
        {
            if (countText != null) countText.text = string.Format(countFormat, ready, total);
        }

        /// <summary>안내 문구를 갈아 끼운다. 비우면 인스펙터의 기본 문구를 그대로 쓴다.</summary>
        /// <param name="message">표시할 문구.</param>
        public void SetMessage(string message)
        {
            if (messageText != null && !string.IsNullOrEmpty(message)) messageText.text = message;
        }

        /// <summary>대기를 끝낸다. 예약된 표시를 취소하고 <b>즉시</b> 알파 0으로 숨긴다.</summary>
        public void EndWait()
        {
            showAt = -1f;
            visible = false;
            ApplyHidden();
        }

        private void ShowNow()
        {
            showAt = -1f;
            visible = true;

            // 문구는 띄우는 시점에 채운다. 비활성 프리팹 상태의 값에 기대지 않기 위함이다.
            if (messageText != null && !string.IsNullOrEmpty(waitingMessage)) messageText.text = waitingMessage;

            group.alpha = 1f;
            group.blocksRaycasts = true;   // 대기 중 뒤의 UI가 눌리지 않게 막는다
        }

        private void ApplyHidden()
        {
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
        }

        private void Update()
        {
            // 대기 신호가 로딩·일시정지와 무관하게 흐르도록 unscaled 시간으로 계산한다.
            if (showAt >= 0f && Time.unscaledTime >= showAt) ShowNow();

            if (visible && spinnerArc != null)
                spinnerArc.Rotate(0f, 0f, -spinDegreesPerSecond * Time.unscaledDeltaTime);
        }
    }
}
