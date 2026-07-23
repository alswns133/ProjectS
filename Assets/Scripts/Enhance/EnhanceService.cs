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

            int baseStat = equip != null ? equip.MainStatBase : 0;
            int curStat = baseStat + (bonus != null ? bonus.GetTotalBonus(step) : 0);

            int nextStat = curStat;
            float rate = 0f;
            int zeny = 0, low = 0, high = 0;
            var mainType = equip != null ? equip.MainStatType : MainStatType.None;

            if (!isMax)
            {
                int nextStep = step + 1;
                nextStat = baseStat + (bonus != null ? bonus.GetTotalBonus(nextStep) : 0);

                var cost = FindCost(nextStep);
                if (cost != null)
                {
                    rate = EffectiveRate(cost, target);
                    zeny = cost.ZenyCost;
                    low = cost.LowMaterial;
                    high = cost.HighMaterial;
                }
            }

            return new EnhanceInfo(step, isMax, rate, zeny, low, high, curStat, nextStat, mainType);
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
            bool success = Random.value < EffectiveRate(cost, target);
            int before = target.EnhanceStep;
            int after = success ? before + 1 : before;

            target.ApplyStep(after);
            target.RecordAttempt(success);

            result = new EnhanceResult(success, before, after);
            return true;
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
