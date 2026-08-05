using System;

namespace ProjectS.Data
{
    /// <summary>
    /// 평타·스킬 공통 행. 평타와 스킬은 계수의 출처만 다를 뿐 같은 계산 경로를 타므로
    /// 한 테이블에 같이 둔다(평타=1, 피니시=2, 우클릭 강공격=3, 캐릭터 스킬=101~/201~).
    /// </summary>
    [Serializable]
    public class SkillTable : IDataRow
    {
        public int SkillId;
        public string NameKey;

        /// <summary>타격 계수. 평타는 1.0으로 두고 편차를 RandomMin/Max로 표현한다.</summary>
        public float Coef;

        /// <summary>피해 랜덤 하한.</summary>
        public float RandomMin;

        /// <summary>피해 랜덤 상한.</summary>
        public float RandomMax;

        /// <summary>쿨타임(초). 평타는 0.</summary>
        public float Cooldown;

        /// <summary>스킬 게이지(SG) 소모량.</summary>
        public float SgCost;

        /// <summary>적중 1회당 회복되는 스킬 게이지(SG).</summary>
        public float SgGain;

        /// <summary>시전 딜레이(초). 현재 연출은 애니메이션이 담당하므로 참고용이다.</summary>
        public float CastDelay;

        /// <summary>
        /// 시전 동안 완전 무적(피격 자체 무효)을 부여하는 스킬인지. 각성기(예: SW_ULTIMATE/GN_ULTIMATE)처럼
        /// 큰 한 방을 안전하게 지르는 스킬에 켠다. 다른 스킬의 슈퍼아머(데미지는 받되 경직만 생략)와 달리
        /// 데미지 자체를 씹으며, 무적은 스킬이 끝나 로코모션으로 복귀할 때 해제된다(PlayerCombat 참조).
        /// 미입력(false) 시 무적 없음.
        /// </summary>
        public bool Invincible;

        int IDataRow.Index => SkillId;

        /// <summary>
        /// 랜덤 범위가 뒤집혀 있으면 Random.Range가 의도와 다른 값을 뱉으므로 바로잡는다.
        /// 계수가 음수면 회복이 되어버리므로 0으로 자른다.
        /// </summary>
        /// <param name="error">탈락 사유(통과 시 null)</param>
        /// <returns>사용 가능한 행이면 true</returns>
        public bool Validate(out string error)
        {
            if (Coef < 0f) Coef = 0f;

            // 랜덤 범위 미입력(둘 다 0)은 편차 없음(×1.0)으로 본다.
            // 0을 그대로 두면 모든 피해가 0이 되어 원인을 찾기 어렵다.
            if (RandomMin <= 0f && RandomMax <= 0f)
            {
                RandomMin = 1f;
                RandomMax = 1f;
            }

            if (RandomMin > RandomMax)
                (RandomMin, RandomMax) = (RandomMax, RandomMin);

            if (Cooldown < 0f) Cooldown = 0f;
            if (SgCost < 0f) SgCost = 0f;
            if (SgGain < 0f) SgGain = 0f;

            error = null;
            return true;
        }
    }
}
