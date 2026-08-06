#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectS.Debugging
{
    /// <summary>
    /// 에디터 전용: 게임 속도(Time.timeScale)를 키로 조절한다. 원거리 몬스터의 조준·발사처럼
    /// 실시간으로는 눈으로 쫓기 어려운 타이밍을 느린 화면으로 확인하기 위한 도구다.
    /// <para>
    /// 씬에 배치할 필요가 없다 — 플레이 시작 시 자기 오브젝트를 만들어 붙는다(AutoCreate).
    /// 파일 전체가 #if UNITY_EDITOR라 빌드에는 클래스도 자동 생성도 포함되지 않는다.
    /// </para>
    /// <para>
    /// - , (&lt;) : 느리게(배속 단계 한 칸 내림)
    /// - . (&gt;) : 빠르게(배속 단계 한 칸 올림)
    /// - / : 일시정지 토글(0배속 ↔ 직전 배속)
    /// - M : 1배속으로 즉시 복귀
    /// (P는 장비창 단축키로 옮겨감)
    /// </para>
    /// <para>
    /// Time.timeScale은 씬을 넘어 유지되는 전역 값이라, 느리게 둔 채 플레이를 멈추면 다음 진입도
    /// 느리다. 그 사고를 막기 위해 이 컴포넌트가 비활성화·파괴될 때 1로 되돌린다.
    /// fixedDeltaTime도 함께 스케일해 물리(NavMeshAgent 이동 등)가 같은 비율로 느려지게 한다.
    /// </para>
    /// </summary>
    public class DebugTimeScaleKey : MonoBehaviour
    {
        // 순환할 배속 단계. 0.1배까지 내려가면 발사 궤적을 프레임 단위로 볼 수 있다.
        // 1f가 반드시 포함돼 있어야 \ 복귀와 시작값이 이 배열과 어긋나지 않는다.
        // 자동 생성 오브젝트라 인스펙터에서 못 바꾸므로 코드 기본값이 곧 최종값이다.
        private static readonly float[] SpeedSteps = { 0.1f, 0.25f, 0.5f, 1f, 2f };

        // Awake 시점의 원래 물리 간격. timeScale에 비례해 되돌릴 기준값이라 한 번만 저장한다.
        private float baseFixedDeltaTime;

        // 현재 배속의 SpeedSteps 인덱스. 일시정지는 이 값을 유지한 채 timeScale만 0으로 둔다.
        private int currentIndex;
        private bool isPaused;

        // 플레이 시작 시 자기 오브젝트를 만들어 붙는다. 씬마다 배치하거나 Player에 매달 필요가 없다.
        // AfterSceneLoad: 첫 씬이 올라온 뒤라 DontDestroyOnLoad 이관이 안전하다.
        // 파일이 #if UNITY_EDITOR로 감싸여 있어 이 자동 생성도 에디터 플레이에서만 동작한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            GameObject go = new GameObject("[DebugTimeScaleKey]");
            go.AddComponent<DebugTimeScaleKey>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            baseFixedDeltaTime = Time.fixedDeltaTime;

            // 시작은 항상 1배속. 배열에 1이 없으면 가장 가까운 단계를 고른다.
            currentIndex = FindDefaultIndex();
            ApplyCurrentSpeed();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.commaKey.wasPressedThisFrame) StepSpeed(-1);   // < 줄임
            if (keyboard.periodKey.wasPressedThisFrame) StepSpeed(1);   // > 높임
            if (keyboard.slashKey.wasPressedThisFrame) TogglePause();   // / 정지
            if (keyboard.mKey.wasPressedThisFrame) ResetSpeed();        // m 복귀(1배속)
        }

        // 배속 단계를 한 칸 이동한다. 일시정지 중에 조절하면 자동으로 재개하며 새 배속을 적용한다.
        private void StepSpeed(int direction)
        {
            isPaused = false;
            currentIndex = Mathf.Clamp(currentIndex + direction, 0, SpeedSteps.Length - 1);
            ApplyCurrentSpeed();
        }

        private void ResetSpeed()
        {
            isPaused = false;
            currentIndex = FindDefaultIndex();
            ApplyCurrentSpeed();
        }

        private void TogglePause()
        {
            isPaused = !isPaused;

            if (isPaused)
            {
                // 물리까지 멈춰야 완전한 정지 프레임이 된다. 인덱스는 그대로 둬 재개 시 복원한다.
                Time.timeScale = 0f;
                Time.fixedDeltaTime = baseFixedDeltaTime;
            }
            else
            {
                ApplyCurrentSpeed();
            }
        }

        // 현재 인덱스의 배속을 timeScale과 물리 간격에 함께 반영한다.
        private void ApplyCurrentSpeed()
        {
            float scale = SpeedSteps[currentIndex];
            Time.timeScale = scale;

            // 물리 스텝도 같은 비율로 줄인다. 이게 없으면 화면만 느려지고 NavMeshAgent 등
            // FixedUpdate 기반 이동은 원래 속도로 움직여 연출이 어긋나 보인다.
            Time.fixedDeltaTime = baseFixedDeltaTime * scale;

            DevLog.Log($"[TimeScale] {scale:0.##}x");
        }

        // 1배속 단계를 찾는다. 없으면 1에 가장 가까운 단계로 폴백한다.
        private int FindDefaultIndex()
        {
            int best = 0;
            float bestDistance = float.PositiveInfinity;

            for (int i = 0; i < SpeedSteps.Length; i++)
            {
                float distance = Mathf.Abs(SpeedSteps[i] - 1f);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }

        // 느리게 둔 상태가 다음 플레이·다음 씬으로 새는 것을 막는다.
        // OnDisable에서 하는 이유: 컴포넌트를 꺼도, 오브젝트가 파괴돼도 항상 통과하는 지점이라
        // 시간 스케일이 전역에 남지 않는다.
        private void OnDisable()
        {
            Time.timeScale = 1f;
            Time.fixedDeltaTime = baseFixedDeltaTime;
        }
    }
}
#endif
