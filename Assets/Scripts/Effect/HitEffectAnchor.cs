using UnityEngine;

namespace ProjectS.Effects
{
    /// <summary>
    /// 이 캐릭터(몬스터·플레이어)가 맞았을 때 히트 이펙트를 띄울 기준점.
    /// 몬스터마다 크기·체형이 달라 콜라이더 중심만으로는 몸 한가운데가 안 맞으므로,
    /// 캐릭터 루트에 붙여 기준점을 직접 정한다. Scene 뷰에 기즈모로 위치가 보인다.
    /// <para>
    /// 없으면 맞은 콜라이더의 bounds 중심으로 대체한다(<see cref="Resolve(Collider)"/>).
    /// 그래서 모든 몬스터에 붙일 필요는 없고, 어긋나는 몬스터에만 붙이면 된다.
    /// </para>
    /// </summary>
    public class HitEffectAnchor : MonoBehaviour
    {
        // 비워 두면 이 오브젝트(루트) 기준. 척추·가슴 본을 넣으면 애니메이션(숙이기·점프)을 따라간다.
        [Tooltip("비워 두면 이 오브젝트 기준. 가슴·척추 본을 넣으면 모션을 따라 움직인다.")]
        [SerializeField] private Transform bone;

        // 기준(bone 또는 이 오브젝트)의 로컬 좌표 보정값. 로컬이라 몬스터가 회전·스케일돼도 몸에 붙어 따라간다
        // (같은 프리팹을 2배로 키운 변종도 비율 그대로 맞는다).
        [Tooltip("기준 오브젝트 로컬 좌표. 예: (0, 1, 0)이면 발밑에서 1m 위(스케일 비례).")]
        [SerializeField] private Vector3 offset = new Vector3(0f, 1f, 0f);

        [Header("Gizmo")]
        [SerializeField] private Color gizmoColor = new Color(1f, 0.3f, 0.1f, 1f);
        [SerializeField] private float gizmoRadius = 0.12f;

        /// <summary>히트 이펙트를 띄울 현재 월드 좌표.</summary>
        public Vector3 Position
        {
            get
            {
                if (bone != null) return bone.position + transform.TransformVector(offset);
                return transform.TransformPoint(offset);
            }
        }

        /// <summary>
        /// 맞은 콜라이더에서 히트 이펙트 기준점을 찾는다. 히트 판정 쪽(PlayerCombat·EnemyCombat·Projectile)이
        /// 적중 이벤트를 발행할 때 호출한다. 부모 쪽에 앵커가 있으면 그 위치, 없으면 콜라이더 bounds 중심.
        /// 부모까지 찾는 이유: 히트 콜라이더가 자식(부위 콜라이더)에 있어도 몬스터 하나에 기준점 하나로 맞추기 위함.
        /// </summary>
        /// <param name="hitCollider">맞은 콜라이더. 켜져 있어야 bounds가 유효하다.</param>
        /// <returns>이펙트 기준점 월드 좌표.</returns>
        public static Vector3 Resolve(Collider hitCollider)
        {
            HitEffectAnchor anchor = hitCollider.GetComponentInParent<HitEffectAnchor>();
            return anchor != null ? anchor.Position : hitCollider.bounds.center;
        }

        private void Reset()
        {
            // 처음 붙일 때 콜라이더 중심으로 초기값을 잡아 둔다 — 대부분 여기서 Y만 살짝 조정하면 끝나게.
            Collider col = GetComponentInChildren<Collider>();
            if (col != null) offset = transform.InverseTransformPoint(col.bounds.center);
        }

        private void OnDrawGizmos()
        {
            Vector3 pos = Position;
            Gizmos.color = gizmoColor;
            Gizmos.DrawWireSphere(pos, gizmoRadius);
            Gizmos.DrawLine(pos + Vector3.left * gizmoRadius * 2f, pos + Vector3.right * gizmoRadius * 2f);
            Gizmos.DrawLine(pos + Vector3.down * gizmoRadius * 2f, pos + Vector3.up * gizmoRadius * 2f);
            Gizmos.DrawLine(pos + Vector3.back * gizmoRadius * 2f, pos + Vector3.forward * gizmoRadius * 2f);
        }

        private void OnDrawGizmosSelected()
        {
            // 선택 시엔 채운 구 + 기준 오브젝트에서 이어지는 선으로 "무엇에 붙은 점인지" 보이게.
            Vector3 pos = Position;
            Gizmos.color = gizmoColor;
            Gizmos.DrawSphere(pos, gizmoRadius);
            Gizmos.DrawLine(bone != null ? bone.position : transform.position, pos);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(pos + Vector3.up * gizmoRadius * 2f, "HitFx");
#endif
        }
    }
}
