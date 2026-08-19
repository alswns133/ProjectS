using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;


namespace ProjectS.Enemies
{
    /// <summary>던전 씬에 하나. 어드레서블 적 프리팹을 로드·캐싱하고 스폰한다.
    /// ★ "결정"(무엇을/어디에)은 던전 컨트롤러, "생성"만 여기 — 멀티 전환 대비 분리.
    /// 풀링을 쓰지 않는 이유: Enemy는 사망 시 비활성되고 리셋/풀반환 경로가 없다.
    /// 던전은 방마다 1회 스폰·리스폰 없음이라 Instantiate로 충분하고, 이탈(씬 언로드) 때 일괄 파괴된다.</summary>
    public class EnemySpawner : MonoBehaviour   // 기존 베이스 재사용 (ProjectileSpawner와 동일)
    {
        // RuntimeKey -> 로드 핸들(프리팹 + Release용). 같은 적 중복 로드 방지.
        private readonly Dictionary<object, AsyncOperationHandle<GameObject>> handles = new();

        /// <summary>던전 진입 시 1회. 이 던전이 쓰는 적 세트(중복 제거)를 로드·캐싱한다.</summary>
        public async Task PreloadAsync(IEnumerable<AssetReferenceGameObject> refs)
        {
            foreach (var reference in refs)
            {
                if (reference == null || !reference.RuntimeKeyIsValid()) continue;

                object key = reference.RuntimeKey;
                if (handles.ContainsKey(key)) continue;

                AsyncOperationHandle<GameObject> handle = Addressables.LoadAssetAsync<GameObject>(key);

                await handle.Task;

                if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
                {
                    Debug.LogError($"[EnemySpawner] 적 프리팹 로드 실패: {key}");
                    if (handle.IsValid()) Addressables.Release(handle);
                    continue;
                }

                handles[key] = handle;
                // TODO(멀티): 로드 직후 NetworkClient.RegisterPrefab(go) — 늦게 로드되는 프리팹을 스폰 전에 등록
            }
        }

        /// <summary>적 하나 생성. ★ "생성"만 — 무엇/어디는 인자로 받음(호출측이 결정).
        /// Instantiate라 Awake에서 spawnPosition·HP·상태가 새로 잡힌다(재사용 아님 → 리셋 불필요).</summary>
        public Enemy SpawnOne(AssetReferenceGameObject enemyRef, Vector3 pos, Quaternion rot)
        {
            if (enemyRef == null || !handles.TryGetValue(enemyRef.RuntimeKey, out var handle))
            {
                Debug.LogError($"[EnemySpawner] 프리로드 안 된 적 스폰 시도: {enemyRef?.RuntimeKey}");
                return null;
            }
            GameObject obj = Instantiate(handle.Result, pos, rot);

            // TODO(멀티): NetworkServer.Spawn(go) — 서버가 스폰, 클라에 전파
            return obj.GetComponent<Enemy>();
        }

        /// <summary>던전 이탈 시. 어드레서블 핸들 Release(인스턴스는 씬 언로드로 함께 파괴됨).</summary>
        public void ClearAndRelease()
        {
            if(handles == null) return;

            foreach (var handle in handles.Values)
                if (handle.IsValid()) Addressables.Release(handle);
            handles.Clear();
        }

    }
}

