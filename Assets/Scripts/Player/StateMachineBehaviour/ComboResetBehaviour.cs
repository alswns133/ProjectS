using UnityEngine;

namespace ProjectS.Players
{
    public class ComboResetBehaviour : StateMachineBehaviour
    {
        // SMB는 컨트롤러 에셋에 붙는 객체라 Awake에서 GameObject에 접근할 수 없다.
        // 대신 런타임에 Animator마다 인스턴스가 따로 생성되므로, 첫 진입 시 1회만 캐싱한다.
        // (컨트롤러 교체 시 인스턴스가 새로 생겨 캐시도 자연히 다시 채워진다.)
        private Player player;
        private PlayerCombat combat;

        // 이번 로코모션 진입에서 콤보 정리를 했는지. 블렌드 '완료' 프레임에 1회만 돌리기 위함.
        private bool tornDown;

        // 로코모션(평상시) 상태에 진입한 순간 = 공격·스킬 동작이 끝난 시점.
        // 이때 이동 잠금을 풀어 다시 움직일 수 있게 한다.
        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (player == null)
            {
                player = animator.GetComponent<Player>();
                combat = animator.GetComponent<PlayerCombat>();
            }

            player?.UnlockMovement();
            tornDown = false;
        }

        public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (!tornDown && !animator.IsInTransition(layerIndex))
            {
                tornDown = true;
                combat?.EndComboChain();
            }
        }
    }
}
