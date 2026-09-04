using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 파티 슬롯 한 칸. 초상화를 중심으로 한 카드 형태이며(docs/PARTY_WINDOW_UI.md §2),
    /// 표시와 클릭 통지만 한다.
    ///
    /// <para>구역은 여섯이다 —
    /// ① 레벨 · ② 닉네임 · ③ 클래스 아이콘 · ④ 파티장 아이콘 · ⑤ 초상화 · ⑥ 바이탈 그래프.</para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>파티장은 ④ 아이콘 하나로만 나타낸다.</b> 텍스트 태그나 테두리 색을 같이 쓰지 않는다 —
    /// 같은 사실을 두 곳에서 말하면 계승이 일어났을 때 한쪽만 갱신되는 어긋남이 생긴다.
    /// </para>
    /// <para>
    /// <b>⑥ 바이탈 그래프는 연출이다.</b> 실제 HP를 그리지 않는다(던전 입장 전이라 볼 HP가 없다).
    /// <c>HpEcgBar</c> 머티리얼을 붙여 두면 파형이 알아서 돌고, 이 스크립트는 켜고 끄기만 한다.
    /// 나중에 진짜 HP를 물리려면 <see cref="HpEcg"/>를 붙이고 <c>SetHpRatio</c>를 부르면 되는데,
    /// 그 컴포넌트는 <c>SetMaterial</c>을 먼저 받아야 동작한다는 점에 주의한다.
    /// </para>
    /// <para>
    /// <b>빈 칸과 채워진 칸을 같은 오브젝트로 만들지 않는다.</b> 하나를 텍스트만 바꿔 쓰면
    /// "＋ 파티원 초대"와 초상화 카드의 구성이 전혀 달라 매번 배치를 손봐야 한다.
    /// </para>
    /// <para>
    /// <b>비활성일 때도 오브젝트는 켜 둔다.</b> 던전을 고르기 전에는 빈 칸을 누를 수 없어야 하는데,
    /// 오브젝트를 꺼 버리면 칸이 통째로 사라져 하단 배치가 흔들린다. <c>interactable</c>만 내린다.
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

        [Header("① 레벨 · ② 닉네임")]
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private TMP_Text nameText;

        [Header("③ 클래스 아이콘")]
        [Tooltip("인덱스는 CharacterSaveData.characterType과 같고, 해당하는 하나만 켠다.")]
        [SerializeField] private GameObject[] classIcons;

        [Header("④ 파티장 아이콘")]
        [Tooltip("파티장임을 나타내는 유일한 표식. 계승이 일어나면 이 표시가 옮겨간다.")]
        [SerializeField] private GameObject leaderIcon;

        [Header("⑤ 초상화")]
        [SerializeField] private Image portraitImage;
        [Tooltip("클래스별 초상화. 인덱스는 classIcons와 같은 characterType이다.")]
        [SerializeField] private Sprite[] portraitsByClass;

        [Header("⑥ 바이탈 그래프")]
        [Tooltip("연출용 파형. HpEcgBar 머티리얼을 붙여 두면 스스로 흐른다.")]
        [SerializeField] private GameObject vitalGraph;

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
        /// <param name="isLeader">이 사람이 파티장인지(④ 아이콘으로만 표시된다)</param>
        /// <param name="interactable">누를 수 있는지(내보내기·나가기가 가능한 상황인지)</param>
        public void SetMember(PartyMemberInfo member, bool isLeader, bool interactable)
        {
            Member = member;

            if (emptyRoot != null) emptyRoot.SetActive(false);
            if (filledRoot != null) filledRoot.SetActive(true);
            if (slotButton != null) slotButton.interactable = interactable;
            if (leaderIcon != null) leaderIcon.SetActive(isLeader);
            if (vitalGraph != null) vitalGraph.SetActive(true);
            if (member == null) return;

            if (levelText != null) levelText.text = $"Lv.{member.Level}";
            if (nameText != null) nameText.text = member.Nickname;

            ApplyClass(member.CharacterType);
        }

        // ③ 아이콘과 ⑤ 초상화는 같은 characterType으로 함께 고른다. 범위를 벗어나면 둘 다 비워
        // 엉뚱한 직업을 보여주지 않는다(빈 자리가 틀린 정보보다 낫다).
        private void ApplyClass(int characterType)
        {
            if (classIcons != null)
            {
                for (int i = 0; i < classIcons.Length; i++)
                {
                    if (classIcons[i] != null) classIcons[i].SetActive(i == characterType);
                }
            }

            if (portraitImage == null) return;

            bool hasPortrait = portraitsByClass != null
                            && characterType >= 0
                            && characterType < portraitsByClass.Length
                            && portraitsByClass[characterType] != null;

            portraitImage.sprite = hasPortrait ? portraitsByClass[characterType] : null;
            portraitImage.enabled = hasPortrait;
        }

        private void HandleClicked() => OnClicked?.Invoke();
    }
}
