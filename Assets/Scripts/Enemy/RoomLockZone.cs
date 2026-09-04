using UnityEngine;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 방 '안쪽'에 두는 잠금 전용 트리거. 플레이어가 출입문을 완전히 통과해 방에 들어온 순간을 잡아
    /// 소속 <see cref="EnemyRoom"/>의 전투(문 잠금)를 시작시킨다.
    ///
    /// <b>왜 분리하나</b>: 소환은 방 앞쪽의 <see cref="EnemyRoom"/> 트리거가 담당한다. 소환과 잠금을 한
    /// 트리거의 진입/이탈로 처리하면, 트리거가 문 앞에서 끝날 때 플레이어가 문을 통과하기 '전에' 이탈이
    /// 잡혀 문이 미리 잠겨 밖에 갇히는 문제가 있다. 그래서 잠금 판정만 문 안쪽의 이 트리거로 떼어낸다.
    ///
    /// <b>붙이는 위치</b>: 출입문 '안쪽'(방 입구)에 감지 전용 자식 오브젝트로 둔다. 자기 콜라이더를
    /// 강제로 isTrigger로 만들므로, 플레이어를 막는 솔리드 콜라이더로 겸용하지 않는다.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class RoomLockZone : MonoBehaviour
    {
        [Tooltip("이 잠금존이 속한 방. 플레이어가 들어오면 이 방의 전투(문 잠금)를 시작한다. 비워 두면 부모에서 EnemyRoom을 찾는다.")]
        [SerializeField] private EnemyRoom room;

        private Collider trigger;

        private void Awake()
        {
            trigger = GetComponent<Collider>();
            trigger.isTrigger = true;   // 물리 충돌이 아니라 감지 전용

            // 슬롯을 비워 뒀으면 부모 계층에서 방을 찾아 준다(잠금존을 방 자식으로 두는 일반 배치 대비).
            if (room == null) room = GetComponentInParent<EnemyRoom>();
        }

        // 플레이어가 방에 들어오면 소속 방에 잠금(전투 시작)을 요청한다. 소환 여부·중복은 방이 가드한다.
        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;

            if (room == null)
            {
                ProjectS.Debugging.DevLog.Warning($"[RoomLockZone:{name}] room 미배선 — 잠글 방을 못 찾았다. 인스펙터에서 EnemyRoom을 연결하거나, EnemyRoom 자식으로 배치하라.", this);
                return;
            }

            ProjectS.Debugging.DevLog.Log($"[RoomLockZone:{name}] 플레이어 방 진입 감지 → {room.name}에 잠금 요청", this);
            room.BeginEncounterFromLockZone();

            trigger.enabled = false;    // 잠금은 1회면 충분
        }
    }
}
