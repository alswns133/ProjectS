namespace ProjectS.Core
{
    public interface IDamageable
    {
        /// <summary>
        /// 데미지 적용을 시도한다.
        /// </summary>
        /// <param name="amount">데미지 양</param>
        /// <returns>
        /// 데미지가 실제로 적용됐으면 true. 무적·사망 등으로 씹혔으면 false.
        /// 때린 쪽은 이 값으로 히트 이펙트/게이지 회복 같은 "적중했을 때만" 하는 연출을 분기한다
        /// (무적에 씹힌 공격에도 이펙트가 나오면 플레이어가 맞은 것으로 오인한다).
        /// </returns>
        bool TakeDamage(int amount);

        bool IsDead { get; }
    }
}
