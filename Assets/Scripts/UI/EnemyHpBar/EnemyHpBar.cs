using System;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Enemies;

namespace ProjectS.UI
{
    /// <summary>
    /// 일반 몬스터 한 기의 월드 공간 HP 바.
    /// 데미지 텍스트처럼 생성/삭제를 반복하지 않고 Spawner의 풀에서 꺼내 재사용된다.
    /// 데미지 텍스트와 달리 특정 적(target)에 붙어 그 적이 살아 있고 피가 닳아 있는 동안
    /// 계속 머리 위를 따라다니며, 풀피 복귀·사망 시 Spawner가 풀로 되돌린다.
    /// </summary>
    public class EnemyHpBar : MonoBehaviour
    {
        // Filled 타입 Image. fillAmount(0~1)로 남은 HP 비율을 그린다.
        [SerializeField] private Image fillImage;

        // 이 바가 붙어 있는 적. 위치·높이(HpBarHeight)를 매 프레임 읽어 따라간다.
        // Spawner가 발행하는 EnemyStats를 그대로 받으므로 UI→전투 단방향 참조만 생긴다.
        private EnemyStats target;
        private Transform cam;

        // 풀 반환 콜백(Spawner가 넘겨줌). 사망/풀피(Spawner 호출)와 target 소실(자체 방어)
        // 두 경로 모두 이 콜백으로 반납하므로 반환 경로가 하나로 모인다.
        private Action<EnemyHpBar> returnToPool;

        /// <summary>
        /// 이 바가 현재 붙어 있는 적. Spawner가 반환 콜백에서 딕셔너리 키를 지울 때 쓴다.
        /// </summary>
        public EnemyStats Target => target;

        /// <summary>
        /// 바를 특정 적에 붙여 활성화한다. 풀에서 재사용되므로 이전 대상 흔적을 모두 덮어쓴다.
        /// </summary>
        /// <param name="targetStats">따라다닐 적. 위치와 HpBarHeight의 소유자.</param>
        /// <param name="ratio">초기 HP 비율(0~1).</param>
        /// <param name="onReturn">풀 반환 콜백.</param>
        public void Show(EnemyStats targetStats, float ratio, Action<EnemyHpBar> onReturn)
        {
            target = targetStats;
            returnToPool = onReturn;
            SetRatio(ratio);

            if (cam == null && Camera.main != null)
                cam = Camera.main.transform;

            // 첫 프레임부터 어긋난 위치에 뜨지 않게 활성화 전에 한 번 맞춘다.
            FollowTarget();
            gameObject.SetActive(true);
        }

        /// <summary>남은 HP 비율을 갱신한다. 같은 적이 다시 맞을 때 Spawner가 호출한다.</summary>
        /// <param name="ratio">HP 비율(0~1). 범위를 벗어난 값도 안전하게 클램프한다.</param>
        public void SetRatio(float ratio)
        {
            if (fillImage != null)
                fillImage.fillAmount = Mathf.Clamp01(ratio);
        }

        private void Update()
        {
            // 사망 이벤트 없이 파괴/씬 언로드로 적이 사라지면 Spawner가 반납 신호를 못 준다.
            // 데미지 텍스트가 수명 종료 시 스스로 돌아가듯, 여기선 대상 소실을 스스로 감지해 반납한다.
            if (target == null)
            {
                Deactivate();
                return;
            }

            FollowTarget();
        }

        // 적 머리 위(적별 높이)로 위치를 맞추고, 카메라를 향해 빌보드한다.
        private void FollowTarget()
        {
            transform.position = target.transform.position + Vector3.up * target.HpBarHeight;

            if (cam != null)
                // 카메라 회전을 복사해 어느 각도에서 봐도 바가 정면으로 읽히게 한다.
                transform.rotation = cam.rotation;
        }

        /// <summary>
        /// 바를 숨기고 풀로 반납한다. Spawner(풀피·사망)와 자체 방어(대상 소실)가 함께 쓴다.
        /// </summary>
        public void Deactivate()
        {
            gameObject.SetActive(false);

            // 반납 콜백을 비운 뒤 호출해 중복 반환을 막는다(Contains 방어와 이중 안전).
            Action<EnemyHpBar> callback = returnToPool;
            returnToPool = null;
            callback?.Invoke(this);
        }
    }
}
