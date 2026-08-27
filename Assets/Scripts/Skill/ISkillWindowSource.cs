using System.Collections.Generic;

namespace ProjectS.Skills
{
    /// <summary>
    /// 스킬창이 표시·커밋에 쓰는 데이터 공급자(경계면). View/Presenter는 이 인터페이스에만 의존하고,
    /// "SP를 어디서 벌고 · 스킬 목록이 어디서 오고 · 레벨을 올리면 무엇이 강해지는가"는 구현체가 안다.
    /// </summary>
    /// <remarks>
    /// 실사용 구현체는 <c>TableSkillSource</c>(<c>SkillGrowthTable</c> + 현재 캐릭터)다.
    /// 아직 없는 것(배운 레벨 저장·SP 스탯)은 그 안에서 자리표시자로 둔다.
    /// 테이블이 비면 개발용 <c>PlaceholderSkillSource</c>로 대체된다.
    /// </remarks>
    public interface ISkillWindowSource
    {
        /// <summary>이 캐릭터가 스킬에 투자할 수 있는 총 SP 예산(스크린샷의 "/45").</summary>
        int TotalSp { get; }

        /// <summary>액티브 스킬 슬롯 목록(좌측 상단, 스크린샷 기준 4칸).</summary>
        IReadOnlyList<SkillSlotInfo> GetActiveSlots();

        /// <summary>패시브 스킬 슬롯 목록(좌측 하단, 스크린샷 기준 7칸).</summary>
        IReadOnlyList<SkillSlotInfo> GetPassiveSlots();

        /// <summary>
        /// 한 레벨 올리는 데 드는 SP 비용. 스킬·구간별로 다를 수 있어 메서드로 둔다(플레이스홀더는 항상 1).
        /// </summary>
        /// <param name="skillId">대상 스킬</param>
        /// <param name="fromLevel">현재 레벨(이 레벨 → +1로 올릴 때의 비용)</param>
        /// <returns>필요 SP</returns>
        int SpCost(int skillId, int fromLevel);

        /// <summary>
        /// [확인]을 눌러 배치 편집을 확정할 때 호출된다. 실제 저장·스탯 반영은 구현체가 담당한다.
        /// </summary>
        /// <param name="changes">커밋 대상(바뀐 슬롯만). 비어 있으면 변경 없음.</param>
        void Apply(IReadOnlyList<SkillLevelChange> changes);
    }
}
