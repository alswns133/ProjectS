using System;

namespace ProjectS.Data
{
    /// <summary>
    /// 레이드 한 판의 규칙 행. JsonManager가 <see cref="DungeonId"/>를 키로 로드해 캐시한다.
    /// 지금은 <b>플레이 제한 시간</b>(싱글/파티)만 담는다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>키 = 던전 ID(2자리 <c>[던전][난이도]</c>, docs/ID_NUMBERING.md §4)</b>. 레이드는 <c>99</c>다.
    /// 던전 보상(<see cref="DungeonRewardTable"/>)과 같은 키를 쓰지만 테이블을 나눈 이유는, 보상은 모든 던전에 있고
    /// 제한 시간은 레이드만의 규칙이라 한 테이블에 두면 일반 던전 행마다 빈 칸이 생기기 때문이다.
    /// </para>
    /// <para>
    /// <b>행이 없거나 값이 0 이하면 제한 시간 없음</b>으로 동작한다(<c>RaidTimeLimit</c>이 타이머를 열지 않는다).
    /// 싱글과 파티를 따로 두는 이유는 인원에 따라 딜량이 달라 같은 시간으로는 한쪽이 너무 빡빡하거나 헐겁기 때문이다.
    /// </para>
    /// </remarks>
    [Serializable]
    public class RaidTable : IDataRow
    {
        /// <summary>던전 ID(2자리 [던전][난이도]). DungeonContext.CurrentDungeonId와 같은 값이다.</summary>
        public int DungeonId;

        /// <summary>싱글(파티 없이 혼자) 제한 시간(초). 0 이하면 제한 없음.</summary>
        public float SoloTimeLimit;

        /// <summary>파티 레이드 제한 시간(초). 0 이하면 제한 없음.</summary>
        public float PartyTimeLimit;

        int IDataRow.Index => DungeonId;

        /// <summary>
        /// 던전 ID가 없으면(0 이하) 키로 못 써 행을 제외한다. 음수 시간은 오타로 보고 0(제한 없음)으로 보정만 하고
        /// 행은 살린다(DungeonRewardTable과 같은 관대한 검증).
        /// </summary>
        /// <param name="error">탈락 사유(통과 시 null)</param>
        /// <returns>사용 가능한 행이면 true</returns>
        public bool Validate(out string error)
        {
            if (DungeonId <= 0)
            {
                error = $"Raid: DungeonId가 유효하지 않음({DungeonId}) (제외됨)";
                return false;
            }

            if (SoloTimeLimit < 0f) SoloTimeLimit = 0f;
            if (PartyTimeLimit < 0f) PartyTimeLimit = 0f;

            error = null;
            return true;
        }
    }
}
