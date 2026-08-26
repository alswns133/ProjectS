using System;

namespace ProjectS.Skills
{
    /// <summary>
    /// 스킬창 슬롯 하나의 "정적 정보" + 현재 배운 레벨(커밋된 값) 스냅샷.
    /// View(<c>SkillPopup</c>)와 배치 편집 모델(<c>SkillAllocation</c>)이 공유하는 DTO다.
    /// </summary>
    /// <remarks>
    /// 성장 데이터는 <c>SkillGrowthTable</c>(신설, 2026-08-26)가 소유하고, 소스(<c>TableSkillSource</c>)가
    /// 현재 캐릭터 행을 이 DTO로 변환해 넘긴다. 배운 레벨 저장·SP 스탯이 생기면 소스만 바꾸면 되고,
    /// 이 DTO와 View는 그대로 둔다.
    /// </remarks>
    [Serializable]
    public readonly struct SkillSlotInfo
    {
        /// <summary>스킬 식별자. 슬롯 조회·배치 편집·확인 커밋의 키다.</summary>
        public readonly int SkillId;

        /// <summary>표시 이름(우측 프리뷰의 "스킬 이름").</summary>
        public readonly string Name;

        /// <summary>표시 설명(우측 프리뷰의 "스킬 설명").</summary>
        public readonly string Description;

        /// <summary>아이콘 어드레서블 주소. 아이템 아이콘과 같은 로더 경로를 탄다(없으면 빈 칸).</summary>
        public readonly string IconAddress;

        /// <summary>액티브 스킬이면 true, 패시브면 false. 좌측 두 그룹 중 어느 쪽에 놓일지 가른다.</summary>
        public readonly bool IsActive;

        /// <summary>레벨 하한(SP 계산의 바닥). 스크린샷 기준 배운 스킬은 1부터 시작한다.</summary>
        public readonly int MinLevel;

        /// <summary>레벨 상한(스테퍼의 "/5").</summary>
        public readonly int MaxLevel;

        /// <summary>지금 배워 둔(=커밋된) 레벨. 창을 열면 이 값에서 편집을 시작한다.</summary>
        public readonly int CurrentLevel;

        /// <summary>
        /// 스킬 소개 영상/이미지 프리뷰 주소. 영상 재생은 후속 작업이라 지금은 자리만 잡아 둔다(없으면 빈 프리뷰).
        /// </summary>
        public readonly string PreviewMediaAddress;

        public SkillSlotInfo(int skillId, string name, string description, string iconAddress,
            bool isActive, int minLevel, int maxLevel, int currentLevel, string previewMediaAddress = null)
        {
            SkillId = skillId;
            Name = name;
            Description = description;
            IconAddress = iconAddress;
            IsActive = isActive;
            MinLevel = minLevel;
            MaxLevel = maxLevel;
            CurrentLevel = currentLevel;
            PreviewMediaAddress = previewMediaAddress;
        }
    }

    /// <summary>
    /// 확인(커밋) 시 소스에 넘기는 "이 스킬을 이 레벨로" 변경 한 건.
    /// </summary>
    public readonly struct SkillLevelChange
    {
        /// <summary>대상 스킬.</summary>
        public readonly int SkillId;

        /// <summary>확정할 새 레벨.</summary>
        public readonly int NewLevel;

        public SkillLevelChange(int skillId, int newLevel)
        {
            SkillId = skillId;
            NewLevel = newLevel;
        }
    }
}
