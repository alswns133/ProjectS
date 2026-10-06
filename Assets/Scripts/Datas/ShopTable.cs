using System;
using System.Collections.Generic;
using ProjectS.Debugging;

namespace ProjectS.Data
{
    /// <summary>
    /// 한 상점(NPC 상점)의 '판매 목록' 정의 행. JsonManager가 ShopId를 키로 로드해 캐시한다.
    /// 한 행 = 한 상점, 파는 아이템은 <see cref="Items"/> 배열로 담는다(QuestTable.Rewards와 같은 결).
    /// 아이템의 이름·아이콘·설명은 여기 중복 저장하지 않고 <see cref="ItemData"/>(같은 ItemId)에서 조회한다
    /// ("ID만 저장, 값은 테이블 조회" 원칙) — UI 카드의 아이템명/정보/아이콘이 그쪽에서 온다.
    ///
    /// ※ 임시 테이블: 상점 콘텐츠(어느 NPC가 무엇을 파는지)가 확정되기 전 스캐폴드다.
    ///   ShopId 넘버링은 아직 docs/ID_NUMBERING.md에 없어 자유 값이다(콘텐츠 확정 시 규칙을 정한다).
    /// </summary>
    [Serializable]
    public class ShopTable : IDataRow
    {
        /// <summary>상점 고유 ID. 상호작용한 NPC가 이 값으로 자기 판매 목록을 연다.</summary>
        public int ShopId;

        /// <summary>표시용 상점 이름(선택). 창 제목 등에 쓸 수 있다.</summary>
        public string ShopName = string.Empty;

        /// <summary>
        /// 상점 종류. 카테고리 바에서 어떤 탭을 보여줄지(장비상점=장비 탭, 잡화상점=잡화 탭, 전체 탭은 공통)를 정한다.
        /// 품목에서 자동 판정하지 않고 컬럼으로 둔 이유: 상점 성격은 기획이 정하는 값이라 데이터에 명시돼 있어야
        /// 품목 구성이 바뀌어도 UI가 흔들리지 않는다. JSON에 없으면 General.
        /// </summary>
        public ShopType ShopType = ShopType.General;

        /// <summary>이 상점이 파는 항목 목록(아이템 ID + 구매가). 최소 1개.</summary>
        public List<ShopItemEntry> Items = new();

        int IDataRow.Index => ShopId;

        /// <summary>
        /// 판매 목록이 비어 있으면 열어봐야 살 게 없으므로 행을 제외한다.
        /// 구매가 음수는 데이터 오타로 보고 0으로 보정만 한다(행은 살린다).
        /// 상점 종류와 안 맞는 품목(장비상점의 포션, 잡화상점의 장비)은 행을 살리되 경고한다 —
        /// 그런 품목은 카테고리 탭(전체 포함) 어디에도 안 떠서, 경고가 없으면 "목록이 비었다"로만 보인다.
        /// </summary>
        /// <param name="error">탈락 사유(통과 시 null)</param>
        /// <returns>사용 가능한 행이면 true</returns>
        public bool Validate(out string error)
        {
            if (Items == null || Items.Count == 0)
            {
                error = $"ShopId {ShopId}: 판매 목록(Items)이 비어있음 (제외됨)";
                return false;
            }

            int mismatched = 0;
            foreach (ShopItemEntry entry in Items)
            {
                if (entry == null) continue;
                if (entry.BuyPrice < 0) entry.BuyPrice = 0;
                if (IsEquipmentId(entry.ItemId) != (ShopType == ShopType.Equipment)) mismatched++;
            }

            // ItemData를 조회하지 않고 ID로 판정한다: 테이블은 병렬로 로드돼 검증 시점에 ItemData가 아직 없을 수 있고,
            // 아이템 ID 첫 자리가 분류를 인코딩한다(docs/ID_NUMBERING.md — 1무기·2방어구·3소비·4재료).
            if (mismatched > 0)
            {
                DevLog.Warning($"[ShopTable] ShopId {ShopId}({ShopName}): ShopType={ShopType}와 맞지 않는 품목 {mismatched}개 " +
                               "— 이 상점의 탭에 표시되지 않습니다. JSON의 \"ShopType\"(General/Equipment)을 확인하세요.");
            }

            error = null;
            return true;
        }

        // 아이템 ID 첫 자리(6자리 기준 10만 자리)가 1·2면 장비(무기·방어구).
        private static bool IsEquipmentId(int itemId)
        {
            int kind = itemId / 100000;
            return kind == 1 || kind == 2;
        }
    }

    /// <summary>상점 종류. JSON에는 문자열("General"/"Equipment")로 적는다(Newtonsoft가 이름으로 파싱).</summary>
    public enum ShopType
    {
        /// <summary>잡화상점(포션·재료). 장비 탭은 꺼진다.</summary>
        General = 0,
        /// <summary>장비상점. 잡화(포션) 탭은 꺼진다.</summary>
        Equipment
    }

    /// <summary>
    /// 상점 판매 항목 한 개. 아이템 ID와 그 상점에서의 구매가만 든다.
    /// 판매가(플레이어가 되팔 때)는 <see cref="ItemData.SellPrice"/>를 그대로 쓴다(기획: 구매가의 20%) —
    /// 여기에 중복 두지 않아 두 값이 어긋날 일을 없앤다.
    /// </summary>
    [Serializable]
    public class ShopItemEntry
    {
        /// <summary>판매하는 아이템의 테이블 ID(ItemData.Index).</summary>
        public int ItemId;

        /// <summary>이 상점에서의 구매가(골드). 0이면 무료(테스트용).</summary>
        public int BuyPrice;
    }
}
