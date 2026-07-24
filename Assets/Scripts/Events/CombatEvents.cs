using System;
using UnityEngine;
using ProjectS.Core;
using ProjectS.Effects;
using ProjectS.Players;

namespace ProjectS.Events
{
    /// <summary>
    /// 전투 사실을 알리는 static 이벤트 허브. 데미지 발생 같은 전투 '사실'만 발행하고,
    /// 연출(데미지 텍스트·히트 이펙트·사운드)은 각자 구독으로 반응한다.
    /// 전투 로직이 UI/이펙트 코드를 직접 참조하지 않게 하는 경계.
    /// </summary>
    public static class CombatEvents
    {
        /// <summary>
        /// 데미지 적용 완료 (피격 월드 좌표, 실제 적용된 데미지, 표시 종류).
        /// 데미지를 '받은 쪽'(IDamageable 구현체)이 발행한다
        /// → 방어력·저항 등으로 보정된 최종 수치를 아는 곳이 사실의 소유자이기 때문.
        /// 표시 종류도 받은 쪽이 정한다: 자신이 플레이어인지는 스스로 알고,
        /// 치명타 여부는 때린 쪽이 DamageResult에 담아 TakeDamage로 넘겨주기 때문.
        /// </summary>
        public static event Action<Vector3, int, DamageTextKind> OnDamageDealt;

        /// <summary>
        /// 데미지 적용 이벤트 발행. TakeDamage 구현체가 HP에 데미지를 실제 반영한 직후 호출한다.
        /// 빗나감/무적으로 데미지가 안 들어갔으면 발행하지 않는다(0 데미지 텍스트 방지).
        /// </summary>
        /// <param name="worldPos">텍스트/이펙트를 띄울 월드 좌표(보통 피격자 머리 위)</param>
        /// <param name="amount">실제 적용된 데미지</param>
        /// <param name="kind">데미지 텍스트 색을 정하는 종류</param>
        public static void FireDamageDealt(Vector3 worldPos, int amount, DamageTextKind kind)
            => OnDamageDealt?.Invoke(worldPos, amount, kind);

        /// <summary>
        /// 플레이어의 공격이 적에게 적중 (맞은 부위의 월드 좌표).
        /// OnDamageDealt와 달리 '때린 쪽'이 발행한다 → 어디를 때렸는지는 히트 판정을
        /// 수행한 공격자만 알기 때문. 적 몸에 붙는 타격 이펙트가 구독한다.
        /// (OnDamageDealt의 좌표는 데미지 텍스트용 머리 위 고정 높이라 접점 연출에는 못 쓴다.)
        /// 공격 주체별로 이벤트를 나눈 이유: 플레이어의 타격감 이펙트와 플레이어가 맞았을 때의
        /// 피격 이펙트는 연출이 달라야 해서, 구독자가 발행처를 구분할 수 있어야 한다.
        /// </summary>
        public static event Action<Vector3> OnPlayerHitLanded;

        /// <summary>
        /// 플레이어 공격 적중 이벤트 발행. 플레이어 쪽 히트 판정 주체(PlayerCombat, Projectile)가
        /// 대상에 데미지를 넣은 직후, 맞은 콜라이더 표면의 접점 좌표로 호출한다.
        /// </summary>
        /// <param name="hitPos">맞은 부위의 월드 좌표(콜라이더 표면 접점)</param>
        public static void FirePlayerHitLanded(Vector3 hitPos)
            => OnPlayerHitLanded?.Invoke(hitPos);

        /// <summary>
        /// 몬스터의 공격이 플레이어에게 적중 (맞은 부위의 월드 좌표).
        /// 플레이어 몸에 붙는 피격 이펙트가 구독한다.
        /// </summary>
        public static event Action<Vector3> OnEnemyHitLanded;

        /// <summary>
        /// 몬스터 공격 적중 이벤트 발행. EnemyCombat이 플레이어에게 데미지를
        /// 넣은 직후, 맞은 콜라이더 표면의 접점 좌표로 호출한다.
        /// </summary>
        /// <param name="hitPos">맞은 부위의 월드 좌표(콜라이더 표면 접점)</param>
        public static void FireEnemyHitLanded(Vector3 hitPos)
            => OnEnemyHitLanded?.Invoke(hitPos);

        /// <summary>
        /// 투사체가 지형·벽에 막혀 소멸 (접점 월드 좌표, 표면 법선, 재생할 이펙트 프리팹).
        /// 벽에 남는 탄흔·스파크가 구독한다. 피격 이벤트와 분리한 이유는 '맞은 대상'이 없어서다
        /// — 데미지도 없고 연출도 다르므로 같은 이벤트로 묶으면 구독자가 매번 구분해야 한다.
        /// <para>
        /// 법선을 함께 넘기는 이유: 스파크가 벽에 파묻히거나 반대로 튀지 않으려면
        /// 이펙트를 표면 바깥 방향으로 세워야 하는데, 그 방향은 충돌을 판정한 쪽만 안다.
        /// </para>
        /// <para>
        /// 이펙트 프리팹까지 실어 보내는 이유: 벽 충돌 연출은 총알 종류마다 다르다(검기 흔적,
        /// 화살 박힘, 총알 스파크). 구독자가 프리팹을 소유하면 종류가 늘 때마다 씬 오브젝트와
        /// 이벤트를 늘려야 하므로, "무엇으로 터질지"는 투사체 프리팹이 소유한다.
        /// </para>
        /// </summary>
        public static event Action<Vector3, Vector3, HitEffect> OnProjectileBlocked;

        /// <summary>
        /// 투사체 차단 이벤트 발행. Projectile이 장애물 접점을 확정하고 소멸하기 직전에 호출한다.
        /// </summary>
        /// <param name="hitPos">벽과 만난 접점의 월드 좌표</param>
        /// <param name="normal">접점의 표면 법선(벽에서 바깥으로 향하는 방향)</param>
        /// <param name="effectPrefab">재생할 이펙트 프리팹. null이면 연출 없이 소멸만 한다.</param>
        public static void FireProjectileBlocked(Vector3 hitPos, Vector3 normal, HitEffect effectPrefab)
            => OnProjectileBlocked?.Invoke(hitPos, normal, effectPrefab);

        /// <summary>
        /// 적 사망 (죽은 적의 월드 좌표).
        /// 죽는 적은 즉시 비활성화되므로 처치 연출을 적 오브젝트에 붙일 수 없다
        /// → 밖에 있는 구독자(처치 이펙트·드롭·퀘스트 카운트)가 이 사실을 받아 처리한다.
        /// </summary>
        public static event Action<Vector3> OnEnemyDied;

        /// <summary>
        /// 적 사망 이벤트 발행. 사망이 확정된 직후, 오브젝트를 비활성화하기 '전에' 호출한다
        /// (비활성화 후에는 transform 위치를 읽는 쪽이 신뢰할 수 없으므로).
        /// </summary>
        /// <param name="worldPos">죽은 적의 월드 좌표(발밑 기준. 높이 보정은 연출 쪽 프리팹이 담당)</param>
        public static void FireEnemyDied(Vector3 worldPos)
            => OnEnemyDied?.Invoke(worldPos);

        /// <summary>
        /// 모든 구독을 초기화. 도메인 리로드를 꺼도 플레이 시작 시 깨끗한 상태를 보장한다.
        /// (static 이벤트가 이전 플레이 세션의 죽은 구독자를 들고 있는 것을 방지)
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnDamageDealt = null;   // ★ 새 이벤트는 여기에도 반드시 추가
            OnPlayerHitLanded = null;
            OnEnemyHitLanded = null;
            OnProjectileBlocked = null;
            OnEnemyDied = null;
        }
    }
}
