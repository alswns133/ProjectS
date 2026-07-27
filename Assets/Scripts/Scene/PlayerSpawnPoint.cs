using UnityEngine;

namespace ProjectS.Scenes
{
    /// <summary>
    /// 씬에서 플레이어가 스폰될 위치·방향을 표시하는 마커. 빈 GameObject에 붙여 배치한다.
    /// 씬 진입 시 <see cref="ProjectS.Managers.PlayerManager.WarpToSpawn"/>이 이 마커를 찾아
    /// (비활성 상태의) 지속 플레이어를 이 자리로 옮긴 뒤 활성화한다.
    ///
    /// 한 씬에 하나를 전제로 한다(여러 개면 첫 번째가 선택된다). 입구가 여러 개라 스폰 지점을 나눠야 하면
    /// 나중에 식별자 필드를 추가한다.
    /// </summary>
    public class PlayerSpawnPoint : MonoBehaviour
    {
#if UNITY_EDITOR
        // 배치 위치·방향을 에디터에서 눈으로 확인하기 위한 기즈모(전방 화살표).
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, 0.4f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 1f);
        }
#endif
    }
}
