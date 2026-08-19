#if UNITY_EDITOR
using UnityEditor;
#endif

using UnityEngine;
using UnityEngine.AddressableAssets;


namespace ProjectS.Enemies
{
    /// <summary>던전 씬의 빈 오브젝트에 붙는 스폰 마커. 에셋을 품지 않고 어드레서블 "포인터"만 든다.</summary>
    public class EnemySpawnPoint : MonoBehaviour
    {
        // 스폰 순간 재생할 이펙트 하나. 이펙트마다 나올 위치가 달라야 해서 프리팹 + 오프셋을 함께 든다.
        [System.Serializable]
        private struct SpawnEffectEntry
        {
            public GameObject prefab;
            public Vector3 offset;   // 스폰 포인트 기준 오프셋(회전 반영). 이펙트별로 위치를 따로 튜닝한다.
        }

        [SerializeField] private AssetReferenceGameObject enemyRef;  // 어떤 적( 어드레서블 포인터)
        [SerializeField] private int count = 1; // 몇 마리 스폰할지

        [SerializeField] private Color gizmoColor = new Color(1f, 0.3f, 0.2f, 1f); // 종류

        // 스폰 순간 재생할 이펙트(들). 여러 개 = 강한 연출. 포인트마다·이펙트마다 위치를 따로 준다.
        [SerializeField] private SpawnEffectEntry[] spawnEffects;

        public AssetReferenceGameObject EnemyRef => enemyRef;
        public int Count => count;

        public Vector3 Position => transform.position;
        public Quaternion Rotation => transform.rotation;

        // 오프셋 → 월드 위치. 포인트 회전은 반영하되 스케일은 무시한다(마커라 스케일 영향 없게).
        private Vector3 EffectPos(Vector3 offset) => transform.position + transform.rotation * offset;

        /// <summary>스폰 순간 이 포인트의 연출 이펙트를 각자 오프셋 위치에서 재생한다. 여러 개면 모두.</summary>
        public void PlaySpawnEffects()
        {
            if (spawnEffects == null) return;
            foreach (var e in spawnEffects)
                if (e.prefab != null) Instantiate(e.prefab, EffectPos(e.offset), transform.rotation);
        }

        // 씬 뷰에서 스폰 위치·바라보는 방향·종류·수 + 각 이펙트가 나올 위치를 항상 표시한다.
        private void OnDrawGizmos()
        {
            bool empty = enemyRef == null || !enemyRef.RuntimeKeyIsValid();
            Gizmos.color = empty ? Color.magenta : gizmoColor;   // 비어있으면 눈에 띄게

            Gizmos.DrawWireSphere(transform.position, 0.4f);                          // 위치
            Gizmos.DrawLine(transform.position, transform.position + transform.forward); // 스폰 회전(방향)

#if UNITY_EDITOR
            string label = empty ? "⚠ (몬스터 미지정)"
                                 : $"{enemyRef.editorAsset?.name} x{count}";
            Handles.Label(transform.position + Vector3.up * 0.6f, label);
#endif

            // 이펙트가 나올 위치(이펙트마다). 시안 구슬 + 연결선 + 이펙트 이름으로 정확한 지점을 잡을 수 있게.
            if (spawnEffects == null) return;
            Gizmos.color = Color.cyan;
            foreach (var e in spawnEffects)
            {
                Vector3 p = EffectPos(e.offset);
                Gizmos.DrawWireSphere(p, 0.2f);
                Gizmos.DrawLine(transform.position, p);
#if UNITY_EDITOR
                Handles.Label(p + Vector3.up * 0.25f, e.prefab != null ? e.prefab.name : "(빈)");
#endif
            }
        }
    }
}
