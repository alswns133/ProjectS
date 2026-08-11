using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 캐릭터 선택 페이지의 화면 묶음. 슬롯 6칸과 하단 버튼들의 참조를 들고,
    /// "지금 몇 번 카드가 선택됐는가"만 시각적으로 반영한다.
    ///
    /// 로스터 로드·생성·삭제 같은 서버 동작은 알지 못한다. 상위 컨트롤러가
    /// <see cref="Slots"/>의 이벤트를 구독하고, 데이터를 채운 뒤 <see cref="SetSelectedIndex"/>로
    /// 선택을 지시한다. 슬롯은 런타임에 만들지 않고 씬에 6칸 고정 배치다.
    /// </summary>
    public class CharacterSelectPageView : MonoBehaviour
    {
        [Header("모델 프리뷰")]
        [SerializeField] private RawImage modelViewport;

        [Header("슬롯 (씬 고정 6칸)")]
        [SerializeField] private CharacterSlotView[] slots;

        [Header("하단 바")]
        [SerializeField] private Button quitButton;
        [SerializeField] private Button optionButton;
        [SerializeField] private TMP_Text versionText;

        /// <summary>슬롯 카드 목록. 인덱스가 곧 슬롯 번호다.</summary>
        public IReadOnlyList<CharacterSlotView> Slots => slots;

        /// <summary>게임 종료 버튼.</summary>
        public Button QuitButton => quitButton;

        /// <summary>환경설정 버튼.</summary>
        public Button OptionButton => optionButton;

        /// <summary>모델 프리뷰가 그려지는 RawImage. 페이지마다 rect가 달라 참조로 노출한다.</summary>
        public RawImage ModelViewport => modelViewport;

        /// <summary>현재 선택된 슬롯 번호. 선택이 없으면 -1.</summary>
        public int SelectedIndex { get; private set; } = -1;

        /// <summary>하단 버전 표기를 세운다.</summary>
        /// <param name="version">표시할 빌드 버전 문자열</param>
        public void SetVersion(string version)
        {
            if (versionText != null) versionText.text = version;
        }

        /// <summary>
        /// 선택된 카드를 바꾼다. 빈 카드를 지목하면 그 카드가 알아서 무시하므로
        /// 결과적으로 선택 없음이 된다.
        /// </summary>
        /// <param name="index">선택할 슬롯 번호. -1이면 전체 해제</param>
        public void SetSelectedIndex(int index)
        {
            SelectedIndex = index;

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null) slots[i].SetSelected(i == index);
            }
        }

        /// <summary>선택을 모두 해제한다(목록 갱신·삭제 직후).</summary>
        public void ClearSelection() => SetSelectedIndex(-1);
    }
}
