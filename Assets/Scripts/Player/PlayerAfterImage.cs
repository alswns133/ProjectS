using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectS.Players
{
    /// <summary>
    /// 그 순간의 캐릭터 포즈를 정적 메시로 구워 월드에 남기는 잔상 연출.
    /// 각성기처럼 궤적을 그리는 동작에서 꼭짓점마다 <see cref="OnAfterImage"/>를
    /// Animation Event로 호출해 "지나간 자리에 캐릭터가 남는" 그림을 만든다.
    ///
    /// ★ 캐릭터 프리팹을 Instantiate 하지 않고 <c>SkinnedMeshRenderer.BakeMesh</c>를 쓰는 이유:
    ///   복제는 Animator·컨트롤러·물리까지 통째로 따라와 무겁고, 복제본이 자기 애니메이션을
    ///   이어서 재생해 "그 순간 포즈로 멈춘 잔상"이 되지 않는다. 구운 메시는 정지한 스냅샷이다.
    ///
    /// ★ 본체가 감춰져 있어도(<c>PlayerBodyVisibility</c>가 렌더러를 꺼도) 정상 동작한다.
    ///   BakeMesh는 렌더러 표시 여부가 아니라 뼈 트랜스폼을 읽고, 뼈는 Animator가 계속 굴린다.
    ///
    /// ★ 반드시 Animator와 같은 GameObject(플레이어 루트)에 붙일 것.
    ///   Animation Event는 Animator가 붙은 오브젝트의 컴포넌트에서만 메서드를 찾는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerAfterImage : MonoBehaviour
    {
        [Header("잔상 재료")]
        // 잔상 전용 머티리얼(반투명 Unlit 계열 권장). 원본 머티리얼을 그대로 쓰면
        // 잔상인지 본체인지 구분이 안 되므로 별도로 지정한다. 비어 있으면 아무것도 남기지 않는다.
        [SerializeField] private Material ghostMaterial;

        // 페이드에 쓸 색상 프로퍼티 이름. URP/Unlit·URP/Lit은 "_BaseColor",
        // 직접 만든 Shader Graph는 노출한 이름(보통 "_Color")을 넣는다.
        // 이름이 틀리면 색만 안 변하고 잔상 자체는 보인다(원인 찾기 쉬우라고 예외로 막지 않는다).
        [SerializeField] private string colorProperty = "_BaseColor";

        // 잔상이 처음 뜰 때의 색. 알파가 시작 투명도다.
        [SerializeField] private Color startColor = new Color(0.45f, 0.75f, 1f, 0.6f);

        [Header("수명")]
        // true면 잔상이 각자 수명으로 사라지지 않고, OnAfterImageFadeOut 신호를 받을 때까지
        // 그대로 떠 있다가 전부 함께 사라진다. 별 궤적처럼 "다 그려진 도형이 한 번에 흩어지는"
        // 연출용. false면 아래 lifetime으로 하나씩 사라진다(기존 동작).
        [SerializeField] private bool waitForFadeSignal;

        // 개별 수명(초). waitForFadeSignal이 꺼져 있을 때만 쓴다.
        [SerializeField, Min(0.05f)] private float lifetime = 0.6f;

        // 신호를 받은 뒤 다 사라지기까지 걸리는 시간(초). waitForFadeSignal일 때만 쓴다.
        // 0이면 페이드 없이 신호를 받은 프레임에 즉시 사라진다(툭 끊기는 연출).
        [SerializeField, Min(0f)] private float fadeOutDuration = 0.35f;

        // 신호를 못 받았을 때 강제로 사라지는 시간(초).
        // 이벤트를 빠뜨리거나 연출이 중간에 끊겨도 잔상이 화면에 영원히 남지 않게 하는 안전장치다.
        [SerializeField, Min(0.1f)] private float maxHoldTime = 6f;

        // 수명 진행도(0~1)에 따른 알파 배율. 기본은 1 → 0으로 서서히 사라진다.
        // 확 나타났다 천천히 지우고 싶으면 앞부분을 평탄하게 만든다.
        [SerializeField] private AnimationCurve alphaOverLife = AnimationCurve.Linear(0f, 1f, 1f, 0f);

        // 동시에 존재할 수 있는 잔상 수. 별 하나가 5꼭짓점이므로 그보다 넉넉히 잡는다.
        // 초과하면 가장 오래된 잔상을 재사용한다(새 잔상이 안 나오는 것보다 낫다).
        [SerializeField, Min(1)] private int maxGhosts = 8;

        [Header("지정 위치 잔상")]
        // 키로 부르는 잔상 슬롯. 위치·회전은 인스펙터에서 잡고 클립에서는 키만 넘긴다
        // (히트박스·이펙트 슬롯과 같은 규약). 별 꼭짓점처럼 "정해진 자리"에 남길 때 쓴다.
        // Animation Event가 인자를 하나만 넘길 수 있어서, 위치와 회전을 함께 주려면 이 방식이 필요하다.
        [SerializeField] private PoseSlot[] poseSlots;

        // 잔상 하나의 배치 값. key는 클립 Animation Event의 string 인자와 맞춘다.
        [Serializable]
        private class PoseSlot
        {
            public string key;

            // 기준점에서의 오프셋(미터). 기준이 바라보는 쪽이 +Z다.
            // 월드 좌표가 아니라 기준 상대값인 이유: 캐릭터가 어느 방향을 보고 시전하든
            // 같은 모양이 그려져야 하기 때문이다.
            public Vector3 offset;

            // 잔상을 좌우로 돌리는 각도(도). OnAfterImage의 Float 인자와 같은 의미다.
            public float yawOffset;

            // true면 이번 묶음의 첫 잔상이 뜬 시점(또는 OnAfterImageAnchor를 찍은 시점)의
            // 캐릭터 위치·방향을 기준으로 삼는다. 클립이 루트모션으로 이동하는 동안에도
            // 도형이 흔들리지 않으므로, 별처럼 정해진 모양을 그릴 땐 켜는 쪽이 맞다.
            // false면 이벤트가 찍힌 순간의 캐릭터가 기준이다.
            public bool fromAnchor = true;
        }

        [Header("대상")]
        // 잔상으로 구울 렌더러. 비워 두면 PlayerBodyVisibility가 확정한 목록(몸+무기)을 그대로 쓰고,
        // 그것도 없으면 자식에서 직접 수집한다. 보통은 비워 두는 것이 맞다.
        [SerializeField] private Renderer[] sourceRenderers;

        // 직접 수집할 때만 쓰는 제외 목록(PlayerBodyVisibility의 같은 이름 필드와 같은 의미).
        [SerializeField] private Transform[] excludeRoots;

        // 잔상 1개 = 원본 렌더러 수만큼의 자식 메시 묶음.
        // 몸(스킨드)과 무기(정적)를 함께 남겨야 포즈가 완성되므로 개별 오브젝트가 아니라 묶음으로 다룬다.
        private class Ghost
        {
            public Transform root;
            public Transform[] parts;
            public MeshFilter[] filters;
            public MeshRenderer[] renderers;

            // 스킨드 원본용 재사용 메시. 매번 new Mesh()를 만들면 잔상을 뿌릴 때마다 GC가 튄다.
            // 정적 원본(무기)은 원본 메시를 그대로 참조하므로 null이다.
            public Mesh[] bakedMeshes;

            // 파트별로 마지막에 적용한 머티리얼과 서브메시 수.
            // 매 잔상마다 머티리얼 배열을 새로 만들지 않기 위한 캐시이며,
            // 인스펙터에서 머티리얼을 갈아끼우면 값이 달라져 자동으로 다시 적용된다.
            public Material[] appliedMaterials;
            public int[] appliedSubMeshCounts;

            public float bornTime;
            public bool active;
        }

        private readonly List<Ghost> ghosts = new List<Ghost>();

        // 원본 렌더러를 종류별로 갈라 캐싱한다. 스킨드는 매번 구워야 하고,
        // 정적 메시는 원본 메시를 그대로 쓰면 되어 처리가 다르다.
        private Renderer[] sources;
        private MeshFilter[] sourceFilters;   // 정적 원본의 MeshFilter(스킨드 자리는 null)

        // 잔상은 월드에 고정되어야 하므로 사용 중엔 부모에서 분리한다.
        // 대기 중인 잔상만 이 컨테이너 아래에 모아 둔다(플레이어를 따라다녀도 무방).
        private Transform poolRoot;

        // 알파만 바꾸려고 머티리얼 인스턴스를 만들면 잔상 수만큼 머티리얼이 새로 생긴다.
        // MaterialPropertyBlock은 인스턴스를 만들지 않고 렌더러별 값만 덮어쓴다.
        private MaterialPropertyBlock propertyBlock;
        private int colorPropertyId;

        // 구르기·피격·사망으로 끊긴 뒤 뒤늦게 도착한 이벤트를 무시하기 위한 중앙 컨텍스트.
        private Player player;

        // 일괄 소멸이 시작된 시각. 음수면 아직 신호를 받지 않은 상태(잔상은 그대로 떠 있다).
        private float fadeStartTime = -1f;

        // 이번 묶음의 첫 잔상이 뜬 시각. maxHoldTime 초과 판정에만 쓴다.
        private float holdStartTime;

        // 지정 위치 잔상의 기준 좌표계(이번 묶음이 시작된 시점의 캐릭터 위치·방향).
        // 이동하는 클립에서도 도형이 제자리에 그려지게 하는 근거다.
        private Vector3 anchorPosition;
        private Quaternion anchorRotation = Quaternion.identity;
        private bool hasAnchor;

        // 이번 묶음의 기준을 OnAfterImageAnchor로 직접 찍었는지.
        // 켜져 있으면 첫 잔상이 뜰 때 기준을 자동으로 덮어쓰지 않는다.
        private bool anchorSetManually;

        // 매 이벤트마다 배열을 뒤지지 않도록 Awake에서 1회 구축하는 조회용 사전.
        private readonly Dictionary<string, PoseSlot> poseMap = new Dictionary<string, PoseSlot>();

        private void Awake()
        {
            player = GetComponent<Player>();
            propertyBlock = new MaterialPropertyBlock();
            colorPropertyId = Shader.PropertyToID(colorProperty);

            poolRoot = new GameObject("AfterImagePool").transform;
            poolRoot.SetParent(transform, false);

            if (poseSlots == null) return;

            foreach (PoseSlot slot in poseSlots)
            {
                if (slot == null || string.IsNullOrEmpty(slot.key)) continue;

                // 다른 이벤트 슬롯과 같은 방침: 언더바 표기 차이를 흡수하려 정규화한 키로 등록·조회한다
                // (AnimationEventKey 참조). 클립이 "Skill_4_1", 인스펙터가 "Skill41"이어도 맞물린다.
                string normKey = AnimationEventKey.Normalize(slot.key);

                // 키 중복을 조용히 덮어쓰면 한쪽 잔상이 영영 안 나와 원인 찾기 어렵다 → 경고.
                if (poseMap.ContainsKey(normKey))
                {
                    Debug.LogWarning($"Duplicate after-image key '{slot.key}'. Only the first slot is used.", this);
                    continue;
                }

                poseMap.Add(normKey, slot);
            }
        }

        // 원본 목록 확정은 Awake가 아니라 첫 사용 시점에 한다.
        // 같은 GameObject에 붙은 컴포넌트끼리는 Awake 호출 순서가 보장되지 않아,
        // Awake에서 읽으면 PlayerBodyVisibility가 아직 목록을 못 채운 상태일 수 있다.
        // 그러면 제외 설정이 빠진 채 히트박스 메시까지 잔상으로 구워진다.
        private bool EnsureSources()
        {
            if (sources != null) return sources.Length > 0;

            sources = ResolveSources();
            sourceFilters = new MeshFilter[sources.Length];

            for (int i = 0; i < sources.Length; i++)
            {
                // 스킨드가 아닌 원본은 MeshFilter에서 메시를 가져온다. 없으면 그 슬롯은 건너뛴다.
                if (sources[i] == null || sources[i] is SkinnedMeshRenderer) continue;

                sources[i].TryGetComponent(out MeshFilter filter);
                sourceFilters[i] = filter;
            }

            if (sources.Length == 0)
                Debug.LogWarning("No source renderers for after-image. Nothing will be spawned.", this);

            return sources.Length > 0;
        }

        private void Update()
        {
            if (waitForFadeSignal) UpdateHoldSafety();

            for (int i = 0; i < ghosts.Count; i++)
            {
                Ghost ghost = ghosts[i];
                if (!ghost.active) continue;

                // 씬 전환 등으로 월드에 분리돼 있던 잔상이 파괴됐을 수 있다.
                if (ghost.root == null)
                {
                    ghost.active = false;
                    continue;
                }

                float progress = GetProgress(ghost);

                if (progress >= 1f)
                {
                    Recycle(ghost);
                    continue;
                }

                ApplyAlpha(ghost, alphaOverLife.Evaluate(progress));
            }
        }

        // 잔상의 소멸 진행도(0~1). 두 모드의 유일한 차이가 여기에 모여 있다.
        private float GetProgress(Ghost ghost)
        {
            // 개별 수명 모드: 잔상마다 자기 나이로 사라진다.
            if (!waitForFadeSignal) return (Time.time - ghost.bornTime) / lifetime;

            // 신호 대기 모드: 신호 전에는 진행도를 0으로 묶어 둔다 → 먼저 생긴 잔상도 옅어지지 않아
            // 별 모양이 온전히 남는다. 신호 뒤에는 모든 잔상이 같은 진행도를 공유하므로
            // 생성 프레임이 제각각이어도 정확히 같은 순간에 사라진다.
            if (fadeStartTime < 0f) return 0f;

            // 0초 페이드는 나눗셈이 성립하지 않는다(0/0 = NaN이라 소멸 판정을 통과하지 못한다).
            // 신호 프레임에 바로 사라지도록 진행도를 끝값으로 못박는다.
            if (fadeOutDuration <= 0f) return 1f;

            return (Time.time - fadeStartTime) / fadeOutDuration;
        }

        // 신호가 영영 오지 않는 경우(이벤트 누락·연출 캔슬)를 걸러 페이드로 넘긴다.
        // 없으면 잔상이 화면에 박제된 채 남는다.
        private void UpdateHoldSafety()
        {
            if (fadeStartTime >= 0f || !HasActiveGhost) return;

            // 구르기·피격·사망으로 각성기가 끊기면 클립이 끝까지 재생되지 않아 신호도 오지 않는다.
            if (player != null && player.IsActionInterrupted)
            {
                BeginFadeOut();
                return;
            }

            if (Time.time - holdStartTime >= maxHoldTime)
            {
                Debug.LogWarning($"After-images held for {maxHoldTime}s without a fade signal. Fading by safety timeout — check the OnAfterImageFadeOut Animation Event.", this);
                BeginFadeOut();
            }
        }

        private bool HasActiveGhost
        {
            get
            {
                foreach (Ghost ghost in ghosts)
                {
                    if (ghost.active) return true;
                }

                return false;
            }
        }

        /// <summary>
        /// 현재 포즈로 잔상 하나를 월드에 남긴다.
        /// 별 궤적의 꼭짓점 프레임마다 Animation Event로 호출한다.
        /// </summary>
        /// <param name="yawOffset">
        /// 잔상을 캐릭터 발밑(루트) 기준으로 좌우로 돌리는 각도(도). Animation Event의 Float 칸에 넣는다.
        /// 0이면 그 순간 포즈 그대로, 180이면 정반대를 바라본다.
        /// 클립이 전방만 보고 위치만 이동하는 경우, 되돌아가는 구간의 잔상을 뒤로 돌려
        /// 실제로 그 방향으로 벤 것처럼 보이게 하는 용도다.
        /// </param>
        public void OnAfterImage(float yawOffset) => Spawn(yawOffset, Vector3.zero, false);

        /// <summary>
        /// 인스펙터 슬롯에 지정해 둔 자리에 잔상을 남긴다. 인자는 슬롯 키.
        /// 위치와 회전을 함께 줘야 할 때 쓴다(Animation Event는 인자를 하나만 넘길 수 있다).
        /// </summary>
        /// <param name="key">슬롯 키. 언더바 표기 차이는 무시된다(예: "Skill_4_1" == "Skill41").</param>
        public void OnAfterImageAt(string key)
        {
            // 다른 이벤트 슬롯과 같은 방침: 인자 오타는 경고만 남기고 플레이는 계속한다.
            if (string.IsNullOrEmpty(key) || !poseMap.TryGetValue(AnimationEventKey.Normalize(key), out PoseSlot slot))
            {
                Debug.LogWarning($"After-image key not found or empty ('{key}'). Check the Animation Event string.", this);
                return;
            }

            Spawn(slot.yawOffset, slot.offset, slot.fromAnchor);
        }

        /// <summary>
        /// 지정 위치 잔상이 쓸 기준 좌표계를 지금 이 순간의 캐릭터 위치·방향으로 고정한다.
        /// 스킬 시작 프레임에 찍어 두면, 이후 캐릭터가 어디로 움직이든 도형이 그 자리에 그려진다.
        /// 찍지 않으면 이번 묶음의 첫 잔상이 뜨는 시점이 자동으로 기준이 된다.
        /// </summary>
        public void OnAfterImageAnchor()
        {
            CaptureAnchor();
            anchorSetManually = true;
        }

        // 잔상 생성 본체. 두 진입점(현재 위치 / 지정 위치)이 같은 경로를 타게 모아 둔다.
        private void Spawn(float yawOffset, Vector3 offset, bool fromAnchor)
        {
            // 다른 이펙트 이벤트와 같은 게이트: 구르기·피격·사망으로 끊겼으면
            // 블렌드 아웃 중 뒤늦게 도착한 이벤트가 엉뚱한 자리에 잔상을 남기지 않게 막는다.
            if (player != null && player.IsActionInterrupted) return;

            if (ghostMaterial == null)
            {
                Debug.LogWarning("Ghost material is not assigned. After-image is skipped.", this);
                return;
            }

            if (!EnsureSources()) return;

            // 이전 묶음이 사라지는 중에 새 잔상이 시작되면 남은 것을 먼저 정리한다.
            // 섞이면 새 잔상이 이전 묶음의 페이드 진행도를 그대로 물려받아 뜨자마자 옅어진다.
            if (waitForFadeSignal && fadeStartTime >= 0f) ClearAll();

            // 떠 있는 잔상이 하나도 없으면 이번이 새 묶음의 시작이다.
            bool bundleStart = !HasActiveGhost;

            if (bundleStart)
            {
                holdStartTime = Time.time;

                // 묶음마다 기준을 새로 잡되, 스킬 시작 프레임에 직접 찍어 둔 기준이 있으면 그것을 존중한다.
                if (!anchorSetManually) CaptureAnchor();

                anchorSetManually = false;
            }

            // 앵커를 한 번도 못 잡은 상태에서 앵커 기준 잔상이 들어오면 지금 잡는다(방어).
            if (fromAnchor && !hasAnchor) CaptureAnchor();

            Ghost ghost = GetGhost();
            if (ghost == null) return;

            // 기준 좌표계: 앵커(묶음 시작 포즈) 또는 지금 이 순간의 캐릭터.
            Vector3 basePosition = fromAnchor ? anchorPosition : transform.position;
            Quaternion baseRotation = fromAnchor ? anchorRotation : transform.rotation;

            // 잔상이 놓일 자리와 바라볼 방향.
            // 오프셋은 기준의 정면(+Z)으로 해석하고, yaw는 배치와 독립적으로 방향만 정한다
            // (오프셋까지 yaw로 돌리면 각도를 조금 바꿀 때마다 위치가 같이 튀어 튜닝이 어렵다).
            Vector3 targetPosition = basePosition + baseRotation * offset;
            Quaternion targetRotation = Quaternion.AngleAxis(yawOffset, Vector3.up) * baseRotation;

            // 원본 포즈를 캐릭터 루트 기준 상대값으로 바꿀 때 쓴다.
            Quaternion inverseRoot = Quaternion.Inverse(transform.rotation);

            for (int i = 0; i < sources.Length; i++)
            {
                Renderer source = sources[i];
                Transform part = ghost.parts[i];

                if (source == null)
                {
                    part.gameObject.SetActive(false);
                    continue;
                }

                Mesh mesh;

                if (source is SkinnedMeshRenderer skinned)
                {
                    // useScale=true로 구우면 스케일이 메시에 반영되므로, 잔상 쪽 스케일은 1로 둔다.
                    skinned.BakeMesh(ghost.bakedMeshes[i], true);
                    mesh = ghost.bakedMeshes[i];
                    part.localScale = Vector3.one;
                }
                else
                {
                    MeshFilter filter = sourceFilters[i];

                    if (filter == null || filter.sharedMesh == null)
                    {
                        part.gameObject.SetActive(false);
                        continue;
                    }

                    mesh = filter.sharedMesh;
                    part.localScale = source.transform.lossyScale;
                }

                // 원본을 캐릭터 루트 기준 상대 포즈로 바꾼 뒤, 목표 루트 포즈에 그대로 얹는다.
                // 파트를 각각 옮기지 않고 한 덩어리로 옮기기 위한 계산이다 —
                // 따로 옮기면 축에서 먼 무기일수록 몸과 크게 어긋난다.
                Vector3 localPosition = inverseRoot * (source.transform.position - transform.position);
                Quaternion localRotation = inverseRoot * source.transform.rotation;

                part.gameObject.SetActive(true);
                part.SetPositionAndRotation(
                    targetPosition + targetRotation * localPosition,
                    targetRotation * localRotation);

                ghost.filters[i].sharedMesh = mesh;

                // 서브메시가 여러 개인 메시는 머티리얼도 그 수만큼 있어야 전부 그려진다
                // (하나만 넣으면 첫 서브메시만 보여 잔상 일부가 사라진 것처럼 나온다).
                int subMeshCount = Mathf.Max(1, mesh.subMeshCount);

                // 머티리얼이나 서브메시 수가 직전과 다를 때만 배열을 새로 만들어 넣는다.
                // ★ 개수만 비교하면 안 된다: 런타임에 만든 MeshRenderer는 슬롯 1개짜리 기본
                //   머티리얼을 갖고 태어나므로, 서브메시가 1개인 메시에서는 개수가 같아
                //   잔상 머티리얼이 영영 적용되지 않는다(URP에서 마젠타로 보이는 원인).
                if (ghost.appliedMaterials[i] != ghostMaterial || ghost.appliedSubMeshCounts[i] != subMeshCount)
                {
                    var materials = new Material[subMeshCount];
                    for (int m = 0; m < subMeshCount; m++) materials[m] = ghostMaterial;

                    ghost.renderers[i].sharedMaterials = materials;
                    ghost.appliedMaterials[i] = ghostMaterial;
                    ghost.appliedSubMeshCounts[i] = subMeshCount;
                }
            }

            // 월드에 고정한다. 붙어 있으면 플레이어가 이동할 때 잔상이 따라와
            // "지나간 자리에 남는다"는 연출 자체가 성립하지 않는다.
            ghost.root.SetParent(null, true);
            ghost.root.gameObject.SetActive(true);
            ghost.bornTime = Time.time;
            ghost.active = true;

            ApplyAlpha(ghost, alphaOverLife.Evaluate(0f));
        }

        /// <summary>
        /// 떠 있는 잔상 전부를 같은 순간에 사라지게 한다(<c>fadeOutDuration</c> 동안 함께 옅어짐).
        /// 별을 다 그린 프레임에 Animation Event로 호출한다.
        /// <c>waitForFadeSignal</c>이 켜져 있을 때만 의미가 있다 — 꺼져 있으면 잔상은 각자 수명대로 사라진다.
        /// </summary>
        public void OnAfterImageFadeOut()
        {
            if (!HasActiveGhost) return;

            BeginFadeOut();
        }

        /// <summary>
        /// 남아 있는 잔상을 모두 즉시 지운다(페이드 없음).
        /// 연출이 강제로 끊길 때 잔상만 공중에 남지 않게 한다.
        /// </summary>
        public void ClearAll()
        {
            foreach (Ghost ghost in ghosts)
            {
                if (ghost.active) Recycle(ghost);
            }

            // 다음 묶음이 신호를 기다리는 상태에서 시작하도록 되돌린다.
            fadeStartTime = -1f;

            // 기준 좌표계도 함께 놓아준다. 안 그러면 다음 각성기가 이전 시전 위치에 도형을 그린다.
            hasAnchor = false;
            anchorSetManually = false;
        }

        private void BeginFadeOut() => fadeStartTime = Time.time;

        private void CaptureAnchor()
        {
            anchorPosition = transform.position;
            anchorRotation = transform.rotation;
            hasAnchor = true;
        }

        private void OnDisable()
        {
            // 감춤 복구와 같은 방침: 비활성화되면 Update가 멈춰 잔상이 영영 안 사라지므로 여기서 정리한다.
            ClearAll();
        }

        // 대기 중인 잔상을 꺼내고, 없으면 상한까지 새로 만든다.
        // 상한에 도달했으면 가장 오래된 것을 빼앗아 재사용한다.
        private Ghost GetGhost()
        {
            foreach (Ghost ghost in ghosts)
            {
                if (!ghost.active && ghost.root != null) return ghost;
            }

            if (ghosts.Count < maxGhosts)
            {
                Ghost created = CreateGhost();
                ghosts.Add(created);
                return created;
            }

            Ghost oldest = null;

            foreach (Ghost ghost in ghosts)
            {
                if (ghost.root == null) continue;
                if (oldest == null || ghost.bornTime < oldest.bornTime) oldest = ghost;
            }

            return oldest;
        }

        private Ghost CreateGhost()
        {
            var ghost = new Ghost
            {
                parts = new Transform[sources.Length],
                filters = new MeshFilter[sources.Length],
                renderers = new MeshRenderer[sources.Length],
                bakedMeshes = new Mesh[sources.Length],
                appliedMaterials = new Material[sources.Length],
                appliedSubMeshCounts = new int[sources.Length],
            };

            var rootObject = new GameObject("AfterImage");
            ghost.root = rootObject.transform;
            ghost.root.SetParent(poolRoot, false);

            for (int i = 0; i < sources.Length; i++)
            {
                var partObject = new GameObject("Part");
                Transform part = partObject.transform;
                part.SetParent(ghost.root, false);

                ghost.parts[i] = part;
                ghost.filters[i] = partObject.AddComponent<MeshFilter>();
                ghost.renderers[i] = partObject.AddComponent<MeshRenderer>();

                // 잔상은 연출용 껍데기다. 그림자를 만들면 바닥에 검은 실루엣이 여러 개 겹쳐
                // 지저분해지고, 그림자를 받을 필요도 없다.
                ghost.renderers[i].shadowCastingMode = ShadowCastingMode.Off;
                ghost.renderers[i].receiveShadows = false;

                // 스킨드 원본만 구울 메시를 미리 만들어 계속 재사용한다.
                if (sources[i] is SkinnedMeshRenderer)
                    ghost.bakedMeshes[i] = new Mesh { name = "AfterImageBaked" };
            }

            rootObject.SetActive(false);
            return ghost;
        }

        private void Recycle(Ghost ghost)
        {
            ghost.active = false;

            if (ghost.root == null) return;

            ghost.root.gameObject.SetActive(false);

            // 분리해 뒀던 잔상을 대기 자리로 되돌린다. 이걸 안 하면 씬 루트에 잔상 오브젝트가
            // 계속 쌓이고, 씬 전환 때 통째로 파괴되어 풀이 비어 버린다.
            if (poolRoot != null) ghost.root.SetParent(poolRoot, false);
        }

        private void ApplyAlpha(Ghost ghost, float alphaScale)
        {
            Color color = startColor;
            color.a *= alphaScale;

            propertyBlock.SetColor(colorPropertyId, color);

            for (int i = 0; i < ghost.renderers.Length; i++)
            {
                if (ghost.renderers[i] != null)
                    ghost.renderers[i].SetPropertyBlock(propertyBlock);
            }
        }

        // 잔상으로 구울 원본을 정한다. 우선순위는
        // 인스펙터 지정 → PlayerBodyVisibility가 확정한 목록 → 자식에서 직접 수집.
        // 가운데 경로가 기본값인 이유: 감추는 대상과 잔상으로 남기는 대상이 같아야
        // "사라진 본체가 그 자리에 남는다"는 연출이 성립하기 때문이다.
        private Renderer[] ResolveSources()
        {
            if (sourceRenderers != null && sourceRenderers.Length > 0) return sourceRenderers;

            if (TryGetComponent(out PlayerBodyVisibility visibility))
            {
                IReadOnlyList<Renderer> body = visibility.BodyRenderers;

                if (body != null && body.Count > 0)
                {
                    var copy = new Renderer[body.Count];
                    for (int i = 0; i < body.Count; i++) copy[i] = body[i];
                    return copy;
                }
            }

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
        // 이벤트를 찍기 전에 색·수명·머티리얼을 눈으로 튜닝하기 위한 편의 기능.
        [ContextMenu("잔상 남기기 테스트")]
        private void DebugSpawn() => OnAfterImage(0f);

        [ContextMenu("잔상 남기기 테스트 (180도)")]
        private void DebugSpawnTurned() => OnAfterImage(180f);

        // 지정 위치 슬롯의 배치 미리보기. 캐릭터를 기준으로 어디에 어떤 방향으로 설지 보여준다.
        // 별처럼 여러 점을 잡을 때 숫자만 보고 맞추기는 사실상 불가능해서 넣는다.
        // (앵커 기준 슬롯도 편집 중에는 현재 캐릭터 포즈를 기준으로 그린다 — 실제 기준은 시전 시점에 잡힌다.)
        private void OnDrawGizmosSelected()
        {
            if (poseSlots == null) return;

            Gizmos.color = Color.cyan;

            foreach (PoseSlot slot in poseSlots)
            {
                if (slot == null) continue;

                Vector3 worldPosition = transform.position + transform.rotation * slot.offset;
                Gizmos.DrawWireSphere(worldPosition, 0.15f);

                // 그 자리에서 바라볼 방향까지 함께 그려 위치와 회전을 한 번에 확인한다.
                Vector3 forward = Quaternion.AngleAxis(slot.yawOffset, Vector3.up) * transform.rotation * Vector3.forward;
                Gizmos.DrawLine(worldPosition, worldPosition + forward * 0.6f);
            }
        }

        [ContextMenu("잔상 일괄 소멸 테스트")]
        private void DebugFadeOut() => OnAfterImageFadeOut();

        [ContextMenu("잔상 전부 지우기")]
        private void DebugClear() => ClearAll();
#endif
    }
}
