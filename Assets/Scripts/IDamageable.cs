public interface IDamageable
{
    void TakeDamage(int amount);
    bool IsDead { get; }
}
