using System;

public class PlayerEvents
{
    /// <summary>
    /// HP 변경 (현재HP, 최대HP)
    /// </summary>
    public static event Action<float, float> OnHpChanged;

    /// <summary>
    /// SG 변경 (현재SG, 최대SG)
    /// </summary>
    public static event Action<float, float> OnSGChanged;

    /// <summary>
    /// 레벨업 (레벨)
    /// </summary>
    public static event Action<int> OnLevelUp;

    /// <summary>
    /// 경험치 변경 (현재EXP, 최대EXP)
    /// </summary>
    public static event Action<int, int> OnExpChanged;

    /// 골드 변경 (현재골드)
    public static event Action<int> OnGoldChanged;

    // Fire 메서드 (Player쪽에서 호출)

    /// <summary>
    /// HP 변경 이벤트 발행. 구독자(HP UI 등)에게 현재/최대 HP를 알림.
    /// </summary>
    /// <param name="cur">현재 HP</param>
    /// <param name="max">최대 HP</param>
    public static void FireHpChanged(float cur, float max)
        => OnHpChanged?.Invoke(cur, max);

    /// <summary>
    /// SG 변경 이벤트 발행. 구독자에게 현재/최대 SG를 알림.
    /// </summary>
    /// <param name="cur">현재 SG</param>
    /// <param name="max">최대 SG</param>
    public static void FireSgChanged(float cur, float max)
        => OnSGChanged?.Invoke(cur, max);

    /// <summary>
    /// 레벨업 이벤트 발행. 구독자에게 도달한 레벨을 알림.
    /// </summary>
    /// <param name="level">새로 도달한 레벨</param>
    public static void FireLevelUp(int level)
        => OnLevelUp?.Invoke(level);

    /// <summary>
    /// Exp 변경 이벤트 발행. 구독자(HP UI 등)에게 현재/최대 Exp를 알림.
    /// </summary>
    /// <param name="cur">현재 Exp</param>
    /// <param name="max">다음 레벨까지 필요한 Exp</param>
    public static void FireExpChanged(int cur, int max)
        => OnExpChanged?.Invoke(cur, max);

    /// <summary>
    /// 골드 변경 이벤트 발행. 구독자에게 현재 보유 골드를 알림.
    /// </summary>
    /// <param name="gold">현재 보유 골드</param>
    public static void FireGoldChanged(int gold)
        => OnGoldChanged?.Invoke(gold);
}
