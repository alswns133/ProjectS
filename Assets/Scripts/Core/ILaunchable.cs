namespace ProjectS.Core
{
    /// <summary>
    /// 하루 강공격 Hit 프레임이 해당 인터페이스를 통해서 대상에게 띄우기를 요청한다.
    /// IDamageable과 분리한 이유는 Launch는 해당 Hit가 공중에 띄우는가를 판단하기 위한 연출 신호이다.
    /// DamageResult과 분리한 이유는 수명이 달라 같은 경로에 섞으면 계산 구조를 건드리게 된다.
    /// </summary>
    public interface ILaunchable
    {
        /// <summary>
        /// 하루의 강공격 Hit 프레임에서 1회 호출된다.
        /// 상승 높이는 인자로 넘기지 않고 루트모션에 baked된 높이를 사용한다.
        /// 공중 상태 진입, 공중 상태 때 이동 제어를 루트모션으로 넘기고 착지 시 원래 이동 체계로 복귀한다.
        /// 이미 사망하거나 공중에 뜬 대상은 구현체가 안전하게 무시한다.
        /// </summary>
        void Launch();
    }
}
