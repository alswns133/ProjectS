namespace ProjectS.Core
{
    /// <summary>
    /// 캐릭터별 튜토리얼 진행 상태. <see cref="ProjectS.Data.CharacterSaveData.tutorialState"/>에
    /// 저장돼 접속 시 튜토리얼/마을 분기와 이어하기 판정에 쓰인다.
    /// </summary>
    public enum TutorialState
    {
        /// <summary>아직 시작하지 않음.</summary>
        Undone,

        /// <summary>진행 중(도중 이탈 포함). 다음 접속 시 이어하기 대상.</summary>
        Ongoing,

        /// <summary>끝까지 완료.</summary>
        Completed,

        /// <summary>플레이어가 건너뜀.</summary>
        Skipped
    }
}
