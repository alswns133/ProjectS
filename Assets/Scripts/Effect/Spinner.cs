using UnityEngine;

namespace ProjectS.Effects
{
    /// <summary>
    /// 오브젝트를 지정한 축을 중심으로 일정한 속도로 계속 회전시킨다.
    /// 바퀴, 프로펠러, 팬, 레이더, 떠 있는 홀로그램처럼 "그냥 계속 도는" 연출용.
    ///
    /// 이동 스크립트와 분리해 둔 이유는, 도는 것과 움직이는 것이 서로 다른 오브젝트에 필요할 때가
    /// 많기 때문이다. 예를 들어 차량은 본체가 이동하고 바퀴만 돌아야 하므로, 이동은 본체에,
    /// 이 스크립트는 바퀴 각각에 붙인다.
    ///
    /// 주의: 진행 방향을 바라보게 하는 회전(WaypointLoopMover의 Face Move Direction)과
    /// 같은 오브젝트에 함께 쓰면 서로 회전을 덮어써서 떨린다. 그럴 땐 모델을 자식으로 한 단계
    /// 감싸고, 자식에 이 스크립트를 붙인다.
    /// </summary>
    public class Spinner : MonoBehaviour
    {
        [Header("회전")]
        [Tooltip("회전축. 바퀴는 보통 모델이 향한 쪽에 따라 (1,0,0) 또는 (0,0,1), " +
                 "레이더나 팽이처럼 수평으로 도는 것은 (0,1,0)이다. 길이는 자동으로 정규화한다.")]
        [SerializeField] private Vector3 axis = Vector3.up;

        [Tooltip("초당 회전 각도(도/초). 360이면 1초에 한 바퀴. 음수면 반대 방향으로 돈다.")]
        [SerializeField] private float degreesPerSecond = 180f;

        [Tooltip("켜면 자기 자신의 로컬 축을 기준으로 돈다(부모가 기울어도 따라 기운다). " +
                 "끄면 월드 축 기준이라 부모가 어떻게 놓이든 항상 같은 방향으로 돈다.")]
        [SerializeField] private bool useLocalAxis = true;

        [Header("여러 개 배치할 때")]
        [Tooltip("켜면 시작 각도를 무작위로 정한다. 같은 프리팹을 여러 개 뿌렸을 때 " +
                 "전부 같은 각도로 딱 맞춰 도는 어색함을 없앤다.")]
        [SerializeField] private bool randomizeStartAngle;

        [Tooltip("속도에 줄 무작위 편차 비율(0~1). 0.2면 설정 속도의 80%~120% 사이에서 정해진다. " +
                 "여러 개가 한 몸처럼 도는 것을 막는다. 0이면 편차 없음.")]
        [Range(0f, 1f)]
        [SerializeField] private float speedVariance;

        private Vector3 normalizedAxis;
        private float speed;

        /// <summary>
        /// 회전 속도(도/초). 기어를 올리거나 정지시키는 등 런타임에 바꿀 때 쓴다.
        /// 무작위 편차가 적용된 실제 속도이며, 0을 넣으면 그 자리에 멈춘다.
        /// </summary>
        public float Speed
        {
            get => speed;
            set => speed = value;
        }

        private void Awake()
        {
            // 축이 0이면 Rotate가 아무 일도 하지 않아 "코드가 안 도는" 것처럼 보인다.
            // 원인을 바로 알 수 있게 여기서 끊는다.
            if (axis.sqrMagnitude < 0.0001f)
            {
                Debug.LogWarning($"{name}: 회전축이 (0,0,0)이라 회전할 수 없다. 비활성화한다.", this);
                enabled = false;
                return;
            }

            normalizedAxis = axis.normalized;
            speed = degreesPerSecond * (1f + Random.Range(-speedVariance, speedVariance));

            if (randomizeStartAngle)
            {
                Rotate(Random.Range(0f, 360f));
            }
        }

        private void Update()
        {
            Rotate(speed * Time.deltaTime);
        }

        private void Rotate(float degrees)
        {
            transform.Rotate(normalizedAxis, degrees, useLocalAxis ? Space.Self : Space.World);
        }
    }
}
