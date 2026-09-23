namespace ProjectS.Core
{
    /// <summary>
    /// 사운드가 어느 믹서 그룹(= 옵션의 어느 볼륨 슬라이더)으로 나가는지 가르는 분류.
    /// SoundTable의 SoundType 문자열을 로딩 시점에 이 값으로 파싱해 두고, SoundManager가 재생할 때마다
    /// 이 값으로 출력 그룹을 고른다. 테이블(Data)과 SoundManager(Managers)가 함께 쓰는 계약이라 Core에 둔다.
    /// </summary>
    public enum SoundCategory
    {
        /// <summary>배경음. 전용 BGM 소스 하나로만 재생된다.</summary>
        BGM,

        /// <summary>효과음(타격, UI, 발소리 등). SoundType이 비었거나 알 수 없을 때의 기본값이기도 하다.</summary>
        SFX,

        /// <summary>환경음(바람, 물, 군중 소음 등). 보통 Loop로 깔린다.</summary>
        Ambient,

        /// <summary>음성(캐릭터 대사, 기합, NPC 보이스).</summary>
        Voice,
    }
}
