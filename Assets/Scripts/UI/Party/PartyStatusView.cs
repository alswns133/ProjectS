using UnityEngine;

namespace ProjectS.UI
{
    /// <summary>
    /// 던전/레이드 안에서 파티원 상태를 곁눈으로 보여 주는 HUD 조각. 기획서 2-2 전체.
    /// 좌측 캐릭터 상태창(내 HP/SG) 아래에 붙는다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>파티는 본인 포함 최대 2인이라 슬롯은 최대 1칸이다</b>(<see cref="MaxOtherMembers"/>).
    /// 본인은 이미 좌측 상태창에 있으므로 여기 그리지 않는다 — 같은 정보를 두 곳에 그리면
    /// 어느 쪽이 내 HP인지 헷갈린다.
    /// </para>
    /// <para>
    /// <b>파티원이 없으면 뷰 전체를 끈다.</b> "멀티플레이 요소는 솔로 플레이를 방해하지 않게
    /// 보조적으로 배치한다"는 기획 방향(1장) 때문이다. 그래서 솔로 입장에서는 아무 것도 하지 않아도
    /// 화면에 빈 슬롯이 남지 않는다.
    /// </para>
    /// <para>
    /// 값을 넣는 쪽은 네트워크(Mirror) 계층이다. 이 컴포넌트는 데이터를 스스로 찾지 않는다.
    /// </para>
    /// </remarks>
    public class PartyStatusView : MonoBehaviour
    {
        /// <summary>본인 포함 파티 최대 인원(기획서 2-1·2-2).</summary>
        public const int MaxPartySize = 2;

        /// <summary>본인을 뺀 파티원 수 = 슬롯 개수.</summary>
        public const int MaxOtherMembers = MaxPartySize - 1;

        [Header("파티원 슬롯")]
        [Tooltip("본인을 뺀 파티원 칸. 파티 최대 2인이라 1개다.")]
        [SerializeField] private PartyMemberSlot[] slots = new PartyMemberSlot[MaxOtherMembers];

        [Header("루트")]
        [Tooltip("파티원이 없을 때 통째로 끌 오브젝트. 비우면 이 게임오브젝트를 쓴다.")]
        [SerializeField] private GameObject root;

        /// <summary>현재 표시 중인 파티원 수.</summary>
        public int MemberCount { get; private set; }

        private void Awake()
        {
            if (root == null) root = gameObject;

            // 씬에 켠 채로 저장돼 있어도 파티가 생기기 전에는 보이지 않게 한다.
            ClearParty();
        }

        /// <summary>
        /// 슬롯 하나에 파티원을 앉히고 뷰를 켠다.
        /// </summary>
        /// <param name="index">슬롯 번호(0부터). <see cref="MaxOtherMembers"/> 이상이면 무시한다.</param>
        /// <param name="memberName">파티원 닉네임</param>
        /// <param name="level">파티원 레벨</param>
        /// <param name="portrait">초상화. null이면 프리팹 기본값을 쓴다.</param>
        public void SetMember(int index, string memberName, int level, Sprite portrait = null)
        {
            PartyMemberSlot slot = GetSlot(index);
            if (slot == null) return;

            slot.gameObject.SetActive(true);
            slot.SetMember(memberName, level, portrait);

            MemberCount = Mathf.Max(MemberCount, index + 1);
            root.SetActive(true);
        }

        /// <summary>파티원 HP를 갱신한다.</summary>
        /// <param name="index">슬롯 번호</param>
        /// <param name="ratio">0~1</param>
        public void SetHp(int index, float ratio) => GetSlot(index)?.SetHp(ratio);

        /// <summary>파티원 사망/부활 표시를 갱신한다(③ UI_MP_013).</summary>
        /// <param name="index">슬롯 번호</param>
        /// <param name="dead">사망 상태인가</param>
        public void SetDead(int index, bool dead) => GetSlot(index)?.SetDead(dead);

        /// <summary>슬롯 하나를 비운다(파티원 이탈).</summary>
        /// <param name="index">슬롯 번호</param>
        public void RemoveMember(int index)
        {
            PartyMemberSlot slot = GetSlot(index);
            if (slot == null) return;

            slot.gameObject.SetActive(false);
            MemberCount = Mathf.Max(0, MemberCount - 1);

            if (MemberCount == 0) root.SetActive(false);
        }

        /// <summary>파티 해산·솔로 입장. 모든 슬롯을 끄고 뷰를 숨긴다.</summary>
        public void ClearParty()
        {
            for (int i = 0; i < (slots?.Length ?? 0); i++)
                if (slots[i] != null) slots[i].gameObject.SetActive(false);

            MemberCount = 0;
            if (root != null) root.SetActive(false);
        }

        private PartyMemberSlot GetSlot(int index)
        {
            if (slots == null || index < 0 || index >= slots.Length)
            {
                Debug.LogWarning($"[PartyStatusView] 슬롯 {index}번이 없습니다. 파티는 최대 {MaxPartySize}인입니다.");
                return null;
            }
            return slots[index];
        }
    }
}
