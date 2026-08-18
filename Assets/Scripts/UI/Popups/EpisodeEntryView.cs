using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 던전 입장 화면의 에피소드 카드 1장. 카탈로그 항목 수만큼 목록에 생성되며, 표시와 클릭 통지만 담당한다.
    ///
    /// <para>
    /// <b>카드 높이는 프리팹에 박힌 고정값이다.</b> 선택돼도 높이가 변하지 않는다 —
    /// <see cref="VerticalLayoutGroup"/> 안에서 높이가 바뀌면 아래 카드가 밀려
    /// 커서 밑의 카드가 바뀌고, 엉뚱한 에피소드가 선택된다(캐릭터 선택 슬롯에서 같은 이유로 접은 방식).
    /// </para>
    /// <para>
    /// <b>잠금은 오브젝트를 끄는 게 아니라 <c>interactable = false</c>로 표현한다.</b>
    /// 꺼 버리면 목록 인덱스와 화면에 보이는 줄 수가 어긋나 W/S 이동이 건너뛴다.
    /// </para>
    /// </summary>
    public class EpisodeEntryView : MonoBehaviour
    {
        /// <summary>카드를 눌렀다(= 이 에피소드를 선택). 인자는 목록 인덱스. 잠긴 카드는 발행하지 않는다.</summary>
        public event Action<int> OnClicked;

        [Header("카드")]
        [SerializeField] private Button cardButton;
        [SerializeField] private Image selectedFrame;

        [Header("헥사곤 배지")]
        [SerializeField] private Image hexIcon;
        [SerializeField] private TMP_Text hexLabel;

        [Header("이름 줄")]
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private GameObject mainTag;

        [Header("상태 표식")]
        [SerializeField] private GameObject clearedMark;
        [SerializeField] private GameObject lockedMark;

        /// <summary>이 카드가 맡은 목록 인덱스(0부터).</summary>
        public int Index { get; private set; }

        /// <summary>레벨 제한에 걸려 선택할 수 없는 카드인지.</summary>
        public bool IsLocked { get; private set; }

        /// <summary>현재 선택된 카드인지.</summary>
        public bool IsSelected { get; private set; }

        private void OnEnable()
        {
            if (cardButton != null) cardButton.onClick.AddListener(HandleClicked);
        }

        private void OnDisable()
        {
            if (cardButton != null) cardButton.onClick.RemoveListener(HandleClicked);
        }

        /// <summary>
        /// 카드 내용을 채운다. 선택 상태는 건드리지 않으므로 목록을 다시 그린 뒤
        /// <see cref="SetSelected"/>를 따로 불러 줘야 한다.
        /// </summary>
        /// <param name="index">목록 인덱스(0부터)</param>
        /// <param name="info">표시할 에피소드</param>
        /// <param name="cleared">클리어한 에피소드인지</param>
        /// <param name="locked">레벨 제한에 걸려 잠겼는지</param>
        public void SetEpisode(int index, EpisodeInfo info, bool cleared, bool locked)
        {
            Index = index;
            IsLocked = locked;

            if (nameText != null) nameText.text = info.DisplayName;
            if (mainTag != null) mainTag.SetActive(info.IsMain);

            // 잠긴 카드는 "몇 레벨부터 되는지"를, 열린 카드는 에피소드 번호를 배지에 보여준다.
            if (hexLabel != null) hexLabel.text = locked ? $"Lv.{info.RequiredLevel}" : $"EP.{info.DungeonNumber}";

            // 전용 아이콘이 없으면 프리팹에 박힌 기본 배지 그림을 그대로 둔다.
            // 여기서 enabled를 꺼 버리면 배지 자리가 통째로 사라져 라벨만 떠 있는 모양이 된다.
            if (hexIcon != null && info.HexIcon != null) hexIcon.sprite = info.HexIcon;

            if (clearedMark != null) clearedMark.SetActive(cleared && !locked);
            if (lockedMark != null) lockedMark.SetActive(locked);

            // 오브젝트는 켜 둔 채 입력만 막는다(위 주석 참고).
            if (cardButton != null) cardButton.interactable = !locked;
        }

        /// <summary>선택 테두리를 켜고 끈다.</summary>
        /// <param name="selected">이 카드가 선택된 상태인지</param>
        public void SetSelected(bool selected)
        {
            IsSelected = selected;
            if (selectedFrame != null) selectedFrame.enabled = selected;
        }

        private void HandleClicked()
        {
            if (IsLocked) return;
            OnClicked?.Invoke(Index);
        }
    }
}
