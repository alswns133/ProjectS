using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

namespace ProjectS.Players
{
    /// <summary>
    /// VFX Graph(<see cref="VisualEffect"/>) 이펙트를 재생·중지하는 통로.
    /// <c>PlayerEffects</c>와 완전히 같은 규약(슬롯 키·정규화·월드 고정·중단 게이트)을 따르는 병렬 컴포넌트다.
    ///
    /// 별도 컴포넌트로 나눈 이유: <c>PlayerEffects</c>의 슬롯 타입이 <see cref="ParticleSystem"/>이라
    /// VisualEffect를 등록할 수 없고, <c>ParticleSystem.Play()</c>는 자식 ParticleSystem만 재생하므로
    /// "부모 하나만 등록" 트릭도 통하지 않기 때문이다. Shuriken보다 품질이 높은 VFX Graph 에셋을
    /// 쓰기 위해 필요하다.
    ///
    /// ★ 반드시 Animator와 같은 GameObject(플레이어 루트)에 붙일 것.
    ///   Animation Event는 Animator가 붙은 오브젝트의 컴포넌트에서만 메서드를 찾는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerVfxEffects : MonoBehaviour
    {
        // 키는 클립 Animation Event의 string 인자와 맞춘다.
        // PlayerEffects와 키 공간이 분리되어 있으므로, 같은 이름을 써도 서로 간섭하지 않는다.
        [Serializable]
        private class VfxSlot
        {
            public string key;
            public VisualEffect vfx;

            // 재생 속도 배율(Shuriken Main 모듈의 Simulation Speed에 해당).
            // 이펙트 전체 시간이 배속된다 — 스폰·수명·움직임·디졸브가 같은 비율로 빨라지거나 느려진다.
            // VisualEffect 컴포넌트 인스펙터에 노출되지 않는 런타임 값이라 슬롯에서 지정한다.
            // 같은 그래프를 여러 슬롯에 재사용하면서 타수마다 속도만 다르게 줄 때 유용하다.
            [Range(0.05f, 5f)] public float playRate = 1f;

            // 시전 위치에 남아야 하는 이펙트(땅의 검흔 등)만 켠다.
            // 재생 순간 플레이어에서 분리해 월드에 고정하므로 이동해도 따라오지 않는다.
            public bool anchorToWorld;

            // 월드 고정 이펙트를 중단 정리(구르기·피격·사망)에도 함께 멈출지 여부.
            // anchorToWorld는 기본적으로 "동작이 끊겨도 그대로 남는다"(땅의 검흔은 죽어도 남아야 함)라
            // StopAll에서 제외된다. 하지만 설치형 지속 이펙트(장판·오라 등)는 캐릭터를 따라오지
            // 않게 월드 고정을 켜면서도, 동작이 끊기면 같이 걷어야 할 때가 있다.
            // 이 값을 켜면 월드 고정 슬롯이라도 StopAll 중단 정리에 포함된다.
            // ★ anchorToWorld가 false면 의미 없다(그때는 어차피 항상 정리 대상).
            public bool stopOnInterrupt;

            // 분리했다가 다음 재생 때 제자리로 복귀시키기 위한 원래 부모/로컬 포즈.
            [NonSerialized] public Transform originalParent;
            [NonSerialized] public Vector3 originalLocalPosition;
            [NonSerialized] public Quaternion originalLocalRotation;
        }

        [SerializeField] private VfxSlot[] effects;

        // 매 이벤트마다 배열을 뒤지지 않도록 Awake에서 1회 구축하는 조회용 사전.
        private readonly Dictionary<string, VfxSlot> effectMap = new Dictionary<string, VfxSlot>();

        // 구르기·피격·사망 중 뒤늦게 도착한 이펙트 이벤트를 무시하기 위해 상태를 조회할 중앙 컨텍스트.
        private Player player;

        // PlayerEffects.AllStopEffect()는 RollState/HitState/DeadState/JumpDashState가 직접 호출하는데,
        // 그 State들은 수정 금지 영역이라 우리를 불러주지 않는다. 그래서 같은 시점을 스스로 감지하려고
        // IsActionInterrupted의 false→true 엣지를 추적한다(폴링이지만 bool 비교 한 번이라 비용은 무시 가능).
        private bool wasInterrupted;

        private void Awake()
        {
            player = GetComponent<Player>();

            if (effects == null) return;

            foreach (var slot in effects)
            {
                if (slot == null || string.IsNullOrEmpty(slot.key)) continue;

                // PlayerEffects와 같은 방침: 언더바 표기 차이를 흡수하려 정규화한 키로 등록·조회한다
                // (AnimationEventKey 참조). 클립이 "Skill_2_1", 인스펙터가 "Skill21"이어도 맞물린다.
                string normKey = AnimationEventKey.Normalize(slot.key);

                // 키 중복을 조용히 덮어쓰면 한쪽 이펙트가 영영 안 나와 원인 찾기 어렵다 → 경고.
                if (effectMap.ContainsKey(normKey))
                {
                    Debug.LogWarning($"Duplicate VFX key '{slot.key}'. Only the first slot is used.", this);
                    continue;
                }

                // 월드 고정 슬롯은 분리 후 복귀할 원래 자리를 기억해 둔다.
                if (slot.anchorToWorld && slot.vfx != null)
                {
                    Transform tr = slot.vfx.transform;
                    slot.originalParent = tr.parent;
                    slot.originalLocalPosition = tr.localPosition;
                    slot.originalLocalRotation = tr.localRotation;
                }

                effectMap.Add(normKey, slot);
            }
        }

        private void Update()
        {
            // 구르기·피격·사망으로 동작이 끊긴 그 프레임에 잔여 이펙트를 정리한다.
            // PlayerEffects.AllStopEffect()가 각 State의 Enter()에서 하는 일과 같은 효과.
            bool interrupted = player != null && player.IsActionInterrupted;

            if (interrupted && !wasInterrupted) StopAll();

            wasInterrupted = interrupted;
        }

        /// <summary>
        /// 공격/스킬 클립의 Animation Event가 호출한다. 인자는 인스펙터에 등록한 슬롯 키.
        /// 월드 고정 슬롯이면 현재 시전 위치에서 분리해 그 자리에 남긴다.
        /// </summary>
        /// <param name="key">슬롯 키. 언더바 표기 차이는 무시된다(예: "Skill_2_1" == "Skill21").</param>
        public void OnVfxEffect(string key)
        {
            // 입력을 막는 조건과 동일(Player.IsActionInterrupted) → 이벤트도 같은 기준으로 게이트.
            if (player != null && player.IsActionInterrupted) return;

            if (!TryGetSlot(key, out VfxSlot slot)) return;

            if (slot.anchorToWorld)
            {
                // ① 원래 자리(플레이어 자식)로 복귀시켜 이번 시전 위치에 맞춘 뒤
                // ② 그 월드 포즈 그대로 분리한다 → 재생 중 플레이어가 움직여도 따라오지 않는다.
                // 끝난 뒤 되붙이지 않는 이유: 다음 재생 때 ①이 다시 제자리로 데려오므로 콜백이 필요 없다.
                Transform tr = slot.vfx.transform;
                tr.SetParent(slot.originalParent, false);
                tr.localPosition = slot.originalLocalPosition;
                tr.localRotation = slot.originalLocalRotation;
                tr.SetParent(null, true);
            }

            // 재생 직전에 매번 넣는 이유: 인스펙터에서 값을 돌리면 플레이 중에도 바로 반영되어
            // 튜닝이 빠르다(재생 중 이펙트에는 다음 재생부터 적용).
            // Max로 감싸는 이유: 슬롯을 이미 채워 둔 프리팹에 playRate 필드가 나중에 추가되면
            // 기존 직렬화 데이터에 값이 없어 0으로 들어올 수 있다. 0이면 이펙트가 멈춘 채로 보인다.
            slot.vfx.playRate = Mathf.Max(0.05f, slot.playRate);

            // Play()는 그래프의 내장 "OnPlay" 이벤트를 보낸다(Spawn 컨텍스트가 기본으로 듣는 이벤트).
            // Initial Event Name과는 별개라, Initial Event Name을 더미로 비워 자동 재생을 꺼도 정상 동작한다.
            slot.vfx.Play();
        }

        /// <summary>
        /// 해당 이펙트의 방출을 멈춘다(이미 나온 파티클은 수명대로 자연 소멸).
        /// 차징·오라 같은 루프 이펙트를 꺼야 하는 프레임에 Animation Event로 호출한다.
        /// </summary>
        /// <param name="key">슬롯 키.</param>
        public void OffVfxEffect(string key)
        {
            if (!TryGetSlot(key, out VfxSlot slot)) return;
            slot.vfx.Stop();
        }

        /// <summary>
        /// 모든 이펙트를 즉시 제거한다. 구르기 캔슬·사망처럼 동작이 강제로 끊길 때
        /// 잔상이 화면에 남지 않게 한다(Update의 중단 감지에서 자동 호출).
        /// </summary>
        public void StopAll()
        {
            if (effects == null) return;

            foreach (var slot in effects)
            {
                // 아직 안 채운 슬롯이 있어도 순회가 끊기지 않게 건너뛴다.
                // 월드 고정 슬롯을 건드리지 않는 것도 PlayerEffects.AllStopEffect와 같은 규칙
                // (땅에 남긴 검흔은 플레이어가 구르든 죽든 그대로 둔다).
                // 단, stopOnInterrupt가 켜진 설치형 지속 이펙트는 월드 고정이어도 함께 걷는다.
                if (slot == null || slot.vfx == null) continue;
                if (slot.anchorToWorld && !slot.stopOnInterrupt) continue;

                slot.vfx.Stop();

                // Stop()은 방출만 멈춰 이미 뜬 입자가 남는다. Reinit()으로 버퍼까지 비워야
                // ParticleSystem의 StopEmittingAndClear와 같은 결과가 된다.
                // ★ Reinit()은 Initial Event Name을 다시 보내므로, 그 이름이 "OnPlay"로 남아 있으면
                //   지우자마자 다시 재생된다. 인스펙터에서 더미 이름(예: "Manual")으로 바꿔 둘 것.
                slot.vfx.Reinit();
            }
        }

        private bool TryGetSlot(string key, out VfxSlot slot)
        {
            // PlayerEffects와 같은 방침: 이벤트 인자 실수(오타)는 경고만 남기고 플레이는 계속한다.
            if (string.IsNullOrEmpty(key) || !effectMap.TryGetValue(AnimationEventKey.Normalize(key), out slot) || slot.vfx == null)
            {
                Debug.LogWarning($"VFX key not found or empty ('{key}'). Check the Animation Event string.", this);
                slot = null;
                return false;
            }

            return true;
        }
    }
}
