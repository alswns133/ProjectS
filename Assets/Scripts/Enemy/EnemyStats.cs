using ProjectS.Core;
using ProjectS.Data;
using ProjectS.Events;
using ProjectS.Managers;
using System.Threading.Tasks;

namespace ProjectS.Enemies
{
    ﻿using UnityEngine;

    /// <summary>
    /// 몬스터 HP와 사망 판정. 피격 진입점(IDamageable)을 구현한다.
    /// </summary>
    public class EnemyStats : MonoBehaviour, IDamageable
    {
        // 이 몬스터의 "타입"을 정하는 base ID. 홈 던전의 노말 행을 넣는다(순번=뒤2자리만 의미 있음).
        // 앞2자리(던전·난이도)는 입장 시 ResolveMonsterId가 덮어쓴다. → 던전1 A타입이면 1101.
        [Tooltip("이 몬스터의 타입을 정하는 base ID. 홈 던전의 '노말' 행을 넣는다 (던전1 A타입=1101, 보스=1109 / 던전2 A타입=2101).\n" +
                 "실제로 의미 있는 건 뒤 2자리(순번: A=01·B=02·…·보스=09)뿐이다.\n" +
                 "앞 2자리(던전·난이도)는 던전 입장 시 현재 값으로 자동으로 덮어써지므로 노말로 두면 된다.")]
        [SerializeField] private int monsterId = 1101;

        // 아래 전투 스탯은 MonsterStatTable이 덮어쓴다. 인스펙터 값은 테이블 로딩 전과
        // 행 조회 실패 시에만 쓰이는 폴백이다(0으로 두면 로딩 대기 중 즉시 사망 판정이 난다).
        [SerializeField] private int maxHp = 100;
        [SerializeField] private float attackPower = 10f;
        [SerializeField] private float defense;
        [SerializeField] private bool isBoss;

        [SerializeField] private float damageTextHeight = 0.5f;

        // HP 바가 뜨는 머리 위 높이. 몹마다 모델 키가 달라 한 값으로 못 박으면 큰 몹은 배에,
        // 작은 몹은 너무 위에 떠서 프리팹별로 맞춰야 한다(데미지 텍스트 높이와 같은 이유).
        [SerializeField] private float hpBarHeight = 2f;

        private int currentHp;
        private Enemy enemy;

        // 피격 순간 하이라이트를 잠깐 켰다 끄는 선택 컴포넌트. 없으면(테스트 더미 등) 무시.
        private EnemyHitHighlight hitHighlight;

        // 그로기 게이지를 소유하는 선택 컴포넌트(보스에만 부착). 없으면 그로기 데미지가 조용히 무시된다.
        private EnemyGroggy groggy;

        // 보스 HP 바 UI가 읽는 값들. 테이블 로딩 시 채워진다(일반몹은 0/빈 값으로 남는다).
        private int segmentCount;
        private float groggyMax;
        private string nameKey;
        private string displayName;

        // 관찰자 클라가 서버에서 받은 표시 이름. 비어 있으면 로컬 테이블 이름을 쓴다(싱글/서버).
        // 레이드에서 관찰자는 서버와 다른 테이블 행을 읽을 수 있어(MaxHp와 같은 이유) 서버 값을 우선한다.
        private string networkDisplayName;

        // 페이즈 전환용 데미지 하한 비율(0~1). 0보다 크면 TakeDamage가 HP를 maxHp*ratio 밑으로 깎지 않아,
        // 보스가 이 지점에서 죽지 않고 멈춘다(BossPhaseTransition이 프리팹을 갈아끼울 틈을 준다). 0이면 기존 동작 그대로.
        private float damageFloorRatio;

        // 스폰 시 이어받을 HP(페이즈 인계). 0 이상이면 테이블 로딩이 풀피로 리셋해도 이 값으로 다시 맞춘다. 음수면 미설정.
        private int spawnHpOverride = -1;

        // 페이즈 전환 연출 동안의 피해 면역 만료 시각(Time.time). 이 시각 전의 TakeDamage는 무시된다. 0이면 면역 없음.
        // 코루틴이 아니라 시각 비교인 이유: 연출 Timeline이 다음 페이즈 오브젝트를 껐다 켜 코루틴이 끊길 수 있어서다.
        private float damageImmuneUntil;

        // 스폰한 쪽이 직접 지정한 던전 ID. 0이면 전역 DungeonContext를 쓴다(싱글·일반 던전 경로).
        private int dungeonIdOverride;

        // 관찰자 클라가 서버에서 받은 표시값. 0 이상이면 로컬 테이블 로딩이 늦게 끝나도 이 값으로 되돌린다.
        // 음수면 미설정(= 로컬 테이블 값이 곧 진실인 싱글/서버).
        private int networkMaxHp = -1;
        private int networkSegmentCount = -1;
        private int networkHp = -1;

        /// <summary>테이블 조회가 끝나 스탯이 확정됐는지. 테이블이 없거나 행이 없어 폴백을 쓰기로 한 경우도 확정으로 본다.</summary>
        public bool IsStatsReady { get; private set; }

        /// <summary>
        /// 스탯이 확정되는 순간 1회 발행. 서버가 확정된 최대 HP를 관찰자에게 복제할 시점으로 쓴다
        /// (<c>BossNetSync</c>). 테이블 로딩이 비동기라 스폰 직후엔 아직 인스펙터 폴백 값이기 때문이다.
        /// </summary>
        public event System.Action StatsReady;

        /// <summary>스폰한 쪽이 지정한 던전 ID. 0이면 미지정. 다음 페이즈 보스에 같은 값을 넘길 때 쓴다.</summary>
        public int DungeonIdOverride => dungeonIdOverride;

        public bool IsDead => currentHp <= 0;

        /// <summary>현재 HP. 보스 HP 바가 "현재/최대" 수치와 남은 줄 수 계산에 쓴다.</summary>
        public int CurrentHp => currentHp;

        /// <summary>최대 HP. 보스 HP 바 표기·줄 수 계산의 분모.</summary>
        public int MaxHp => maxHp;

        /// <summary>풀 HP일 때 표시할 줄(세그먼트) 수. 0이면 세그먼트 없이 단일 바로 취급한다.</summary>
        public int SegmentCount => segmentCount;

        /// <summary>그로기 게이지 최대치. EnemyGroggy가 최대치 초기화에 참고한다(보스에만 의미 있음).</summary>
        public float GroggyMax => groggyMax;

        /// <summary>표시용 이름 키(MonsterStatTable.NameKey). 보스 HP 바가 보스 이름으로 쓴다.</summary>
        public string DisplayNameKey => nameKey;

        /// <summary>
        /// 화면에 보여 줄 이름(MonsterStatTable.Name). 관찰자 클라는 서버가 동기화한 값을 우선한다.
        /// 비어 있을 수 있으므로 표시측(보스 HP 바)이 <see cref="DisplayNameKey"/> → 오브젝트 이름으로 폴백한다.
        /// </summary>
        public string DisplayName => !string.IsNullOrEmpty(networkDisplayName) ? networkDisplayName : displayName;

        /// <summary>
        /// 관찰자 클라에서 서버가 확정한 표시 이름을 반영한다. <c>BossNetSync</c>가 SyncVar로 받아 부른다.
        /// </summary>
        /// <param name="serverDisplayName">서버 테이블의 표시 이름.</param>
        public void SetNetworkDisplayName(string serverDisplayName) => networkDisplayName = serverDisplayName;

        /// <summary>몬스터의 총 AD. 공격 패턴의 계수와 곱해져 피해가 된다.</summary>
        public float AttackPower => attackPower;

        /// <summary>IDamageable. 방어 경감은 때린 쪽이 이 값을 읽어 계산한다.</summary>
        public float Defense => defense;

        /// <summary>IDamageable. 보스면 공격자의 보스 추가뎀%가 적용된다.</summary>
        public bool IsBoss => isBoss;

        /// <summary>월드 HP 바가 뜰 머리 위 높이. 바가 매 프레임 이 값으로 위치를 맞춘다.</summary>
        public float HpBarHeight => hpBarHeight;

        /// <summary>
        /// 데미지 하한 비율을 건다(0~1). 이후 <see cref="TakeDamage"/>는 HP를 <c>MaxHp*ratio</c> 밑으로 깎지 않는다.
        /// 보스가 이 지점에서 죽지 않고 멈춰, <see cref="BossPhaseTransition"/>이 다음 페이즈로 프리팹을 갈아끼울 틈이 생긴다.
        /// 빠지면(또는 0이면) 하한 없이 0까지 깎여 즉사 레이스로 페이즈 전환을 놓친다.
        /// </summary>
        /// <param name="ratio">하한 비율(0=하한 없음 · 0.5=절반에서 멈춤).</param>
        public void SetDamageFloorRatio(float ratio) => damageFloorRatio = Mathf.Clamp01(ratio);

        /// <summary>데미지 하한을 해제한다(0까지 깎이는 기존 동작으로 복귀). 최종 페이즈가 정상 사망하려면 반드시 호출한다.</summary>
        public void ClearDamageFloor() => damageFloorRatio = 0f;

        /// <summary>
        /// 지정한 시간 동안 피해를 받지 않게 한다. <see cref="BossPhaseTransition"/>이 전환 연출 동안 두 페이즈 모두에 건다.
        /// </summary>
        /// <remarks>
        /// 다음 페이즈는 나가는 페이즈와 같은 자리에 미리 스폰돼 연출 내내 맞을 수 있다. 면역이 없으면 연출 중 들어온
        /// 타격에 죽어 DeadState에 들어간 채 연출이 끝나고, ResumeAI가 그 상태를 덮어써 "HP 0인데 안 죽는 보스"가 된다.
        /// 정상 경로는 연출 종료 시 <see cref="ClearDamageImmune"/>로 푼다. <paramref name="maxSeconds"/>는 종료를 못 잡았을 때의 안전 만료다.
        /// </remarks>
        /// <param name="maxSeconds">안전 만료까지의 시간(초).</param>
        public void SetDamageImmune(float maxSeconds) => damageImmuneUntil = Time.time + Mathf.Max(0f, maxSeconds);

        /// <summary>피해 면역을 즉시 해제한다. 페이즈 전환 연출이 끝나 다음 페이즈가 교전을 시작할 때 부른다.</summary>
        public void ClearDamageImmune() => damageImmuneUntil = 0f;

        /// <summary>
        /// 스폰 직후 이어받을 현재 HP를 지정한다(페이즈 인계). 즉시 반영하고, <b>테이블 로딩이 풀피로 리셋해도</b>
        /// (<see cref="ApplyStatTableAsync"/>) 이 값으로 다시 맞춘다 — 페이즈가 바뀌어도 HP가 이어지게 하기 위함이다.
        /// 두 페이즈가 같은 <c>monsterId</c>(같은 MaxHp)를 쓰면 HP 바가 튀지 않고 연속된다.
        /// </summary>
        /// <param name="hp">이어받을 현재 HP(0~MaxHp로 클램프).</param>
        public void SetSpawnHp(int hp)
        {
            spawnHpOverride = Mathf.Max(0, hp);
            currentHp = Mathf.Clamp(spawnHpOverride, 0, maxHp);
        }

        /// <summary>
        /// 관찰자 클라에서 서버가 동기화한 현재 HP를 표시값에 반영한다(보스 HP 바용). 서버 권위 보스는 데미지가
        /// 서버에서만 적용돼 관찰자의 <see cref="currentHp"/>가 안 바뀌므로, <c>BossNetSync</c>가 SyncVar로 받은 값을
        /// 이걸로 밀어넣어 바가 서버 HP를 따라가게 한다. <see cref="TakeDamage"/>와 달리 이벤트·그로기·사망 처리를
        /// 하지 않는다 — 순수 표시값 갱신이며, 사망/연출은 서버가 주도한다.
        /// </summary>
        /// <param name="hp">서버가 동기화한 현재 HP(0~MaxHp로 클램프).</param>
        public void SetNetworkHp(int hp)
        {
            networkHp = Mathf.Max(0, hp);
            currentHp = Mathf.Clamp(networkHp, 0, maxHp);
        }

        /// <summary>
        /// 관찰자 클라에서 서버가 확정한 최대 HP·줄 수를 반영한다(보스 HP 바의 분모).
        /// </summary>
        /// <remarks>
        /// ★ 관찰자가 최대 HP를 <b>자기 로컬 테이블로 계산하면 안 된다.</b> 몬스터 ID의 던전·난이도 자리는
        /// 입장 컨텍스트로 입혀지는데, 서버와 클라의 컨텍스트가 어긋나면 서로 다른 행을 읽어 바의 분모가
        /// 서버 HP와 맞지 않는다(2026-09-17 "멀티 보스 바가 정보창과 다름"). 그래서 서버 값을 받아 고정하고,
        /// 로컬 테이블 로딩이 나중에 끝나도 이 값으로 되돌린다.
        /// </remarks>
        /// <param name="serverMaxHp">서버의 최대 HP.</param>
        /// <param name="serverSegmentCount">서버의 줄 수.</param>
        public void SetNetworkStats(int serverMaxHp, int serverSegmentCount)
        {
            if (serverMaxHp <= 0) return;

            networkMaxHp = serverMaxHp;
            networkSegmentCount = Mathf.Max(0, serverSegmentCount);
            ApplyNetworkOverrides();
        }

        /// <summary>
        /// 이 몬스터가 속한 던전 ID를 직접 지정한다. <b>스폰 직후, Start 전에</b> 불러야 테이블 조회에 반영된다.
        /// </summary>
        /// <remarks>
        /// 서버가 파티 인스턴스에 보스를 스폰할 때 쓴다. 파티 경로는 <c>DungeonRouter</c>/<c>RaidGather.Enter</c>를
        /// 거치지 않아 전역 컨텍스트가 레이드로 세팅되지 않는다 — 그대로 두면 보스가 프리팹 기준 ID(예: 1101,
        /// 1던전 노말)의 스탯으로 떠서 HP·공격력·방어력이 전부 틀린다. 공유 서버는 여러 인스턴스를 동시에
        /// 돌리므로 전역 값을 바꾸는 대신 몬스터마다 쥐여 준다.
        /// </remarks>
        /// <param name="dungeonId">2자리 던전 ID(예: 레이드 99).</param>
        public void SetDungeonId(int dungeonId) => dungeonIdOverride = dungeonId;

        // 서버에서 받은 값이 있으면 로컬 값 위에 덮는다. 테이블 로딩 전/후 어느 쪽에서 불려도 결과가 같다.
        private void ApplyNetworkOverrides()
        {
            if (networkMaxHp > 0)
            {
                maxHp = networkMaxHp;
                segmentCount = networkSegmentCount;
            }

            if (networkHp >= 0) currentHp = Mathf.Clamp(networkHp, 0, maxHp);
            else if (networkMaxHp > 0) currentHp = Mathf.Min(currentHp, maxHp);
        }

        private void Awake()
        {
            currentHp = maxHp;
            // 상태 머신 없이 단독 배치된 대상(테스트용)도 있을 수 있어 null을 허용한다.
            enemy = GetComponent<Enemy>();
            hitHighlight = GetComponent<EnemyHitHighlight>();
            groggy = GetComponent<EnemyGroggy>();
        }

        // async void는 Awake/Start 같은 진입점에서만 예외적으로 허용한다(JsonManager와 같은 방침).
        private async void Start()
        {
            await ApplyStatTableAsync();
            if (this == null) return;

            // 관찰자가 서버 값을 먼저 받아 뒀다면, 방금 로컬 테이블이 덮은 값을 서버 값으로 되돌린다.
            ApplyNetworkOverrides();

            IsStatsReady = true;
            StatsReady?.Invoke();
        }

        /// <summary>
        /// MonsterStatTable에서 monsterId 행을 읽어 스탯에 반영한다.
        /// 테이블이나 행이 없으면 인스펙터 폴백을 유지해 몬스터가 무적/즉사가 되지 않게 한다.
        /// </summary>
        private async Task ApplyStatTableAsync()
        {
            JsonManager json = JsonManager.Instance;
            if (json == null) return;

            if (!json.IsReady) await json.ReadyTask;
            if (this == null) return;

            // ★ 난이도를 입힌 실제 ID로 통일한다. 이후 조회·킬 집계가 모두 이 값을 쓴다.
            //   던전 밖(직접 테스트)이면 원본 그대로 반환되므로 base ID가 유지된다.
            //   스폰한 쪽이 던전을 직접 지정했으면(서버의 파티 인스턴스) 전역 컨텍스트보다 그 값을 우선한다.
            monsterId = dungeonIdOverride > 0
                ? ProjectS.Scenes.DungeonContext.ResolveMonsterId(monsterId, dungeonIdOverride)
                : ProjectS.Scenes.DungeonContext.ResolveMonsterId(monsterId);

            MonsterStatTable row = json.Get<MonsterStatTable>(monsterId);
            if (row == null)
            {
                Debug.LogWarning($"[EnemyStats] MonsterStatTable에 MonsterId {monsterId} 행이 없습니다. 인스펙터 폴백을 사용합니다.", this);
                return;
            }

            maxHp = row.MaxHp;
            attackPower = row.AttackPower;
            defense = row.Defense;
            isBoss = row.IsBoss;
            segmentCount = row.SegmentCount;
            groggyMax = row.GroggyMax;
            nameKey = row.NameKey;
            displayName = row.Name;
            currentHp = maxHp;

            // 페이즈 인계로 이어받은 HP가 있으면 방금의 풀피 리셋을 덮는다(스폰 시 SetSpawnHp가 지정).
            // 이게 없으면 2페이즈가 테이블 로딩 후 풀피가 되어 "남은 HP 이어짐"이 깨진다.
            if (spawnHpOverride >= 0) currentHp = Mathf.Clamp(spawnHpOverride, 0, maxHp);

            // 테이블이 확정된 뒤 그로기 최대치를 주입한다(있을 때만). 호출 순서를 여기서 소유해
            // EnemyGroggy가 자체 async로 값을 읽다 경쟁하는 것을 피한다.
            groggy?.ConfigureMax(groggyMax);
        }

        /// <summary>
        /// 데미지 적용. 몬스터는 무적이 없어 살아 있으면 항상 적용된다.
        /// </summary>
        /// <returns>실제 적용됐으면 true. 이미 죽은 대상이면 false(IDamageable 계약).</returns>
        public bool TakeDamage(in DamageResult result)
        {
            if (IsDead) return false;                 // 이미 죽었으면 무시(1회 사망 보장)
            if (Time.time < damageImmuneUntil) return false;   // 페이즈 전환 연출 중(SetDamageImmune) — 그로기도 쌓지 않는다

            int hpBefore = currentHp;

            // 페이즈 하한이 걸려 있으면(damageFloorRatio>0) 그 밑으로는 깎지 않는다 → 보스가 하한에서
            // 죽지 않고 멈춰 페이즈 전환 틈이 생긴다. 하한이 0(기본)이면 기존대로 0까지 깎인다.
            int floorHp = damageFloorRatio > 0f ? Mathf.CeilToInt(maxHp * damageFloorRatio) : 0;
            currentHp = Mathf.Max(floorHp, currentHp - result.Amount);

            // 진단: 보스가 죽는 타격의 출처(2026-09-18 "2페이즈가 HP 0인데 안 죽음" 추적). 잡몹은 처치마다 찍히지 않게 보스만.
            if (isBoss && IsDead)
                Debug.Log($"[진단][BossDeath] '{name}' 사망 타격 — 피해 {result.Amount}, HP {hpBefore}→{currentHp}/{maxHp}, " +
                          $"t={Time.time:0.00}\n{System.Environment.StackTrace}", this);

            // 피격 피드백: 하이라이트를 잠깐 번쩍인다(사망 타격 포함, "맞았다"를 항상 보여준다).
            hitHighlight?.Flash();

            // TODO(sound): 몬스터 피격음 — SoundManager.Instance.PlaySFX3D(SoundID.SFX_MonsterHit, transform.position);
            //   여기가 "실제 데미지 적용" 타이밍. 사망 타격도 여기를 지나므로, 처치음을 따로 낼지는 아래 사망 분기와 조율.

            // 연출은 이벤트로만 알린다(데미지 텍스트·이펙트가 각자 구독).
            // 받은 쪽이 발행하는 이유: 방어력까지 반영된 '실제 적용된' 수치를 아는 곳이 여기이기 때문.
            // 치명타 여부는 때린 쪽만 알 수 있어 DamageResult에 실려 온다.
            CombatEvents.FireDamageDealt(
                transform.position + Vector3.up * damageTextHeight,
                result.Amount,
                result.IsCritical ? DamageTextKind.Critical : DamageTextKind.Normal);

            // HP 바 갱신용. 사망(비율 0)까지 포함해 항상 발행한다 — 구독자가 풀피/사망을 "바 치우기"로,
            // 그 사이를 "붙이기·갱신"으로 처리한다. 비활성화 전에 발행해 위치 참조가 유효하다.
            CombatEvents.FireEnemyHealthChanged(this, maxHp > 0 ? (float)currentHp / maxHp : 0f);

            if (IsDead)
            {
                // 비활성화 '전에' 발행해야 구독자(처치 이펙트 등)가 위치를 신뢰할 수 있다.
                CombatEvents.FireEnemyDied(transform.position);

                // TODO(sound): 몬스터 처치음 — SoundManager.Instance.PlaySFX3D(<처치 SFX>, transform.position);
                //   전용 SoundID가 아직 없음(추가 필요). 보스면 별도 처치/사망 음으로 갈릴 수 있다.

                // 퀘스트 처치 목표 집계용. 어떤 몬스터를 잡았는지(종류)를 알린다.
                CombatEvents.FireEnemyKilled(monsterId);

                // 사망 연출(애니메이션·AI/충돌 해제·제거 타이밍)은 DeadState가 담당한다.
                // 상태 머신이 없는 단독 배치 대상만 예전처럼 즉시 비활성화한다.
                if (enemy != null) enemy.OnDied();
                else gameObject.SetActive(false);
            }
            else
            {
                // 살아 있는 타격에만 그로기를 누적한다(죽는 타격이면 무력화가 의미 없다).
                // 컴포넌트가 없으면(일반몹) 무시, 잠금·평타(0)면 컴포넌트 내부에서 걸러진다.
                groggy?.AddGroggyDamage(result.GroggyDamage);

                enemy?.OnDamaged();
            }

            return true;
        }
    }
}
