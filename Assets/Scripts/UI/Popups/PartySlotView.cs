using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 던전 입장 창 하단의 파티 슬롯 한 칸(docs/PARTY_WINDOW_UI.md §2).
    /// 비었을 때와 채워졌을 때 두 모습을 오가며, 클릭 통지만 한다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>빈 칸과 채워진 칸을 같은 오브젝트로 만들지 않는다.</b> 하나를 텍스트만 바꿔 쓰면
    /// "＋ 파티원 초대"와 "하루 / Lv.24"의 정렬·아이콘 유무가 달라 매번 레이아웃을 손봐야 한다.
    /// 두 덩어리를 갈아 켜는 편이 프리팹에서 각각을 그대로 두고 만질 수 있어 단순하다.
    /// </para>
    /// <para>
    /// <b>비활성일 때도 오브젝트는 켜 둔다.</b> 던전을 고르기 전에는 빈 칸을 누를 수 없어야 하는데,
    /// 오브젝트를 꺼 버리면 하단 줄에서 칸이 통째로 사라져 배치가 흔들린다.
    /// <c>interactable</c>만 내린다.
    /// </para>
    /// </remarks>
    public class PartySlotView : MonoBehaviour
    {
        /// <summary>칸을 눌렀다. 비었으면 초대 열기, 채워졌으면 내보내기/나가기로 이어진다.</summary>
        public event Action OnClicked;

        [Header("칸")]
        [SerializeField] private Button slotButton;

        [Header("비었을 때")]
        [Tooltip("'＋ 파티원 초대'가 들어가는 덩어리.")]
        [SerializeField] private GameObject emptyRoot;
        [Tooltip("초대 대기 중에 대신 보여줄 문구(예: 초대 중…). 비워도 된다.")]
        [SerializeField] private TMP_Text emptyLabel;

        [Header("채워졌을 때")]
        [SerializeField] private GameObject filledRoot;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text levelText;
        [Tooltip("직업 아이콘. 인덱스는 CharacterSaveData.characterType과 같고, 해당하는 하나만 켠다.")]
        [SerializeField] private GameObject[] classIcons;
        [Tooltip("파티장 표식. 계승이 일어나면 이 표시가 옮겨간다.")]
        [SerializeField] private GameObject leaderTag;

        /// <summary>이 칸이 그리고 있는 사람. 비었으면 null.</summary>
        public PartyMemberInfo Member { get; private set; }

        /// <summary>빈 칸인지.</summary>
        public bool IsEmpty => Member == null;

        private void OnEnable()
        {
            if (slotButton != null) slotButton.onClick.AddListener(HandleClicked);
        }

        private void OnDisable()
        {
            if (slotButton != null) slotButton.onClick.RemoveListener(HandleClicked);
        }

        /// <summary>빈 칸으로 그린다.</summary>
        /// <param name="interactable">지금 누를 수 있는지(던전을 고르기 전에는 false)</param>
        /// <param name="label">빈 칸에 적을 문구. null이면 프리팹에 적힌 것을 그대로 둔다</param>
        public void SetEmpty(bool interactable, string label = null)
        {
            Member = null;

            if (emptyRoot != null) emptyRoot.SetActive(true);
            if (filledRoot != null) filledRoot.SetActive(false);
            if (label != null && emptyLabel != null) emptyLabel.text = label;
            if (slotButton != null) slotButton.interactable = interactable;
        }

        /// <summary>사람을 채워 그린다.</summary>
        /// <param name="member">표시할 사람</param>
        /// <param name="isLeader">이 사람이 파티장인지</param>
        /// <param name="interactable">누를 수 있는지(내보내기·나가기가 가능한 상황인지)</param>
        public void SetMember(PartyMemberInfo member, bool isLeader, bool interactable)
        {
            Member = member;

            if (emptyRoot != null) emptyRoot.SetActive(false);
            if (filledRoot != null) filledRoot.SetActive(true);
            if (slotButton != null) slotButton.interactable = interactable;
            if (leaderTag != null) leaderTag.SetActive(isLeader);
            if (member == null) return;

            if (nameText != null) nameText.text = member.Nickname;
            if (levelText != null) levelText.text = $"Lv.{member.Level}";

            // 해당하는 아이콘만 켠다. 범위를 벗어나면 전부 꺼져 자리가 빈다(엉뚱한 직업을 보여주는 것보다 낫다).
            if (classIcons == null) return;

            for (int i = 0; i < classIcons.Length; i++)
            {
                if (classIcons[i] != null) classIcons[i].SetActive(i == member.CharacterType);
            }
        }

        private void HandleClicked() => OnClicked?.Invoke();
    }
}
