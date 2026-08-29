using UnityEngine;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 보스 공격의 히트박스 범위를 바닥에 미리 보여주는 예고 장판. 판정에 쓰는 바로 그 히트박스 Transform을
    /// 그대로 읽어 배치하므로 "보이는 자리 = 맞는 자리"가 항상 일치한다(별도 값 저장 없음).
    /// <para>
    /// <b>보스 전용.</b> 일반 몬스터는 이 컴포넌트를 붙이지 않는다. 붙이지 않으면 EnemyCombat이 예고를
    /// 호출하지 않아(참조 null) 아무 일도 일어나지 않는다 — 코드 분기 없이 보스에만 켜진다.
    /// </para>
    /// <para>
    /// <b>현재는 제자리 멜리(Melee)만.</b> 히트박스가 한 자리에 있는 공격은 그 박스의 XZ 발자국이 곧 범위다.
    /// 이동하며 훑는 돌진(Charge)은 박스가 지나가는 '경로'를 따로 재야 하므로 아직 예고하지 않는다(추후 확장).
    /// </para>
    /// <para>
    /// <b>비주얼 규약:</b> <see cref="telegraphVisual"/>에는 XZ 평면에 평평하게 눕힌, 발자국 1×1(m) 기준의
    /// 데칼/쿼드를 넣는다. 이 피벗을 XZ로 스케일하면 그 자식 발자국이 히트박스 크기에 1:1로 맞는다.
    /// 룩(색·페이드·경고 애니메이션)은 이 비주얼 프리팹 쪽에서 자유롭게 만든다 — 이 스크립트는 위치·크기·표시만 맡는다.
    /// </para>
    /// </summary>
    public class AttackTelegraph : MonoBehaviour
    {
        [Tooltip("바닥에 깔릴 장판 비주얼(피벗). XZ 평면에 평평하게 눕힌, 발자국 1×1 기준의 자식 데칼/쿼드를 둔다.")]
        [SerializeField] private Transform telegraphVisual;

        [Tooltip("장판을 지면 위로 살짝 띄우는 높이(z-fighting 방지).")]
        [SerializeField] private float groundOffsetY = 0.02f;

        [Tooltip("장판을 놓을 지면 Y를 아래로 레이캐스트해서 찾을지. 끄면 이 오브젝트(보스 발밑)의 Y를 지면으로 본다.")]
        [SerializeField] private bool snapToGround = true;

        [Tooltip("snapToGround가 켜졌을 때 지면으로 인정할 레이어.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("지면 탐색 레이캐스트가 히트박스 위에서 아래로 훑는 최대 거리.")]
        [SerializeField, Min(0.1f)] private float groundRayDistance = 10f;

        // 켜질 때 비주얼은 꺼둔 상태로 시작한다(예고 요청이 올 때만 보인다).
        private void Awake()
        {
            if (telegraphVisual != null) telegraphVisual.gameObject.SetActive(false);
        }

        /// <summary>
        /// 히트박스 박스의 XZ 발자국에 맞춰 장판을 배치하고 켠다. 공격 예고 시작 시 호출한다.
        /// 히트박스의 yaw만 반영해 바닥 평면에 눕히고, 크기는 박스의 XZ 스케일을 그대로 쓴다.
        /// </summary>
        /// <param name="hitBox">판정에 쓰는 히트박스 Transform(위치·회전·스케일이 곧 박스). null이면 무시.</param>
        public void Show(Transform hitBox)
        {
            if (telegraphVisual == null || hitBox == null) return;

            Vector3 center = hitBox.position;
            float groundY = ResolveGroundY(center);

            telegraphVisual.position = new Vector3(center.x, groundY + groundOffsetY, center.z);

            // 미니맵과 같은 이유로 yaw만 쓴다 — 히트박스가 기울어져 있어도 장판은 바닥 평면에 눕힌다.
            telegraphVisual.rotation = Quaternion.Euler(0f, hitBox.eulerAngles.y, 0f);

            // 크기를 판정 박스의 '월드' 크기(hitBox.lossyScale)와 똑같이 맞춘다 = 실제 맞는 범위.
            // 단 localScale은 부모 스케일의 영향을 받으므로(피벗이 스케일된 보스 밑에 있으면 값이 달라 보이고 실제로도 틀어진다),
            // 부모의 월드 스케일로 나눠 상쇄한다 → telegraphVisual을 어디에 붙이든 '월드' 가로/세로가 히트박스와 정확히 일치한다.
            Vector3 boxSize = hitBox.lossyScale;
            Vector3 parentScale = telegraphVisual.parent != null ? telegraphVisual.parent.lossyScale : Vector3.one;
            telegraphVisual.localScale = new Vector3(
                Mathf.Approximately(parentScale.x, 0f) ? boxSize.x : boxSize.x / parentScale.x,
                telegraphVisual.localScale.y,   // 두께 축 — 평평한 데칼이라 시각엔 영향 없음(그대로 둠)
                Mathf.Approximately(parentScale.z, 0f) ? boxSize.z : boxSize.z / parentScale.z);

            if (!telegraphVisual.gameObject.activeSelf) telegraphVisual.gameObject.SetActive(true);
        }

        /// <summary>장판을 끈다. 타격 프레임 또는 공격 중단(피격/사망/상태 종료) 시 호출한다.</summary>
        public void Hide()
        {
            if (telegraphVisual != null && telegraphVisual.gameObject.activeSelf)
                telegraphVisual.gameObject.SetActive(false);
        }

        // 장판을 놓을 지면 Y를 정한다. snapToGround면 박스 중심 위에서 아래로 훑어 찾고,
        // 실패하거나 꺼져 있으면 이 오브젝트(보스 발밑)의 Y를 지면으로 본다.
        private float ResolveGroundY(Vector3 center)
        {
            if (snapToGround)
            {
                Vector3 origin = new Vector3(center.x, center.y + groundRayDistance, center.z);
                if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundRayDistance * 2f, groundMask, QueryTriggerInteraction.Ignore))
                    return hit.point.y;
            }

            return transform.position.y;
        }
    }
}
