using UnityEngine;
using ProjectS.Core;
using ProjectS.Data;
using ProjectS.Managers;

namespace ProjectS.Enhance
{
    /// <summary>
    /// 강화 1회의 확률·비용 조회와 판정을 담당하는 순수 로직 계층.
    /// View/Presenter와 분리돼 있어 UI 없이도 테스트할 수 있다.
    /// 확률·비용·보너스는 JsonManager의 강화 테이블에서 읽고, 재화·재료 차감은
    /// 직접 하지 않고 <see cref="IEnhanceResources"/>에 위임한다(인벤토리 구현과의 의존을 끊는다).
    /// (2026-07-23 TH)
    /// </summary>
    public class EnhanceService
    {
        private readonly IEnhanceResources resources;

        /// <summary>
        /// 자원 공급자(지갑·재료 소유자)를 물려 서비스를 만든다.
        /// </summary>
        /// <param name="resources">골드·재료 차감을 담당할 구현(보통 InventoryManager)</param>
        public EnhanceService(IEnhanceResources resources)
        {
            this.resources = resources;
        }

        /// <summary>
        /// 대상 장비의 현재 상태로 View에 뿌릴 강화 정보(다음 확률·비용·스탯 프리뷰)를 만든다.
        /// 판정을 하지 않으므로 여러 번 호출해도 안전하다.
        /// </summary>
        /// <param name="target">강화 대상 인스턴스</param>
        /// <returns>표시용 스냅샷. target이 null이거나 데이터 미로드면 기본값</returns>
        public EnhanceInfo BuildInfo(EquipmentInstance target)
        {
            if (target == null || JsonManager.Instance == null) return default;

            int step = target.EnhanceStep;
            bool isMax = target.IsMax;
            var equip = target.Equipment;
            var bonus = FindBonus(target.Item != null ? target.Item.Category : ItemCategory.None,
                                  target.Item != null ? target.Item.Grade : ItemGrade.Normal);

            // 주스탯 기준은 이 인스턴스의 롤값(RolledMainStat). 강화 보너스를 여기 더한다.
            // (RolledMainStat은 롤이 없으면 MainStatBase로 폴백하므로 프로브에도 안전.)
            int baseStat = target.RolledMainStat;
            int curStat = baseStat + (bonus != null ? bonus.GetTotalBonus(step) : 0);

            int nextStat = curStat;
            float rate = 0f, baseRate = 0f;
            int zeny = 0, low = 0, high = 0;
            var mainType = equip != null ? equip.MainStatType : MainStatType.None;

            if (!isMax)
            {
                int nextStep = step + 1;
                nextStat = baseStat + (bonus != null ? bonus.GetTotalBonus(nextStep) : 0);

                var cost = FindCost(nextStep);
                if (cost != null)
                {
                    baseRate = Mathf.Clamp01(cost.SuccessRate);   // 자비 보너스 전 기본 확률(게이지 색 분기 기준)
                    rate = EffectiveRate(cost, target);           // 기본 + 자비
                    zeny = cost.ZenyCost;
                    low = cost.LowMaterial;
                    high = cost.HighMaterial;
                }
            }

            return new EnhanceInfo(step, isMax, rate, baseRate, zeny, low, high, curStat, nextStat, mainType);
        }

        /// <summary>
        /// 강화를 시도한다. 재료·골드가 부족하면 아무것도 소모하지 않고 false를 돌려준다(View는 토스트).
        /// 성공/실패는 out result로 전달하며, 재화·재료 차감은 판정·연출 전에 끝낸다.
        /// (연출 도중 씬 전환·강제 종료가 나도 데이터가 어긋나지 않게 하기 위한 순서 — 설계 문서 3절)
        /// 성공률에는 연속 실패 자비 보너스가 더해진다(<see cref="EffectiveRate"/>).
        /// </summary>
        /// <param name="target">강화 대상 인스턴스</param>
        /// <param name="result">판정 결과(성공/실패, 단계 변화)</param>
        /// <returns>시도가 성립했으면 true. 최대 단계·비용 부족·데이터 미로드면 false</returns>
        public bool TryEnhance(EquipmentInstance target, out EnhanceResult result)
        {
            result = default;

            if (target == null || target.IsMax || resources == null || JsonManager.Instance == null)
                return false;

            int nextStep = target.EnhanceStep + 1;
            var cost = FindCost(nextStep);
            if (cost == null) return false;

            if (!resources.CanAfford(cost.ZenyCost, cost.LowMaterial, cost.HighMaterial))
                return false;

            // 1) 차감 먼저 — 여기서 데이터가 확정된다(연출 전).
            resources.Spend(cost.ZenyCost, cost.LowMaterial, cost.HighMaterial);

            // 2) 판정. 실패해도 단계 하락은 없고(EnhanceCostData 주석 참고), 연속 실패는
            //    다음 시도 성공률에 자비 보너스로 쌓인다(성공하면 RecordAttempt가 0으로 리셋).
            //    롤/실효 성공률/자비 누적은 로그에 원값 그대로 남기려 변수로 붙잡는다
            //    (화면 표기 %는 반올림, 스탯 프리뷰도 포맷돼서 실제 판정값과 다를 수 있다).
            float rate = EffectiveRate(cost, target);
            int failStreakBefore = target.FailStreak;
            float roll = Random.value;
            bool success = roll < rate;
            int before = target.EnhanceStep;
            int after = success ? before + 1 : before;

            target.ApplyStep(after);
            target.RecordAttempt(success);

            LogAttempt(target, cost, before, after, success, rate, roll, failStreakBefore);

            result = new EnhanceResult(success, before, after);
            return true;
        }

        // 강화 1회의 "실제" 수치를 콘솔에 남긴다 — 화면에 보이는 반올림/포맷 값이 아니라 판정에 쓰인 원값.
        // [Conditional]이라 에디터/개발빌드에서만 컴파일되고, 릴리스에선 호출부까지 통째로 사라진다(문자열 조립 비용 0).
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void LogAttempt(EquipmentInstance target, EnhanceCostData cost,
            int before, int after, bool success, float rate, float roll, int failStreakBefore)
        {
            var bonus = FindBonus(target.Item != null ? target.Item.Category : ItemCategory.None,
                                  target.Item != null ? target.Item.Grade : ItemGrade.Normal);
            int baseStat = target.RolledMainStat;
            int bonusBefore = bonus != null ? bonus.GetTotalBonus(before) : 0;
            int bonusAfter = bonus != null ? bonus.GetTotalBonus(after) : 0;
            var type = target.Equipment != null ? target.Equipment.MainStatType : MainStatType.None;

            Debug.Log(
                $"[강화 디버그] {(target.Item != null ? target.Item.Name : "?")} +{before} → +{after}  {(success ? "성공" : "실패")}\n" +
                $"  실효 성공률 {rate:F4} = 기본 {cost.SuccessRate:F4} + 자비 {failStreakBefore}×{PityBonusPerFail:F2}" +
                $"  |  롤 {roll:F4} ({(success ? "<" : "≥")} {rate:F4})  |  화면표기 {Mathf.RoundToInt(rate * 100f)}%\n" +
                $"  주스탯 {type} 실제값 {baseStat + bonusBefore} → {baseStat + bonusAfter} (Δ{bonusAfter - bonusBefore})" +
                $"  [롤base {baseStat} + 보너스 {bonusBefore}→{bonusAfter}]\n" +
                $"  비용 골드 {cost.ZenyCost} · 하급 {cost.LowMaterial} · 상급 {cost.HighMaterial}  |  다음 연속실패 {target.FailStreak}");
        }

        /// <summary>
        /// 연속 실패 1회당 다음 시도 성공률에 더해지는 보너스(5%p). 성공하면 실패 누적이 0으로 리셋된다.
        /// 지금은 상수지만, 밸런스가 안정되면 강화 테이블(EnhanceCostData 등)로 옮길 수 있다.
        /// </summary>
        public const float PityBonusPerFail = 0.05f;

        // 기본 성공률에 연속 실패 자비 보너스를 더한 실제 성공률(0~1로 클램프).
        private static float EffectiveRate(EnhanceCostData cost, EquipmentInstance target)
        {
            return Mathf.Clamp01(cost.SuccessRate + target.FailStreak * PityBonusPerFail);
        }

        // 장비 종류(Weapon/Armor) × 등급으로 보너스 행을 찾는다. 8행이라 선형 탐색으로 충분.
        private EnhanceBonusData FindBonus(ItemCategory category, ItemGrade grade)
        {
            foreach (var row in JsonManager.Instance.EnhanceBonusDict.Values)
            {
                if (row.Category == category && row.Grade == grade) return row;
            }
            return null;
        }

        // 비용/확률 행은 단계로만 결정된다. Index==Step이 아닐 수 있어 Step으로 찾는다.
        private EnhanceCostData FindCost(int step)
        {
            foreach (var row in JsonManager.Instance.EnhanceCostDict.Values)
            {
                if (row.Step == step) return row;
            }
            return null;
        }
    }
}
