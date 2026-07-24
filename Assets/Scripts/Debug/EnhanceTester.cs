using System.Collections;
using UnityEngine;
using ProjectS.Core;
using ProjectS.Data;
using ProjectS.Enhance;
using ProjectS.Events;
using ProjectS.Managers;

namespace ProjectS.Debugging
{
    /// <summary>
    /// 강화 기능(EnhanceService)을 플레이 모드에서 수동 검증하는 테스트 하네스.
    /// 씬 아무 GameObject에 붙이고 플레이 → 인스펙터 컨텍스트 메뉴(⋮)로
    /// Try Once / Try Batch / Print Info / Reset Target을 실행해 결과·상태를 콘솔 로그로 확인한다.
    ///
    /// - 강화 확률·비용·보너스는 JsonManager 테이블에서 오므로 IsReady를 기다린다.
    ///   (EnhanceCostData 테이블이 로드돼 있지 않으면 모든 시도가 "성립 안 됨"으로 막힌다 → 로그로 알림)
    /// - 재화는 자체 인메모리(TestResources)라 InventoryManager가 씬에 없어도 강화 로직만 격리 테스트된다.
    ///   전체 체인(골드 이벤트→HUD)까지 보려면 resources를 InventoryManager.Instance로 바꾸면 된다.
    /// (2026-07-23 TH)
    /// </summary>
    public class EnhanceTester : MonoBehaviour
    {
        [Header("대상 아이템 (JsonManager Item/Equipment Index). 0 이하면 아래 합성 데이터 사용)")]
        [SerializeField] private int itemIndex = 0;
        [SerializeField] private int startStep = 0;

        [Header("합성 데이터 (itemIndex <= 0 일 때)")]
        [SerializeField] private ItemCategory category = ItemCategory.Weapon;
        [SerializeField] private ItemGrade grade = ItemGrade.Normal;
        [SerializeField] private int mainStatBase = 100;

        [Header("테스트 지갑 (넉넉히)")]
        [SerializeField] private int gold = 100000000;
        [SerializeField] private int lowMaterial = 9999;
        [SerializeField] private int highMaterial = 9999;

        [Header("일괄 시도 횟수")]
        [SerializeField] private int batchCount = 50;

        [Header("확률 샘플링 (단계 고정 · 자비 미포함)")]
        [SerializeField] private int sampleCount = 10000;

        private EnhanceService service;
        private EquipmentInstance target;
        private TestResources resources;
        private bool ready;

        private IEnumerator Start()
        {
            // 강화 결과 브로드캐스트가 실제로 나가는지도 함께 확인한다.
            EnhanceEvents.OnEnhanced += OnEnhanced;

            if (JsonManager.Instance == null)
            {
                Debug.LogError("[EnhanceTester] JsonManager가 씬에 없습니다. 부트스트랩/인게임 씬에서 실행하세요.");
                yield break;
            }

            while (!JsonManager.Instance.IsReady) yield return null;   // 강화 테이블 로드 대기

            resources = new TestResources { Gold = gold, Low = lowMaterial, High = highMaterial };
            service = new EnhanceService(resources);
            target = BuildTarget();
            ready = true;

            Debug.Log("[EnhanceTester] 준비 완료. 컨텍스트 메뉴로 Try Once / Try Batch / Print Info / Reset Target 실행.");
            PrintInfo();
        }

        private void OnDestroy() => EnhanceEvents.OnEnhanced -= OnEnhanced;

        /// <summary>한 번 강화하고 결과·단계·연속실패·재화 변화를 로그로 남긴다.</summary>
        [ContextMenu("Try Once")]
        private void TryOnce()
        {
            if (!Guard()) return;

            int failBefore = target.FailStreak;
            if (!service.TryEnhance(target, out var r))
            {
                Debug.LogWarning($"[EnhanceTester] 시도 성립 안 됨(최대 단계 또는 재화/데이터 부족). " +
                                 $"step={target.EnhanceStep}, gold={resources.Gold}");
                return;
            }

            Debug.Log($"[EnhanceTester] {(r.Success ? "성공" : "실패")}  " +
                      $"+{r.StepBefore}→+{r.StepAfter}  failStreak {failBefore}→{target.FailStreak}  " +
                      $"gold={resources.Gold}  low={resources.Low}  high={resources.High}");
        }

        /// <summary>batchCount만큼 연속 강화하고 성공/실패/중단 합계를 로그로 남긴다.</summary>
        [ContextMenu("Try Batch")]
        private void TryBatch()
        {
            if (!Guard()) return;

            int success = 0, fail = 0;
            bool blocked = false;
            for (int i = 0; i < batchCount; i++)
            {
                if (!service.TryEnhance(target, out var r)) { blocked = true; break; }
                if (r.Success) success++; else fail++;
            }

            Debug.Log($"[EnhanceTester] 배치 {batchCount}회 → 성공 {success} / 실패 {fail}" +
                      $"{(blocked ? " (최대 단계·재화부족으로 중단)" : "")}. " +
                      $"현재 step={target.EnhanceStep}, failStreak={target.FailStreak}, gold={resources.Gold}");
        }

        /// <summary>
        /// 현재 단계를 고정하고 sampleCount회 굴려 실측 성공률을 표 기대값과 비교한다.
        /// 매 시도마다 새 인스턴스(단계 고정·연속실패 0)를 써서 단계 진행과 자비 보너스를 배제하므로,
        /// 순수하게 "Random 판정이 표 확률대로 나오는지"만 검증한다.
        /// </summary>
        [ContextMenu("Sample Rate (fixed step)")]
        private void SampleRate()
        {
            if (!Guard()) return;
            if (target.IsMax)
            {
                Debug.LogWarning("[EnhanceTester] 최대 단계라 샘플링할 다음 시도가 없습니다.");
                return;
            }

            int step = target.EnhanceStep;
            int n = Mathf.Max(1, sampleCount);

            // 본 지갑을 건드리지 않도록 샘플 전용 서비스/재화를 따로 쓴다.
            var probeRes = new TestResources();
            var probeService = new EnhanceService(probeRes);

            // 기대값: 연속실패 0인 새 인스턴스의 성공률 = 해당 단계 기본 확률.
            float expected = probeService.BuildInfo(new EquipmentInstance(target.Item, target.Equipment, step)).SuccessRate;

            int success = 0;
            for (int i = 0; i < n; i++)
            {
                // 매 시도 재화를 채워 고갈로 중단되지 않게 한다.
                probeRes.Gold = 1_000_000_000;
                probeRes.Low = 1_000_000_000;
                probeRes.High = 1_000_000_000;

                var probe = new EquipmentInstance(target.Item, target.Equipment, step); // 단계 고정·failStreak 0
                if (!probeService.TryEnhance(probe, out var r))
                {
                    Debug.LogWarning($"[EnhanceTester] 샘플 중단(+{step} 단계 비용/확률 행 없음 등). {i}회까지 진행.");
                    return;
                }
                if (r.Success) success++;
            }

            float measured = (float)success / n;
            Debug.Log($"[EnhanceTester] 확률 샘플 +{step}→+{step + 1}  {n}회 → " +
                      $"실측 {measured:P2} / 기대 {expected:P2}  (편차 {(measured - expected) * 100f:+0.00;-0.00}%p)");
        }

        /// <summary>현재 대상의 강화 정보(성공률·비용·스탯 프리뷰)를 로그로 남긴다.</summary>
        [ContextMenu("Print Info")]
        private void PrintInfo()
        {
            if (!Guard()) return;

            var info = service.BuildInfo(target);
            Debug.Log($"[EnhanceTester] step=+{info.CurrentStep} max={info.IsMax}  " +
                      $"rate={info.SuccessRate:P1} (연속실패 {target.FailStreak}회 자비 포함)  " +
                      $"cost={info.ZenyCost}G low={info.LowMaterial} high={info.HighMaterial}  " +
                      $"{info.MainStatType} {info.CurrentMainStat}→{info.NextMainStat}");
        }

        /// <summary>대상 장비와 테스트 지갑을 초기 상태로 되돌린다.</summary>
        [ContextMenu("Reset Target")]
        private void ResetTarget()
        {
            if (JsonManager.Instance == null || !JsonManager.Instance.IsReady) return;

            resources.Gold = gold;
            resources.Low = lowMaterial;
            resources.High = highMaterial;
            target = BuildTarget();

            Debug.Log("[EnhanceTester] 대상/지갑 초기화 완료.");
            PrintInfo();
        }

        private void OnEnhanced(EnhanceResult r)
            => Debug.Log($"[EnhanceTester] (이벤트 수신) EnhanceEvents.OnEnhanced → {(r.Success ? "성공" : "실패")} +{r.StepBefore}→+{r.StepAfter}");

        private bool Guard()
        {
            if (!ready)
            {
                Debug.LogWarning("[EnhanceTester] 아직 준비 안 됨(JsonManager 데이터 로드 대기 중).");
                return false;
            }
            return true;
        }

        private EquipmentInstance BuildTarget()
        {
            var json = JsonManager.Instance;

            if (itemIndex > 0
                && json.ItemDict.TryGetValue(itemIndex, out var item)
                && json.EquipmentDict.TryGetValue(itemIndex, out var equip))
            {
                // 실제 테이블 데이터 사용
                return new EquipmentInstance(item, equip, ClampStep(startStep));
            }

            // 합성 데이터: EnhanceBonus(category, grade) 행이 테이블에 있으면 스탯 프리뷰까지 나온다.
            var synthItem = new ItemData
            {
                Index = -1,
                Name = "테스트 장비",
                Category = category,
                Grade = grade,
                Level = 1
            };
            var synthEquip = new EquipmentData
            {
                Index = -1,
                EquipSlot = EquipSlot.Weapon,
                WeaponType = WeaponType.Sword,
                MainStatType = MainStatType.AttackDamage,
                MainStatBase = mainStatBase
            };
            return new EquipmentInstance(synthItem, synthEquip, ClampStep(startStep));
        }

        private static int ClampStep(int step) => Mathf.Clamp(step, 0, EnhanceBonusData.MaxStep);

        // 강화 로직만 격리 테스트하기 위한 인메모리 재화 공급자.
        private class TestResources : IEnhanceResources
        {
            public int Gold;
            public int Low;
            public int High;

            int IEnhanceResources.Gold => Gold;
            int IEnhanceResources.LowMaterial => Low;
            int IEnhanceResources.HighMaterial => High;

            public bool CanAfford(int zeny, int low, int high) => Gold >= zeny && Low >= low && High >= high;

            public void Spend(int zeny, int low, int high)
            {
                Gold -= zeny;
                Low -= low;
                High -= high;
            }
        }
    }
}
