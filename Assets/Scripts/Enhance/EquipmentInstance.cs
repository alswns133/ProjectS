using System.Collections.Generic;
using ProjectS.Data;
using ProjectS.Items;

namespace ProjectS.Enhance
{
    /// <summary>
    /// 인벤토리에 실재하는 장비 1개의 런타임 상태. 정적 테이블(ItemData/EquipmentData)과 달리
    /// "이 장비의 현재 +N"처럼 인스턴스마다 달라지는 값을 담는다. 강화 결과가 여기에 반영된다.
    /// 강화창은 이 인스턴스를 대상으로 삼는다(ItemData만으로는 특정 장비 1개를 식별할 수 없다).
    /// (2026-07-23 TH)
    /// </summary>
    /// <remarks>
    /// [아이템 기능 연동 계획 — 메인 프로그래머 인수인계용 (2026-07-23 TH)]
    /// 이 클래스가 강화 ↔ 아이템(인벤토리) 연동의 다리이자 "+N"의 유일한 원본이다.
    /// 인벤토리가 붙을 때 아래 4곳만 갈아끼우면 강화 로직(Service/Panel/Presenter/Events)은 그대로 둔다.
    ///
    /// 1) 인스턴스 소유권: 아이템 획득 시 인벤토리가 이 인스턴스를 생성·소유한다.
    ///    강화는 만들지 않고 참조만 빌려 ApplyStep으로 단계만 올린다.
    ///    ★ 반드시 "같은 인스턴스 객체"를 공유할 것. 인벤토리가 자기만의 아이템 표현을
    ///      따로 만들면 +N이 두 벌로 갈라진다.
    /// 2) 재화·재료: 지금은 InventoryManager가 IEnhanceResources를 스캐폴드로 구현한다.
    ///    진짜 인벤토리가 같은 인터페이스를 구현하면 되고, 재료는 아이템 스택 카운트로 매핑,
    ///    Spend는 InventoryEvents.FireItemRemoved 발행으로 연결한다.
    /// 3) 강화 대상 목록: ItemSelectPopup은 InventoryManager.OwnedEquipment를 나열한다.
    ///    인벤토리의 "장비 카테고리 필터" 결과로 교체한다.
    /// 4) 강화 후 스탯 반영(★ 아직 소비자 없음): ApplyStep은 단계만 올릴 뿐 플레이어 전투
    ///    스탯을 아무도 재계산하지 않는다. EnhanceEvents.OnEnhanced는 이미 발행되니,
    ///    장비 스탯 집계기가 이것(또는 InventoryEvents.OnItemEquipped)을 구독해 장착 중이면 재계산
    ///    → PlayerStats 반영 → PlayerEvents로 UI 갱신하는 흐름을 새로 만들어야 한다.
    ///    이때 EnhanceService.BuildInfo의 "주스탯 = MainStatBase + GetTotalBonus(step)" 계산을
    ///    이 인스턴스의 메서드로 끌어올리면 강화창·인벤 상세·전투 스탯이 같은 계산을 공유한다.
    ///
    /// 구조 조정 후보: 이 타입은 "보유 장비"라는 범용 개념이라 ProjectS.Enhance보다
    /// 중립 네임스페이스(예: ProjectS.Items)가 맞다. 인벤토리 설계 확정 시 함께 옮긴다.
    /// </remarks>
    public class EquipmentInstance
    {
        /// <summary>공통 아이템 정보(이름·아이콘·카테고리·등급).</summary>
        public ItemData Item { get; }

        /// <summary>장비 고유 정보(주 스탯·부위). Item과 같은 Index를 가진다.</summary>
        public EquipmentData Equipment { get; }

        /// <summary>현재 강화 단계(0~9).</summary>
        public int EnhanceStep { get; private set; }

        /// <summary>더 강화할 수 없는 상태(최대 단계 도달).</summary>
        public bool IsMax => EnhanceStep >= EnhanceBonusData.MaxStep;

        /// <summary>연속 실패 횟수. 다음 시도의 자비(pity) 성공률 보너스 계산에 쓰인다. 성공하면 0으로 리셋된다.</summary>
        public int FailStreak { get; private set; }

        /// <summary>드랍 시 롤된 +0강 기준 주 스탯값(MainStatBase × 0.95~1.05). 강화 보너스는 여기 더해 최종값이 된다.</summary>
        public int RolledMainStat { get; }

        private readonly List<ItemOption> options;

        /// <summary>드랍 시 롤된 옵션 목록(인스턴스 고유). 스탯 집계·툴팁이 읽는다.</summary>
        public IReadOnlyList<ItemOption> Options => options;

        /// <summary>
        /// 런타임 장비 인스턴스를 만든다. 새 드랍은 <see cref="ProjectS.Items.ItemOptionRoller.Create"/>가,
        /// 세이브 복원은 저장된 롤값·옵션을 넘겨 이 생성자를 직접 부른다.
        /// </summary>
        /// <param name="item">공통 아이템 행</param>
        /// <param name="equipment">장비 고유 행</param>
        /// <param name="enhanceStep">초기 강화 단계(기본 0)</param>
        /// <param name="rolledMainStat">롤된 주 스탯(음수면 MainStatBase로 폴백 — 강화 확률 프로브 등 롤이 필요 없는 경우)</param>
        /// <param name="options">롤된 옵션(null이면 없음)</param>
        public EquipmentInstance(ItemData item, EquipmentData equipment, int enhanceStep = 0,
            int rolledMainStat = -1, List<ItemOption> options = null)
        {
            Item = item;
            Equipment = equipment;
            EnhanceStep = enhanceStep;
            RolledMainStat = rolledMainStat >= 0 ? rolledMainStat : (equipment?.MainStatBase ?? 0);
            this.options = options ?? new List<ItemOption>();
        }

        /// <summary>
        /// 강화 단계를 갱신한다. EnhanceService의 판정 결과를 적용하는 용도로만 호출한다
        /// (View/Presenter가 임의로 단계를 바꾸지 못하게 진입점을 좁혀둔다).
        /// </summary>
        /// <param name="step">새 강화 단계</param>
        public void ApplyStep(int step)
        {
            // ★ 이 값이 바뀌어도 장착 중 플레이어 스탯은 아직 재계산되지 않는다(소비자 미구현).
            //   아이템 기능 연동 계획은 이 클래스 상단 <remarks> 참조. (2026-07-23 TH)
            EnhanceStep = step;
        }

        /// <summary>
        /// 강화 시도 결과를 연속 실패 횟수에 반영한다. 실패하면 1 증가, 성공하면 0으로 리셋.
        /// EnhanceService의 판정 결과 적용 용도로만 호출한다.
        /// </summary>
        /// <param name="success">이번 시도 성공 여부</param>
        public void RecordAttempt(bool success)
        {
            FailStreak = success ? 0 : FailStreak + 1;
        }
    }
}
