using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 퀘스트 추적 목록(QuestList)의 카드 한 장. 제목·진행 내용 텍스트와 완료 토글을 들고 갱신만 담당한다.
    /// 이 컴포넌트가 붙은 프리팹을 <see cref="QuestTrackerHud"/>가 진행 중 퀘스트마다 하나씩 생성한다.
    ///
    /// 카드 높이는 코드가 정하지 않는다. 프리팹 루트의 Vertical Layout Group + Content Size Fitter가
    /// 내용 텍스트의 줄 수에 맞춰 늘려 준다(설정은 프리팹 쪽 책임).
    /// </summary>
    public class QuestTrackerEntry : MonoBehaviour
    {
        // 제목 강조색. TMP 리치 텍스트 태그로 감싸므로 titleText의 Rich Text가 켜져 있어야 한다(기본값 켜짐).
        private const string TitleColorHex = "#E5D64B";

        [SerializeField] private TMP_Text titleText;         // 퀘스트 제목
        [SerializeField] private TMP_Text progressText;      // 진행도/내용(여러 줄이면 카드가 늘어난다)
        [SerializeField] private Toggle completedToggle;     // 완료 표시. 플레이어가 누르는 용도가 아니다

        private void Awake()
        {
            // 상태 표시 전용이라 클릭을 막는다. 눌러서 꺼버리면 표시가 실제 퀘스트 상태와 어긋난다.
            // (Toggle의 Transition을 None으로 둬야 비활성 색으로 어두워지지 않는다.)
            if (completedToggle != null) completedToggle.interactable = false;
        }

        /// <summary>제목을 설정한다(카드 생성 시 1회). 지정 강조색으로 표시한다.</summary>
        public void SetTitle(string title)
        {
            if (titleText != null) titleText.text = $"<color={TitleColorHex}>{title}</color>";
        }

        /// <summary>진행 내용 텍스트를 갱신한다(목표 카운트가 바뀔 때마다).</summary>
        public void SetProgress(string progress)
        {
            if (progressText != null) progressText.text = progress;
        }

        /// <summary>
        /// 완료(반납 대기) 여부를 토글에 반영한다.
        /// onValueChanged를 태우지 않는다 — 표시 갱신이 다른 로직을 건드리지 않게 하기 위함이다.
        /// </summary>
        /// <param name="check">true=완료 표시 켬</param>
        public void SetQuestCompletedCheck(bool check)
        {
            if (completedToggle != null) completedToggle.SetIsOnWithoutNotify(check);
        }
    }
}
