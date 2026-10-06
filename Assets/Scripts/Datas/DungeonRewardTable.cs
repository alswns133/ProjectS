using System;
using System.Collections.Generic;

namespace ProjectS.Data
{
    /// <summary>
    /// 던전 한 판 클리어 보상 정의 행. JsonManager가 <see cref="DungeonId"/>를 키로 로드해 캐시한다.
    /// 한 행 = 한 던전(난이도 포함). 결과 화면(<c>DungeonResultReporter</c>/<c>DungeonResultPanel</c>)이
    /// 클리어한 던전 ID로 이 행을 찾아 경험치·골드·아이템 보상을 채운다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>키 = 던전 ID(2자리 <c>[던전][난이도]</c>, docs/ID_NUMBERING.md §4)</b>이며
    /// <c>DungeonContext.CurrentDungeonId</c>와 같은 값이다. 그래서 난이도별로 보상을 따로 준다
    /// (11=던전1 노말 · 13=던전1 매니악처럼 행을 나눠 난이도가 오를수록 보상을 키운다).
    /// </para>
    /// <para>
    /// 아이템의 이름·아이콘·설명은 여기 중복 저장하지 않고 <see cref="ItemData"/>(같은 ItemId)에서 조회한다
    /// ("ID만 저장, 값은 테이블 조회" 원칙 — ShopTable과 같은 결). 결과 화면 슬롯 3종에 대응한다:
    /// <see cref="BaseRewards"/>(기본 보상) · <see cref="FixedRewards"/>(확정 획득) ·
    /// <see cref="RandomRewards"/>(랜덤 획득 — 가중치 뽑기).
    /// </para>
    /// <para>
    /// ※ 임시 스캐폴드: 드랍 밸런스가 확정되기 전 값이라 수치는 자유롭게 조정한다(문서 6장 미결).
    /// </para>
    /// </remarks>
    [Serializable]
    public class DungeonRewardTable : IDataRow
    {
        /// <summary>던전 ID(2자리 [던전][난이도]). DungeonContext.CurrentDungeonId와 같은 값이다.</summary>
        public int DungeonId;

        /// <summary>완료 보상 경험치.</summary>
        public int Exp;

        /// <summary>완료 보상 재화(재니 = 골드).</summary>
        public int Gold;

        /// <summary>
        /// 랭크 산정의 시간 만점 기준(초). 이 시간 이내에 클리어하면 시간 점수가 만점이다.
        /// </summary>
        /// <remarks>
        /// <see cref="LimitTime"/>과 한 쌍이며, 둘 다 0이거나 역전되면(Limit ≤ Target) 시간 점수가 0이 된다
        /// (<c>DungeonRankScorer.ScoreTime</c>). 랭크를 매기는 던전이라면 반드시 채워야 하는 값이다.
        /// 콤보 쪽 기준은 던전별 값이 필요 없다 — 그 판의 전투 시간에 대한 콤보 유지 시간 비율로 나온다.
        /// </remarks>
        public float TargetTime;

        /// <summary>랭크 산정의 시간 0점 기준(초). 이 시간을 넘기면 시간 점수가 0이다.</summary>
        /// <remarks>
        /// 레이드(던전 ID 99)는 <see cref="RaidTable"/>에 제한 시간이 이미 있으므로 그 값을 그대로 적고
        /// <see cref="TargetTime"/>을 그 절반쯤으로 두면 된다. 두 값 사이는 선형 보간이다.
        /// </remarks>
        public float LimitTime;

        /// <summary>랭크 산정의 생존 축 0점 기준(피격 횟수). 이 횟수를 맞으면 생존 점수가 0이다.</summary>
        /// <remarks>
        /// 무피격이면 만점이고 이 값까지 선형으로 깎인다(<c>DungeonRankScorer.ScoreSurvival</c>).
        /// <b>난이도별로 다르게 둬야 하는 값</b>이다 — 이 테이블의 키가 <c>[던전][난이도]</c>라
        /// 행이 이미 난이도별로 갈려 있으니, 난이도가 오를수록 허용치를 늘려 준다(적이 강해져 피격이
        /// 자연히 늘기 때문에, 같은 값을 쓰면 매니악에서 생존 축이 항상 0점이 된다).
        /// 비워 두면 <c>DungeonRankScorer.DefaultHitLimit</c>으로 폴백하고 Reporter가 경고를 남긴다.
        /// </remarks>
        public int HitLimit;

        /// <summary>기본 보상(항상 확정 지급). 결과 화면 슬롯0.</summary>
        public List<RewardItemEntry> BaseRewards = new();

        /// <summary>확정 획득 보상. 결과 화면 슬롯1.</summary>
        public List<RewardItemEntry> FixedRewards = new();

        /// <summary>랜덤 획득 풀. 결과 화면 슬롯2 — Weight 비율로 하나(또는 여러) 뽑는다.</summary>
        public List<RandomRewardEntry> RandomRewards = new();

        int IDataRow.Index => DungeonId;

        /// <summary>
        /// 던전 ID가 없으면(0 이하) 키로 못 써 행을 제외한다. 음수 수치는 데이터 오타로 보고
        /// 0/1로 보정만 하고 행은 살린다(ShopTable과 같은 관대한 검증).
        /// </summary>
        /// <param name="error">탈락 사유(통과 시 null)</param>
        /// <returns>사용 가능한 행이면 true</returns>
        public bool Validate(out string error)
        {
            if (DungeonId <= 0)
            {
                error = $"DungeonReward: DungeonId가 유효하지 않음({DungeonId}) (제외됨)";
                return false;
            }

            if (Exp < 0) Exp = 0;
            if (Gold < 0) Gold = 0;

            // 랭크 기준 시간: 음수는 오타로 보고 0으로 내린다. 역전(Limit ≤ Target)은 여기서 고치지 않고
            // 그대로 둔다 — 산정기가 0점으로 처리하며 Reporter가 경고를 남기므로, 조용히 보정해
            // "값을 넣었는데 왜 점수가 안 나오나"를 숨기는 쪽이 더 나쁘다.
            if (TargetTime < 0f) TargetTime = 0f;
            if (LimitTime < 0f) LimitTime = 0f;

            // 피격 허용치도 같은 결로 둔다. 음수는 0으로 내려 "미설정"과 같게 만들고(산정기가 기본값으로
            // 폴백한다), 0을 1 같은 값으로 올려 보정하지는 않는다 — 올려 두면 행을 빠뜨린 것이
            // "피격 1회에 생존 0점"이라는 가혹한 설정으로 조용히 바뀐다.
            if (HitLimit < 0) HitLimit = 0;

            NormalizeCounts(BaseRewards);
            NormalizeCounts(FixedRewards);

            if (RandomRewards != null)
            {
                foreach (RandomRewardEntry entry in RandomRewards)
                {
                    if (entry == null) continue;
                    if (entry.Count <= 0) entry.Count = 1;
                    if (entry.Weight < 0) entry.Weight = 0;   // 0이면 뽑히지 않는 항목(임시 비활성)
                }
            }

            error = null;
            return true;
        }

        // 수량이 0 이하인 항목은 1로 보정한다(빈 지급 방지). null 리스트/항목은 건너뛴다.
        private static void NormalizeCounts(List<RewardItemEntry> list)
        {
            if (list == null) return;

            foreach (RewardItemEntry entry in list)
                if (entry != null && entry.Count <= 0) entry.Count = 1;
        }
    }

    /// <summary>확정 지급 보상 한 개(아이템 ID + 수량). 이름·아이콘은 ItemData에서 조회한다.</summary>
    [Serializable]
    public class RewardItemEntry
    {
        /// <summary>지급 아이템의 테이블 ID(ItemData.Index).</summary>
        public int ItemId;

        /// <summary>지급 수량. 1 이상.</summary>
        public int Count = 1;
    }

    /// <summary>랜덤 보상 풀의 한 항목(아이템 ID + 수량 + 가중치).</summary>
    [Serializable]
    public class RandomRewardEntry
    {
        /// <summary>지급 아이템의 테이블 ID(ItemData.Index).</summary>
        public int ItemId;

        /// <summary>당첨 시 지급 수량. 1 이상.</summary>
        public int Count = 1;

        /// <summary>뽑기 가중치. 풀 안 상대 비율이다(크면 잘 나온다). 0이면 뽑히지 않는다.</summary>
        public int Weight = 1;
    }
}
