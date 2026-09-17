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
    public class NetworkDamageRelay : NetworkBehaviour
    {
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
    }
}
