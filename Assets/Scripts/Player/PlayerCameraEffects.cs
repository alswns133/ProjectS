using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ProjectS.Cameras;

namespace ProjectS.Players
{
    /// <summary>
    /// 타격감용 카메라 흔들림을 재생하는 통로.
    /// <c>PlayerEffects</c>/<c>PlayerVfxEffects</c>와 같은 규약(슬롯 키·정규화·중단 게이트)을 따르는
    /// 세 번째 병렬 컴포넌트다. 클립의 Animation Event가 <see cref="OnCameraEffect"/>를 호출한다.
    ///
    /// 슬롯은 두 가지 모드로 동작한다.
    ///   - <c>duration</c> = 0 : 이벤트 프레임에 1회 툭 흔들기(평타 타격감).
    ///   - <c>duration</c> &gt; 0 : 그 시간 동안 <c>interval</c>마다 임펄스를 반복 발사해
    ///     계속 흔들리게 한다(강공격 차징·내려찍기 여진·보스 등장 지진 등).
    ///
    /// ★ 카메라 transform을 직접 흔들지 않고 Cinemachine Impulse를 쓰는 이유:
    ///   실제 카메라는 CinemachineBrain이 LateUpdate에서 매 프레임 덮어쓰고,
    ///   <c>CameraPivotController</c>도 LateUpdate에서 rotation을 통째로 대입한다.
    ///   직접 흔들면 같은 프레임에 지워져 아무 일도 일어나지 않는다.
    ///
    /// ★ 지속 흔들림을 "긴 임펄스 1발"(ImpulseDefinition.ImpulseDuration 조절)로 만들지 않는 이유:
    ///   ImpulseSource는 이 오브젝트에 하나뿐이라 슬롯마다 길이를 바꾸려면 공유 정의를 런타임에
    ///   고쳐야 하는데, Cinemachine의 SignalSource는 그 정의를 참조로 들고 매 프레임 읽는다.
    ///   → 다음 슬롯이 값을 바꾸면 아직 날아가는 중인 이전 임펄스까지 같이 변형된다.
    ///   또 한 번 쏜 임펄스는 취소 수단이 없어 구르기로 캔슬해도 흔들림이 끝까지 남는다.
    ///   반복 발사 방식은 정의를 건드리지 않고, 중단도 <see cref="StopShake"/>로 가능하다.
    ///
    /// ★ 반드시 Animator와 같은 GameObject(플레이어 루트)에 붙일 것.
    ///   Animation Event는 Animator가 붙은 오브젝트의 컴포넌트에서만 메서드를 찾는다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CinemachineImpulseSource))]
    public class PlayerCameraEffects : MonoBehaviour
    {
        // 키는 클립 Animation Event의 string 인자와 맞춘다.
        // PlayerEffects/PlayerVfxEffects와 키 공간이 분리되어 있어 같은 이름을 써도 간섭하지 않는다.
        [Serializable]
        private class CameraEffectSlot
        {
            public string key;

            [Header("흔들림")]
            // 0이면 흔들림 없음. 이 슬롯은 히트스톱만 쓰는 용도로도 만들 수 있다.
            [Min(0f)] public float shakeForce = 0.3f;

            // 캐릭터 기준 방향(로컬). 내려찍기=(0,-1,0), 앞으로 찌르기=(0,0,1) 식으로 준다.
            // 월드 고정이 아니라 캐릭터 기준이라, 어느 쪽을 보고 때려도 연출이 같게 나온다.
            public Vector3 shakeDirection = new Vector3(0f, -1f, 0f);

            [Header("지속 흔들림 (0이면 1회만)")]
            // 이 시간 동안 계속 흔든다. 0이면 기존처럼 이벤트 프레임에 한 번만 흔들린다
            // (인스펙터에 이미 등록해 둔 슬롯은 이 값이 0이라 동작이 그대로 유지된다).
            [Min(0f)] public float duration;

            // 임펄스 재발사 간격. 짧을수록 촘촘하고 무겁게, 길수록 툭툭 끊기는 느낌이 된다.
            // ImpulseSource의 ImpulseDuration(기본 0.2초)보다 짧으면 임펄스끼리 겹쳐 더 강해지므로,
            // 간격을 줄일 땐 shakeForce도 같이 낮춰야 세기가 유지된다.
            [Min(0.01f)] public float interval = 0.06f;

            // 진행도(0~1)에 따른 세기 배율. 기본은 1로 평탄(=끝까지 같은 세기).
            // 여진처럼 서서히 잦아들게 하려면 1 → 0 으로 내려가는 곡선을 그린다.
            public AnimationCurve strengthOverTime = AnimationCurve.Constant(0f, 1f, 1f);

            // 매 발사마다 방향을 랜덤하게 틀어 주는 정도(0=항상 같은 방향, 1=완전 랜덤).
            // 0이면 같은 축으로만 밀려 기계적으로 보인다. 지진·차징 진동은 0.3~0.6이 자연스럽다.
            [Range(0f, 1f)] public float directionJitter = 0.35f;

            [Header("궤도 회전 (0이면 사용 안 함)")]
            // 연출 동안 카메라가 도는 각도. 360이면 한 바퀴 돌아 시작 방향으로 정확히 돌아온다.
            // 음수면 반대 방향으로 돈다.
            public float orbitDegrees;

            // 도는 데 걸리는 시간(초). 각도/시간이 곧 회전 속도다.
            [Min(0.01f)] public float orbitDuration = 4f;

            // 연출 중 마우스 조작을 막을지. 안 막으면 플레이어가 카메라를 돌려 궤도 연출이 깨진다.
            public bool lockCameraInput = true;

            [Header("높이·각도 (연출 중 상하 각도)")]
            // true면 도는 동안 카메라 각도(부감/앙각)도 지정 값으로 옮긴다.
            // ThirdPersonFollow는 이 각도로 캐릭터를 도는 반경 위쪽/아래쪽에서 비추므로,
            // 별도 '높이' 값 없이 각도만으로 "얼마나 높은 곳에서 내려다보며 비출지"가 정해진다.
            public bool useOrbitPitch;

            // 연출 중 상하 각도(도). 양수면 위에서 내려다본다(높은 곳에서 비추는 느낌),
            // 음수면 아래에서 올려다본다.
            [Range(-70f, 70f)] public float orbitPitch = 20f;

            [Header("거리 (0이면 사용 안 함)")]
            // 연출 동안 고정할 카메라 거리. 각성기처럼 화면을 넓게 보여줘야 하는 연출에 쓴다.
            // 플레이어 줌 한계(CameraRig의 min/max)를 무시하므로 기획 값을 그대로 적으면 된다.
            [Min(0f)] public float overrideDistance;

            // 그 거리까지 옮겨가는 시간(초). 0이면 즉시 바뀌어 화면이 순간이동한 것처럼 보인다.
            [Min(0f)] public float distanceBlend = 0.4f;

            [Header("위치 (0이면 사용 안 함)")]
            // 연출 동안 카메라 중심(= 궤도 회전의 축)을 옮길 오프셋.
            // 기준은 캐릭터가 바라보는 방향이다 — +Z 캐릭터 정면, +Y 위, +X 오른쪽.
            // 캐릭터 앞에 생기는 마법진처럼 '캐릭터가 아닌 것'을 중심으로 돌려야 할 때 쓴다
            // (예: 마법진이 정면 4m 앞에 생기면 Z를 4로).
            // 값은 시전 순간에 월드 좌표로 확정되므로, 회전이 도는 동안 중심점이 흔들리지 않는다.
            public Vector3 followOffset;

            // 그 위치까지 옮겨가는 시간(초). 0이면 즉시 바뀌어 화면이 순간이동한 것처럼 보인다.
            [Min(0f)] public float followOffsetBlend = 0.4f;

            [Header("추적 정지")]
            // true면 연출 동안 카메라가 캐릭터를 따라가지 않고 지금 자리에 멈춘다.
            // 왔다갔다 하는 스킬에서 추적 때문에 화면이 심하게 흔들리는 것을 막는다.
            public bool freezeFollow;

            [Header("렌즈 왜곡 (0이면 사용 안 함)")]
            // 타격 순간 화면이 확 빨려들었다가 풀리는 펀치 연출. PlayerSpeedEffect의 대시 이펙트와
            // 같은 파라미터지만 용도가 다르다 — 저건 '지속 상태(대시 중)'를 따라가고,
            // 이건 흔들림처럼 '이벤트 한 번에 즉발 후 감쇠'다. 음수면 안으로, 양수면 밖으로 왜곡된다.
            [Range(-1f, 1f)] public float lensDistortionIntensity;

            // 최대값에서 0으로 풀리는 시간(초). 흔들림의 감쇠와 같은 역할.
            [Min(0.05f)] public float lensDistortionRelease = 0.3f;
        }

        [SerializeField] private CameraEffectSlot[] effects;

        // 궤도 회전·입력 잠금·거리·추적 정지는 카메라 쪽 컴포넌트가 수행한다(흔들림과 달리 임펄스로는 표현할 수 없다).
        // 플레이어와 다른 오브젝트라 인스펙터로 연결하고, 비워 두면 씬에서 찾는다.
        [SerializeField] private CameraPivotController cameraPivot;
        [SerializeField] private CameraRig cameraRig;

        // 해제 신호를 놓쳤을 때 마우스 잠금을 되돌리는 안전장치. 연출이 아무리 길어도 이 시간을
        // 넘기면 강제로 풀린다 — 없으면 조작 불능 상태로 게임을 진행할 수 없게 된다.
        [SerializeField, Min(0.5f)] private float maxOrbitTime = 8f;

        // 매 이벤트마다 배열을 뒤지지 않도록 Awake에서 1회 구축하는 조회용 사전.
        private readonly Dictionary<string, CameraEffectSlot> effectMap = new Dictionary<string, CameraEffectSlot>();

        // 구르기·피격·사망 중 뒤늦게 도착한 이벤트를 무시하기 위해 상태를 조회할 중앙 컨텍스트.
        private Player player;

        private CinemachineImpulseSource impulseSource;

        // 지속 흔들림은 동시에 하나만 굴린다. 새 지속 흔들림이 시작되면 이전 것을 끊는다
        // (겹치면 세기가 눈덩이처럼 불어나 화면이 통제 불능으로 흔들린다).
        private Coroutine sustainRoutine;

        // 궤도 회전·거리 연출 중 하나라도 잡고 있는 중인지. 해제(OffCameraEffect)와
        // 안전장치의 기준이 된다. 이름은 orbitLock이지만 거리 연출도 같은 생명주기로 묶인다 —
        // 하나의 스킬 연출이 카메라 각도·거리를 함께 잡았다가 함께 놓는 구조이기 때문이다.
        private bool hasOrbitLock;
        private float orbitLockStartTime;

        // 렌즈 왜곡 전용 Volume/컴포넌트. PlayerSpeedEffect와 같은 이유로 자체 Volume을 들고 다닌다
        // (씬에 연결된 전역 Volume이 없어 거기 얹으면 아무 데서도 동작하지 않는다).
        private LensDistortion lensDistortion;

        // 현재 적용 중인 왜곡 값. 트리거되면 목표치로 즉시 튀고, 이후 매 프레임 0으로 감쇠한다.
        private float lensCurrent;
        private float lensDecayPerSecond;

        private void Awake()
        {
            player = GetComponent<Player>();
            impulseSource = GetComponent<CinemachineImpulseSource>();

            // 인스펙터 연결을 깜빡해도 동작하도록 씬에서 찾아 둔다.
            if (cameraPivot == null) cameraPivot = FindFirstObjectByType<CameraPivotController>();
            if (cameraRig == null) cameraRig = FindFirstObjectByType<CameraRig>();

            SetupLensDistortionVolume();

            if (effects == null) return;

            foreach (var slot in effects)
            {
                if (slot == null || string.IsNullOrEmpty(slot.key)) continue;

                // 다른 이펙트 컴포넌트와 같은 방침: 언더바 표기 차이를 흡수하려 정규화한 키로 등록·조회한다
                // (AnimationEventKey 참조). 클립이 "Skill_2_1", 인스펙터가 "Skill21"이어도 맞물린다.
                string normKey = AnimationEventKey.Normalize(slot.key);

                // 키 중복을 조용히 덮어쓰면 한쪽이 영영 안 나와 원인 찾기 어렵다 → 경고.
                if (effectMap.ContainsKey(normKey))
                {
                    Debug.LogWarning($"Duplicate camera effect key '{slot.key}'. Only the first slot is used.", this);
                    continue;
                }

                effectMap.Add(normKey, slot);
            }
        }

        /// <summary>
        /// 공격/스킬 클립의 Animation Event가 호출한다. 인자는 인스펙터에 등록한 슬롯 키.
        /// </summary>
        /// <param name="key">슬롯 키. 언더바 표기 차이는 무시된다(예: "Skill_2_1" == "Skill21").</param>
        public void OnCameraEffect(string key)
        {
            // 구르기·피격으로 캔슬했는데 뒤늦게 화면이 흔들리는 걸 막는다.
            // 사망(Stats.IsDead)은 예외로 통과시킨다 — 사망 연출(Die/Die_Large 클립)의 셰이크는
            // 캔슬 잔재가 아니라 그 클립에 의도적으로 심은 것이기 때문이다. IsActionInterrupted를
            // 그대로 쓰면 사망 시점엔 이미 IsDead가 true라 사망 셰이크까지 함께 삼켜진다.
            if (player != null && (player.IsRolling || player.IsStaggered)) return;

            if (!TryGetSlot(key, out CameraEffectSlot slot)) return;

            if (slot.duration > 0f)
                StartSustain(slot.shakeDirection, slot.shakeForce, slot.duration, slot.interval, slot.directionJitter, slot.strengthOverTime);
            else
                Shake(slot.shakeDirection, slot.shakeForce);

            StartOrbitIfNeeded(slot);
            PunchLensDistortion(slot);
        }

        /// <summary>
        /// 궤도 회전과 마우스 잠금을 풀고 평소 카메라 조작으로 되돌린다.
        /// 스킬이 끝나는 프레임에 Animation Event로 호출한다.
        /// </summary>
        /// <param name="key">인자는 받지만 실제로는 무시한다 — 지금 걸려 있는 잠금을 그냥 푼다.</param>
        public void OffCameraEffect(string key)
        {
            ReleaseOrbitLock();
        }

        private void Update()
        {
            UpdateLensDistortionDecay();

            if (!hasOrbitLock) return;

            // 구르기·피격·사망으로 스킬이 끊기면 해제 이벤트가 담긴 프레임까지 재생되지 않는다.
            // 흔들림이 같은 조건으로 멈추는 것과 같은 기준으로 카메라 잠금도 풀어준다.
            if (player != null && player.IsActionInterrupted)
            {
                // 캔슬은 즉시 복구한다. 플레이어가 이미 회피 모션에 들어갔으므로
                // 카메라가 천천히 되돌아오면 그 시간 동안 조작이 겉도는 것처럼 느껴진다.
                ReleaseOrbitLock();
                return;
            }

            if (Time.time - orbitLockStartTime >= maxOrbitTime)
            {
                Debug.LogWarning($"Camera orbit lock lasted {maxOrbitTime}s. Releasing by safety timeout — check the OffCameraEffect Animation Event.", this);
                ReleaseOrbitLock();
            }
        }

        private void OnDisable()
        {
            // 비활성화되면 Update가 멈춰 안전장치도 함께 멈춘다.
            // 마우스 조작이 잠긴 채 남지 않도록 여기서 즉시 풀어준다(씬 전환 대비).
            // 보간할 시간 자체가 없으므로 무조건 즉시 복구다.
            ReleaseOrbitLock();

            // 비활성화되면 Unity가 코루틴을 알아서 멈추지만, 핸들은 남아 다음 활성화 때
            // 죽은 코루틴을 붙잡고 있게 된다 → 여기서 비운다.
            sustainRoutine = null;
        }

        // 슬롯에 궤도·거리 값이 있으면 카메라에 적용하고 필요 시 조작을 잠근다.
        // 둘 다 0이면 아무 일도 하지 않는다(흔들림만 쓰는 기존 슬롯은 영향을 받지 않는다).
        private void StartOrbitIfNeeded(CameraEffectSlot slot)
        {
            // 각도만 바꾸는 연출(회전 0 + Use Orbit Pitch)도 카메라 쪽에 전달해야 하므로
            // '회전이 있는가'가 아니라 '카메라 회전 제어를 쓰는가'로 판단한다.
            bool useOrbit = cameraPivot != null
                && (!Mathf.Approximately(slot.orbitDegrees, 0f) || slot.useOrbitPitch);
            bool useDistance = slot.overrideDistance > 0f && cameraRig != null;
            bool useFreeze = slot.freezeFollow && cameraRig != null;
            bool useOffset = slot.followOffset != Vector3.zero && cameraRig != null;

            if (!useOrbit && !useDistance && !useFreeze && !useOffset) return;

            // 오프셋을 먼저 건 뒤 얼려야 한다. 순서가 반대면 오프셋이 반영되지 않은 자리에서
            // 얼어붙어(추적 정지 중에는 앵커를 갱신하지 않으므로) 위치 값이 무시된다.
            //
            // 캐릭터 기준 값을 지금 이 순간의 캐릭터 방향으로 월드 좌표로 바꿔서 넘긴다.
            // 이렇게 확정해 두면 궤도가 도는 동안에도 중심점이 그 자리에 그대로 있는다.
            if (useOffset)
                cameraRig.SetFollowOffset(transform.rotation * slot.followOffset, slot.followOffsetBlend);

            // 추적 정지를 거리 변경보다 먼저 걸어야 한다. 순서가 반대면 거리 전환이 진행되는
            // 동안 카메라가 여전히 캐릭터를 따라가다가 그 다음 프레임에야 멈춰 한 번 덜컹인다.
            if (useFreeze) cameraRig.FreezeFollow();

            if (useDistance) cameraRig.SetDistanceOverride(slot.overrideDistance, slot.distanceBlend);

            if (useOrbit)
                cameraPivot.StartOrbit(slot.orbitDegrees, slot.orbitDuration, slot.useOrbitPitch, slot.orbitPitch);

            // 잠금은 궤도 회전 여부와 무관하게 건다. "연출 중에는 카메라 조작 금지"가 규칙이므로,
            // 회전 없이 거리·위치만 바꾸는 연출도 똑같이 잠긴다.
            if (slot.lockCameraInput && cameraPivot != null) cameraPivot.SetInputLocked(true);

            // 궤도든 거리든 하나라도 걸렸으면 같은 생명주기(해제·안전 타임아웃)로 관리한다.
            // 거리만 쓰는 슬롯도 Off 이벤트를 놓치면 원래 줌으로 돌아오지 못하는 사고가 나므로,
            // 회전이 없다는 이유로 안전장치를 빼면 안 된다.
            hasOrbitLock = true;
            orbitLockStartTime = Time.time;
        }

        // 연출 해제는 항상 '그 프레임에 즉시'다.
        // 부드럽게 되돌리면 연출이 끝난 뒤에도 카메라가 혼자 미끄러지며 따라와 어색하고,
        // 그 사이 플레이어 조작과 복귀 보간이 서로 밀어내 통제감이 무너진다.
        // 들어갈 때(연출 시작)만 블렌드를 쓴다.
        private void ReleaseOrbitLock()
        {
            // 연출이 끝나면(정상 종료든 캔슬이든) 조건 없이 회전을 멈추고 조작을 돌려준다.
            if (cameraPivot != null)
            {
                cameraPivot.StopOrbit();
                cameraPivot.SetInputLocked(false);
            }

            if (cameraRig != null)
            {
                if (cameraRig.HasDistanceOverride) cameraRig.ClearDistanceOverride(0f);
                if (cameraRig.HasFollowOffset) cameraRig.ClearFollowOffset(true);
                if (cameraRig.IsFollowFrozen) cameraRig.ReleaseFollow();
            }

            hasOrbitLock = false;
        }

        // PlayerSpeedEffect와 같은 이유(씬에 연결된 전역 Volume이 없음)로 자체 Volume을 만든다.
        // 레이어 0(Default) 자식 오브젝트에 붙이는 이유도 동일 — 플레이어 루트가 Player 레이어에
        // 있으면 카메라의 Volume Layer Mask(보통 Default만 포함)가 이 Volume을 무시해 버린다
        // (에러 없이 조용히 아무 효과도 안 보이는 사고. PlayerSpeedEffect에서 실제로 겪었다).
        private void SetupLensDistortionVolume()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            lensDistortion = profile.Add<LensDistortion>(true);
            lensDistortion.intensity.Override(0f);

            var volumeObject = new GameObject("CameraEffectLensDistortionVolume");
            volumeObject.layer = 0;
            volumeObject.transform.SetParent(transform, false);

            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 110; // PlayerSpeedEffect(100)보다 살짝 높게 — 타격 펀치가 대시 왜곡 위에 겹쳐 보이도록.
            volume.weight = 1f;
            volume.profile = profile;
        }

        // 슬롯에 값이 있으면 그 프레임에 목표치로 즉시 튄다(흔들림과 같은 '즉발' 감각).
        // 이미 감쇠 중이었다면 그 값 위에 다시 튀지 않고 새 목표치로 덮어써서, 콤보 중 연속
        // 타격에도 값이 무한히 누적되지 않는다.
        private void PunchLensDistortion(CameraEffectSlot slot)
        {
            if (Mathf.Approximately(slot.lensDistortionIntensity, 0f)) return;

            lensCurrent = slot.lensDistortionIntensity;
            lensDecayPerSecond = Mathf.Abs(slot.lensDistortionIntensity) / Mathf.Max(0.05f, slot.lensDistortionRelease);
        }

        private void UpdateLensDistortionDecay()
        {
            if (lensDistortion == null || lensCurrent == 0f) return;

            lensCurrent = Mathf.MoveTowards(lensCurrent, 0f, lensDecayPerSecond * Time.deltaTime);
            lensDistortion.intensity.value = lensCurrent;
        }

        /// <summary>
        /// 카메라를 1회 흔든다. 코드에서 직접 부를 수 있게 public으로 열어 둔다
        /// (몬스터 사망·보스 등장처럼 클립 이벤트가 아닌 곳에서 쓰는 경우).
        /// </summary>
        /// <param name="localDirection">캐릭터 기준 흔들림 방향. 정규화되지 않아도 된다.</param>
        /// <param name="force">세기. 0.2~0.5가 평타, 1 이상은 강타 느낌.</param>
        public void Shake(Vector3 localDirection, float force)
        {
            if (impulseSource == null || force <= 0f) return;

            // 캐릭터 기준 방향을 월드로 변환한다. 이걸 안 하면 캐릭터가 뒤를 보고 때릴 때
            // 흔들림 방향만 반대로 나가 연출이 어긋난다.
            Vector3 world = transform.TransformDirection(localDirection.normalized);

            impulseSource.GenerateImpulseWithVelocity(world * force);
        }

        /// <summary>
        /// 지정한 시간 동안 카메라를 계속 흔든다. 이미 지속 흔들림이 돌고 있으면 그것을 끊고 새로 시작한다.
        /// 클립 이벤트가 아닌 곳(보스 등장 연출 등)에서 쓰기 위한 진입점이며,
        /// 슬롯의 세밀한 값(세기 곡선)이 필요 없을 때 쓴다.
        /// </summary>
        /// <param name="localDirection">캐릭터 기준 흔들림 방향. 정규화되지 않아도 된다.</param>
        /// <param name="force">세기. 지속 흔들림은 임펄스가 겹치므로 1회 흔들기보다 낮게 잡는 편이 좋다.</param>
        /// <param name="duration">흔드는 시간(초). 0 이하면 아무 일도 하지 않는다.</param>
        /// <param name="interval">임펄스 재발사 간격(초).</param>
        /// <param name="directionJitter">방향 랜덤화 정도(0~1).</param>
        public void ShakeFor(Vector3 localDirection, float force, float duration, float interval = 0.06f, float directionJitter = 0.35f)
        {
            StartSustain(localDirection, force, duration, interval, directionJitter, null);
        }

        /// <summary>
        /// 진행 중인 지속 흔들림을 즉시 멈춘다.
        /// 코루틴은 매 프레임 <c>Player.IsActionInterrupted</c>를 보고 스스로 멈추므로 보통은 부를 일이 없고,
        /// 컷신 종료처럼 그 조건 밖에서 끊어야 할 때 쓴다.
        /// </summary>
        public void StopShake()
        {
            if (sustainRoutine == null) return;

            StopCoroutine(sustainRoutine);
            sustainRoutine = null;
        }

        private void StartSustain(Vector3 localDirection, float force, float duration, float interval, float jitter, AnimationCurve curve)
        {
            if (impulseSource == null || force <= 0f || duration <= 0f) return;

            StopShake();
            sustainRoutine = StartCoroutine(SustainRoutine(localDirection, force, duration, Mathf.Max(0.01f, interval), jitter, curve));
        }

        private IEnumerator SustainRoutine(Vector3 localDirection, float force, float duration, float interval, float jitter, AnimationCurve curve)
        {
            Vector3 baseDirection = localDirection.normalized;
            float elapsed = 0f;

            // 첫 발은 이벤트 프레임에 바로 나가야 타격 순간과 어긋나지 않는다.
            float sinceLastPulse = interval;

            while (elapsed < duration)
            {
                // 시작 시점과 같은 기준으로 매 프레임 재검사 → 구르기·피격·사망으로 캔슬되면 흔들림도 끊긴다.
                // 이게 없으면 긴 스킬 도중 구르기로 빠져나와도 화면만 계속 흔들린다.
                if (player != null && player.IsActionInterrupted) break;

                if (sinceLastPulse >= interval)
                {
                    sinceLastPulse = 0f;

                    // 세기 곡선은 진행도(0~1)로 평가한다. 비어 있는 곡선은 0을 돌려주므로
                    // (인스펙터에서 키를 다 지운 경우) 그때는 곡선을 무시하고 원래 세기를 쓴다.
                    float scale = (curve != null && curve.length > 0) ? curve.Evaluate(elapsed / duration) : 1f;

                    if (scale > 0f) Shake(Jitter(baseDirection, jitter), force * scale);
                }

                yield return null;

                // 스케일된 시간을 쓴다 → 히트스톱·슬로우모션이 걸리면 흔들림도 같이 늘어져
                // 애니메이션과 박자가 맞는다.
                elapsed += Time.deltaTime;
                sinceLastPulse += Time.deltaTime;
            }

            sustainRoutine = null;
        }

        // 기준 방향에서 무작위 방향 쪽으로 jitter만큼 틀어 준다.
        // 같은 축으로만 밀면 카메라가 한 방향으로 규칙적으로 튕겨 기계처럼 보인다.
        private static Vector3 Jitter(Vector3 baseDirection, float jitter)
        {
            if (jitter <= 0f) return baseDirection;

            return Vector3.Slerp(baseDirection, UnityEngine.Random.onUnitSphere, jitter).normalized;
        }

        private bool TryGetSlot(string key, out CameraEffectSlot slot)
        {
            // 다른 이펙트 컴포넌트와 같은 방침: 이벤트 인자 실수(오타)는 경고만 남기고 플레이는 계속한다.
            if (string.IsNullOrEmpty(key) || !effectMap.TryGetValue(AnimationEventKey.Normalize(key), out slot))
            {
                Debug.LogWarning($"Camera effect key not found or empty ('{key}'). Check the Animation Event string.", this);
                slot = null;
                return false;
            }

            return true;
        }
    }
}
