using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using ProjectS.Players; // AnimationEventKey (슬롯 키 정규화). 히트박스·투사체·이펙트와 같은 규약을 공유한다.

namespace ProjectS.Enemies
{
    /// <summary>
    /// 몬스터 타격감용 카메라 흔들림 + 히트스톱을 재생하는 통로.
    /// <c>PlayerCameraEffects</c>의 축소판이며, <c>EnemyEffects</c>가 <c>PlayerEffects</c>의
    /// 축소판인 것과 같은 병렬 관계다(슬롯 키 + <see cref="AnimationEventKey.Normalize"/> 규약 공유).
    /// 클립의 Animation Event가 <see cref="OnCameraEffect"/>를 호출해 재생 시점을 정한다.
    ///
    /// ★ 플레이어판과의 차이:
    ///   - 궤도 회전·거리·렌즈 왜곡·카메라 조작 잠금은 제외. 그건 플레이어 카메라 제어
    ///     (CameraPivotController/CameraRig) 전용이라 몬스터엔 맞지 않는다.
    ///   - 중단 게이트(IsRolling/IsStaggered)도 뺐다. Enemy엔 아직 그 개념이 없다
    ///     (EnemyEffects가 IsActionInterrupted를 뺀 것과 같은 이유).
    ///
    /// ★ 카메라 transform을 직접 흔들지 않고 Cinemachine Impulse를 쓰는 이유:
    ///   실제 카메라는 CinemachineBrain이 LateUpdate에서 매 프레임 덮어써서, 직접 흔들면
    ///   같은 프레임에 지워진다. Impulse는 전역 신호라 vcam의 ImpulseListener가
    ///   소스 위치와 무관하게 받는다 → 보스에 ImpulseSource만 붙이면 카메라 쪽은 손댈 게 없다.
    ///
    /// ★ 반드시 Animator와 같은 GameObject(몬스터 루트)에 붙일 것.
    ///   Animation Event는 Animator가 붙은 오브젝트의 컴포넌트에서만 메서드를 찾는다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CinemachineImpulseSource))]
    public class EnemyCameraEffects : MonoBehaviour
    {
        // 키는 클립 Animation Event의 string 인자와 맞춘다.
        // EnemyEffects/투사체 등과 키 공간이 분리되어 있어 같은 이름을 써도 간섭하지 않는다.
        [Serializable]
        private class CameraEffectSlot
        {
            public string key;

            [Header("흔들림")]
            // 0이면 흔들림 없음. 이 슬롯을 히트스톱 전용으로도 만들 수 있다.
            [Min(0f)] public float shakeForce = 0.4f;

            // 캐릭터 기준 방향(로컬). 내려찍기=(0,-1,0), 앞으로 찌르기=(0,0,1) 식.
            // 월드 고정이 아니라 캐릭터 기준이라, 보스가 어느 쪽을 보고 때려도 연출이 같게 나온다.
            public Vector3 shakeDirection = new Vector3(0f, -1f, 0f);

            [Header("지속 흔들림 (0이면 1회만)")]
            // 이 시간 동안 interval마다 임펄스를 반복 발사한다. 0이면 이벤트 프레임에 한 번만.
            [Min(0f)] public float duration;

            // 임펄스 재발사 간격. 짧을수록 촘촘하고 무겁게 흔들린다.
            [Min(0.01f)] public float interval = 0.06f;

            // 매 발사마다 방향을 랜덤하게 트는 정도(0=항상 같은 방향, 1=완전 랜덤).
            [Range(0f, 1f)] public float directionJitter = 0.35f;

            [Header("히트스톱 (0이면 사용 안 함)")]
            // 타격 순간 게임 전체를 잠깐 멈춰 타격의 무게를 강조한다. 보통 0.03~0.08초면 충분.
            [Min(0f)] public float hitStopDuration;

            // 멈추는 동안의 시간 배율. 0=완전 정지, 0.05 같은 작은 값=슬로우모션 느낌.
            [Range(0f, 1f)] public float hitStopTimeScale;
        }

        [SerializeField] private CameraEffectSlot[] effects;

        // 매 이벤트마다 배열을 뒤지지 않도록 Awake에서 1회 구축하는 조회용 사전.
        private readonly Dictionary<string, CameraEffectSlot> effectMap = new Dictionary<string, CameraEffectSlot>();

        private CinemachineImpulseSource impulseSource;

        // 지속 흔들림은 동시에 하나만 굴린다(겹치면 세기가 눈덩이처럼 불어난다).
        private Coroutine sustainRoutine;

        // 히트스톱도 하나만. 연타로 겹치면 timeScale 복구 타이밍이 꼬인다.
        private Coroutine hitStopRoutine;

        private void Awake()
        {
            impulseSource = GetComponent<CinemachineImpulseSource>();

            if (effects == null) return;

            foreach (var slot in effects)
            {
                if (slot == null || string.IsNullOrEmpty(slot.key)) continue;

                // 언더바 표기 차이를 흡수하려 정규화한 키로 등록·조회한다(AnimationEventKey 참조).
                // 클립이 "Attack_2", 인스펙터가 "Attack2"여도 맞물린다.
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

        private void OnDisable()
        {
            // 히트스톱 도중 보스가 죽어 비활성화되면 timeScale이 낮은 값에 갇힌 채로 남아
            // 게임 전체가 느려지거나 멈춘다. 우리가 건 히트스톱이 진행 중일 때만 복구한다
            // (다른 시스템의 timeScale을 함부로 덮어쓰지 않기 위해 hitStopRoutine으로 가드).
            if (hitStopRoutine != null) Time.timeScale = 1f;

            // 비활성화되면 Unity가 코루틴을 멈추지만 핸들은 남아 다음 활성화 때
            // 죽은 코루틴을 붙잡게 된다 → 여기서 비운다.
            sustainRoutine = null;
            hitStopRoutine = null;
        }

        /// <summary>
        /// 공격 클립의 Animation Event가 호출한다. 인자는 인스펙터에 등록한 슬롯 키.
        /// 흔들림과 히트스톱을 슬롯 값에 따라 함께 재생한다.
        /// </summary>
        /// <param name="key">슬롯 키. 언더바 표기 차이는 무시된다(예: "Attack_2" == "Attack2").</param>
        public void OnCameraEffect(string key)
        {
            if (!TryGetSlot(key, out CameraEffectSlot slot)) return;

            if (slot.shakeForce > 0f)
            {
                if (slot.duration > 0f)
                    StartSustain(slot.shakeDirection, slot.shakeForce, slot.duration, slot.interval, slot.directionJitter);
                else
                    Shake(slot.shakeDirection, slot.shakeForce);
            }

            if (slot.hitStopDuration > 0f)
                StartHitStop(slot.hitStopDuration, slot.hitStopTimeScale);
        }

        /// <summary>
        /// 카메라를 1회 흔든다. 클립 이벤트가 아닌 곳(보스 등장 연출 등)에서도 부를 수 있게 public.
        /// </summary>
        /// <param name="localDirection">캐릭터 기준 흔들림 방향. 정규화되지 않아도 된다.</param>
        /// <param name="force">세기. 0.2~0.5가 평타, 1 이상은 강타 느낌.</param>
        public void Shake(Vector3 localDirection, float force)
        {
            if (impulseSource == null || force <= 0f) return;

            // 캐릭터 기준 방향을 월드로 변환한다. 안 하면 보스가 뒤를 보고 때릴 때 방향만 반대로 나간다.
            Vector3 world = transform.TransformDirection(localDirection.normalized);
            impulseSource.GenerateImpulseWithVelocity(world * force);
        }

        /// <summary>
        /// 지정 시간 동안 카메라를 계속 흔든다. 이미 지속 흔들림이 돌고 있으면 끊고 새로 시작한다.
        /// </summary>
        public void ShakeFor(Vector3 localDirection, float force, float duration, float interval = 0.06f, float directionJitter = 0.35f)
        {
            StartSustain(localDirection, force, duration, interval, directionJitter);
        }

        /// <summary>
        /// 진행 중인 지속 흔들림을 즉시 멈춘다(컷신 종료 등에서 사용).
        /// </summary>
        public void StopShake()
        {
            if (sustainRoutine == null) return;

            StopCoroutine(sustainRoutine);
            sustainRoutine = null;
        }

        private void StartSustain(Vector3 localDirection, float force, float duration, float interval, float jitter)
        {
            if (impulseSource == null || force <= 0f || duration <= 0f) return;

            StopShake();
            sustainRoutine = StartCoroutine(SustainRoutine(localDirection, force, duration, Mathf.Max(0.01f, interval), jitter));
        }

        private IEnumerator SustainRoutine(Vector3 localDirection, float force, float duration, float interval, float jitter)
        {
            Vector3 baseDirection = localDirection.normalized;
            float elapsed = 0f;

            // 첫 발은 이벤트 프레임에 바로 나가야 타격 순간과 어긋나지 않는다.
            float sinceLastPulse = interval;

            while (elapsed < duration)
            {
                if (sinceLastPulse >= interval)
                {
                    sinceLastPulse = 0f;
                    Shake(Jitter(baseDirection, jitter), force);
                }

                yield return null;

                // 스케일된 시간을 쓴다 → 히트스톱이 걸리면 흔들림도 같이 멈췄다 이어져 박자가 맞는다.
                elapsed += Time.deltaTime;
                sinceLastPulse += Time.deltaTime;
            }

            sustainRoutine = null;
        }

        // 기준 방향에서 무작위 방향 쪽으로 jitter만큼 튼다.
        // 같은 축으로만 밀면 카메라가 규칙적으로 튕겨 기계처럼 보인다.
        private static Vector3 Jitter(Vector3 baseDirection, float jitter)
        {
            if (jitter <= 0f) return baseDirection;

            return Vector3.Slerp(baseDirection, UnityEngine.Random.onUnitSphere, jitter).normalized;
        }

        private void StartHitStop(float duration, float timeScale)
        {
            if (hitStopRoutine != null) StopCoroutine(hitStopRoutine);
            hitStopRoutine = StartCoroutine(HitStopRoutine(duration, Mathf.Clamp01(timeScale)));
        }

        private IEnumerator HitStopRoutine(float duration, float timeScale)
        {
            Time.timeScale = timeScale;

            // ★ 반드시 Realtime(unscaled) 대기. timeScale=0에서 일반 WaitForSeconds는
            //   시간이 안 흘러 영영 안 풀려 게임이 멈춘다.
            yield return new WaitForSecondsRealtime(duration);

            Time.timeScale = 1f;
            hitStopRoutine = null;
        }

        private bool TryGetSlot(string key, out CameraEffectSlot slot)
        {
            // 이벤트 인자 실수(오타)는 경고만 남기고 플레이는 계속한다(다른 이펙트 컴포넌트와 같은 방침).
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
