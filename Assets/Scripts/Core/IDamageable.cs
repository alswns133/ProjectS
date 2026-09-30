namespace ProjectS.Core
{
    /// <summary>
    /// 데미지를 받을 수 있는 대상(플레이어·몬스터·허수아비)의 공통 계약. 때리는 쪽은 구체 클래스가 아니라 이 인터페이스에만 의존한다.
    /// </summary>
    public interface IDamageable
    {
        /// <summary>
        /// 데미지 적용을 시도한다. 계산은 이미 끝나 있고 여기서는 HP 반영과 그 결과 처리만 한다
        /// (계산과 적용의 분리 — 구현체 안에서 방어력·치명타를 다시 계산하면 이중 적용이 된다).
        /// </summary>
        /// <param name="result">
        /// 계산이 끝난 피해. 최종 수치와 치명타 여부를 함께 담는다.
        /// 치명타 여부는 때린 쪽만 알 수 있으므로 이렇게 넘겨받아야 하고,
        /// 받는 쪽은 이 값으로 데미지 텍스트 종류를 정한다.
        /// </param>
        /// <returns>
        /// 데미지가 실제로 적용됐으면 true. 무적·사망 등으로 씹혔으면 false.
        /// 때린 쪽은 이 값으로 히트 이펙트/게이지 회복 같은 "적중했을 때만" 하는 연출을 분기한다
        /// (무적에 씹힌 공격에도 이펙트가 나오면 플레이어가 맞은 것으로 오인한다).
        /// </returns>
        bool TakeDamage(in DamageResult result);

        /// <summary>이미 죽었는지. 때리는 쪽이 죽은 대상을 판정·타게팅에서 거르는 데 쓴다.</summary>
        bool IsDead { get; }

        /// <summary>
        /// 이 대상의 방어도. 방어 경감은 맞는 쪽 값으로 계산되는데,
        /// 계산 자체는 때린 쪽(DamageCalculator 호출부)이 하므로 여기서 읽어갈 수 있어야 한다.
        /// 계산과 적용을 분리하기 위한 노출이며, TakeDamage 안에서 다시 경감을 적용하면 이중 적용이 된다.
        /// </summary>
        float Defense { get; }

        /// <summary>
        /// 보스 여부. 공격자의 '보스 추가뎀%' 적용 조건이라 방어도와 같은 이유로 노출한다.
        /// </summary>
        bool IsBoss { get; }
    }
}
