using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

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
        }

        [SerializeField] private CameraEffectSlot[] effects;

        // 매 이벤트마다 배열을 뒤지지 않도록 Awake에서 1회 구축하는 조회용 사전.
        private readonly Dictionary<string, CameraEffectSlot> effectMap = new Dictionary<string, CameraEffectSlot>();

        // 구르기·피격·사망 중 뒤늦게 도착한 이벤트를 무시하기 위해 상태를 조회할 중앙 컨텍스트.
        private Player player;

        private CinemachineImpulseSource impulseSource;

        // 지속 흔들림은 동시에 하나만 굴린다. 새 지속 흔들림이 시작되면 이전 것을 끊는다
        // (겹치면 세기가 눈덩이처럼 불어나 화면이 통제 불능으로 흔들린다).
        private Coroutine sustainRoutine;

        private void Awake()
        {
            player = GetComponent<Player>();
            impulseSource = GetComponent<CinemachineImpulseSource>();

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
            // 입력을 막는 조건과 동일(Player.IsActionInterrupted) → 이벤트도 같은 기준으로 게이트.
            // 구르기로 캔슬했는데 뒤늦게 화면이 흔들리는 걸 막는다.
            if (player != null && player.IsActionInterrupted) return;

            if (!TryGetSlot(key, out CameraEffectSlot slot)) return;

            if (slot.duration > 0f)
                StartSustain(slot.shakeDirection, slot.shakeForce, slot.duration, slot.interval, slot.directionJitter, slot.strengthOverTime);
            else
                Shake(slot.shakeDirection, slot.shakeForce);
        }

        private void OnDisable()
        {
            // 비활성화되면 Unity가 코루틴을 알아서 멈추지만, 핸들은 남아 다음 활성화 때
            // 죽은 코루틴을 붙잡고 있게 된다 → 여기서 비운다.
            sustainRoutine = null;
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
