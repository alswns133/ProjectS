using UnityEngine;

namespace ProjectS.Effects
{
    /// <summary>
    /// 오브젝트를 지정한 로컬 Y까지 일정한 속도로 계속 왕복시키고,
    /// 선택적으로 특정 로컬 Y에 도달하는 순간 이펙트를 재생한다.
    /// 부유하는 드론, 오르내리는 플랫폼, 바닥에 닿을 때마다 김을 뿜는 장비 같은 데 쓴다.
    ///
    /// 월드가 아니라 로컬 Y를 쓰는 이유는 두 가지다.
    ///   1) 부모를 통째로 옮겨도 왕복 구간이 따라온다. 맵 구역을 통으로 이동시킬 때 안 깨진다.
    ///   2) 프리팹으로 만들어 여러 곳에 뿌릴 수 있다. 월드 Y였다면 배치할 때마다 값을 다시 잡아야 한다.
    ///
    /// 시작 지점은 Awake 시점의 위치다. 즉 씬에 놓아둔 그 자리가 왕복의 한쪽 끝이 되고,
    /// targetLocalY가 반대쪽 끝이 된다.
    /// </summary>
    public class VerticalOscillator : MonoBehaviour
    {
        /// <summary>도달 이펙트를 어느 진행 방향에서 재생할지 고르는 값.</summary>
        public enum TriggerDirection
        {
            /// <summary>올라갈 때와 내려갈 때 모두.</summary>
            Both,
            /// <summary>올라가는 중일 때만.</summary>
            Upward,
            /// <summary>내려가는 중일 때만.</summary>
            Downward
        }

        [Header("왕복 구간")]
        [Tooltip("왕복의 반대쪽 끝이 되는 로컬 Y. 시작 지점은 씬에 배치한 현재 위치다. " +
                 "시작 지점보다 낮은 값을 넣으면 아래로 내려갔다 올라온다.")]
        [SerializeField] private float targetLocalY = 2f;

        [Tooltip("이동 속도(유닛/초). 왕복 구간의 길이와 무관하게 이 속도로 움직이므로, " +
                 "구간이 길면 한 번 왕복하는 데 그만큼 오래 걸린다.")]
        [SerializeField] private float speed = 1f;

        [Header("시작 위상")]
        [Tooltip("켜면 시작 위치와 진행 방향을 무작위로 정한다. " +
                 "같은 프리팹을 여러 개 뿌렸을 때 전부 한 몸처럼 움직이는 것을 막기 위한 것이다. " +
                 "끄면 항상 배치한 자리에서 targetLocalY 쪽으로 출발한다.")]
        [SerializeField] private bool randomStart = true;

        [Header("도달 이펙트")]
        [Tooltip("도달 시 재생할 파티클. 비워두면 이펙트 기능 전체가 꺼진다. " +
                 "이 파티클은 Looping과 Play On Awake를 꺼야 한다. 켜져 있으면 스스로 계속 재생돼 " +
                 "도달 판정이 아무 의미가 없어진다.")]
        [SerializeField] private ParticleSystem arrivalEffect;

        [Tooltip("이 로컬 Y에 도달하면 이펙트를 재생한다. 왕복 구간의 끝(배치 위치 또는 targetLocalY)에 " +
                 "두면 방향이 꺾이는 순간 터지고, 구간 중간에 두면 지나갈 때마다 터진다.")]
        [SerializeField] private float triggerLocalY;

        [Tooltip("어느 진행 방향에서 재생할지. 왕복 구간의 끝에 트리거를 두면 그 지점에서 속도가 0에 " +
                 "가까워 방향을 가리기 어려우므로 Both로 두는 편이 안전하다.")]
        [SerializeField] private TriggerDirection triggerDirection = TriggerDirection.Both;

        [Tooltip("도달로 인정할 허용 범위(유닛). 왕복 구간의 끝에 트리거를 둘 때 반드시 필요하다. " +
                 "끝점은 닿았다가 되돌아갈 뿐 넘어가지 않아서, 통과 판정만으로는 영원히 안 터진다. " +
                 "속도가 빠르면 한 프레임에 건너뛸 수 있으므로 (속도 / 60)보다 넉넉하게 준다.")]
        [SerializeField] private float triggerTolerance = 0.05f;

        private float startLocalY;
        private float distance;

        // PingPong(phase, 1)의 주기는 2다. 0→1이 가는 길, 1→2가 돌아오는 길.
        // 그래서 무작위 시작은 [0, 2)에서 뽑아야 위치뿐 아니라 진행 방향까지 갈린다.
        private float phase;

        private float previousLocalY;

        // 도달 상태가 유지되는 동안 매 프레임 재생되는 것을 막는 걸쇠.
        // 도달 판정에서 빠져나와야 다시 잠기고, 다음 도달 때 한 번만 열린다.
        private bool wasArrived;

        private void Awake()
        {
            startLocalY = transform.localPosition.y;
            distance = Mathf.Abs(targetLocalY - startLocalY);

            // 구간이 0이면 아래 나눗셈이 무한대가 되어 위치가 NaN으로 깨진다.
            // 조용히 제자리에 있는 게 아니라 오브젝트가 사라지는 사고가 되므로 여기서 끊는다.
            if (distance < 0.0001f)
            {
                Debug.LogWarning($"{name}: 시작 Y와 targetLocalY가 같아 왕복할 구간이 없다. 비활성화한다.", this);
                enabled = false;
                return;
            }

            if (randomStart)
            {
                phase = Random.Range(0f, 2f);
            }

            // 무작위 위상을 여기서 바로 위치에 반영한다. Update까지 미루면 한 프레임 동안
            // 배치한 자리에 있다가 튀는 것이 보인다.
            float y = ApplyPosition();

            // 첫 프레임에 없는 이동을 도달로 오인하지 않도록 이전 위치를 현재로 맞춰둔다.
            previousLocalY = y;

            // 무작위 시작이면 트리거 범위 안에서 시작할 수도 있다. 그 상태를 미리 걸쇠에 넣어두지 않으면
            // 생성되자마자 이펙트가 한 번 터진다.
            wasArrived = Mathf.Abs(y - triggerLocalY) <= triggerTolerance;
        }

        private void Update()
        {
            // 정규화된 진행도로 누적하면서 속도를 구간 길이로 나눈다.
            // 이렇게 해야 구간이 길든 짧든 speed가 그대로 유닛/초가 된다.
            phase += speed / distance * Time.deltaTime;

            // 값이 계속 커지면 부동소수점 정밀도가 떨어져 왕복이 미세하게 어긋난다.
            // 주기가 2이므로 2로 접어도 움직임은 완전히 같다.
            if (phase > 2f)
            {
                phase -= 2f;
            }

            float y = ApplyPosition();

            if (arrivalEffect != null)
            {
                UpdateArrival(y);
            }

            previousLocalY = y;
        }

        /// <summary>
        /// 현재 위상에 해당하는 로컬 Y를 계산해 트랜스폼에 반영한다.
        /// </summary>
        /// <returns>반영한 로컬 Y.</returns>
        private float ApplyPosition()
        {
            Vector3 local = transform.localPosition;
            local.y = Mathf.Lerp(startLocalY, targetLocalY, Mathf.PingPong(phase, 1f));
            transform.localPosition = local;
            return local.y;
        }

        /// <summary>
        /// 트리거 지점 도달을 판정하고, 이번 프레임에 새로 도달했다면 이펙트를 재생한다.
        /// </summary>
        /// <param name="y">이번 프레임의 로컬 Y.</param>
        private void UpdateArrival(float y)
        {
            float previousDelta = previousLocalY - triggerLocalY;
            float delta = y - triggerLocalY;

            // 판정을 두 갈래로 두는 이유.
            //   통과(crossed): 트리거가 구간 중간에 있을 때. 빠르게 지나가 허용 범위를 한 프레임에
            //                  건너뛰어도 부호가 뒤집히므로 놓치지 않는다.
            //   접촉(touched): 트리거가 구간 끝에 있을 때. 끝점은 닿았다 되돌아갈 뿐 넘어가지
            //                  않으므로 부호가 영원히 안 뒤집힌다. 허용 범위로 잡아야 터진다.
            bool crossed = (previousDelta < 0f) != (delta < 0f);
            bool touched = Mathf.Abs(delta) <= triggerTolerance;
            bool arrived = crossed || touched;

            if (arrived && !wasArrived && MatchesDirection(y))
            {
                PlayArrivalEffect();
            }

            wasArrived = arrived;
        }

        /// <summary>
        /// 이번 프레임의 진행 방향이 인스펙터에서 고른 재생 방향과 맞는지 본다.
        /// </summary>
        /// <param name="y">이번 프레임의 로컬 Y.</param>
        /// <returns>재생해도 되는 방향이면 true.</returns>
        private bool MatchesDirection(float y)
        {
            if (triggerDirection == TriggerDirection.Both) return true;

            float moved = y - previousLocalY;
            return triggerDirection == TriggerDirection.Upward ? moved > 0f : moved < 0f;
        }

        private void PlayArrivalEffect()
        {
            // 이전 분출이 아직 끝나지 않았을 수 있다. StopEmitting은 이미 떠 있는 파티클은
            // 수명대로 두고 방출만 끊으므로, 앞의 김이 뚝 끊기지 않으면서 새로 시작된다.
            // StopEmittingAndClear를 쓰면 남아 있던 김이 그 자리에서 사라져 눈에 띈다.
            arrivalEffect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            arrivalEffect.Play();
        }

        // 플레이하지 않아도 왕복 구간과 트리거 지점이 씬 뷰에 보여야 배치할 때 값을 가늠할 수 있다.
        private void OnDrawGizmosSelected()
        {
            // 플레이 중이면 오브젝트가 이미 구간 안을 움직이고 있으므로 캐싱해 둔 시작점을 쓴다.
            float from = Application.isPlaying ? startLocalY : transform.localPosition.y;

            Vector3 worldFrom = ToWorld(from);
            Vector3 worldTo = ToWorld(targetLocalY);

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(worldFrom, worldTo);
            Gizmos.DrawWireSphere(worldFrom, 0.15f);
            Gizmos.DrawWireSphere(worldTo, 0.15f);

            if (arrivalEffect == null) return;

            // 허용 범위를 실제 크기로 그린다. 이게 왕복 구간에 비해 너무 크면 계속 터지고,
            // 너무 작으면 빠른 속도에서 건너뛴다는 것을 눈으로 보고 판단할 수 있다.
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(ToWorld(triggerLocalY), Mathf.Max(triggerTolerance, 0.02f));
        }

        private Vector3 ToWorld(float localY)
        {
            Vector3 local = transform.localPosition;
            local.y = localY;

            Transform parent = transform.parent;
            return parent != null ? parent.TransformPoint(local) : local;
        }
    }
}
