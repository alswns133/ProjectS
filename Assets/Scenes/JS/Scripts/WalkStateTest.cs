using UnityEngine;

namespace ProjectS.Debugging
{
    /// <summary>
    /// [테스트 전용] 3단 걷기 State 머신(Idle → Start → Loop → Stop) 검증용.
    /// WASD/방향키 입력이 있으면 isMoving = true, 없으면 false로 세팅한다.
    /// 기존 PlayerMovement/PlayerAnimation과 무관한 독립 스크립트.
    /// 검증이 끝나면 이 컴포넌트를 제거할 것.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class WalkStateTest : MonoBehaviour
    {
        // 컨트롤러의 Bool 파라미터 이름과 정확히 일치해야 함(대소문자 포함).
        [SerializeField] private string paramName = "isMoving";

        // 이 값보다 입력 크기가 크면 "이동 중"으로 판정.
        [SerializeField] private float inputThreshold = 0.1f;

        private Animator animator;
        private int paramHash;

        // 직전 프레임의 상태. 값이 바뀔 때만 로그를 찍어 콘솔이 도배되는 걸 막는다.
        private bool lastIsMoving;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            paramHash = Animator.StringToHash(paramName);
        }

        private void Update()
        {
            // 레거시 Input으로 방향키/WASD를 읽는다(테스트 단순화 목적).
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            Vector2 input = new Vector2(h, v);

            bool isMoving = input.magnitude > inputThreshold;
            animator.SetBool(paramHash, isMoving);

            // 상태가 바뀐 순간만 콘솔에 출력 → 전이가 코드 신호대로 도는지 눈으로 확인.
            if (isMoving != lastIsMoving)
            {
                Debug.Log($"[WalkStateTest] isMoving → {isMoving}");
                lastIsMoving = isMoving;
            }
        }

        // 현재 재생 중인 State 정보를 화면 좌상단에 표시.
        private void OnGUI()
        {
            if (animator == null) return;

            var info = animator.GetCurrentAnimatorStateInfo(0);
            string label =
                $"isMoving: {lastIsMoving}\n" +
                $"NormalizedTime: {info.normalizedTime % 1f:F2}";

            GUI.Label(new Rect(10, 10, 400, 60), label);
        }
    }
}
