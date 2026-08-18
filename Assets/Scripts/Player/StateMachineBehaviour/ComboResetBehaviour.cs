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

            // 방금 공격/스킬을 시작한 유예 구간이면 이 로코모션 진입은 "동작 종료"가 아니라
            // UseSkill 직후 애니메이터가 아직 공격 State로 못 넘어온 레이스다. 여기서 teardown하면
            // UseSkill이 켠 시전 플래그를 도로 지워 검기가 통째로 씹힌다 → 건너뛴다.
            if (player != null && player.IsInActionEnterGrace)
            {
                // tornDown은 false로 남긴다. 이 진입이 진짜 로코모션 정착이었다면(액션이 실제로
                // 안 나갔다면) grace가 끝난 뒤 OnStateUpdate가 정상 teardown을 이어받게 하기 위함.
                // 정상 레이스면 그 전에 애니메이터가 공격 State로 넘어가 이 SMB가 더는 안 돌아 플래그가 보존된다.
                tornDown = false;
                return;
            }

            player?.UnlockMovement();
            tornDown = false;
        }

        public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            // grace 구간에도 EndComboChain(→ResetCombo)이 돌면 currentAction이 None으로 지워진다.
            // OnStateEnter와 같은 이유로, 방금 시작한 액션의 플래그를 보호하려 유예 동안은 미룬다.
            if (player != null && player.IsInActionEnterGrace) return;

            if (!tornDown && !animator.IsInTransition(layerIndex))
            {
                tornDown = true;
                combat?.EndComboChain();
            }
        }
    }
}
