using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace ProjectS.Enemies
{
    /// <summary>방 하나 = 스폰 포인트 묶음 + 발동 트리거. 방마다 종류가 섞여도 됨.
    /// ★ 방은 "직접 스폰" 안 하고, 권위(컨트롤러)에 요청만 한다 — 멀티 대비.</summary>
    public class EnemyRoom : MonoBehaviour
    {
        [SerializeField] private EnemySpawnPoint[] spawnPoints; // 이 방의 포인트들(종류 혼합)

        private bool isStart = false;
        private bool triggered;

        private Collider trigger;

        public EnemySpawnPoint[] Points => spawnPoints;

        // 프리로드 수집용: 이 방이 쓰는 몬스터들
        public IEnumerable<AssetReferenceGameObject> EnemyRefs => spawnPoints.Select(p => p.EnemyRef);

        private System.Action<EnemyRoom> onPlayerEnter; // 컨트롤러가 주인

        private void Awake()
        {
            spawnPoints = GetComponentsInChildren<EnemySpawnPoint>();
            trigger = GetComponent<Collider>();
            trigger.isTrigger = true;
        }

        public void Bind (System.Action<EnemyRoom> callback) => onPlayerEnter = callback;

        // 플레이어 입장 감지 => 컨트롤러(권위)에 "이 방 스폰" 요청. 직접 생성하지 않음.
        private void OnTriggerEnter(Collider other)
        {
            // 준비되지 않았다면 이벤트 종료
            if (isStart == false) return;

            if (triggered  || !other.CompareTag("Player")) return;
            triggered = true;
            // TODO(멀티): 트리거는 클라 감지 → Command로 서버에 요청, 스폰은 서버가.
            onPlayerEnter?.Invoke(this);    // 권위(컨트롤러)에 위임
            trigger.enabled = false;
        }

        // 준비가 됐는지 체크하는 메서드
        public void EnableTrigger()
        {
            isStart = true;
        }
    }
}

