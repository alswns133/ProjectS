using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectS.Players
{
    /// <summary>
    /// 캐릭터 본체(몸 메시 + 무기 메시)만 잠시 보이지 않게 감췄다가 되돌리는 통로.
    /// 각성기처럼 "몸은 사라지고 검 궤적만 허공을 가르는" 연출을 위해 존재하며,
    /// 클립의 Animation Event가 <see cref="OnHideBody"/>/<see cref="OnShowBody"/>를 호출한다.
    ///
    /// ★ 오브젝트를 SetActive(false)로 끄지 않고 Renderer.enabled만 끄는 이유:
    ///   SetActive는 자식까지 통째로 멈춰서, 검에 달린 트레일 파티클도 같이 죽는다.
    ///   렌더러만 끄면 뼈·무기 트랜스폼은 계속 애니메이션되고 파티클도 계속 방출되므로,
    ///   궤적이 검을 따라 그대로 그려진다(연출의 핵심).
    ///
    /// ★ 반드시 Animator와 같은 GameObject(플레이어 루트)에 붙일 것.
    ///   Animation Event는 Animator가 붙은 오브젝트의 컴포넌트에서만 메서드를 찾는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerBodyVisibility : MonoBehaviour
    {
        // 감출 렌더러. 비워 두면 Awake에서 자식의 메시 렌더러를 자동 수집한다
        // (수집 규칙은 CollectBodyRenderers 참조). 특정 부위만 감추고 싶을 때만 손으로 채운다.
        [Header("감출 대상")]
        [SerializeField] private Renderer[] bodyRenderers;

        // 자동 수집에서 제외할 하위 트리. 메시로 만든 검기·잔상 이펙트가 캐릭터 밑에 붙어 있으면
        // 같이 꺼져 버리므로 그 루트를 여기에 넣는다.
        // (파티클·VFX Graph는 렌더러 타입이 달라 애초에 수집되지 않으므로 등록할 필요가 없다.)
        [SerializeField] private Transform[] excludeRoots;

        [Header("연출 옵션")]
        // true면 렌더러를 끄는 대신 그림자만 남긴다(본체는 안 보이고 바닥 그림자는 유지).
        // 심상의 공간처럼 존재 자체가 사라지는 연출이면 false가 맞고,
        // "모습만 감췄을 뿐 거기 있다"를 보여주고 싶으면 true로 켠다.
        [SerializeField] private bool keepShadowWhileHidden;

        // 복구 신호를 놓쳐도 이 시간이 지나면 강제로 되돌린다.
        // 플레이어가 영영 안 보이는 상태로 남는 것은 게임을 못 하게 되는 치명적 사고라,
        // Show 이벤트를 실수로 지웠거나 클립을 갈아끼워도 회복되게 하는 안전장치다.
        // 각성기 End 클립 길이보다 넉넉히 크게 잡을 것.
        [SerializeField, Min(0.1f)] private float maxHiddenTime = 6f;

        /// <summary>현재 본체가 감춰져 있는지 여부.</summary>
        public bool IsHidden { get; private set; }

        /// <summary>
        /// 감춤 대상으로 확정된 렌더러 목록(자동 수집·제외 설정이 반영된 최종 결과).
        /// 잔상(<c>PlayerAfterImage</c>)이 "본체가 무엇인가"를 같은 기준으로 알아야 해서 공개한다.
        /// 두 컴포넌트가 각자 대상을 관리하면, 인스펙터에서 제외 설정을 한쪽만 고쳤을 때
        /// 감춰진 부위와 잔상에 찍히는 부위가 어긋난다.
        /// </summary>
        public IReadOnlyList<Renderer> BodyRenderers => bodyRenderers;

        // 구르기·피격·사망으로 연출이 끊겼는지 확인할 중앙 컨텍스트.
        private Player player;

        // 감추기 직전 상태. 복구를 "무조건 켜기"가 아니라 "원래대로"로 만들기 위해 기억한다
        // (인덱스는 bodyRenderers와 짝). 히트박스 시각화처럼 원래 꺼져 있던 렌더러를
        // 연출이 끝난 뒤 켜 버리면, 각성기 한 번에 판정 박스가 화면에 나타나 버린다.
        private bool[] wasEnabled;
        private ShadowCastingMode[] originalShadowModes;

        // PlayerVfxEffects와 같은 방식: 각 State가 우리를 불러주지 않으므로
        // IsActionInterrupted의 false→true 엣지를 스스로 감지해 그 프레임에 복구한다.
        private bool wasInterrupted;

        // 감추기 시작한 시각. maxHiddenTime 초과 판정에만 쓴다.
        private float hiddenSince;

        private void Awake()
        {
            player = GetComponent<Player>();

            if (bodyRenderers == null || bodyRenderers.Length == 0)
                bodyRenderers = CollectBodyRenderers();

            if (bodyRenderers.Length == 0)
            {
                Debug.LogWarning("No body renderers found. Body hiding will do nothing.", this);
                return;
            }

            // 실제 값은 감추는 시점에 채운다. Awake에 고정하지 않는 이유:
            // 렌더러의 표시 상태는 플레이 중에도 바뀔 수 있어(디버그 토글 등)
            // 시작 시점 값으로 되돌리면 그 변경을 덮어쓰게 된다.
            wasEnabled = new bool[bodyRenderers.Length];
            originalShadowModes = new ShadowCastingMode[bodyRenderers.Length];
        }

        private void Update()
        {
            if (!IsHidden) return;

            // 구르기·피격·사망으로 각성기가 끊긴 그 프레임에 되돌린다.
            // 이게 없으면 연출 도중 캔슬했을 때 몸이 사라진 채로 조작하게 된다.
            bool interrupted = player != null && player.IsActionInterrupted;

            if (interrupted && !wasInterrupted)
            {
                wasInterrupted = true;
                OnShowBody();
                return;
            }

            wasInterrupted = interrupted;

            // 복구 신호를 놓친 경우의 최후 방어선.
            if (Time.time - hiddenSince >= maxHiddenTime)
            {
                Debug.LogWarning($"Body stayed hidden for {maxHiddenTime}s. Restoring by safety timeout — check the OnShowBody Animation Event.", this);
                OnShowBody();
            }
        }

        private void OnDisable()
        {
            // 감춘 상태로 비활성화되면 Update가 돌지 않아 안전장치도 멈춘다.
            // 다음에 다시 켤 때 투명한 채로 등장하지 않도록 여기서 즉시 되돌린다
            // (씬 전환·풀 반환처럼 플레이어가 잠시 꺼지는 경로가 실제로 있다).
            if (IsHidden) OnShowBody();
        }

        /// <summary>
        /// 본체(몸·무기 메시)를 감춘다. 각성기 클립의 Animation Event가 호출한다.
        /// 트레일·검기 같은 파티클/VFX는 대상이 아니므로 그대로 재생된다.
        /// 이미 감춰진 상태에서 다시 불러도 안전하며, 안전 타이머만 새로 시작한다.
        /// </summary>
        public void OnHideBody()
        {
            // 다른 이펙트 이벤트와 같은 게이트: 구르기·피격·사망으로 끊겼으면
            // 블렌드 아웃 중 뒤늦게 도착한 이벤트로 몸이 사라지는 것을 막는다.
            if (player != null && player.IsActionInterrupted) return;

            if (bodyRenderers == null) return;

            // 이미 감춰져 있으면 상태를 다시 찍지 않는다. 덮어쓰면 "감춰진 상태"가 원본으로
            // 기록되어 복구해도 계속 안 보이게 된다.
            bool firstHide = !IsHidden;

            IsHidden = true;
            hiddenSince = Time.time;
            wasInterrupted = player != null && player.IsActionInterrupted;

            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                Renderer r = bodyRenderers[i];
                if (r == null) continue;

                if (firstHide)
                {
                    wasEnabled[i] = r.enabled;
                    originalShadowModes[i] = r.shadowCastingMode;
                }

                // 그림자를 남기는 모드는 렌더러를 켜 둔 채 그림자만 그리게 한다.
                // enabled를 끄면 그림자까지 함께 사라지기 때문에 두 방식을 섞을 수 없다.
                if (keepShadowWhileHidden) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                else r.enabled = false;
            }
        }

        /// <summary>
        /// 감춘 본체를 다시 보이게 한다. 각성기 클립 막바지의 Animation Event가 호출하며,
        /// 연출이 캔슬되거나 안전 타임아웃이 걸렸을 때도 같은 경로로 복구된다.
        /// </summary>
        public void OnShowBody()
        {
            // 감춘 적이 없으면 되돌릴 원본이 없다. 그대로 진행하면 기본값(false)이 들어가
            // 멀쩡한 렌더러를 꺼 버리므로 여기서 끊는다.
            if (!IsHidden) return;

            IsHidden = false;

            if (bodyRenderers == null) return;

            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                Renderer r = bodyRenderers[i];
                if (r == null) continue;

                // 두 방식 중 무엇으로 감췄든 원상복구되게 둘 다 되돌린다
                // (연출 중 인스펙터에서 옵션을 바꿔도 한쪽이 남지 않는다).
                // "무조건 true"가 아니라 감추기 직전 값으로 되돌리는 것이 핵심 —
                // 원래 꺼져 있던 렌더러(히트박스 시각화 등)를 켜지 않기 위함이다.
                r.enabled = wasEnabled[i];
                r.shadowCastingMode = originalShadowModes[i];
            }
        }

        // 자식에서 감출 대상 렌더러를 모은다.
        // 메시 렌더러(SkinnedMeshRenderer·MeshRenderer)만 담는 이유: 파티클과 VFX Graph는
        // 렌더러 타입이 달라 여기서 걸러지므로, 트레일·검기가 실수로 꺼질 일이 없다.
        // 비활성 오브젝트까지 포함해 찾는다(연출용으로 꺼 둔 부위가 나중에 켜질 수 있다).
        private Renderer[] CollectBodyRenderers()
        {
            var found = new List<Renderer>();

            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is SkinnedMeshRenderer || r is MeshRenderer)) continue;
                if (IsExcluded(r.transform)) continue;

                found.Add(r);
            }

            return found.ToArray();
        }

        private bool IsExcluded(Transform target)
        {
            if (excludeRoots == null) return false;

            foreach (Transform root in excludeRoots)
            {
                if (root != null && target.IsChildOf(root)) return true;
            }

            return false;
        }

#if UNITY_EDITOR
        // 플레이 중 인스펙터에서 눌러 연출을 눈으로 확인하기 위한 편의 기능.
        // 클립에 이벤트를 찍기 전에 "무엇이 사라지고 무엇이 남는지" 먼저 검증할 때 쓴다.
        [ContextMenu("본체 감추기 테스트")]
        private void DebugHide() => OnHideBody();

        [ContextMenu("본체 되돌리기 테스트")]
        private void DebugShow() => OnShowBody();
#endif
    }
}
