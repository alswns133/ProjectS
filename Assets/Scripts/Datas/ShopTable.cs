using System;
using System.Collections.Generic;

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

        /// <summary>이 상점이 파는 항목 목록(아이템 ID + 구매가). 최소 1개.</summary>
        public List<ShopItemEntry> Items = new();

        int IDataRow.Index => ShopId;

        /// <summary>
        /// 판매 목록이 비어 있으면 열어봐야 살 게 없으므로 행을 제외한다.
        /// 구매가 음수는 데이터 오타로 보고 0으로 보정만 한다(행은 살린다).
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

            foreach (ShopItemEntry entry in Items)
                if (entry != null && entry.BuyPrice < 0) entry.BuyPrice = 0;

            error = null;
            return true;
        }
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
