using Mirror;
using ProjectS.Core;
using ProjectS.Data;
using ProjectS.Enemies;
using ProjectS.Managers;
using ProjectS.Networking;
using ProjectS.Skills;
using UnityEngine;

namespace ProjectS.Players
{
    /// <summary>
    /// 아바타(오너 소유)에 붙어, <b>서버 권위 보스에 대한 데미지를 서버로 라우팅</b>한다(<see cref="NetworkComboRelay"/>와 같은 결).
    /// 아바타는 클라 권위라, 오너 클라의 <see cref="PlayerCombat"/>가 보스를 때려도 로컬 복제본만 깎일 뿐 서버 보스 HP는
    /// 안 바뀐다. 그래서 보스 히트는 로컬 적용 대신 이 릴레이가 <see cref="CmdBossHit"/>로 서버에 알리고, <b>서버가
    /// 공격자의 권위 스탯(<see cref="NetworkCombatStats"/>)으로 데미지를 재계산</b>해 자기 보스에 적용한다.
    ///
    /// <para><b>누가 라우팅하나.</b> 순수 관찰자(원격 클라)만 서버로 보낸다. 호스트(서버=클라)는 로컬 적용이 곧 서버
    /// 적용이라 그대로 로컬 경로를 타고(<c>BossNetSync</c>가 관찰자에 동기화), 로컬 몹·솔로(비네트워크) 보스도 로컬 경로다.</para>
    ///
    /// <para><b>권위 vs 폴백.</b> 서버가 로그인 세이브로 스탯을 도출해 뒀으면(<see cref="NetworkCombatStats.HasAuthoritativeStats"/>)
    /// 그 값으로 재계산한다(치트 방지). 도출값이 없으면(오프라인 dev) 클라가 계산한 값(<paramref name="fallbackAmount"/>)을
    /// 폴백으로 쓴다 — <c>requireAuth</c> off(개발)에서 테스트가 막히지 않게 한다.</para>
    /// </summary>
    public class NetworkDamageRelay : NetworkBehaviour, ProjectS.Effects.IProjectileHitRouter
    {
        /// <inheritdoc/>
        /// <remarks>플레이어 투사체가 서버 권위 보스를 맞히면 근접 타격과 같은 <see cref="TryReportBossHit"/> 경로로 서버에 보낸다.</remarks>
        public bool TryRoute(Collider hitCollider, in DamageResult result, int skillId, Vector3 point, Vector3 direction, out bool showHitFeedback)
        {
            // 보냈으면 손맛(히트 이펙트·게이지)은 근접과 같이 즉시 준다 — 서버 확인을 기다리면 타격감이 늦는다.
            showHitFeedback = TryReportBossHit(hitCollider, skillId, result.Amount, result.GroggyDamage);
            return showHitFeedback;
        }

        // ── 투사체 보이기 전용 복제(오너 → 서버 → 다른 화면) ─────────────────────
        // 투사체는 네트워크 오브젝트가 아닌 로컬 풀링 오브젝트라 쏜 사람 화면에만 존재한다. 발사 순간의 위치·방향만 보내
        // 다른 화면에서 데미지 없는 복제본을 날린다(판정은 쏜 사람 한 곳에서만).

        /// <summary>내가 투사체를 쐈음을 다른 화면에 알린다. 네트워크 아바타의 오너일 때만 보낸다(싱글은 아무 일도 안 함).</summary>
        /// <param name="slotKey">쏜 투사체 슬롯 키(다른 화면의 같은 캐릭터에서 같은 프리팹을 찾는다).</param>
        /// <param name="position">발사 위치.</param>
        /// <param name="rotation">발사 방향.</param>
        public void ReportProjectileFired(string slotKey, Vector3 position, Quaternion rotation)
        {
            if (!NetworkClient.active || !isOwned || netId == 0) return;
            CmdProjectileFired(slotKey, position, rotation);
        }

        [Command]
        private void CmdProjectileFired(string slotKey, Vector3 position, Quaternion rotation)
            => RpcProjectileFired(slotKey, position, rotation);

        [ClientRpc(includeOwner = false)]
        private void RpcProjectileFired(string slotKey, Vector3 position, Quaternion rotation)
        {
            if (player != null && player.Combat != null) player.Combat.FireVisualProjectile(slotKey, position, rotation);
        }
        /// <summary>
        /// 이 히트 대상이 서버 권위 보스면 서버로 데미지를 보낸다. <b>보냈으면 true</b>(호출측은 로컬 적용을 건너뛴다).
        /// 로컬 몹·솔로 보스·호스트 자신이면 false(호출측이 기존 로컬 경로로 처리).
        /// </summary>
        /// <param name="targetCollider">피격 대상 콜라이더(PlayerCombat이 OverlapBox로 찾은 것).</param>
        /// <param name="skillId">이 타격의 스킬/공격 ID(서버가 계수·랜덤·그로기를 테이블에서 조회).</param>
        /// <param name="fallbackAmount">클라가 계산한 피해량(서버 권위 스탯이 없을 때만 폴백으로 사용).</param>
        /// <param name="fallbackGroggy">클라가 실은 그로기 데미지(폴백용).</param>
        public bool TryReportBossHit(Collider targetCollider, int skillId, int fallbackAmount, float fallbackGroggy)
        {
            if (targetCollider == null) return false;

            // 네트워크 세션이 아니거나 내가 오너가 아니면 로컬 경로(솔로·비네트워크).
            if (!NetworkClient.active || !isOwned) return false;

            // 서버 권위 보스(BossNetSync + NetworkIdentity)인지. 아니면 로컬 몹 → 로컬 경로.
            BossNetSync bossSync = targetCollider.GetComponentInParent<BossNetSync>();
            if (bossSync == null || !bossSync.TryGetComponent(out NetworkIdentity bossId)) return false;

            // 호스트(서버=클라)는 로컬 적용이 곧 서버 적용 → 라우팅 불필요(그대로 로컬 경로, BossNetSync가 동기화).
            if (isServer) return false;

            CmdBossHit(bossId.netId, skillId, fallbackAmount, fallbackGroggy);
            return true;   // 서버로 보냈으니 로컬 적용은 건너뛴다(이중 적용 방지).
        }

        // 원격 클라 → 서버. 서버가 공격자 권위 스탯으로 데미지를 재계산해 자기 보스에 적용한다.
        [Command]
        private void CmdBossHit(uint bossNetId, int skillId, int fallbackAmount, float fallbackGroggy)
        {
            if (!NetworkServer.spawned.TryGetValue(bossNetId, out NetworkIdentity bossIdentity)) return;
            if (!bossIdentity.TryGetComponent(out EnemyStats bossStats) || bossStats.IsDead) return;

            // TODO(B3 검증): 사거리·시야·쿨다운을 서버가 검증해야 원거리/속도핵을 막는다(지금은 히트 신뢰).

            DamageResult result = BuildServerDamage(skillId, bossStats, fallbackAmount, fallbackGroggy);
            bossStats.TakeDamage(in result);
        }

        // 서버가 공격자 권위 스탯으로 데미지를 계산한다. 권위 스탯/스킬행이 없으면 클라 폴백값을 쓴다.
        [Server]
        private DamageResult BuildServerDamage(int skillId, EnemyStats bossStats, int fallbackAmount, float fallbackGroggy)
        {
            bool hasStats = NetworkCombatStats.TryGetForConnection(connectionToClient, out CombatStatBlock stats, out bool authoritative) && authoritative;
            SkillTable skill = JsonManager.Instance != null ? JsonManager.Instance.Get<SkillTable>(skillId) : null;

            if (!hasStats || skill == null)
            {
                // 폴백(오프라인 dev / 스킬행 없음): 클라 계산값 신뢰. 최종 피해·그로기만 실어 보낸다.
                return new DamageResult
                {
                    Amount = Mathf.Max(0, fallbackAmount),
                    IsCritical = false,
                    GroggyDamage = fallbackGroggy,
                };
            }

            // 권위 계산: AttackPower/치명타=서버 도출, 계수=스킬테이블×공격자 성장, 랜덤·치명타 굴림·방어경감=서버.
            // (Penetration/DamageBonus/BossBonus는 클라와 동일하게 0 — 클라가 장비 효과를 켤 때 stats 값으로 함께 켠다.)
            float coef = skill.Coef * SkillProgress.GetCoefMultiplier(skillId, AttackerSkillLevel(skillId));
            AttackContext ctx = new AttackContext
            {
                AttackPower = stats.AttackPower,
                Coef = coef,
                RandomMin = skill.RandomMin,
                RandomMax = skill.RandomMax,
                CritChance = stats.CritChance,
                CritDamage = stats.CritDamage,
                Penetration = 0f,
                DamageBonus = 0f,
                BossBonus = 0f,
                GroggyDamage = skill.GroggyDamage,
            };
            return DamageCalculator.Calculate(in ctx, bossStats.Defense, bossStats.IsBoss);
        }

        // 공격자의 이 스킬 배운 레벨(계수 성장용). 서버가 인증 때 읽은 세이브에서 본다. 없으면 1(액티브 기본).
        [Server]
        private int AttackerSkillLevel(int skillId)
        {
            if (FirebaseServerAuthenticator.TryGet(connectionToClient, out FirebaseServerAuthenticator.PendingConnectionAuth auth)
                && auth.save?.skillLevels != null)
            {
                foreach (SkillLevelSave s in auth.save.skillLevels)
                    if (s != null && s.skillId == skillId) return s.level;
            }
            return 1;
        }

        // ── 몬스터 → 이 플레이어 (서버 → 오너 클라) ─────────────────────────────
        //
        // 아바타는 클라 권위라 서버가 이 아바타의 PlayerStats(서버 쪽 사본)에 데미지를 넣어도 실제 플레이어 HP는 안 바뀐다.
        // 그래서 서버가 판정한 타격·잡기를 오너 컴퓨터로 보내고, <b>오너가 최종 확인</b>한다(사용자 결정 2026-09-17) —
        // 구르기 무적 같은 순간 상태는 오너 컴퓨터만 정확히 알기 때문이다. 경로 입구는 EnemyHitRouter / Boss.

        private Player player;

        private void Awake()
        {
            player = GetComponentInChildren<Player>(true);
        }

        /// <summary>
        /// 서버에서 본 이 아바타가 <b>원격 클라 소유</b>인지. 호스트 자신의 아바타(서버=오너)·싱글 캐릭터는 false —
        /// 그쪽은 서버의 적용이 곧 실제 적용이라 보낼 필요가 없다.
        /// </summary>
        public bool IsRemoteOwnedAvatar
            => isServer && connectionToClient != null && connectionToClient != NetworkServer.localConnection;

        /// <summary>서버가 판정한 몬스터 타격을 이 플레이어의 컴퓨터로 보낸다. 적용 여부(무적 등)는 그 컴퓨터가 정한다.</summary>
        /// <param name="result">서버가 계산한 피해.</param>
        /// <param name="hitPoint">접점(히트 이펙트 위치).</param>
        /// <param name="hitDirection">히트 이펙트 방향.</param>
        [Server]
        public void ServerSendEnemyHit(in DamageResult result, Vector3 hitPoint, Vector3 hitDirection)
            => TargetEnemyHit(connectionToClient, result.Amount, result.IsCritical, result.GroggyDamage, hitPoint, hitDirection);

        [TargetRpc]
        private void TargetEnemyHit(NetworkConnectionToClient target, int amount, bool critical, float groggy,
                                    Vector3 hitPoint, Vector3 hitDirection)
        {
            if (player == null || player.Stats == null) return;

            DamageResult result = new DamageResult { Amount = amount, IsCritical = critical, GroggyDamage = groggy };

            // PlayerStats.TakeDamage가 이 컴퓨터의 실제 무적(구르기) 상태로 판정한다. 들어갔을 때만 히트 이펙트를 낸다.
            if (player.Stats.TakeDamage(in result))
                ProjectS.Events.CombatEvents.FireEnemyHitLanded(hitPoint, hitDirection);
        }

        /// <summary>
        /// 서버가 이 플레이어를 잡기 범위에서 찾았음을 알리고, 잡힐지 이 플레이어 컴퓨터에 묻는다.
        /// 답은 <see cref="Boss.ServerOnGrabAnswer"/>로 돌아간다.
        /// </summary>
        /// <param name="boss">잡으려는 보스.</param>
        /// <param name="grabSlot">보스의 잡기 슬롯 번호(잡는 위치를 오너 쪽 보스에서 같은 슬롯으로 찾는다).</param>
        [Server]
        public void ServerRequestGrab(Boss boss, int grabSlot)
        {
            if (boss == null || !boss.TryGetComponent(out NetworkIdentity bossIdentity)) return;
            TargetGrabRequest(connectionToClient, bossIdentity.netId, grabSlot);
        }

        [TargetRpc]
        private void TargetGrabRequest(NetworkConnectionToClient target, uint bossNetId, int grabSlot)
        {
            bool accepted = false;

            // 이 컴퓨터의 실제 상태(구르기·각성기·사망 등)로 잡힐지 판정하고, 잡히면 이 컴퓨터의 보스 손에 붙는다.
            if (player != null
                && NetworkClient.spawned.TryGetValue(bossNetId, out NetworkIdentity bossIdentity)
                && bossIdentity.TryGetComponent(out Boss boss))
            {
                accepted = player.OnGrabbed(boss, boss.GetGrabAnchor(grabSlot));
            }

            CmdGrabAnswer(bossNetId, accepted);
        }

        [Command]
        private void CmdGrabAnswer(uint bossNetId, bool accepted)
        {
            if (NetworkServer.spawned.TryGetValue(bossNetId, out NetworkIdentity bossIdentity)
                && bossIdentity.TryGetComponent(out Boss boss))
            {
                boss.ServerOnGrabAnswer(this, accepted);
            }
        }

        /// <summary>서버의 잡기 마무리(던지기/놓기)를 이 플레이어 컴퓨터로 보낸다.</summary>
        /// <param name="asThrow">true=던지기(넉백), false=제자리 해제.</param>
        /// <param name="throwHorizontalSpeed">던지기 수평 넉백 세기.</param>
        /// <param name="throwUpSpeed">던지기 수직 띄우기 세기.</param>
        [Server]
        public void ServerSendGrabRelease(bool asThrow, float throwHorizontalSpeed, float throwUpSpeed)
            => TargetGrabRelease(connectionToClient, asThrow, throwHorizontalSpeed, throwUpSpeed);

        [TargetRpc]
        private void TargetGrabRelease(NetworkConnectionToClient target, bool asThrow, float throwHorizontalSpeed, float throwUpSpeed)
        {
            if (player != null) player.ReleaseFromGrab(asThrow, throwHorizontalSpeed, throwUpSpeed);
        }

        /// <summary>
        /// 서버에서 본 이 원격 플레이어가 살아 있는지. 서버 쪽 아바타 사본은 데미지를 받지 않아 HP로 알 수 없으므로,
        /// 오너가 올리는 <see cref="PlayerPresence.HpRatio"/>로 판단한다(보스 어그로가 죽은 플레이어를 건너뛰는 근거).
        /// </summary>
        public bool ServerIsOwnerAlive
        {
            get
            {
                NetworkIdentity ownerObject = connectionToClient != null ? connectionToClient.identity : null;
                if (ownerObject == null || !ownerObject.TryGetComponent(out PlayerPresence presence)) return true;   // 모르면 산 것으로(멈춰 서는 것보다 낫다)
                return presence.HpRatio > 0f;
            }
        }

        /// <summary>서버에서 본 이 아바타의 방어력(잡기 데미지 계산용). 서버 쪽 사본 기준이라 근사값이다.</summary>
        public float ServerDefense => player != null && player.Stats != null ? player.Stats.Defense : 0f;
    }
}
