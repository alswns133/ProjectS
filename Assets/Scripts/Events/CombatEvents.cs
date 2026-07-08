using System;
using UnityEngine;

/// <summary>
/// 전투 사실을 알리는 static 이벤트 허브. 데미지 발생 같은 전투 '사실'만 발행하고,
/// 연출(데미지 텍스트·히트 이펙트·사운드)은 각자 구독으로 반응한다.
/// 전투 로직이 UI/이펙트 코드를 직접 참조하지 않게 하는 경계.
/// </summary>
public static class CombatEvents
{
    /// <summary>
    /// 데미지 적용 완료 (피격 월드 좌표, 실제 적용된 데미지).
    /// 데미지를 '받은 쪽'(IDamageable 구현체)이 발행한다
    /// → 방어력·저항 등으로 보정된 최종 수치를 아는 곳이 사실의 소유자이기 때문.
    /// </summary>
    public static event Action<Vector3, int> OnDamageDealt;

    /// <summary>
    /// 데미지 적용 이벤트 발행. TakeDamage 구현체가 HP에 데미지를 실제 반영한 직후 호출한다.
    /// 빗나감/무적으로 데미지가 안 들어갔으면 발행하지 않는다(0 데미지 텍스트 방지).
    /// </summary>
    /// <param name="worldPos">텍스트/이펙트를 띄울 월드 좌표(보통 피격자 머리 위)</param>
    /// <param name="amount">실제 적용된 데미지</param>
    public static void FireDamageDealt(Vector3 worldPos, int amount)
        => OnDamageDealt?.Invoke(worldPos, amount);

    /// <summary>
    /// 모든 구독을 초기화. 도메인 리로드를 꺼도 플레이 시작 시 깨끗한 상태를 보장한다.
    /// (static 이벤트가 이전 플레이 세션의 죽은 구독자를 들고 있는 것을 방지)
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        OnDamageDealt = null;   // ★ 새 이벤트는 여기에도 반드시 추가
    }
}
