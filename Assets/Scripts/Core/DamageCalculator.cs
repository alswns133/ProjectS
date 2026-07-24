using UnityEngine;

namespace ProjectS.Core
{
    /// <summary>
    /// 데미지 계산에 필요한 공격자 측 정보.
    /// 매 타격마다 만들어지므로 struct로 두고 <c>in</c> 파라미터로 넘겨 복사 비용을 줄인다.
    /// </summary>
    /// <remarks>
    /// AttackPower는 "총 AD"다. 명세의 총AD 수식
    /// <c>(기본AD + 무기AD) × (1+공퍼%) × (1+패시브%) + 깡공</c>은
    /// 무기·옵션·패시브가 전부 미구현이라 현재 기본AD와 결과가 같으므로,
    /// 계산기 밖(PlayerStats/EnemyStats)에서 기본AD를 그대로 넣는다.
    /// 장비를 붙일 때 이 필드를 채우는 쪽만 고치면 계산기는 손대지 않아도 된다.
    /// </remarks>
    public struct AttackContext
    {
        /// <summary>총 AD. 몬스터는 MonsterStatTable.AttackPower가 그대로 들어간다.</summary>
        public float AttackPower;

        /// <summary>타격 계수. SkillTable.Coef(평타·스킬 공통) 또는 몬스터 공격 패턴의 계수.</summary>
        public float Coef;

        /// <summary>피해 랜덤 하한. 평타는 계수 대신 이 범위로 편차를 표현한다.</summary>
        public float RandomMin;

        /// <summary>피해 랜덤 상한.</summary>
        public float RandomMax;

        /// <summary>치명타 확률(0~1).</summary>
        public float CritChance;

        /// <summary>치명타 배율. 치명타피해% 옵션까지 합산한 최종 배율이다.</summary>
        public float CritDamage;

        /// <summary>
        /// 방어력 관통(0~1). 방어도 수치가 아니라 산출된 '경감률'에서 퍼센트포인트를 뺀다.
        /// 현재는 패시브 미구현이라 항상 0이지만, 파라미터 자리는 열어둔다.
        /// </summary>
        public float Penetration;

        /// <summary>피해량 증가%(0~1). 현재 미구현이라 0.</summary>
        public float DamageBonus;

        /// <summary>보스 추가뎀%(0~1). 대상이 보스일 때만 적용된다. 현재 미구현이라 0.</summary>
        public float BossBonus;
    }

    /// <summary>데미지 계산 결과.</summary>
    public struct DamageResult
    {
        /// <summary>정수로 반올림된 최종 피해량.</summary>
        public int Amount;

        /// <summary>이번 타격이 치명타였는지 여부. 데미지 텍스트 연출 분기에 쓸 수 있다.</summary>
        public bool IsCritical;
    }

    /// <summary>
    /// 최종 피해량 산출기. 평타·스킬·몬스터 공격이 전부 이 함수 하나를 탄다(분기 없음).
    /// 상태가 없는 static이라 호출당 GC 할당이 발생하지 않는다.
    /// </summary>
    public static class DamageCalculator
    {
        // 방어도 → 경감률 변환 상수. 30레벨 레어 풀세팅에서 약 70% 경감이 나오도록 역산된 값.
        private const float DefenseConstant = 2000f;

        /// <summary>
        /// 공격 정보와 피격자 방어 정보로 최종 피해량을 산출한다.
        /// 계산 순서(기본피해 → 치명타 → 방어경감 → 최종배율 → 반올림)를 바꾸면 결과가 달라진다.
        /// 중간 계산은 전부 float로 하고 정수 변환은 마지막에 단 한 번만 한다.
        /// 단계마다 반올림하면 오차가 누적되어 밸런스 시뮬레이션값과 어긋난다.
        /// </summary>
        /// <param name="attack">공격자 측 계산 파라미터</param>
        /// <param name="targetDefense">피격자의 방어도. 때리는 쪽이 아니라 맞는 쪽 값이다(실수하기 가장 쉬운 지점).</param>
        /// <param name="isBoss">피격자가 보스인지 여부. 보스 추가뎀% 적용 조건</param>
        /// <returns>정수로 반올림된 최종 피해량과 치명타 발생 여부</returns>
        public static DamageResult Calculate(in AttackContext attack, float targetDefense, bool isBoss)
        {
            // [2] 기본 피해 = 총AD × 계수 × Random(하한, 상한)
            // UnityEngine.Random을 쓰는 이유: System.Random은 인스턴스를 매번 new 해야 해 할당이 생긴다.
            float damage = attack.AttackPower * attack.Coef * Random.Range(attack.RandomMin, attack.RandomMax);

            // [3] 치명타
            bool isCritical = Random.value < attack.CritChance;
            if (isCritical) damage *= attack.CritDamage;

            // [4] 방어 경감 (피격자 기준)
            // 음수 방어도가 들어와도 경감률이 이상해지지 않도록 0에서 자른다.
            float defense = Mathf.Max(0f, targetDefense);
            float reduction = defense / (defense + DefenseConstant);

            // ★ 관통은 방어도가 아니라 '경감률'에서 뺀다. Clamp01이 필수:
            // 관통이 경감률보다 크면 유효경감이 음수가 되어 피해가 오히려 증폭된다
            // (저방어 몬스터를 칠 때 바로 터진다).
            float effectiveReduction = Mathf.Clamp01(reduction - attack.Penetration);
            damage *= 1f - effectiveReduction;

            // [5] 최종 배율. 보스 추가뎀%는 대상이 보스일 때만 적용한다.
            damage *= 1f + attack.DamageBonus;
            if (isBoss) damage *= 1f + attack.BossBonus;

            // [6] 정수 변환은 여기서 딱 한 번만
            return new DamageResult
            {
                Amount = Mathf.RoundToInt(damage),
                IsCritical = isCritical,
            };
        }
    }
}
