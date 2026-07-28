using TMPro;
using UnityEngine;

namespace ProjectS.UI
{
    /// <summary>
    /// 퀘스트 추적 목록(Objective_List)의 한 줄. 제목·진행도 텍스트를 들고 갱신만 담당한다.
    /// 이 컴포넌트가 붙은 프리팹을 <see cref="QuestTrackerHud"/>가 진행 중 퀘스트마다 하나씩 생성한다.
    /// (기존 HUD의 제목+진행도 텍스트 묶음을 그대로 프리팹으로 만들고 여기에 연결하면 된다.)
    /// </summary>
    public class QuestTrackerEntry : MonoBehaviour
    {
        // 제목 강조색. TMP 리치 텍스트 태그로 감싸므로 titleText의 Rich Text가 켜져 있어야 한다(기본값 켜짐).
        private const string TitleColorHex = "#E5D64B";

        [SerializeField] private TMP_Text titleText;      // 퀘스트 제목
        [SerializeField] private TMP_Text progressText;   // 진행도/내용
        [SerializeField] private GameObject checkIm;      // 완료되면 나타나는 이미지

        /// <summary>제목을 설정한다(줄 생성 시 1회). 지정 강조색으로 표시한다.</summary>
        public void SetTitle(string title)
        {
            if (titleText != null) titleText.text = $"<color={TitleColorHex}>{title}</color>";
        }

        /// <summary>진행도 텍스트를 갱신한다(목표 카운트가 바뀔 때마다).</summary>
        public void SetProgress(string progress)
        {
            if (progressText != null) progressText.text = progress;
        }

        /// <summary>
        /// 퀘스트가 완료되었다면 이미지를 활성화하여 표시함
        /// </summary>
        public void SetQuestCompletedCheck(bool check)
        {
            if(checkIm != null) checkIm.SetActive(check);
        }
    }
}
