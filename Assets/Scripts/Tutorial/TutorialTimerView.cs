using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.Tutorials
{
    /// <summary>
    /// 튜토리얼 제한시간 표시. 값을 받아 그리기만 하는 단순한 뷰다 —
    /// 카운트다운과 성공/실패 판정은 <see cref="TutorialRunTrial"/>이 한다.
    ///
    /// 지금은 텍스트만 쓰는 최소 구성이다. <see cref="fillImage"/>는 비워두면 무시되므로,
    /// 나중에 UI 담당이 게이지 바를 만들어 꽂기만 하면 코드 수정 없이 동작한다.
    /// (UI를 기다리느라 로직 작업이 막히지 않게 하려는 구조다.)
    /// </summary>
    public class TutorialTimerView : MonoBehaviour
    {
        [Tooltip("켜고 끌 대상. 비우면 이 오브젝트 자신을 켜고 끈다.")]
        [SerializeField] private GameObject root;

        [SerializeField] private TMP_Text timeText;

        [Tooltip("선택. 나중에 게이지 바를 추가하면 여기 연결한다(Image Type을 Filled로).")]
        [SerializeField] private Image fillImage;

        [Header("표시")]
        [Tooltip("남은 시간 표기 형식. \"0.0\"이면 6.3처럼, \"0\"이면 6처럼 나온다.")]
        [SerializeField] private string numberFormat = "0.0";

        [SerializeField] private Color normalColor = Color.white;

        [Tooltip("남은 시간이 촉박할 때 색. 긴장감을 주려는 연출값이다.")]
        [SerializeField] private Color urgentColor = new Color(1f, 0.35f, 0.3f);

        [SerializeField, Min(0f)] private float urgentThreshold = 3f;

        private void Awake()
        {
            if (root == null) root = gameObject;

            // 씬에 켜둔 채로 저장돼 있어도 시작은 항상 숨김이다.
            // 도전 전에는 화면에 타이머가 떠 있으면 안 된다.
            Hide();
        }

        /// <summary>타이머를 화면에 띄운다. 도전 시작 시 호출된다.</summary>
        public void Show()
        {
            if (root != null) root.SetActive(true);
        }

        /// <summary>타이머를 숨긴다. 성공·실패로 도전이 끝날 때 호출된다.</summary>
        public void Hide()
        {
            if (root != null) root.SetActive(false);
        }

        /// <summary>남은 시간을 갱신한다.</summary>
        /// <param name="remaining">남은 초. 음수는 0으로 보정된다.</param>
        /// <param name="duration">전체 제한시간(게이지 비율 계산용). 0 이하면 게이지는 갱신하지 않는다.</param>
        public void SetRemaining(float remaining, float duration)
        {
            remaining = Mathf.Max(0f, remaining);

            if (timeText != null)
            {
                timeText.text = remaining.ToString(numberFormat);
                timeText.color = remaining <= urgentThreshold ? urgentColor : normalColor;
            }

            // 게이지는 선택 사항이라 없으면 조용히 건너뛴다(지금은 텍스트만 쓰는 구성).
            if (fillImage != null && duration > 0f)
                fillImage.fillAmount = Mathf.Clamp01(remaining / duration);
        }
    }
}
