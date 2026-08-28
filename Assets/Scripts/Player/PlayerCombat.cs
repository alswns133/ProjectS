using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectS.Core;
using ProjectS.Data;
using ProjectS.Debugging;
using ProjectS.Effects;
using ProjectS.Events;
using ProjectS.Managers;
using ProjectS.Skills;

namespace ProjectS.Players
{
    /// <summary>
    /// 플레이어의 전투 진입점.
    /// 입력을 직접 받지는 않고 Player가 호출하며, 이 클래스는 스킬 쿨타임,
    /// 콤보 입력 버퍼, 애니메이션 이벤트 기반 히트 판정을 맡는다.
    /// </summary>
    [RequireComponent(typeof(PlayerAnimation))]
    public class PlayerCombat : MonoBehaviour
    {
        private enum CombatAction
        {
            None,
            Combo,
            Skill,
            StrongAttack,
            RunAttack,
            JumpAttack,
        }

        // 공격/스킬 클립의 Animation Event가 string 키로 조회하는 히트 박스 슬롯.
        // 모션마다 판정 위치·크기뿐 아니라 데미지도 다르므로 슬롯 단위로 묶는다.
        // area의 위치/회전/스케일이 곧 판정 박스다(스케일 = 박스 크기).
        // 키는 PlayerEffects의 이펙트 키와 같은 체계를 쓴다. 예: "Attack1", "Skill2_Wave"
        // → 연출과 판정이 이름으로 짝이 맞아, 클립 이벤트만 봐도 무엇이 나가는지 읽힌다.
        [Serializable]
        private class HitBoxSlot
        {
            public string key;
            public Transform area;

            // 이 타격이 참조할 SkillTable 행 ID. 계수·랜덤 범위·게이지 회복량이 전부 여기서 나온다.
            // 평타=1, 피니시(마지막 타)=2, 우클릭 강공격=3, 캐릭터 스킬=101~(검사) / 201~(거너).
            // 데미지를 슬롯에 직접 넣지 않는 이유: 밸런스 수치는 기획이 시트에서 바꾸는 값이라
            // 인스펙터와 테이블 두 곳에 두면 어느 쪽이 진짜인지 알 수 없게 된다.
            public int skillId = 1;
        }

        // 검기처럼 몸에서 떨어져 날아가는 판정은 히트 박스 대신 투사체로 내보낸다.
        // 슬롯 키 체계는 HitBoxSlot과 동일: 클립 Animation Event의 string 인자와 맞춘다.
        [Serializable]
        private class ProjectileSlot
        {
            public string key;

            // 이 슬롯이 발사할 투사체 프리팹. 종류(검기 가로/세로, 총알 등 = 비주얼·판정 박스가 다른 것)마다
            // 프리팹을 나누고 여기서 고른다. 풀 관리는 씬에 하나 있는 ProjectileSpawner가 프리팹별로 한다.
            public Projectile prefab;

            // 발사 위치·방향 기준 Transform. 보통 캐릭터 가슴 높이의 자식 오브젝트를 쓴다.
            public Transform muzzle;

            // muzzle 회전에 더할 각도(오일러). 같은 프리팹을 대각/세로 등으로 살짝 틀 때만 쓴다.
            // 검기 종류 자체가 다르면 프리팹(spawner)을 나누고, 여기선 미세 각도만 조정한다.
            public Vector3 rotationOffset;

            // 히트 박스 슬롯과 같은 규칙: 계수·랜덤 범위·게이지 회복량은 SkillTable 행에서 온다.
            public int skillId = 101;

            // 관통 여부. true면 경로 위 여러 적을 연속 타격, false면 첫 적중에 소멸한다.
            public bool canPierce = true;
        }

        [SerializeField] private HitBoxSlot[] attackHitBoxes;
        [SerializeField] private LayerMask enemyMask;

        // 스포너는 씬에 하나만 두고 프리팹별 풀을 내부에서 관리한다.
        // 투사체 종류가 늘어도 씬 오브젝트는 늘지 않고, 슬롯에 프리팹만 추가하면 된다.
        [Header("투사체 스킬")]
        [SerializeField] private ProjectileSpawner projectileSpawner;
        [SerializeField] private ProjectileSlot[] projectileSlots;

        // 매 히트 이벤트마다 배열을 뒤지지 않도록 Awake에서 1회 구축하는 조회용 사전.
        private readonly Dictionary<string, HitBoxSlot> hitBoxMap = new Dictionary<string, HitBoxSlot>();
        private readonly Dictionary<string, ProjectileSlot> projectileMap = new Dictionary<string, ProjectileSlot>();

        // 투사체 적중을 TargetHit으로 중계하는 캐시 델리게이트. 발사마다 람다를 새로 만들지 않기 위함.
        private Action<float> relayProjectileHit;

        // 현재 재생 중인 콤보 단계. 0이면 콤보가 시작되지 않은 상태다.
        // 실제 단계 확정은 OnAttackStart Animation Event에서 한다.
        [Header("현재 콤보")]
        [SerializeField] private int comboStep = 0;
        [SerializeField] private int finisherStep = 3;

        // 스킬 입력은 1~4번이다(입력 액션 수, HUD 쿨타임 슬롯 수와 짝을 이룬다).
        // skillReadyTime[0]은 쓰지 않는 더미 슬롯.
        private const int MaxSkillNumber = 4;

        // 우클릭 강공격은 캐릭터 공용 행(SKILL_RCLICK)을 쓴다.
        private const int StrongAttackSkillId = 3;

        // 어떤 스킬이 시전 중 '완전 무적'을 주는지는 SkillTable.Invincible(데이터)로 정한다(예: 각성기).
        // 다른 스킬의 슈퍼아머(데미지는 받되 경직만 생략, Player.OnDamaged)와 달리 피격 자체를 씹는다
        // (구르기와 같은 SetInvincible 토글 재사용). 해제는 스킬이 '진짜로 끝나는' 시점
        // (ResetCombo/CancelAction/UnlockMovement)과 무적이 아닌 다음 액션 진입(UseSkill·OnAttackStart)에만
        // 한다 — AttackCancelBehaviour가 캔슬 창에서 부르는 EndSkillCast(클립 중간)에는 풀지 않아,
        // 후딜까지 무적이 이어진다("스킬 끝난 다음 무적 해제" 기획).

        // 이 캐릭터의 스킬 행 ID를 SkillId 오름차순으로 캐싱한다(스킬 번호 n → [n-1]).
        // PlayerStats.SkillSetPrefix로 걸러 만들기 때문에, 스킬이 늘거나 ID 체계가 바뀌어도
        // 코드가 아니라 테이블만 고치면 된다. 테이블 로딩이 끝난 뒤 첫 사용 시 1회 구축한다.
        private int[] characterSkillIds;

        [Header("강공격")]
        // 테이블(SkillId 3) 조회에 실패했을 때만 쓰는 폴백 쿨타임.
        [SerializeField] private float strongAttackCooldown = 3f;

        private float strongAttackReadyTime;

        // 매 타격마다 할당이 생기지 않도록 NonAlloc 쿼리용 버퍼를 재사용한다.
        private readonly Collider[] buffer = new Collider[64];
        private PlayerAnimation anim;
        private PlayerInputHandler input;

        // 구르기·피격·사망 중 뒤늦게 도착한 검기 발사 이벤트를 무시하기 위해 상태를 조회할 중앙 컨텍스트.
        private Player player;

        // 콤보 창이 열리기 전에 들어온 공격 입력을 기억해 다음 타로 넘긴다.
        private bool attackBuffered;
        private float[] skillReadyTime;

        // 스킬 시전 중에는 일반 공격 입력을 막기 위해 Player가 확인하는 플래그.
        public bool IsCastingSkill { get; private set; }

        /// <summary>
        /// 지금 슈퍼아머가 걸리는 동작(강공격·스킬, 궁 포함) 중인지. 대시 공격·점프 공격은 IsCastingSkill을 켜지만
        /// 여기서는 제외한다(그것들은 CombatAction이 StrongAttack, Skill이 아니다). 피격 시 슈퍼아머 판정에 쓴다 —
        /// 강공격·스킬 시전 중에만 약한 피격을 흘리고, 그 외 공격(일반/대시/점프) 중에는 정상적으로 경직된다.
        /// Player.OnDamaged가 이 값과 강피격 여부(LastHitWasStrong)를 함께 보고 경직 진입을 가른다.
        /// </summary>
        public bool IsSuperArmorMove
        {
            get
            {
                if (currentAction == CombatAction.Skill || currentAction == CombatAction.StrongAttack)
                    return true;
                return false;
            }
        }

        /// <summary>
        /// 각성기(4번 스킬) 시전 중인지 확인. 시전 동안 몬스터 소프트 분리를 끄는 데 쓴다.
        /// (Player.PassThroughEnemies가 구르기·공중대시와 함께 읽어 그 프레임 소프트 분리를 건너뛴다.)
        /// </summary>
        public bool IsCastingUltimate => currentAction == CombatAction.Skill && currentSkillNumber == 4;

        /// <summary>
        /// 지금 '무적을 주는 스킬'(SkillTable.Invincible = 각성기)을 시전 중인지.
        /// 회피(구르기·공중대시)가 이 스킬을 캔슬하지 못하게 막는 데 쓴다 — 각성기는 회피로 끊기지 않는다(기획).
        /// 무적 스킬이 아니면(스킬 1~3) false라 기존의 '회피 최우선 캔슬'이 그대로 동작한다.
        /// 스킬이 진짜로 끝나 무적이 해제되면(ReleaseSkillInvincibility 등) 자동으로 false가 되어 회피가 다시 열린다.
        /// </summary>
        public bool IsCastingInvincibleSkill => skillGrantedInvincibility;

        // 평타(콤보) 전용 상태. IsCastingSkill은 스킬/강공격/대시·공중공격만 켜고 평타는 켜지 않아서,
        // 평타의 연계(캔슬) 창을 따로 둘 필요가 있다. 태그 기반 캐릭터의 강공격/스킬 라우팅이
        // "콤보 도중엔 캔슬 창이 열려야만 통과"를 판정할 때 Player가 읽는다.
        private bool inCombo;
        private bool comboCancelWindowOpen;

        // 콤보 입력 창을 열고 닫는 상태
        private bool comboWindowOpen;

        /// <summary>
        /// 콤보 입력 창이 열려 있는지. Player가 읽어야함
        /// </summary>
        public bool ComboWindowOpen => comboWindowOpen;

        /// <summary>
        /// 지금 평타 콤보 체인에 진입해 있는지. OnAttackStart(실제 타수 진입)에 켜지고,
        /// ResetCombo/CancelAction 또는 공격 태그 이탈(Player가 SetInCombo(false)) 시 꺼진다.
        /// </summary>
        public bool InCombo => inCombo;

        /// <summary>현재 평타 캔슬 창이 열려 있는지. 강공격/스킬로의 콤보 캔슬 허용 판정에 쓴다.</summary>
        public bool ComboCancelWindowOpen => comboCancelWindowOpen;

        /// <summary>
        /// 평타(콤보) 캔슬 창을 연다. Attack_1~3 State에 붙는 AttackCancelBehaviour(SMB)가
        /// 진행도를 넘는 순간 호출한다. 스킬/강공격 State에 붙은 같은 SMB가 호출해도
        /// 그때는 inCombo가 false라 무해하다.
        /// </summary>
        public void OpenComboCancelWindow() => comboCancelWindowOpen = true;

        /// <summary>
        /// 콤보 진입 상태를 외부(Player)가 강제로 내린다. 공격 태그를 벗어나면(로코모션 복귀·회피 등)
        /// 콤보도 끝난 것이므로 Player가 매 프레임 판정해 호출한다.
        /// </summary>
        public void SetInCombo(bool value) => inCombo = value;

        // Animation Event는 취소된 클립이 블렌드 아웃되는 동안에도 늦게 도착할 수 있다.
        // 현재 액션과 이벤트 키가 일치할 때만 판정을 허용해 이전 액션의 유령 타격을 막는다.
        private CombatAction currentAction;
        private int currentSkillNumber;

        // 무적 스킬(SkillTable.Invincible)이 무적을 켰는지 추적한다. 무적 소스가 여럿(구르기 수동/잔여, 스킬)
        // 이라, 우리가 켠 것만 우리가 내리려고 표시해 둔다 → 구르기가 켠 SetInvincible을 실수로 끄지 않는다.
        private bool skillGrantedInvincibility;

        /// <summary>
        /// 콤보 타수가 실제로 시작될 때(OnAttackStart Animation Event) 발행된다.
        /// 꾹 누르기 콤보는 클릭 이벤트(Player.OnAttack)를 거치지 않고 이어지므로,
        /// Player가 이 이벤트로 이동 잠금을 타수마다 갱신한다. 없으면 홀드 콤보 중
        /// 안전장치 타이머나 로코모션 복귀(콤보 루프)로 잠금이 풀린다.
        /// </summary>
        public event Action ComboStepStarted;

        /// <summary>
        /// 공격/스킬이 대상 하나에 적중할 때마다 발행된다(광역이면 대상 수만큼).
        /// 인자는 이 적중으로 회복할 스킬 게이지(SG) 양 — SkillTable의 SgGain에서 온다(평타 +5, 우클릭 +20).
        /// Player가 받아 PlayerStats.GainSkillGauge로 연결한다.
        /// </summary>
        public event Action<float> TargetHit;

        private void Awake()
        {
            anim = GetComponent<PlayerAnimation>();
            input = GetComponent<PlayerInputHandler>();
            player = GetComponent<Player>();
            skillReadyTime = new float[MaxSkillNumber + 1];
            relayProjectileHit = gain => TargetHit?.Invoke(gain);

            if (attackHitBoxes != null)
            {
                foreach (HitBoxSlot slot in attackHitBoxes)
                {
                    if (slot == null || string.IsNullOrEmpty(slot.key)) continue;

                    // 언더바 표기 차이를 흡수하려 정규화한 키로 등록·조회한다(AnimationEventKey 참조).
                    string normKey = AnimationEventKey.Normalize(slot.key);

                    // 키 중복을 조용히 덮어쓰면 한쪽 판정이 영영 안 나가 원인 찾기 어렵다 → 경고.
                    if (hitBoxMap.ContainsKey(normKey))
                    {
                        Debug.LogWarning($"Duplicate hit box key '{slot.key}'. Only the first slot is used.", this);
                        continue;
                    }

                    hitBoxMap.Add(normKey, slot);
                }
            }

            if (projectileSlots != null)
            {
                foreach (ProjectileSlot slot in projectileSlots)
                {
                    if (slot == null || string.IsNullOrEmpty(slot.key)) continue;

                    // 히트 박스와 같은 방침: 정규화한 키로 등록하고, 키 중복은 경고 후 첫 슬롯만 쓴다.
                    string normKey = AnimationEventKey.Normalize(slot.key);
                    if (projectileMap.ContainsKey(normKey))
                    {
                        Debug.LogWarning($"Duplicate projectile key '{slot.key}'. Only the first slot is used.", this);
                        continue;
                    }

                    projectileMap.Add(normKey, slot);
                }
            }
        }

        public bool CanUseSkill(int n)
        {
            if (n < 1 || n > MaxSkillNumber) return false;
            if (Time.time < skillReadyTime[n]) return false;

            // 테이블이 아직 없으면 쿨타임·소모량을 알 수 없다. 0 쿨타임·0 소모로 발동시키면
            // 로딩 중에만 스킬이 공짜가 되므로, 데이터가 준비될 때까지 막는다.
            int skillId = GetSkillId(n);
            return skillId != 0 && SkillState.IsUnlocked(skillId) && GetSkillRow(skillId) != null;
        }

        public float GetRemainingCooldown(int n)
        {
            if (n < 1 || n > MaxSkillNumber) return 0f;
            return Mathf.Max(0f, skillReadyTime[n] - Time.time);
        }

        /// <summary>n번 스킬의 게이지(SG) 소모량. 범위를 벗어나거나 행이 없으면 0을 돌려준다.</summary>
        public float GetSkillGaugeCost(int n)
        {
            if (n < 1 || n > MaxSkillNumber) return 0f;

            int skillId = GetSkillId(n);
            if (skillId == 0) return 0f;

            SkillTable skill = GetSkillRow(skillId);
            return skill != null ? skill.SgCost : 0f;
        }

        // n = 눌린 단축키(1~4). 등록(로드아웃)으로 키와 스킬이 분리됐으므로 안에서 스킬 고유 번호로 환산한다.
        public bool UseSkill(int n)
        {
            if (!CanUseSkill(n)) return false;

            int skillId = GetSkillId(n);
            SkillTable skill = GetSkillRow(skillId);
            if (skill == null) return false;

            // ★ 애니메이션·히트박스·각성기 판정은 스킬 '고유 번호'(s)를 써야 등록을 바꿔도 실제 스킬이 바뀐다.
            //   (히트박스 이벤트 키가 Skill{s}, 애니 State가 스킬 s의 클립이므로.) 누른 키(n)를 그대로 쓰면
            //   데이터만 바뀌고 모션·타격은 그 키의 원래 스킬로 나간다. 쿨타임 배열·HUD 표시는 키(n) 기준으로 둔다.
            int s = IntrinsicSkillNumber(skillId);
            if (s <= 0) return false;

            // 실제 발동에 성공했을 때만 쿨타임과 시전 상태를 시작한다.
            // 실패한 스킬 입력은 이동 잠금으로 이어지면 안 된다.
            skillReadyTime[n] = Time.time + skill.Cooldown;
            IsCastingSkill = true;
            currentAction = CombatAction.Skill;
            currentSkillNumber = s;

            // 무적 스킬(SkillTable.Invincible)이면 시전 동안 무적을 켠다. 무적이 아닌 스킬(캔슬 창에서
            // 다른 스킬로 이어감 포함)이면 직전 무적이 남아 있었어도 여기서 꺼진다.
            SetSkillInvincibility(skill.Invincible);

            // TODO(sound): 스킬 시전음 — 스킬 s별로 다름. SoundManager.Instance.PlaySFX(<스킬 s 시전 SFX>); (각성기=4는 별도 컷인 음)
            anim.PlaySkill(s);

            // UI(쿨타임 표시)가 이 신호로 카운트다운을 시작한다. 발동 성공 시에만 발행. HUD 슬롯은 누른 키(n).
            PlayerEvents.FireSkillUsed(n, skill.Cooldown);
            return true;
        }

        // 스킬 번호(1~)를 실제 발동할 SkillTable 행 ID로 바꾼다. 플레이어가 등록한 로드아웃(단축키 배치)을
        // 우선 따르고, 아직 로드아웃이 없으면(빈 슬롯) 기존 고정 순서로 폴백한다. 해당 번호 스킬이 없으면 0.
        private int GetSkillId(int n)
        {
            int mapped = SkillState.GetSlot(n);
            if (mapped != 0) return mapped;

            // 빈 슬롯(0): 로드아웃이 준비된 뒤라면 '의도적으로 비운 슬롯'이므로 스킬 없음(0) → 발동·쿨타임 없음.
            // 아직 초기화 전(데이터/캐릭터 미준비)에만 기존 고정 순서로 폴백한다.
            if (SkillState.IsLoadoutReady) return 0;

            int[] ids = GetCharacterSkillIds();
            if (ids == null || n < 1 || n > ids.Length) return 0;

            return ids[n - 1];
        }

        // 스킬ID를 그 스킬의 '고유 번호'(1~)로 바꾼다 = 이 캐릭터 스킬 목록(SkillId 오름차순)에서의 순번.
        // 애니메이션 State·히트박스 이벤트 키(Skill{n})·각성기 판정이 이 번호를 기준으로 저작돼 있어,
        // 단축키에 어떤 스킬을 등록하든 스킬 본래의 모션·타격이 나가게 하는 환산이다. 없으면 0.
        private int IntrinsicSkillNumber(int skillId)
        {
            int[] ids = GetCharacterSkillIds();
            if (ids == null) return 0;

            for (int i = 0; i < ids.Length; i++)
                if (ids[i] == skillId) return i + 1;

            return 0;
        }

        // 이 캐릭터의 스킬 행 ID 목록을 만든다(SkillId 오름차순 = 스킬 1, 2, 3, 궁 순서).
        // NameKey 접두사로 거르므로 평타·우클릭 같은 캐릭터 공용 행은 자연히 빠지고,
        // "SW_ULTIMATE"처럼 이름 규칙이 다른 행도 특별 취급 없이 포함된다.
        private int[] GetCharacterSkillIds()
        {
            if (characterSkillIds != null) return characterSkillIds;

            JsonManager json = JsonManager.Instance;
            if (json == null || !json.IsReady) return null;

            string prefix = player.Stats.SkillSetPrefix;
            if (string.IsNullOrEmpty(prefix)) return null;

            string filter = prefix + "_";
            List<int> ids = new List<int>();

            foreach (KeyValuePair<int, SkillTable> pair in json.SkillDict)
            {
                if (pair.Value.NameKey != null
                    && pair.Value.NameKey.StartsWith(filter, StringComparison.Ordinal))
                {
                    ids.Add(pair.Key);
                }
            }

            // 딕셔너리 순회 순서는 보장되지 않으므로 ID로 정렬해 스킬 번호와 짝을 고정한다.
            ids.Sort();
            characterSkillIds = ids.ToArray();

            if (characterSkillIds.Length == 0)
                Debug.LogWarning($"SkillTable에 '{filter}'로 시작하는 스킬 행이 없습니다.", this);

            return characterSkillIds;
        }

        // 없는 행은 경고만 남기고 null을 돌려준다. 호출측이 "발동 안 함/판정 안 함"을 정한다.
        // 로딩 중(IsReady=false)에는 경고 없이 null 이다 — 아직 없는 게 정상이기 때문.
        private SkillTable GetSkillRow(int skillId)
        {
            JsonManager json = JsonManager.Instance;
            if (json == null || !json.IsReady) return null;

            SkillTable skill = json.Get<SkillTable>(skillId);
            if (skill == null)
                Debug.LogWarning($"SkillTable에 SkillId {skillId} 행이 없습니다.", this);

            return skill;
        }

        /// <summary>
        /// SkillTable 행과 현재 플레이어 스탯을 합쳐 데미지 계산 입력을 만든다.
        /// 관통·피해량증가%·보스추가뎀%는 장비·패시브가 미구현이라 0으로 둔다.
        /// </summary>
        /// <param name="skillId">참조할 SkillTable 행 ID</param>
        /// <param name="attack">계산기에 넘길 공격자 정보</param>
        /// <param name="gaugeGain">적중 1회당 회복할 스킬 게이지(SG)</param>
        /// <returns>행을 찾았으면 true. false면 판정 자체를 건너뛴다(0 데미지로 때리는 것보다 원인이 드러난다).</returns>
        private bool TryBuildAttack(int skillId, out AttackContext attack, out float gaugeGain)
        {
            attack = default;
            gaugeGain = 0f;

            SkillTable skill = GetSkillRow(skillId);
            if (skill == null) return false;

            PlayerStats stats = player.Stats;

            // 스킬 계수는 기본값(SkillTable) × 레벨 성장 배율(SkillGrowthTable)이다. 성장행이 없는
            // 평타·강공격이나 세이브 전(레벨=1)에는 배율 1.0이라 기본값 그대로 나간다.
            float coef = skill.Coef * SkillProgress.GetCoefMultiplier(skillId);

            attack = new AttackContext
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
                GroggyDamage = skill.GroggyDamage,   // 스킬 테이블의 그로기 데미지를 그대로 실어 보낸다(평타는 0)
            };

            gaugeGain = skill.SgGain;
            return true;
        }

        public void EndSkillCast() => IsCastingSkill = false;

        /// <summary>
        /// 각성기(4번 스킬)가 켠 무적을 해제한다. 스킬 시전이 '진짜로 끝나는' 시점(로코모션 복귀 =
        /// <see cref="Player.UnlockMovement"/>)에 Player가 호출한다. 캔슬 창의 <see cref="EndSkillCast"/>와
        /// 분리한 것이 핵심 — 캔슬 창(클립 중간)에는 해제하지 않고 후딜까지 무적을 유지하기 위함이다.
        /// 각성기가 아니었으면(플래그 미설정) no-op이라 아무 스킬 종료에나 호출해도 안전하다.
        /// </summary>
        public void ReleaseSkillInvincibility() => SetSkillInvincibility(false);

        // 각성기 무적을 켜고 끈다. 이미 목표 상태면 no-op이라, 매 입력·매 종료 경로에서 불러도
        // 다른 무적 소스(구르기 SetInvincible/잔여 타이머)를 건드리지 않는다. player.Stats가 실제 무적
        // 상태를 소유하고, 여기서는 '우리가 켰다'는 사실만 skillGrantedInvincibility로 함께 관리한다.
        private void SetSkillInvincibility(bool on)
        {
            if (skillGrantedInvincibility == on) return;

            skillGrantedInvincibility = on;
            player.Stats.SetInvincible(on);
        }

        /// <summary>
        /// 현재 씬의 투사체 스포너를 주입한다. 스포너는 원칙상 '씬에 하나 두고 프리팹별 풀을 내부 관리'하는
        /// 씬 단위 서비스라(씬 전환 시 일괄 Release) 플레이어 프리팹에 넣지 않는다. 그래서 씬을 넘어 유지되는
        /// 플레이어는 씬 진입마다 PlayerManager가 이 메서드로 현재 씬의 스포너로 다시 연결한다.
        /// 비전투 씬(마을 등)엔 스포너가 없어 null이 들어올 수 있고, 그땐 투사체 발사가 조용히 무시된다.
        /// </summary>
        public void SetProjectileSpawner(ProjectileSpawner spawner) => projectileSpawner = spawner;

        /// <summary>우클릭 강공격이 쿨타임을 벗어나 사용 가능한지 여부.</summary>
        public bool CanUseStrongAttack => Time.time >= strongAttackReadyTime;

        /// <summary>강공격의 남은 쿨타임(초). HUD 표시용.</summary>
        public float GetStrongAttackRemainingCooldown() => Mathf.Max(0f, strongAttackReadyTime - Time.time);

        /// <summary>
        /// 우클릭 강공격을 발동한다. 좌클릭 콤보 도중이면 콤보를 캔슬하고 우선 발동한다(기획).
        /// 스킬과 마찬가지로 실제 발동에 성공했을 때만 쿨타임을 소모한다.
        /// </summary>
        public bool UseStrongAttack()
        {
            if (!CanUseStrongAttack) return false;

            // 진행 중이던 콤보/입력 버퍼/래치된 Attack 트리거를 정리하고 발동한다.
            // 안 하면 캔슬된 콤보의 트리거가 남아 강공격 직후 일반 공격이 저절로 나간다.
            CancelAction();

            SkillTable strongAttack = GetSkillRow(StrongAttackSkillId);
            strongAttackReadyTime = Time.time + (strongAttack != null ? strongAttack.Cooldown : strongAttackCooldown);
            // 시전 중 좌클릭 차단은 스킬과 같은 규칙(IsCastingSkill)을 재사용한다.
            // 해제는 로코모션 복귀(ComboResetBehaviour→ResetCombo) 또는 안전장치 경로가 담당.
            IsCastingSkill = true;
            currentAction = CombatAction.StrongAttack;
            // TODO(sound): 강공격(우클릭) 시전음 — SoundManager.Instance.PlaySFX(<강공격 SFX>);
            anim.PlayStrongAttack();
            return true;
        }

        /// <summary>
        /// 달리기 공격(단타)을 발동한다. 콤보로 이어지지 않으며(기획),
        /// 시전 중 클릭 차단·해제는 강공격과 같은 규칙(IsCastingSkill)을 재사용한다.
        /// </summary>
        public void UseRunAttack()
        {
            // 더블탭 직후 콤보 잔여 상태가 남아 있을 수 있으므로 정리하고 발동한다.
            CancelAction();
            IsCastingSkill = true;
            currentAction = CombatAction.RunAttack;
            // TODO(sound): 달리기 공격 시전음 — SoundManager.Instance.PlaySFX(<러시 공격 SFX>);
            anim.PlayRunAttack();
        }

        /// <summary>
        /// 점프 공격(단타)을 발동한다. 공중 클릭 시 Player가 라우팅하며,
        /// '점프 1회당 1회' 제한과 호버링(높이 고정)은 Player가 관리한다.
        /// </summary>
        public void UseJumpAttack()
        {
            CancelAction();
            IsCastingSkill = true;
            currentAction = CombatAction.JumpAttack;
            // TODO(sound): 점프 공격 시전음 — SoundManager.Instance.PlaySFX(<점프 공격 SFX>);
            anim.PlayJumpAttack();
        }

        public void OnHitFrame(string key)
        {
            // Animation Event의 인자 실수(오타·빈칸)는 플레이를 멈추지 않고 경고만 남긴다.
            // 조회는 정규화 키로 한다 → 클립이 "Attack_1", 인스펙터가 "Attack1"이어도 맞물린다.
            if (string.IsNullOrEmpty(key) || !hitBoxMap.TryGetValue(AnimationEventKey.Normalize(key), out HitBoxSlot slot))
            {
                Debug.LogWarning($"Hit box key not found ('{key}'). Check the Animation Event string.", this);
                return;
            }

            // 구르기·피격·사망 또는 다른 공격으로 이미 취소된 클립에서 뒤늦게 온 이벤트는 무시한다.
            if (!CanApplyHitFrame(key)) return;

            Transform box = slot.area;
            if (box == null)
            {
                Debug.LogWarning($"Hit box transform is missing ('{key}').", this);
                return;
            }

            // 계수·랜덤 범위·게이지 회복량은 슬롯이 가리키는 SkillTable 행에서 온다.
            // 대상 루프 밖에서 한 번만 만든다(스탯은 타격 내내 같다).
            if (!TryBuildAttack(slot.skillId, out AttackContext attack, out float gaugeGain)) return;

            int count = Physics.OverlapBoxNonAlloc(
                box.position,
                box.lossyScale * 0.5f,
                buffer,
                box.rotation,
                enemyMask);

            if (count == buffer.Length)
                Debug.LogWarning($"Hit buffer is full ({count}). Some targets may have been skipped.", this);

            for (int i = 0; i < count; i++)
            {
                // 대상 쪽은 IDamageable 계약만 알면 된다. 적 종류별 HP 구현은 여기서 몰라도 된다.
                if (buffer[i].TryGetComponent<IDamageable>(out var target))
                {
                    // 방어 경감은 맞는 쪽 방어도로 계산되므로 대상마다 따로 굴린다.
                    // 랜덤 편차·치명타도 대상별로 굴러간다(광역이면 적마다 다른 숫자가 뜬다).
                    DamageResult result = DamageCalculator.Calculate(in attack, target.Defense, target.IsBoss);

                    // 데미지가 씹힌 타격(이미 죽은 적 등)은 이펙트도 게이지 회복도 없다.
                    // 시체 타격으로 스킬 게이지를 채우는 악용을 막는 효과도 겸한다.
                    if (!target.TakeDamage(in result)) continue;

                    // 강공격 데미지가 들어간 대상을 공중으로 띄운다.
                    // currentAction으로 강공격 행동이 맞는지 확인하고,
                    // 대상이 ILaunchable를 가지고 있는지 확인하여 launchable 변수에 담은 뒤 Launch()를 실행한다.
                    if (currentAction == CombatAction.StrongAttack && buffer[i].TryGetComponent<ILaunchable>(out var launchable))
                        launchable.Launch();

                    // 맞은 부위 접점은 히트 판정을 한 여기(때린 쪽)만 알 수 있다.
                    // 콜라이더 표면에서 히트박스 중심에 가장 가까운 점 = 실제 맞은 부위 근사치.
                    // key를 함께 보내 공격마다 다른 타격 이펙트를 고를 수 있게 한다.
                    CombatEvents.FirePlayerHitLanded(buffer[i].ClosestPoint(box.position), key);

                    // 적중 1회당 1번 발행 → 광역 다수 적중이면 게이지도 그만큼 회복된다.
                    // ★ 설계 수치 시트에는 우클릭 SG가 "+20 / 사용당"으로 적혀 있지만,
                    //   적중당이 맞다고 확정됐다(2026-07-23). 시트 표기를 근거로 "사용 1회당 1번"으로
                    //   바꾸지 말 것 — 광역으로 여러 마리를 맞히면 그만큼 차는 것이 의도다.
                    TargetHit?.Invoke(gaugeGain);
                }
            }
        }

        /// <summary>
        /// 스킬 클립의 Animation Event가 검기를 내보내는 프레임에 호출한다. 인자는 투사체 슬롯 키.
        /// 판정 결과가 이펙트·게이지 회복으로 이어지는 흐름은 OnHitFrame과 같고,
        /// 판정 주체만 히트 박스에서 날아가는 투사체로 바뀐다.
        /// </summary>
        public void OnProjectileFrame(string key)
        {
            // Animation Event의 인자 실수(오타·빈칸)는 플레이를 멈추지 않고 경고만 남긴다.
            // 조회는 정규화 키로 한다(히트박스와 같은 방침).
            if (string.IsNullOrEmpty(key) || !projectileMap.TryGetValue(AnimationEventKey.Normalize(key), out ProjectileSlot slot))
            {
                Debug.LogWarning($"Projectile key not found ('{key}'). Check the Animation Event string.", this);
                return;
            }

            if (slot.muzzle == null)
            {
                Debug.LogWarning($"Projectile muzzle is missing ('{key}').", this);
                return;
            }

            if (slot.prefab == null)
            {
                Debug.LogWarning($"Projectile prefab is not assigned ('{key}').", this);
                return;
            }

            if (projectileSpawner == null)
            {
                Debug.LogWarning("ProjectileSpawner is not assigned.", this);
                return;
            }

            // 구르기·피격 등으로 스킬이 캔슬되면 IsCastingSkill이 꺼진다.
            // 스킬 클립이 블렌드 아웃되며 이 이벤트가 뒤늦게 도착해도 검기가 나가지 않게 막는다.
            if (!IsCastingSkill) return;

            // 사망 등 IsCastingSkill이 남아 있을 수 있는 중단 경로까지 이펙트와 같은 기준으로 막는다.
            if (player.IsActionInterrupted) return;

            // 다른 스킬이나 강공격으로 액션이 교체된 뒤 이전 스킬 이벤트가 도착하는 경우도 차단한다.
            if (currentAction != CombatAction.Skill || !IsCurrentSkillKey(key)) return;

            // 투사체는 발사 후에 적을 만나므로, 완성된 데미지 숫자가 아니라 계산 재료를 들려 보낸다.
            // 방어 경감은 맞는 대상마다 달라 발사 시점에는 최종 피해를 알 수 없기 때문이다.
            if (!TryBuildAttack(slot.skillId, out AttackContext attack, out float gaugeGain)) return;

            // muzzle 방향에 슬롯별 회전 오프셋을 더해 검기 방향(가로/세로/대각)을 맞춘다.
            Quaternion rotation = slot.muzzle.rotation * Quaternion.Euler(slot.rotationOffset);

            projectileSpawner.Fire(
                slot.prefab,
                slot.muzzle.position,
                rotation,
                in attack,
                gaugeGain,
                slot.canPierce,
                relayProjectileHit,
                key);
        }

    #if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (attackHitBoxes == null) return;

            Gizmos.color = Color.red;
            foreach (HitBoxSlot slot in attackHitBoxes)
            {
                if (slot == null || slot.area == null) continue;

                Gizmos.matrix = Matrix4x4.TRS(slot.area.position, slot.area.rotation, slot.area.lossyScale);
                Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
            }

            Gizmos.matrix = Matrix4x4.identity;
        }
    #endif

        public bool OnAttackInput()
        {
            currentAction = CombatAction.Combo;
            attackBuffered = true;

            // 첫 타는 콤보 창을 기다릴 필요가 없으므로 즉시 트리거한다.
            // 이후 타수는 OnComboWindowOpen에서 버퍼/홀드 입력을 보고 이어간다.
            if (comboStep == 0)
            {
                anim.PlayAttackTrigger();
                attackBuffered = false;
                return false;
            }

            // 첫 타 이후 홀드, 선입력 콤보창이 열려있는 상태라면 공격을 이어간다.
            if (comboWindowOpen)
                return TryConsumeComboInput();
            return false;
        }

        public void OnAttackStart(int step)
        {
            // 캔슬된 콤보 클립의 시작 이벤트가 뒤늦게 도착해 현재 액션을 되돌리지 못하게 한다.
            if (IsCastingSkill || player.IsActionInterrupted) return;

            // 애니메이션이 실제로 해당 타수에 진입한 시점에 콤보 단계를 확정한다.
            currentAction = CombatAction.Combo;
            comboStep = step;

            // 각성기 캔슬 창에서 평타로 이어 나온 것이면, 각성기는 끝난 것 → 무적 해제.
            SetSkillInvincibility(false);

            // 새 타수 진입 = 콤보 체인 진입. 캔슬 창은 타수마다 다시 닫아, 그 State의
            // AttackCancelBehaviour가 자기 진행도로 다시 열게 한다(공격별 State 튜닝 원칙과 동일).
            inCombo = true;
            comboCancelWindowOpen = false;
            comboWindowOpen = false;

            // TODO(sound): 평타 휘두르는 소리(콤보 타수 step별로 달리 가능) — SoundManager.Instance.PlaySFX(<콤보 step SFX>);
            ComboStepStarted?.Invoke();
            DevLog.Log(comboStep);
        }

        public void ClearAttackBuffer()
        {
            anim.ResetAttackTrigger();
            attackBuffered = false;
            // 버퍼 비울 때 콤보 창도 닫는다.
            comboWindowOpen = false;
        }

        public void OnComboWindowOpen()
        {
            // 강공격/스킬이 콤보를 캔슬한 직후, 밀려나는 공격 클립의 이벤트가 블렌드 중에
            // 뒤늦게 도착할 수 있다. 시전 중 좌클릭 홀드로 Attack 트리거가 래치되어
            // 시전 종료 직후 일반 공격이 저절로 나가는 것을 막는다.
            if (IsCastingSkill) return;

            // 피니시는 창을 열지 않는다(다음 타 없음 → 유령 트리거 차단)
            //if (comboStep >= finisherStep && !input.AttackHeld) return;

            comboWindowOpen = true;

            // 홀드, 선입력 즉시 발동
            TryConsumeComboInput(); 
        }

        public bool TryConsumeComboInput()
        {
            if (!comboWindowOpen) return false;

            if(input.AttackHeld || attackBuffered)
            {
                anim.PlayAttackTrigger();
                attackBuffered = false;
                // 소비하면 콤보 창을 닫아 중복을 방지한다.
                comboWindowOpen = false;

                return true;
            }

            return false;
        }

        public void ResetCombo()
        {
            // Locomotion 복귀 시 호출된다. 콤보와 스킬 시전 상태를 모두 정리한다.
            comboStep = 0;
            currentAction = CombatAction.None;
            currentSkillNumber = 0;
            inCombo = false;
            comboCancelWindowOpen = false;
            comboWindowOpen = false;
            SetSkillInvincibility(false);   // 로코모션 복귀 = 각성기 진짜 종료 → 무적 해제
            EndSkillCast();
            DevLog.Log(comboStep);
        }

        public void EndComboChain()
        {
            // 블렌드 완료(로코모션 완전 진입)에서 호출. 피니시(3타)로 끝났으면 하드 스톱.
            bool wasFinisher = comboStep >= finisherStep;

            ResetCombo();
            OnComboWindowOpen();
            //if (!wasFinisher)
            //    OnComboWindowOpen();
        }

        /// <summary>
        /// 진행 중인 공격/스킬을 강제 중단한다. 구르기(회피 캔슬)처럼
        /// 애니메이션이 끝나기 전에 다른 동작이 끼어들 때 호출된다.
        /// ResetCombo와 달리 입력 버퍼와 래치된 Attack 트리거까지 비운다
        /// → 캔슬 직전의 클릭이 캔슬 후 엉뚱한 타이밍에 발동하는 것을 막는다.
        /// </summary>
        public void CancelAction()
        {
            comboStep = 0;
            attackBuffered = false;
            currentAction = CombatAction.None;
            currentSkillNumber = 0;
            inCombo = false;
            comboCancelWindowOpen = false;
            comboWindowOpen = false;
            SetSkillInvincibility(false);   // 구르기·피격 등 강제 중단 시에도 각성기 무적을 반드시 내린다
            EndSkillCast();
            ClearAttackBuffer();

            // 강공격·달리기 공격 트리거도 래치될 수 있다(ClearAttackBuffer는 일반 Attack만 지운다).
            // 피격·구르기로 캔슬된 뒤 이 둘이 남아 유령 발동하는 것을 막는다.
            anim.ResetStrongAttackTrigger();
            anim.ResetRunAttackTrigger();
        }

        private bool CanApplyHitFrame(string key)
        {
            if (player == null || player.IsActionInterrupted) return false;

            // 단타/스킬은 IsCastingSkill이 액션 수명 플래그다. 안전장치 타이머 등으로
            // 시전이 종료된 뒤 도착한 이벤트가 currentAction 값만 보고 통과하지 못하게 한다.
            if (currentAction != CombatAction.Combo && !IsCastingSkill) return false;

            // 정규화 키로 비교한다 → 언더바 표기 차이(Attack_1 vs Attack1, Strong_Attack vs StrongAttack)를 흡수.
            // 비교 대상 리터럴은 정규화형(언더바 없음)으로 적는다.
            string nk = AnimationEventKey.Normalize(key);
            return currentAction switch
            {
                CombatAction.Combo => comboStep switch
                {
                    1 => nk == "Attack1",
                    2 => nk == "Attack2",
                    3 => nk == "Attack3",
                    _ => false,
                },
                CombatAction.Skill => IsCurrentSkillKey(key),
                CombatAction.StrongAttack => nk == "StrongAttack",
                CombatAction.RunAttack => nk == "RunAttack",
                CombatAction.JumpAttack => nk.StartsWith("Jump", StringComparison.Ordinal),
                _ => false,
            };
        }

        private bool IsCurrentSkillKey(string key)
        {
            if (currentSkillNumber <= 0 || string.IsNullOrEmpty(key)) return false;

            // 애님 이벤트 키의 두 표기를 모두 허용한다.
            //  - 투사체/히트박스 기존 표기: "Skill3", "Skill3_1"
            //  - 이펙트(OnEffect)와 같은 표기: "Skill_3", "Skill_3_1"
            // OnEffect는 게이트 없이 "Skill_N_M"을 쓰는데, 같은 클립의 OnProjectileFrame/OnHitFrame에
            // 그 표기를 그대로 적으면 조용히 막히던 함정을 없애기 위함이다(한 클립의 이벤트 인자 표기를 통일).
            return MatchesSkillPrefix(key, $"Skill{currentSkillNumber}")
                || MatchesSkillPrefix(key, $"Skill_{currentSkillNumber}");
        }

        // key가 정확히 prefix이거나 "prefix_"로 시작하는지. 언더바 경계를 요구해
        // Skill3이 Skill30_1 같은 다른 번호를 잘못 무는 것을 막는다.
        private static bool MatchesSkillPrefix(string key, string prefix)
            => key.Equals(prefix, StringComparison.Ordinal)
            || key.StartsWith(prefix + "_", StringComparison.Ordinal);
    }
}
