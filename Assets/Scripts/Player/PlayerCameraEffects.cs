using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

namespace ProjectS.Players
{
    /// <summary>
    /// 타격감용 카메라 흔들림을 재생하는 통로.
    /// <c>PlayerEffects</c>/<c>PlayerVfxEffects</c>와 같은 규약(슬롯 키·정규화·중단 게이트)을 따르는
    /// 병렬 컴포넌트다. 클립의 Animation Event가 <see cref="OnCameraEffect"/>를 호출한다.
    /// <c>PlayerEffects</c>/<c>PlayerVfxEffects</c>와 같은 규약(슬롯 키·정규화·중단 게이트)을 따르는
    /// 세 번째 병렬 컴포넌트다. 클립의 Animation Event가 <see cref="OnCameraEffect"/>를 호출한다.
    ///
    /// ★ 카메라 transform을 직접 흔들지 않고 Cinemachine Impulse를 쓰는 이유:
    ///   실제 카메라는 CinemachineBrain이 LateUpdate에서 매 프레임 덮어쓰고,
    ///   <c>CameraPivotController</c>도 LateUpdate에서 rotation을 통째로 대입한다.
    ///   직접 흔들면 같은 프레임에 지워져 아무 일도 일어나지 않는다.
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
        }

        [SerializeField] private CameraEffectSlot[] effects;

        // 매 이벤트마다 배열을 뒤지지 않도록 Awake에서 1회 구축하는 조회용 사전.
        private readonly Dictionary<string, CameraEffectSlot> effectMap = new Dictionary<string, CameraEffectSlot>();

        // 구르기·피격·사망 중 뒤늦게 도착한 이벤트를 무시하기 위해 상태를 조회할 중앙 컨텍스트.
        private Player player;

        private CinemachineImpulseSource impulseSource;

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

            Shake(slot.shakeDirection, slot.shakeForce);
        }

        /// <summary>
        /// 카메라를 흔든다. 코드에서 직접 부를 수 있게 public으로 열어 둔다
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
