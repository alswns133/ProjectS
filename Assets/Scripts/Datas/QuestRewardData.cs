using System;

namespace ProjectS.Data
{
    /// <summary>
    /// 퀘스트 보상 한 개의 정의(기획서 7장·8.1). "보상 종류·대상 ID·수량" 구조를 그대로 따른다.
    /// 아이콘은 JSON에 못 넣으므로 어드레서블 주소 문자열로 둔다(ItemData.IconAddress와 같은 방식).
    /// </summary>
    [Serializable]
    public class QuestRewardData
    {
        /// <summary>보상 종류(골드/아이템/경험치/스킬해금).</summary>
        public QuestRewardType Type;

        /// <summary>
        /// 대상 ID. Item=아이템 테이블 ID, SkillUnlock=스킬 ID(LearnedSkillIds에 추가된다).
        /// Gold/Exp는 대상이 없으므로 사용하지 않는다.
        /// </summary>
        public int TargetId;

        /// <summary>지급 수량. Gold=골드량, Item=개수, Exp=경험치량, SkillUnlock=1(의미 없음).</summary>
        public int Amount;

        /// <summary>보상 아이콘 어드레서블 주소. UI가 필요할 때 로드한다(없으면 비워 둔다).</summary>
        public string IconAddress = string.Empty;

        public static int ResolveClassWeaponId(int baseId ,int charType)
        {
            if (baseId / 100000 != 1) return baseId;      // 무기(분류 1)만 스왑, 아니면 그대로
            int weaponDigit = (charType == 2) ? 2 : 1; // 거너 = 총 (2), 그 외 = 검(1)
            int c = (baseId / 1000) % 10;   // 현재 종류 자리
            return baseId - c * 1000 + weaponDigit * 1000;   // 종류 자리만 바꿔서 반환
        }
    }
}
