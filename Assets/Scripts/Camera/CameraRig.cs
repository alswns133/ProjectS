using Unity.Cinemachine;
using UnityEngine;
using ProjectS.Players;

namespace ProjectS.Cameras
{
    /// <summary>
    /// 카메라 줌 제어. 마우스 휠 입력(ZoomDelta)으로 Cinemachine의
    /// 카메라-타겟 거리(CameraDistance)를 조절한다.
    /// ★ 이 스크립트가 붙은 오브젝트에 CinemachineCamera + ThirdPersonFollow(Body)가
    ///   있어야 한다. 없으면 follow가 null이 되어 줌이 조용히 비활성된다.
    /// (플레이어가 아닌 카메라의 책임이라 Player 컴포넌트 패밀리와 분리)
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        // 입력은 플레이어 오브젝트에 있으므로 인스펙터로 연결(다른 오브젝트라 GetComponent 불가).
        [SerializeField] private PlayerInputHandler input;

        [Header("줌")]
        [SerializeField] private float zoomSpeed = 50f;
        [SerializeField] private float minDistance = 2f;   // 줌인 한계(너무 붙지 않게)
        [SerializeField] private float maxDistance = 10f;  // 줌아웃 한계(너무 멀어지지 않게)

        // 실제로 거리를 들고 있는 Cinemachine Body 컴포넌트. 변하지 않으므로 1회 캐싱.
        private CinemachineThirdPersonFollow follow;
        private float distance; // 플레이어가 휠로 맞춘 줌 거리. 연출 중에도 이 값은 계속 갱신된다

        // Follow 대상을 갈아끼우는 방식으로 "캐릭터 추적 정지"를 구현한다. 카메라 거리·회전은
        // ThirdPersonFollow가 이 Follow 대상 기준으로 매 프레임 재계산하므로, 대상을 캐릭터가 아닌
        // 고정된 앵커로 바꿔치기하면 캐릭터가 움직여도 카메라는 그 자리에 머문다.
        private CinemachineCamera cmCamera;
        private Transform realFollowTarget;   // 평소 Follow 대상(캐릭터). 되돌릴 때 필요해 기억해 둔다.
        private Transform freezeAnchor;       // 얼린 동안 대신 Follow로 지정할 고정 지점(런타임 생성).

        private bool isFrozen;      // 지금 앵커를 보고 있는지(추적 정지 중).

        // 얼린 순간의 기준 위치(오프셋을 뺀 순수 캐릭터 자리).
        // 앵커 위치를 '이 값 + 오프셋'으로 매 프레임 다시 계산하기 위해 따로 들고 있는다 —
        // 앵커 위치만 저장해 두면, 얼어 있는 동안 다음 연출이 오프셋을 바꿔도 반영할 방법이 없다.
        private Vector3 frozenBasePosition;

        // 연출용 위치 오프셋. 추적 정지와 같은 앵커를 재활용한다 — 앵커가 '캐릭터 위치 + 오프셋'을
        // 매 프레임 따라가면 카메라 중심만 옮겨진 채 평소처럼 캐릭터를 따라다니게 된다.
        //
        // ★ 값은 '월드 기준'으로 받는다(호출한 쪽이 캐릭터 방향으로 변환해서 넘긴다).
        //   카메라 기준으로 두면 궤도가 도는 동안 오프셋도 함께 돌아가 중심점이 계속 움직인다 —
        //   "마법진을 중심으로 돈다" 같은 연출이 아예 성립하지 않는다.
        private bool hasOffset;
        private Vector3 targetOffset;   // 목표 오프셋(연출이 요구한 값, 월드 기준)
        private Vector3 currentOffset;  // 지금 적용 중인 값. 목표까지 부드럽게 이동한다
        private float offsetBlendTime;

        /// <summary>카메라가 캐릭터 추적을 멈춘 상태인지 여부.</summary>
        public bool IsFollowFrozen => isFrozen;

        /// <summary>연출이 카메라 위치 오프셋을 잡고 있는지 여부.</summary>
        public bool HasFollowOffset => hasOffset || currentOffset.sqrMagnitude > 0.0001f;

        // 연출용 거리 고정 상태.
        private bool hasOverride;
        private float overrideDistance;

        // 거리 전환(부드럽게 옮겨가기) 상태. blendTimer가 0보다 크면 전환 중이다.
        // 값을 즉시 대입하지 않는 이유: 큰 폭으로 튀면 화면이 순간이동한 것처럼 보인다.
        private float blendFrom;
        private float blendTo;
        private float blendDuration;
        private float blendTimer;

        /// <summary>연출이 카메라 거리를 잡고 있는지 여부.</summary>
        public bool HasDistanceOverride => hasOverride;

        private void Awake()
        {
            // 같은 오브젝트의 ThirdPersonFollow를 찾아 캐싱(매 프레임 GetComponent 회피).
            follow = GetComponent<CinemachineThirdPersonFollow>();
            cmCamera = GetComponent<CinemachineCamera>();

            // 시작점을 '현재 카메라의 실제 거리'로 맞춤. 안 하면 distance가 0에서 출발해
            // 첫 휠 입력에 카메라가 minDistance로 확 튀는 버그가 난다.
            if (follow == null)
                Debug.LogWarning($"{name}: ThirdPersonFollow가 없어 줌이 비활성됩니다.", this);
            else
                distance = follow.CameraDistance;

            if (cmCamera != null) realFollowTarget = cmCamera.Follow;
        }

        private void Update()
        {
            if (follow == null) return;   // Body가 없으면 줌 비활성(안전 가드)

            ReadZoomInput();
            ApplyDistance();

            // 얼어 있는 동안에도 '회전'은 계속 따라가고, 오프셋 변경도 계속 반영한다.
            // 얼리는 것은 '캐릭터를 따라가는 것'뿐이다. ThirdPersonFollow는 Follow 대상의
            // 위치와 회전을 둘 다 쓰므로, 회전까지 얼리면 궤도 회전이 화면에 반영되지 않는다.
            if (isFrozen) UpdateFrozenAnchor();

            // 오프셋만 걸린 상태(추적은 살아 있음)에서는 앵커가 캐릭터를 계속 따라다녀야 한다.
            // 해제된 뒤에도 currentOffset이 0으로 줄어들 때까지는 계속 돌려야 한다.
            // (hasOffset만 보면 해제 순간 갱신이 끊겨 오프셋이 걸린 채로 굳는다.)
            if (!isFrozen && HasFollowOffset) UpdateOffsetAnchor();
        }

        /// <summary>
        /// 연출용으로 카메라 중심(Follow 지점)을 옮긴다. 궤도 회전은 이 지점을 축으로 돈다.
        /// </summary>
        /// <param name="offset">
        /// 캐릭터로부터 얼마나 떨어진 곳을 중심으로 삼을지(월드 기준 벡터).
        /// 호출하는 쪽이 캐릭터 방향을 반영해 넘긴다 — 여기서 카메라 방향으로 다시 돌리지 않는다.
        /// </param>
        /// <param name="blendTime">그 위치까지 옮겨가는 시간(초). 0이면 즉시.</param>
        public void SetFollowOffset(Vector3 offset, float blendTime)
        {
            if (cmCamera == null || realFollowTarget == null) return;

            EnsureAnchor();

            hasOffset = true;
            targetOffset = offset;
            offsetBlendTime = Mathf.Max(0f, blendTime);

            if (offsetBlendTime <= 0f) currentOffset = offset;

            // 얼어 있는 중이면 Follow는 이미 앵커를 보고 있고, 위치 갱신은 UpdateFrozenAnchor가
            // '얼린 지점 + 오프셋'으로 매 프레임 처리한다 → 여기서 더 할 일이 없다.
            if (isFrozen) return;

            UpdateOffsetAnchor();
            cmCamera.Follow = freezeAnchor;
        }

        /// <summary>
        /// 위치 오프셋을 풀고 카메라 중심을 원래(캐릭터) 자리로 되돌린다.
        /// </summary>
        /// <param name="instant">true면 보간 없이 그 프레임에 되돌린다.</param>
        public void ClearFollowOffset(bool instant)
        {
            if (!hasOffset && currentOffset.sqrMagnitude <= 0.0001f) return;

            hasOffset = false;
            targetOffset = Vector3.zero;

            if (instant)
            {
                currentOffset = Vector3.zero;

                // 얼어 있으면 그쪽 로직이 Follow를 관리하므로 건드리지 않는다.
                if (!isFrozen && cmCamera != null && realFollowTarget != null)
                    cmCamera.Follow = realFollowTarget;
            }
        }

        /// <summary>
        /// 카메라가 캐릭터를 따라가지 않고 지금 자리에 멈추게 한다(왔다갔다 하는 스킬 연출 등).
        /// 회전·거리 제어는 그대로 동작하고, 오직 '누구를 중심으로' 계산할지만 고정된다.
        /// </summary>
        public void FreezeFollow()
        {
            if (cmCamera == null || realFollowTarget == null || isFrozen) return;

            EnsureAnchor();

            // 지금 캐릭터가 있는 자리에 앵커를 세운 뒤 그쪽을 보게 한다 → 바꿔치기하는 순간
            // 화면이 전혀 움직이지 않는다(캐릭터의 '현재' 위치를 그대로 이어받으므로).
            // 오프셋을 뺀 '순수 캐릭터 자리'를 기준으로 저장한다. 이후 UpdateFrozenAnchor가
            // 여기에 오프셋을 더해 앵커를 놓으므로, 얼어 있는 동안 다음 연출이 오프셋을
            // 바꿔도 그대로 반영된다.
            frozenBasePosition = realFollowTarget.position;

            // 오프셋이 걸려 있으면 그것까지 포함해야 한다. 빠뜨리면 얼리는 순간 오프셋만큼
            // 화면이 툭 튄다(오프셋을 건 채로 추적을 멈추는 연출에서 바로 드러난다).
            freezeAnchor.SetPositionAndRotation(
                frozenBasePosition + currentOffset,
                realFollowTarget.rotation);

            cmCamera.Follow = freezeAnchor;

            isFrozen = true;
        }

        /// <summary>
        /// 추적 정지를 풀고 그 프레임에 곧바로 다시 캐릭터를 따라가게 한다.
        /// 천천히 따라잡는 방식도 시도했지만, 연출이 끝난 뒤 카메라가 혼자 미끄러지며
        /// 쫓아오는 그림이 되어 오히려 어색해 즉시 복귀로 통일했다.
        /// </summary>
        public void ReleaseFollow()
        {
            if (!isFrozen) return;

            isFrozen = false;

            if (cmCamera != null && realFollowTarget != null) cmCamera.Follow = realFollowTarget;
        }

        /// <summary>
        /// 연출용으로 카메라 거리를 지정 값에 고정한다(각성기 등).
        /// 줌 한계(min/max)를 적용하지 않는다 — 연출 값은 기획이 정한 그림이라
        /// 플레이어 줌 범위에 눌리면 의도한 화면이 나오지 않기 때문이다.
        /// </summary>
        /// <param name="target">고정할 거리.</param>
        /// <param name="blendTime">그 거리까지 옮겨가는 시간(초). 0이면 즉시.</param>
        public void SetDistanceOverride(float target, float blendTime)
        {
            hasOverride = true;
            overrideDistance = target;
            StartBlend(target, blendTime);
        }

        /// <summary>
        /// 연출용 거리 고정을 풀고 플레이어가 맞춰 둔 줌 거리로 되돌아간다.
        /// 연출 도중 휠을 돌렸어도 그 입력이 distance에 쌓여 있으므로 그 값으로 복귀한다.
        /// </summary>
        /// <param name="blendTime">되돌아가는 시간(초). 0이면 즉시.</param>
        public void ClearDistanceOverride(float blendTime)
        {
            if (!hasOverride) return;

            hasOverride = false;
            StartBlend(distance, blendTime);
        }

        private void ReadZoomInput()
        {
            float d = input.ZoomDelta;
            if (Mathf.Abs(d) < 0.0001f) return;   // 입력 없으면 매 프레임 연산 건너뜀

            // 휠 방향으로 거리 가감 후 min/max로 제한. 휠 위로=거리 감소(줌인).
            // 연출 중에도 값은 갱신해 둔다 → 연출이 끝나면 플레이어가 원하던 줌으로 돌아간다.
            distance = Mathf.Clamp(distance - d * zoomSpeed * Time.deltaTime, minDistance, maxDistance);
        }

        private void ApplyDistance()
        {
            if (blendTimer > 0f)
            {
                blendTimer -= Time.deltaTime;

                float t = blendDuration <= 0f ? 1f : Mathf.Clamp01(1f - blendTimer / blendDuration);

                // SmoothStep: 시작과 끝에서 속도가 0에 가까워 카메라가 툭 출발하거나 급정거하지 않는다.
                follow.CameraDistance = Mathf.Lerp(blendFrom, blendTo, Mathf.SmoothStep(0f, 1f, t));
                return;
            }

            // 연출이 잡고 있는 동안에는 휠 입력이 화면에 반영되지 않는다(연출이 우선).
            follow.CameraDistance = hasOverride ? overrideDistance : distance;
        }

        private void StartBlend(float target, float blendTime)
        {
            if (follow == null) return;

            // 진행 중이던 전환이 있어도 '지금 보이는 거리'에서 새로 출발한다 → 중간에 값이 튀지 않는다.
            blendFrom = follow.CameraDistance;
            blendTo = target;
            blendDuration = Mathf.Max(0f, blendTime);
            blendTimer = blendDuration;

            if (blendTimer <= 0f) follow.CameraDistance = target;
        }

        // 현재 오프셋을 목표까지 한 걸음 옮긴다. 얼었을 때와 안 얼었을 때가 같은 규칙을 쓰도록
        // 한곳에 모아 둔다(한쪽만 고치면 두 경로의 동작이 갈린다).
        private void BlendOffset()
        {
            if (offsetBlendTime > 0f)
            {
                float t = 1f - Mathf.Exp(-(1f / offsetBlendTime) * Time.deltaTime);
                currentOffset = Vector3.Lerp(currentOffset, targetOffset, t);
            }
            else
            {
                currentOffset = targetOffset;
            }
        }

        private void EnsureAnchor()
        {
            if (freezeAnchor != null) return;

            // 씬 루트에 두는 이유: 캐릭터나 카메라의 자식으로 두면 그것들이 움직일 때
            // 앵커까지 같이 끌려가 '고정'이라는 목적 자체가 깨진다.
            freezeAnchor = new GameObject("CameraFreezeAnchor").transform;
        }

        // 추적 정지 중 앵커 갱신.
        // 위치: 얼린 지점 + 현재 오프셋 → 캐릭터는 안 따라가지만, 다음 연출이 오프셋을 바꾸면 반영된다.
        // 회전: 원본(CameraPivot)을 그대로 따라감 → 궤도 회전·마우스 시점이 정상 동작한다.
        private void UpdateFrozenAnchor()
        {
            if (freezeAnchor == null || realFollowTarget == null) return;

            BlendOffset();

            freezeAnchor.SetPositionAndRotation(
                frozenBasePosition + currentOffset,
                realFollowTarget.rotation);
        }

        // 앵커를 '캐릭터 위치 + 오프셋'에 매 프레임 붙여 둔다.
        // 오프셋이 0으로 다 줄어들면 앵커를 놓고 진짜 대상으로 돌려준다 —
        // 계속 앵커를 거치면 불필요한 한 단계가 남아 나중에 원인 찾기 어려운 미세 오차가 된다.
        private void UpdateOffsetAnchor()
        {
            if (freezeAnchor == null || realFollowTarget == null) return;

            BlendOffset();

            // 오프셋은 월드 기준이므로 그대로 더한다. 여기서 회전을 곱하면
            // 궤도가 도는 동안 중심점이 함께 돌아 연출이 무너진다.
            freezeAnchor.SetPositionAndRotation(
                realFollowTarget.position + currentOffset,
                realFollowTarget.rotation);

            if (!hasOffset && currentOffset.sqrMagnitude < 0.0001f)
            {
                currentOffset = Vector3.zero;
                if (cmCamera != null) cmCamera.Follow = realFollowTarget;
            }
        }

        private void OnDisable()
        {
            // 앵커를 보고 있는 채로 남으면 다음 활성화 때 캐릭터가 이동한 자리와 카메라가
            // 어긋난 채 시작하므로, 여기서 즉시 원래 대상으로 되돌린다(씬 전환 대비).
            if (isFrozen && cmCamera != null && realFollowTarget != null)
                cmCamera.Follow = realFollowTarget;

            isFrozen = false;
        }
    }
}
