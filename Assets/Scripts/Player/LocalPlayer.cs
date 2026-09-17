using ProjectS.Managers;

namespace ProjectS.Players
{
    /// <summary>
    /// "지금 이 화면에서 조작 중인 플레이어"를 돌려준다. 싱글과 멀티에서 그 대상이 다르기 때문에 한곳에서 가른다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>싱글</b>: <see cref="PlayerManager.Player"/>(부트스트랩에서 만든 지속 캐릭터).
    /// </para>
    /// <para>
    /// <b>멀티(파티 인스턴스)</b>: 서버가 스폰한 <b>내 소유 네트워크 아바타</b>. 이때 <see cref="PlayerManager.Player"/>는
    /// <c>OwnerGate</c>가 숨겨 둔 마을 캐릭터라, 여기에 입력 잠금을 걸어도 실제로 조작하는 아바타는 안 잠긴다
    /// (2026-09-17 보스 등장 연출 중 멀티 입력이 안 막히던 원인).
    /// </para>
    /// </remarks>
    public static class LocalPlayer
    {
        /// <summary>조작 중인 플레이어. 아직 없으면 null.</summary>
        public static Player Current
        {
            get
            {
                // Unity 파괴 객체는 ?? 로 걸러지지 않아 명시적으로 비교한다.
                if (OwnerGate.LocalAvatar != null) return OwnerGate.LocalAvatar;

                PlayerManager manager = PlayerManager.Instance;
                return manager != null ? manager.Player : null;
            }
        }
    }
}
