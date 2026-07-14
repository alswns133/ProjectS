using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// [2026.07.13 태하] 테스트 전용: 숫자 0 키는 피해, 숫자 9 키는 회복.
/// 아직 적 공격이 없어 피격 비네트(HpVignette)를 검증할 방법이 없어서 임시로 추가했다.
/// 게임플레이 입력이 아니므로 PlayerInputHandler를 거치지 않으며,
/// 적 공격이 붙으면 이 파일과 Player 오브젝트의 컴포넌트를 함께 삭제하면 된다.
/// </summary>
[RequireComponent(typeof(PlayerStats))]
[DefaultExecutionOrder(-50)]   // PlayerStats.Start의 초기 HP 발행보다 먼저 HUD를 초기화해야 NRE가 없다.
public class DebugDamageKey : MonoBehaviour
{
    [SerializeField] private int damagePerPress = 1;

    // [2026.07.13 태하] 회복 시 비네트가 옅어지는지 검증하기 위해 회복 키(9) 추가.
    [SerializeField] private int healPerPress = 1;

    private PlayerStats stats;

    private void Awake()
    {
        stats = GetComponent<PlayerStats>();
    }

    private void Start()
    {
        // 개인 테스트 씬에는 ShowPanel<HUDPanel>()을 호출해 줄 정식 씬 흐름(BaseScene.Enter →
        // GameSceneManager)이 없어서 HUD 게이지가 초기화되지 않는다. 테스트 동안만 대신 호출한다.
        // (Awake가 아닌 Start인 이유: UIManager.Awake의 패널 수집이 모든 Start보다 먼저 끝난다.)
        if (UIManager.Instance != null)
            UIManager.Instance.ShowPanel<HUDPanel>();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        // 상단 숫자열과 넘패드 둘 다 허용한다. 0 = 피해, 9 = 회복.
        if (keyboard.digit0Key.wasPressedThisFrame || keyboard.numpad0Key.wasPressedThisFrame)
        {
            stats.TakeDamage(damagePerPress);
        }

        if (keyboard.digit9Key.wasPressedThisFrame || keyboard.numpad9Key.wasPressedThisFrame)
        {
            stats.Heal(healPerPress);
        }
    }
}
