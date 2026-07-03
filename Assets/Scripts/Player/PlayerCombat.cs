using UnityEngine;

/// <summary>
/// 플레이어의 전투 진입점.
/// 입력을 직접 받지는 않고 Player가 호출하며, 이 클래스는 스킬 쿨타임,
/// 콤보 입력 버퍼, 애니메이션 이벤트 기반 히트 판정을 맡는다.
/// </summary>
[RequireComponent(typeof(PlayerAnimation))]
public class PlayerCombat : MonoBehaviour
{
    // 공격 클립의 Animation Event가 넘기는 인덱스로 사용할 히트 박스 목록.
    // 모션마다 판정 위치와 크기가 다르므로 Transform 단위로 분리한다.
    [SerializeField] private Transform[] attackHitBoxes;
    [SerializeField] private LayerMask enemyMask;

    // 현재 재생 중인 콤보 단계. 0이면 콤보가 시작되지 않은 상태다.
    // 실제 단계 확정은 OnAttackStart Animation Event에서 한다.
    [SerializeField] private int comboStep = 0;

    [Header("Skill Cooldown")]
    // 인덱스는 스킬 번호와 맞춘다. [0]은 사용하지 않는 더미 슬롯.
    [SerializeField] private float[] skillCooldowns = { 0f, 5f, 5f, 8f, 10f };

    // 매 타격마다 할당이 생기지 않도록 NonAlloc 쿼리용 버퍼를 재사용한다.
    private readonly Collider[] buffer = new Collider[64];
    private PlayerAnimation anim;
    private PlayerInputHandler input;

    // 콤보 창이 열리기 전에 들어온 공격 입력을 기억해 다음 타로 넘긴다.
    private bool attackBuffered;
    private float[] skillReadyTime;

    // 스킬 시전 중에는 일반 공격 입력을 막기 위해 Player가 확인하는 플래그.
    public bool IsCastingSkill { get; private set; }

    private void Awake()
    {
        anim = GetComponent<PlayerAnimation>();
        input = GetComponent<PlayerInputHandler>();
        skillReadyTime = new float[skillCooldowns.Length];
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

    public bool UseSkill(int n)
    {
        if (!CanUseSkill(n)) return false;

        // 실제 발동에 성공했을 때만 쿨타임과 시전 상태를 시작한다.
        // 실패한 스킬 입력은 이동 잠금으로 이어지면 안 된다.
        skillReadyTime[n] = Time.time + skillCooldowns[n];
        IsCastingSkill = true;
        anim.PlaySkill(n);
        return true;
    }

    public void EndSkillCast() => IsCastingSkill = false;

    public void OnHitFrame(int hitBoxIndex)
    {
        // Animation Event의 인자 실수는 플레이를 멈추지 않고 경고만 남긴다.
        if (attackHitBoxes == null || hitBoxIndex < 0 || hitBoxIndex >= attackHitBoxes.Length)
        {
            Debug.LogWarning($"Hit box index out of range ({hitBoxIndex}). Check the Animation Event value.", this);
            return;
        }

        Transform box = attackHitBoxes[hitBoxIndex];
        if (box == null)
        {
            Debug.LogWarning($"Hit box transform is missing ({hitBoxIndex}).", this);
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
                target.TakeDamage(10);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (attackHitBoxes == null) return;

        Gizmos.color = Color.red;
        foreach (Transform box in attackHitBoxes)
        {
            if (box == null) continue;

            Gizmos.matrix = Matrix4x4.TRS(box.position, box.rotation, box.lossyScale);
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
    }

    public void ClearAttackBuffer()
    {
        anim.ResetAttackTrigger();
    }

    public void OnComboWindowOpen()
    {
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
    }
}
