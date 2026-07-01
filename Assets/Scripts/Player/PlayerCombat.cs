using UnityEngine;

/// <summary>
/// 플레이어 전투. 스킬 실행의 진입점이다.
/// 현재는 애니메이션 트리거만 넘기지만, 전투 로직(쿨다운·히트박스·데미지)이
/// 자라날 자리. Player는 이 진입점만 알고 내부 구현은 여기에 가둔다.
/// </summary>
[RequireComponent(typeof(PlayerAnimation))]
public class PlayerCombat : MonoBehaviour
{
    [SerializeField] private Transform hitOrigin;   // 판정 기준점(보통 무기 끝/캐릭터 앞)
    [SerializeField] private Transform hitBox;   // 칼에 맞게 늘린 큐브

    [SerializeField] private LayerMask enemyMask;   // "적" 레이어만 검사 → 불필요한 충돌 배제
    private readonly Collider[] buffer = new Collider[64];   // 미리 할당(GC 0)

    [SerializeField] private int comboStep = 0;        // 현재 콤보 단계 (0=아직 시작 안 함)
    const int comboMax = 4;

    private PlayerAnimation anim;
    private PlayerInputHandler input;
    private bool attackBuffered;   // 최근 클릭을 기억
    [SerializeField] private float radius;  //임시 범위

    // ── 스킬 쿨타임 ──────────────────────────────────────────────
    // 인덱스 = 스킬 번호(1~). [0]은 더미 → PlayerAnimation.Skill 배열과 동일한 규칙.
    // 인스펙터에서 스킬별 쿨타임(초)을 설정. 배열 길이가 유효 스킬 개수를 결정한다.
    [Header("Skill Cooldown")]
    [SerializeField] private float[] skillCooldowns = { 0f, 5f, 5f, 8f, 10f };
    private float[] skillReadyTime;   // 각 스킬이 다시 사용 가능해지는 시각(Time.time 기준)

    private void Awake()
    {
        anim = GetComponent<PlayerAnimation>();
        input = GetComponent<PlayerInputHandler>();
        skillReadyTime = new float[skillCooldowns.Length];   // 시작 시 전부 0 → 즉시 사용 가능
    }

    /// <summary>
    /// n번 스킬이 지금 사용 가능한지 여부(유효 번호 + 쿨타임 종료).
    /// </summary>
    /// <param name="n">스킬 번호(1~)</param>
    /// <returns>사용 가능하면 true</returns>
    public bool CanUseSkill(int n)
    {
        if (n < 1 || n >= skillCooldowns.Length) return false;   // 정의되지 않은 스킬 번호
        return Time.time >= skillReadyTime[n];                   // 쿨타임이 지났나
    }

    /// <summary>
    /// 남은 쿨타임(초). 사용 가능하면 0. HUD 표시 등에 사용.
    /// </summary>
    /// <param name="n">스킬 번호(1~)</param>
    /// <returns>남은 쿨타임(초), 사용 가능하면 0</returns>
    public float GetRemainingCooldown(int n)
    {
        if (n < 1 || n >= skillCooldowns.Length) return 0f;
        return Mathf.Max(0f, skillReadyTime[n] - Time.time);
    }

    /// <summary>
    /// n번 스킬 실행. 실제로 발동했으면 true, 쿨타임 중이거나 없는 스킬이면 false.
    /// 호출측(Player)은 이 반환값으로 이동 잠금 여부를 결정한다
    /// → 쿨타임 중엔 잠그지 않아 "스킬도 안 나가고 못 움직이는" 상황을 막는다.
    /// </summary>
    /// <param name="n">스킬 번호(1~)</param>
    /// <returns>실제로 발동했으면 true</returns>
    public bool UseSkill(int n)
    {
        if (!CanUseSkill(n)) return false;                  // 쿨타임 중/없는 스킬 → 발동 실패
        skillReadyTime[n] = Time.time + skillCooldowns[n];  // 쿨타임 시작
        anim.PlaySkill(n);
        return true;
    }

    // ★ 애니메이션 이벤트가 임팩트 프레임에 호출 (인스펙터에서 클립에 연결)
    public void OnHitFrame()
    {
        // 스킬 데이터에서 반경·데미지를 읽어오는 게 이상적(여기선 상수로 단순화)
        float radius = this.radius;

        // 구 형태 판정
        //int count = Physics.OverlapSphereNonAlloc(hitOrigin.position, radius, buffer, enemyMask);

        // 칼 크기에 맞게 큐브 형태 판정
        int count = Physics.OverlapBoxNonAlloc(
        hitBox.position,
        hitBox.lossyScale * 0.5f,   // 부모 1이라 지금은 localScale과 동일, 그래도 안전하게 lossy
        buffer,
        hitBox.rotation,
        enemyMask);

        // ★ 버퍼가 꽉 찼다 = 더 있었을 수도 있다 → 개발 중에 알아채게
        if (count == buffer.Length)
        {
            Debug.LogWarning($"히트 버퍼 가득참({count}). 누락 가능 → 버퍼 확대 검토", this);
            // 필요하면 여기서 버퍼를 2배로 키워 한 번 더 쿼리(아래 C 방식)
        }

        for (int i = 0; i < count; i++)
            if (buffer[i].TryGetComponent<IDamageable>(out var t))
                t.TakeDamage(10);
    }

    // 선택했을 때만 그림 → 씬에 플레이어 많아도 안 지저분함
    private void OnDrawGizmosSelected()
    {
        if (hitOrigin != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(hitOrigin.position, radius);
        }
        if (hitBox != null) 
        {
            Gizmos.matrix = Matrix4x4.TRS(hitBox.position, hitBox.rotation, hitBox.lossyScale);
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);  // matrix가 위치·회전·크기 다 처리
            Gizmos.matrix = Matrix4x4.identity;
        }
       
    }

    public void OnAttackInput()
    {
        attackBuffered = true;   // 즉시 발동 대신 "눌렀다" 기록
                                 // (단, 콤보 시작 전 첫 타는 즉시 나가야 하니 그 분기는 따로)
        if (comboStep == 0 || comboStep == comboMax)
            anim.PlayAttackTrigger();
    }

    // ★ 각 공격 클립 '시작' 프레임에 Animation Event로 호출. 클립마다 인자 1,2,3,4,5
    public void OnAttackStart(int step)
    {
        comboStep = step;   // 화면이 실제 그 타를 재생할 때 단계 확정 → 화면과 100% 일치
    }

    // Animation Event가 부를 진입점
    public void ClearAttackBuffer()
    {
        anim.ResetAttackTrigger();
    }

    public void OnComboWindowOpen()   // 각 공격 클립의 "다음 타 받기 시작" 프레임에 Animation Event
    {
        // 꾹 누름(Held) 또는 최근 클릭(buffered) 둘 다 같은 게이트로 처리
        if (input.AttackHeld || attackBuffered)
            anim.PlayAttackTrigger();
        attackBuffered = false;   // 소비
    }

    public void ResetCombo() => comboStep = 0;
    // TODO: 쿨다운(스킬별 타이머), 히트박스 활성/판정, 데미지 계산,
    //       스킬 데이터 테이블 조회(ID로 계수·쿨타임 등 로드)
}
