using UnityEngine;
using ProjectS.Data;
using ProjectS.Managers;

namespace ProjectS.Skills
{
    /// <summary>
    /// 스킬 "배운 레벨 → 성장 배율"을 잇는 런타임 헬퍼. 데미지 계산(<c>PlayerCombat</c>)이
    /// 최종 계수를 구할 때 쓴다: <c>최종 계수 = SkillTable.Coef × GetCoefMultiplier(skillId)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>"현재 스킬 레벨"을 아는 유일한 자리를 여기 하나로 모았다.</b> 아직 배운 레벨을 저장하는
    /// 세이브가 없어서(2026-08-26) <see cref="GetLevel"/>은 지금 항상 1(=성장 배율 1.0, 무효과)을 준다.
    /// 세이브가 생기면 <see cref="GetLevel"/> 한 곳만 바꾸면 데미지·UI가 함께 살아난다.
    /// </para>
    /// <para>
    /// 성장행(<see cref="SkillGrowthTable"/>)이 없는 스킬(평타·피니시·강공격 등)은 1.0을 돌려주므로
    /// 아무 skillId에나 안전하게 호출할 수 있다.
    /// </para>
    /// </remarks>
    public static class SkillProgress
    {
        /// <summary>
        /// 이 스킬의 현재 배운 레벨. TODO: 세이브에 스킬 레벨이 생기면 거기서 읽는다(지금은 항상 1).
        /// </summary>
        /// <param name="skillId">스킬 식별자</param>
        /// <returns>현재 레벨(최소 1)</returns>
        public static int GetLevel(int skillId) => 1;

        /// <summary>
        /// 현재 레벨 기준 계수 배율. 성장행이 없거나 로딩 전이면 1.0.
        /// </summary>
        /// <param name="skillId">스킬 식별자</param>
        /// <returns>SkillTable.Coef에 곱할 배율</returns>
        public static float GetCoefMultiplier(int skillId) => GetCoefMultiplier(skillId, GetLevel(skillId));

        /// <summary>
        /// 지정 레벨 기준 계수 배율. 프리뷰(레벨을 미리 대입해 보기)에서도 쓸 수 있게 레벨을 받는다.
        /// </summary>
        /// <param name="skillId">스킬 식별자</param>
        /// <param name="level">기준 레벨(1부터)</param>
        /// <returns>SkillTable.Coef에 곱할 배율(범위 밖이면 1.0)</returns>
        public static float GetCoefMultiplier(int skillId, int level)
        {
            JsonManager json = JsonManager.Instance;
            if (json == null || !json.IsReady) return 1f;

            SkillGrowthTable row = json.Get<SkillGrowthTable>(skillId);
            if (row == null || row.CoefMultiplierPerLevel == null || row.CoefMultiplierPerLevel.Length == 0)
                return 1f;

            int index = Mathf.Clamp(level - 1, 0, row.CoefMultiplierPerLevel.Length - 1);
            return row.CoefMultiplierPerLevel[index];
        }
    }
}
