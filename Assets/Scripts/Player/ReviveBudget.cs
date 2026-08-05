using UnityEngine;

namespace ProjectS.Players
{
    /// <summary>
    /// 던전 한 판(run)에 주어지는 부활 기회. 던전에 입장할 때 채워지고, 부활할 때 하나 줄어든다.
    /// 남은 기회가 0인 상태로 죽으면 선택 없이 마을로 돌아간다.
    /// </summary>
    /// <remarks>
    /// UI가 아니라 여기에 두는 이유: 사망 팝업은 씬에 얹힌 UI라 씬이 바뀌면 사라지는데, 부활 기회는
    /// "던전 입장 ~ 퇴장"이라는 씬보다 긴 수명을 가진다. 팝업이 들고 있으면 씬을 다시 로드할 때마다
    /// 기회가 슬그머니 복구된다.
    ///
    /// MonoBehaviour 싱글톤이 아닌 static인 이유는 상태가 값 하나뿐이고 씬 배치가 필요 없어서다.
    /// 대신 플레이 모드 리로드 후에도 값이 남을 수 있어, 이벤트 허브와 같은 규칙으로
    /// <see cref="ResetOnLoad"/>에서 초기화한다.
    ///
    /// 나중에 "1일 무료 3회"처럼 계정 단위로 바뀌면 이 클래스의 내부만 세이브 연동으로 교체하면 되고,
    /// 호출부(DungeonGather·DeathPopup)는 그대로 둘 수 있다.
    /// </remarks>
    public static class ReviveBudget
    {
        /// <summary>던전 한 판에 가질 수 있는 부활 기회의 상한.</summary>
        public const int MaxPerRun = 1;

        /// <summary>던전에 입장할 때 더해 주는 기회 수.</summary>
        public const int GrantPerEntry = 1;

        /// <summary>남은 부활 기회.</summary>
        public static int Remaining { get; private set; }

        /// <summary>남은 기회가 있는지. 사망 팝업이 "선택을 줄지 / 바로 내보낼지"를 이걸로 가른다.</summary>
        public static bool CanRevive => Remaining > 0;

        /// <summary>
        /// 던전 입장 시 기회를 채운다. <see cref="DungeonGather"/>의 Enter에서 호출한다.
        /// </summary>
        /// <remarks>
        /// 더하되 <see cref="MaxPerRun"/>에서 자르는 이유: 죽지 않고 나갔다 다시 들어오길 반복해도
        /// 기회가 쌓이지 않게 하기 위함이다. 상한이 1이면 결과적으로 "입장할 때마다 1회로 리셋"과 같다.
        /// </remarks>
        public static void GrantOnDungeonEnter()
        {
            Remaining = Mathf.Min(Remaining + GrantPerEntry, MaxPerRun);
        }

        /// <summary>
        /// 부활 기회를 하나 쓴다. 남은 기회가 없으면 아무것도 하지 않고 false를 돌려준다.
        /// </summary>
        /// <returns>기회를 실제로 소모했으면 true</returns>
        public static bool TryConsume()
        {
            if (Remaining <= 0) return false;

            Remaining--;
            return true;
        }

        /// <summary>
        /// 기회를 0으로 되돌린다. 마을 복귀처럼 판이 끝나는 지점에서 호출한다.
        /// </summary>
        public static void Clear() => Remaining = 0;

        // 플레이 모드 리로드를 꺼두면 static이 에디터 세션에 남는다. 이전 플레이의 잔여 기회를
        // 물고 시작하면 "던전에 들어가지도 않았는데 부활이 가능한" 상태가 된다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad() => Remaining = 0;
    }
}
