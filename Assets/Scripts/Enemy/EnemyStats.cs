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

        // 페이즈 전환용 데미지 하한 비율(0~1). 0보다 크면 TakeDamage가 HP를 maxHp*ratio 밑으로 깎지 않아,
        // 보스가 이 지점에서 죽지 않고 멈춘다(BossPhaseTransition이 프리팹을 갈아끼울 틈을 준다). 0이면 기존 동작 그대로.
        private float damageFloorRatio;

        // 스폰 시 이어받을 HP(페이즈 인계). 0 이상이면 테이블 로딩이 풀피로 리셋해도 이 값으로 다시 맞춘다. 음수면 미설정.
        private int spawnHpOverride = -1;

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
            monsterId = ProjectS.Scenes.DungeonContext.ResolveMonsterId(monsterId);

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

            // 페이즈 하한이 걸려 있으면(damageFloorRatio>0) 그 밑으로는 깎지 않는다 → 보스가 하한에서
            // 죽지 않고 멈춰 페이즈 전환 틈이 생긴다. 하한이 0(기본)이면 기존대로 0까지 깎인다.
            int floorHp = damageFloorRatio > 0f ? Mathf.CeilToInt(maxHp * damageFloorRatio) : 0;
            currentHp = Mathf.Max(floorHp, currentHp - result.Amount);

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
