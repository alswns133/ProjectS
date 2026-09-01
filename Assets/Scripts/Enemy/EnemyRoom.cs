using ProjectS.Scenes;
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

        // 최종 보스를 넘길 결과 감시자. 씬에 하나 있는 것을 Awake에서 찾아 캐싱한다(없으면 null → SetEndBoss가 경고).
        private DungeonResultReporter dungeonReporter;

        private void Awake()
        {
            spawnPoints = GetComponentsInChildren<EnemySpawnPoint>();
            trigger = GetComponent<Collider>();
            dungeonReporter = FindAnyObjectByType<DungeonResultReporter>();
            trigger.isTrigger = true;
        }

        public void Bind(System.Action<EnemyRoom> callback) => onPlayerEnter = callback;

        // 플레이어 입장 감지 => 컨트롤러(권위)에 "이 방 스폰" 요청. 직접 생성하지 않음.
        private void OnTriggerEnter(Collider other)
        {
            // 준비되지 않았다면 이벤트 종료
            if (isStart == false) return;

            if (triggered || !other.CompareTag("Player")) return;
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

        /// <summary>
        /// 방금 이 방에서 스폰된 최종 보스를 결과 감시자(<see cref="DungeonResultReporter"/>)에 넘긴다.
        /// 스폰 권위(<see cref="ProjectS.Scenes.DungeonGather.RequestRoomSpawn"/>)가 IsEndBoss 포인트의
        /// 몬스터를 만든 직후 호출한다 — 그 보스가 사라지면 던전 클리어로 보고 결과창을 연다.
        /// </summary>
        /// <remarks>
        /// 최종 보스라도 스폰되는 것은 일반 <see cref="Enemy"/>라 Boss가 아닌 대상은 <c>as Boss</c>가 null이 되고,
        /// <see cref="dungeonReporter"/>가 없으면(씬에 감시자 미배치·비활성) 넘길 곳이 없어 결과창이 열리지 않는다.
        /// 어느 쪽이든 배선 실수라 경고를 남긴다 — 씬에 감시자를 활성으로 두고 최종 보스 프리팹이 Boss인지 확인해야 한다.
        /// </remarks>
        /// <param name="enemy">이 포인트에서 스폰된 몬스터. Boss이고 감시자가 있을 때만 최종 보스로 등록된다.</param>
        public void SetEndBoss(Enemy enemy)
        {
            if (enemy == null) return;

            Boss boss = enemy as Boss;

            if (boss != null && dungeonReporter != null)
                dungeonReporter.SetEndBossSpawn(boss);
            else
                ProjectS.Debugging.DevLog.Warning($"{name}: DungeonResultReporter가 씬에 없어 결과창이 안 열린다.", this);
        }
    }
}

