using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플레이어의 전투 진입점.
/// 입력을 직접 받지는 않고 Player가 호출하며, 이 클래스는 스킬 쿨타임,
/// 콤보 입력 버퍼, 애니메이션 이벤트 기반 히트 판정을 맡는다.
/// </summary>
[RequireComponent(typeof(PlayerAnimation))]
public class PlayerCombat : MonoBehaviour
{
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
        public int damage = 10;

        // 적중 1회당 회복되는 스킬 게이지(SG). 기획: 강공격은 일반 공격보다 많이 찬다
        // → 공격 종류 분기 대신 슬롯 값으로 표현한다(강공격 슬롯만 높게 설정).
        public float gaugeGain = 5f;
    }

    // 검기처럼 몸에서 떨어져 날아가는 판정은 히트 박스 대신 투사체로 내보낸다.
    // 슬롯 키 체계는 HitBoxSlot과 동일: 클립 Animation Event의 string 인자와 맞춘다.
    [Serializable]
    private class ProjectileSlot
    {
        public string key;

        // 발사 위치·방향 기준 Transform. 보통 캐릭터 가슴 높이의 자식 오브젝트를 쓴다.
        public Transform muzzle;
        public int damage = 15;
        public float gaugeGain = 5f;

        // 관통 여부. true면 경로 위 여러 적을 연속 타격, false면 첫 적중에 소멸한다.
        public bool canPierce = true;
    }

    [SerializeField] private HitBoxSlot[] attackHitBoxes;
    [SerializeField] private LayerMask enemyMask;

    [Header("투사체 스킬")]
    [SerializeField] private SwordWaveSpawner swordWaveSpawner;
    [SerializeField] private ProjectileSlot[] projectileSlots;

    // 매 히트 이벤트마다 배열을 뒤지지 않도록 Awake에서 1회 구축하는 조회용 사전.
    private readonly Dictionary<string, HitBoxSlot> hitBoxMap = new Dictionary<string, HitBoxSlot>();
    private readonly Dictionary<string, ProjectileSlot> projectileMap = new Dictionary<string, ProjectileSlot>();

    // 투사체 적중을 TargetHit으로 중계하는 캐시 델리게이트. 발사마다 람다를 새로 만들지 않기 위함.
    private Action<float> relayProjectileHit;

    // 현재 재생 중인 콤보 단계. 0이면 콤보가 시작되지 않은 상태다.
    // 실제 단계 확정은 OnAttackStart Animation Event에서 한다.
    [SerializeField] private int comboStep = 0;

    [Header("스킬 쿨타임")]
    // 인덱스는 스킬 번호와 맞춘다. [0]은 사용하지 않는 더미 슬롯.
    [SerializeField] private float[] skillCooldowns = { 0f, 5f, 5f, 8f, 10f };

    // 스킬별 게이지(SG) 소모량. skillCooldowns와 같은 규칙(인덱스 = 스킬 번호, [0]은 더미).
    // 잔량 판정·차감은 PlayerStats가 담당하고, 여기는 '얼마를 쓸지'만 보관한다.
    [SerializeField] private float[] skillGaugeCosts = { 0f, 25f, 25f, 25f, 25f };

    [Header("강공격")]
    // 우클릭 강공격 쿨타임. 스킬과 달리 단일 동작이라 배열 대신 단일 값으로 둔다.
    [SerializeField] private float strongAttackCooldown = 3f;

    private float strongAttackReadyTime;

    // 매 타격마다 할당이 생기지 않도록 NonAlloc 쿼리용 버퍼를 재사용한다.
    private readonly Collider[] buffer = new Collider[64];
    private PlayerAnimation anim;
    private PlayerInputHandler input;

    // 콤보 창이 열리기 전에 들어온 공격 입력을 기억해 다음 타로 넘긴다.
    private bool attackBuffered;
    private float[] skillReadyTime;

    // 스킬 시전 중에는 일반 공격 입력을 막기 위해 Player가 확인하는 플래그.
    public bool IsCastingSkill { get; private set; }

    /// <summary>
    /// 콤보 타수가 실제로 시작될 때(OnAttackStart Animation Event) 발행된다.
    /// 꾹 누르기 콤보는 클릭 이벤트(Player.OnAttack)를 거치지 않고 이어지므로,
    /// Player가 이 이벤트로 이동 잠금을 타수마다 갱신한다. 없으면 홀드 콤보 중
    /// 안전장치 타이머나 로코모션 복귀(콤보 루프)로 잠금이 풀린다.
    /// </summary>
    public event Action ComboStepStarted;

    /// <summary>
    /// 공격/스킬이 대상 하나에 적중할 때마다 발행된다(광역이면 대상 수만큼).
    /// 인자는 이 적중으로 회복할 스킬 게이지(SG) 양 — 히트박스 슬롯별로 다르다(강공격 > 일반).
    /// Player가 받아 PlayerStats.GainSkillGauge로 연결한다.
    /// </summary>
    public event Action<float> TargetHit;

    private void Awake()
    {
        anim = GetComponent<PlayerAnimation>();
        input = GetComponent<PlayerInputHandler>();
        skillReadyTime = new float[skillCooldowns.Length];
        relayProjectileHit = gain => TargetHit?.Invoke(gain);

        if (attackHitBoxes != null)
        {
            foreach (HitBoxSlot slot in attackHitBoxes)
            {
                if (slot == null || string.IsNullOrEmpty(slot.key)) continue;

                // 키 중복을 조용히 덮어쓰면 한쪽 판정이 영영 안 나가 원인 찾기 어렵다 → 경고.
                if (hitBoxMap.ContainsKey(slot.key))
                {
                    Debug.LogWarning($"Duplicate hit box key '{slot.key}'. Only the first slot is used.", this);
                    continue;
                }

                hitBoxMap.Add(slot.key, slot);
            }
        }

        if (projectileSlots != null)
        {
            foreach (ProjectileSlot slot in projectileSlots)
            {
                if (slot == null || string.IsNullOrEmpty(slot.key)) continue;

                // 히트 박스와 같은 방침: 키 중복은 경고를 남기고 첫 슬롯만 쓴다.
                if (projectileMap.ContainsKey(slot.key))
                {
                    Debug.LogWarning($"Duplicate projectile key '{slot.key}'. Only the first slot is used.", this);
                    continue;
                }

                projectileMap.Add(slot.key, slot);
            }
        }
    }

    public bool CanUseSkill(int n)
    {
        if (n < 1 || n >= skillCooldowns.Length) return false;
        return Time.time >= skillReadyTime[n];
    }

    public float GetRemainingCooldown(int n)
    {
        if (n < 1 || n >= skillCooldowns.Length) return 0f;
        return Mathf.Max(0f, skillReadyTime[n] - Time.time);
    }

    /// <summary>n번 스킬의 게이지(SG) 소모량. 범위를 벗어난 번호는 0을 돌려준다.</summary>
    public float GetSkillGaugeCost(int n)
    {
        if (n < 1 || n >= skillGaugeCosts.Length) return 0f;
        return skillGaugeCosts[n];
    }

    public bool UseSkill(int n)
    {
        if (!CanUseSkill(n)) return false;

        // 실제 발동에 성공했을 때만 쿨타임과 시전 상태를 시작한다.
        // 실패한 스킬 입력은 이동 잠금으로 이어지면 안 된다.
        skillReadyTime[n] = Time.time + skillCooldowns[n];
        IsCastingSkill = true;
        anim.PlaySkill(n);

        // UI(쿨타임 표시)가 이 신호로 카운트다운을 시작한다. 발동 성공 시에만 발행.
        PlayerEvents.FireSkillUsed(n, skillCooldowns[n]);
        return true;
    }

    public void EndSkillCast() => IsCastingSkill = false;

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

        strongAttackReadyTime = Time.time + strongAttackCooldown;
        // 시전 중 좌클릭 차단은 스킬과 같은 규칙(IsCastingSkill)을 재사용한다.
        // 해제는 로코모션 복귀(ComboResetBehaviour→ResetCombo) 또는 안전장치 경로가 담당.
        IsCastingSkill = true;
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
        anim.PlayJumpAttack();
    }

    public void OnHitFrame(string key)
    {
        // Animation Event의 인자 실수(오타·빈칸)는 플레이를 멈추지 않고 경고만 남긴다.
        if (string.IsNullOrEmpty(key) || !hitBoxMap.TryGetValue(key, out HitBoxSlot slot))
        {
            Debug.LogWarning($"Hit box key not found ('{key}'). Check the Animation Event string.", this);
            return;
        }

        Transform box = slot.area;
        if (box == null)
        {
            Debug.LogWarning($"Hit box transform is missing ('{key}').", this);
            return;
        }

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
                // 데미지가 씹힌 타격(이미 죽은 적 등)은 이펙트도 게이지 회복도 없다.
                // 시체 타격으로 스킬 게이지를 채우는 악용을 막는 효과도 겸한다.
                if (!target.TakeDamage(slot.damage)) continue;

                // 맞은 부위 접점은 히트 판정을 한 여기(때린 쪽)만 알 수 있다.
                // 콜라이더 표면에서 히트박스 중심에 가장 가까운 점 = 실제 맞은 부위 근사치.
                CombatEvents.FireHitLanded(buffer[i].ClosestPoint(box.position));

                // 적중 1회당 1번 발행 → 광역 다수 적중이면 게이지도 그만큼 회복된다(기획).
                TargetHit?.Invoke(slot.gaugeGain);
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
        if (string.IsNullOrEmpty(key) || !projectileMap.TryGetValue(key, out ProjectileSlot slot))
        {
            Debug.LogWarning($"Projectile key not found ('{key}'). Check the Animation Event string.", this);
            return;
        }

        if (slot.muzzle == null)
        {
            Debug.LogWarning($"Projectile muzzle is missing ('{key}').", this);
            return;
        }

        if (swordWaveSpawner == null)
        {
            Debug.LogWarning("SwordWaveSpawner is not assigned.", this);
            return;
        }

        swordWaveSpawner.Fire(
            slot.muzzle.position,
            slot.muzzle.rotation,
            slot.damage,
            slot.gaugeGain,
            slot.canPierce,
            relayProjectileHit);
    }

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

    public void OnAttackInput()
    {
        attackBuffered = true;

        // 첫 타는 콤보 창을 기다릴 필요가 없으므로 즉시 트리거한다.
        // 이후 타수는 OnComboWindowOpen에서 버퍼/홀드 입력을 보고 이어간다.
        if (comboStep == 0)
        {
            anim.PlayAttackTrigger();
            attackBuffered = false;
        }
    }

    public void OnAttackStart(int step)
    {
        // 애니메이션이 실제로 해당 타수에 진입한 시점에 콤보 단계를 확정한다.
        comboStep = step;
        ComboStepStarted?.Invoke();
        Debug.Log(comboStep);
    }

    public void ClearAttackBuffer()
    {
        anim.ResetAttackTrigger();
        attackBuffered = false;
    }

    public void OnComboWindowOpen()
    {
        // 강공격/스킬이 콤보를 캔슬한 직후, 밀려나는 공격 클립의 이벤트가 블렌드 중에
        // 뒤늦게 도착할 수 있다. 시전 중 좌클릭 홀드로 Attack 트리거가 래치되어
        // 시전 종료 직후 일반 공격이 저절로 나가는 것을 막는다.
        if (IsCastingSkill) return;

        // 짧게 누른 입력과 계속 누르고 있는 입력을 같은 규칙으로 처리한다.
        if (input.AttackHeld || attackBuffered)
            anim.PlayAttackTrigger();

        attackBuffered = false;
    }

    public void ResetCombo()
    {
        // Locomotion 복귀 시 호출된다. 콤보와 스킬 시전 상태를 모두 정리한다.
        comboStep = 0;
        EndSkillCast();
        Debug.Log(comboStep);
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
        EndSkillCast();
        ClearAttackBuffer();
    }
}
