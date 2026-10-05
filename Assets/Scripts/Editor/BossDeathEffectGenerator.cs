// 'Editor' 폴더 필수(UnityEditor 참조). 네임스페이스는 UnityEditor.Editor 가림 회피로 EditorTools.
using ProjectS.Enemies;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 보스·레이드보스가 공용으로 쓰는 사망 이펙트(머티리얼 + 파티클 프리팹 1개)를 만드는 에디터 툴.
    /// 메뉴: <b>Tools ▸ ProjectS ▸ Generate Boss Death Effect</b> (생성),
    ///       <b>Tools ▸ ProjectS ▸ Attach Boss Death Effect</b> (선택한 보스 프리팹에 물리기).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>프리팹은 하나다.</b> 처음엔 보스용·레이드용을 따로 뽑았지만, 둘은 "같은 연출의 크기 차이"일
    /// 뿐이라 자산을 둘로 가르면 한쪽만 손보게 된다. 지금은 <see cref="PrefabPath"/> 하나만 만들고,
    /// 덩치 차이는 <b>붙일 때 인스턴스의 localScale</b>로 준다(<see cref="RaidScale"/>).
    /// 루트부터 모든 파티클이 <c>ScalingMode.Hierarchy</c>라 스케일 하나로 전부 따라 커진다.
    /// </para>
    /// <para>
    /// <b>왜 프리팹을 손으로 안 만들고 코드로 찍어내나.</b> 파티클 열 개에 모듈이 수십 개씩이라
    /// 인스펙터로 만들면 "어떤 값이 왜 그 값인지"가 어디에도 남지 않는다. 여기 숫자만 고쳐 다시
    /// 실행하면 같은 구성이 값만 바뀌어 나온다. 프리팹은 덮어쓰기(GUID 유지)라 이미 물려 둔 참조도
    /// 끊기지 않는다 — 단 <b>프리팹 연결을 풀어(Unpack) 버린 인스턴스는 따라오지 않는다.</b>
    /// </para>
    /// <para>
    /// <b>구성(서브 파티클 10개).</b> 준비된 텍스처를 역할별로 나눠 쓴다.
    /// <list type="bullet">
    ///   <item><c>Flash</c> — Starburst. 사망 프레임의 다발 섬광.</item>
    ///   <item><c>CoreGlow</c> — GlowDot. 섬광 자리에 남아 꺼지는 코어.</item>
    ///   <item><c>Corona</c> — CoronaRing. 가시 돋친 폭발 링(카메라 정면).</item>
    ///   <item><c>ShockRing</c> — TubeRing + <b>노말맵</b>. 바닥을 훑는 입체 충격파.</item>
    ///   <item><c>Ripple</c> — RippleRings. 한 박자 늦게 번지는 바닥 파문.</item>
    ///   <item><c>Streaks</c> — Streak. 사방으로 뻗는 빛줄기(속도 방향으로 늘어남).</item>
    ///   <item><c>ShardsLarge</c> / <c>ShardsSmall</c> — 4x4 플립북 2종. 큰 파편과 잔파편.</item>
    ///   <item><c>Smoke</c> — SmokePuff. <b>유일하게 알파 블렌드</b>라 뒤를 가린다.</item>
    ///   <item><c>Embers</c> — GlowDot. 뒤늦게 떠오르는 불티.</item>
    /// </list>
    /// 루트는 아무것도 뿜지 않는 '시계' 역할만 한다(렌더러 끔, emission 끔). 루트 duration이 가장
    /// 길어야 자식이 중간에 잘리지 않는다 — <c>HitEffect</c>가 루트 기준으로 종료를 판정하는 것과 같은 이유다.
    /// </para>
    /// <para>
    /// <b>색은 머티리얼이, 밝기 변화는 파티클이 담당한다.</b> Shuriken의 파티클 색은 32비트로 압축되어
    /// 1을 넘는 값을 못 싣는다(HDR 불가). 그래서 블룸을 물리는 1 초과 세기는 머티리얼 <c>_BaseColor</c>에
    /// 두고, 파티클 쪽 Color over Lifetime은 0~1 범위의 색조·알파만 다룬다.
    /// </para>
    /// </remarks>
    public static class BossDeathEffectGenerator
    {
        private const string ShaderName = "ProjectS/Particle Additive (No Fog)";

        private const string TextureDir = "Assets/Textures/Effect";
        private const string MaterialDir = "Assets/Materials/Effect";

        /// <summary>보스·레이드보스가 함께 쓰는 단 하나의 사망 이펙트.</summary>
        private const string PrefabPath = "Assets/Effect/Boss/BossDeath.prefab";

        /// <summary>EnemyEffects 슬롯 키. 사망 클립의 Animation Event 인자와 같아야 한다.</summary>
        private const string DeathSlotKey = "Death";

        /// <summary>레이드보스에 붙일 때 주는 배율. 같은 프리팹을 덩치만 키워 쓴다.</summary>
        private const float RaidScale = 1.9f;

        /// <summary>
        /// 빌보드 파티클이 화면에서 차지할 수 있는 최대 비율. Unity 기본값 0.5는 이 이펙트에는 너무 낮아
        /// (Flash·ShockRing이 10~16 유닛짜리 쿼드다) 가까이서 잘려 "거리마다 크기 비율이 달라 보이는"
        /// 증상이 난다. 잘리기 시작하는 거리가 파티클마다 달라서 서로의 비율까지 틀어지는 게 진짜 문제다.
        /// 값은 실제로 보고 1로 정했다 — 화면을 꽉 채우되 넘지는 않는 선.
        /// </summary>
        private const float MaxParticleSize = 1f;

        // UnityEngine.Rendering.BlendMode 값. 셰이더의 _SrcBlend/_DstBlend에 그대로 들어간다.
        private const float BlendSrcAlpha = 5f;
        private const float BlendOne = 1f;
        private const float BlendOneMinusSrcAlpha = 10f;

        // 색 팔레트. 사이버펑크 톤(흰 코어 → 시안 → 마젠타).
        // 여기만 고치면 열 개 파티클의 색이 한꺼번에 바뀐다.
        private static readonly Color CoreWhite = new Color(1f, 0.97f, 0.90f);
        private static readonly Color EnergyCyan = new Color(0.35f, 0.92f, 1f);
        private static readonly Color AccentMagenta = new Color(1f, 0.40f, 0.85f);
        private static readonly Color SmokeGray = new Color(0.16f, 0.17f, 0.21f);

        // ── 텍스처 ────────────────────────────────────────────────────────────────

        private const string TexStarburst = TextureDir + "/Effect_Starburst.png";
        private const string TexGlowDot = TextureDir + "/Effect_GlowDot.png";
        private const string TexCoronaRing = TextureDir + "/Effect_CoronaRing.png";
        private const string TexTubeRing = TextureDir + "/Effect_TubeRing.png";
        private const string TexTubeRingNormal = TextureDir + "/Effect_TubeRing_Normal.png";
        private const string TexRippleRings = TextureDir + "/Effect_RippleRings.png";
        private const string TexStreak = TextureDir + "/Effect_Streak.png";
        private const string TexSmokePuff = TextureDir + "/Effect_SmokePuff.png";
        private const string TexShardLarge = TextureDir + "/Effect_ShardSheet.png";
        private const string TexShardSmall = TextureDir + "/Effect_ShardSheetSmall.png";

        /// <summary>
        /// 머티리얼과 사망 이펙트 프리팹 1개를 만들거나 덮어쓴다.
        /// 텍스처 임포트 설정(클램프·알파·노말맵 타입)도 함께 맞춘다.
        /// </summary>
        [MenuItem("Tools/ProjectS/Generate Boss Death Effect")]
        public static void Generate()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                EditorUtility.DisplayDialog("보스 사망 이펙트",
                    $"셰이더를 찾을 수 없습니다: {ShaderName}\n" +
                    "Assets/Shaders/ParticleAdditiveNoFog.shader 가 있는지, 컴파일 에러가 없는지 확인하세요.", "확인");
                return;
            }

            // 임포트 설정부터 맞춘다. 기본값(Repeat)으로 들어오면 링·플레어의 흐릿한 가장자리가
            // 반대편으로 말려 들어가 네모난 이음매가 보인다.
            ConfigureColorTexture(TexStarburst);
            ConfigureColorTexture(TexGlowDot);
            ConfigureColorTexture(TexCoronaRing);
            ConfigureColorTexture(TexTubeRing);
            ConfigureColorTexture(TexRippleRings);
            ConfigureColorTexture(TexStreak);
            ConfigureColorTexture(TexSmokePuff);
            ConfigureColorTexture(TexShardLarge);
            ConfigureColorTexture(TexShardSmall);
            ConfigureNormalTexture(TexTubeRingNormal);

            // 세기(1 초과)는 여기서 준다 — 파티클 색으로는 HDR을 실을 수 없기 때문.
            var mats = new Mats
            {
                starburst = BuildMaterial(shader, "Effect_Starburst", TexStarburst, CoreWhite * 3.2f),
                glowDot = BuildMaterial(shader, "Effect_GlowDot", TexGlowDot, CoreWhite * 2.6f),
                corona = BuildMaterial(shader, "Effect_CoronaRing", TexCoronaRing, EnergyCyan * 2.4f),
                ripple = BuildMaterial(shader, "Effect_RippleRings", TexRippleRings, EnergyCyan * 1.8f),
                streak = BuildMaterial(shader, "Effect_Streak", TexStreak, CoreWhite * 2.2f),
                shardLarge = BuildMaterial(shader, "Effect_ShardSheet", TexShardLarge, new Color(0.80f, 0.93f, 1f) * 1.7f),
                shardSmall = BuildMaterial(shader, "Effect_ShardSheetSmall", TexShardSmall, new Color(0.85f, 0.95f, 1f) * 1.5f),

                // 노말맵을 물린 유일한 머티리얼. 평평한 링이 '빛나는 튜브'로 읽히게 한다.
                tubeRing = BuildMaterial(shader, "Effect_TubeRing", TexTubeRing, Color.white * 2.2f,
                                         normalPath: TexTubeRingNormal, normalStrength: 0.85f),

                // 연기만 알파 블렌드다. 가산으로 두면 연기가 뒤를 '가리는' 게 아니라 밝히기만 해서
                // 폭발의 무게가 사라진다.
                smoke = BuildMaterial(shader, "Effect_SmokePuff", TexSmokePuff, SmokeGray,
                                      srcBlend: BlendSrcAlpha, dstBlend: BlendOneMinusSrcAlpha),
            };

            if (!mats.IsComplete) return;

            BuildPrefab(mats);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[BossDeathEffect] 생성 완료 → {PrefabPath}\n" +
                      "보스 프리팹에 물리려면 Project 창에서 보스 프리팹을 고르고 " +
                      "Tools ▸ ProjectS ▸ Attach Boss Death Effect 를 실행하세요.");
        }

        /// <summary>
        /// Project 창에서 고른 보스 프리팹에 사망 이펙트를 자식으로 넣고,
        /// <see cref="EnemyEffects"/>의 "<c>Death</c>" 슬롯으로 등록한다(월드 고정 켬).
        /// 레이드보스(<see cref="RaidBossLocomotion"/> 보유)면 인스턴스 스케일만 <see cref="RaidScale"/>로 키운다.
        /// <para>
        /// 월드 고정(anchorToWorld)을 켜는 이유: 보스는 사망 연출이 끝나면 DespawnDelay 뒤에 통째로
        /// 비활성화된다. 이펙트가 보스의 자식으로 남아 있으면 파편이 공중에 뜬 채로 함께 사라진다.
        /// 월드 고정이면 재생 순간 분리되어 보스가 사라져도 끝까지 재생된다. <c>stopOnInterrupt</c>는
        /// 끈 채로 둔다 — DeadState가 진입하며 호출하는 StopAll에 사망 이펙트 자신이 걷히면 안 되기 때문.
        /// </para>
        /// </summary>
        [MenuItem("Tools/ProjectS/Attach Boss Death Effect")]
        public static void AttachToSelection()
        {
            GameObject[] selection = Selection.GetFiltered<GameObject>(SelectionMode.Assets);
            if (selection.Length == 0)
            {
                EditorUtility.DisplayDialog("보스 사망 이펙트",
                    "Project 창에서 보스 프리팹을 하나 이상 선택한 뒤 실행하세요.\n" +
                    "(NineClaw, CoreBreaker, Hecaton_Phase2_DualBlades)", "확인");
                return;
            }

            var effectPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (effectPrefab == null)
            {
                EditorUtility.DisplayDialog("보스 사망 이펙트",
                    "이펙트 프리팹이 아직 없습니다. 먼저 Tools ▸ ProjectS ▸ Generate Boss Death Effect 를 실행하세요.", "확인");
                return;
            }

            int attached = 0;

            foreach (GameObject obj in selection)
            {
                string path = AssetDatabase.GetAssetPath(obj);

                // FBX 같은 모델 에셋도 GameObject로 잡혀 들어온다. 그쪽은 열 수도 저장할 수도 없다.
                if (!path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
                {
                    Debug.LogWarning($"[BossDeathEffect] 프리팹이 아니라 건너뜁니다: {path}", obj);
                    continue;
                }

                GameObject root = PrefabUtility.LoadPrefabContents(path);

                try
                {
                    var effects = root.GetComponent<EnemyEffects>();
                    if (effects == null)
                    {
                        Debug.LogWarning($"[BossDeathEffect] '{root.name}'에 EnemyEffects가 없어 건너뜁니다. " +
                                         "(EnemyEffects는 Animator와 같은 오브젝트, 즉 몬스터 루트에 있어야 합니다.)", obj);
                        continue;
                    }

                    if (HasSlot(effects, DeathSlotKey))
                    {
                        Debug.Log($"[BossDeathEffect] '{root.name}'에는 이미 '{DeathSlotKey}' 슬롯이 있어 건너뜁니다. " +
                                  "다시 붙이려면 그 슬롯을 먼저 지우세요.", obj);
                        continue;
                    }

                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(effectPrefab, root.transform);
                    instance.transform.localPosition = Vector3.zero;
                    instance.transform.localRotation = Quaternion.identity;

                    // 레이드보스 판정: 전용 이동 컴포넌트가 붙어 있는 쪽(현재 Hecaton)만 크게 쓴다.
                    // 이름 문자열로 가르지 않는 이유는 보스가 늘 때 규칙이 조용히 어긋나기 때문.
                    bool isRaid = root.GetComponent<RaidBossLocomotion>() != null;
                    instance.transform.localScale = Vector3.one * (isRaid ? RaidScale : 1f);

                    AddSlot(effects, DeathSlotKey, instance.GetComponent<ParticleSystem>());

                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    attached++;

                    Debug.Log($"[BossDeathEffect] '{root.name}' ← BossDeath 부착 (스케일 {instance.transform.localScale.x:0.##}) " +
                              $"+ '{DeathSlotKey}' 슬롯 등록.", obj);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }

                // 저장 결과를 되읽어 검증한다. 중첩 프리팹 인스턴스는 저장 과정에서 참조가 끊기는
                // 사례가 있어(실제로 NineClaw에서 particle이 null로 남은 적이 있다), 조용히 넘어가지 않게 한다.
                VerifyAttachment(path);
            }

            if (attached > 0)
            {
                EditorUtility.DisplayDialog("보스 사망 이펙트",
                    $"{attached}개 보스에 부착했습니다.\n\n" +
                    "마지막 한 단계가 남았습니다: 각 보스의 사망 애니메이션 클립에 Animation Event를 찍고\n" +
                    $"Function = OnEffect, String = {DeathSlotKey}\n" +
                    "으로 설정하세요. 이벤트가 없으면 이펙트는 재생되지 않습니다.", "확인");
            }
        }

        // 저장된 프리팹을 다시 열어 Death 슬롯이 실제 파티클을 가리키는지 본다.
        private static void VerifyAttachment(string path)
        {
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (saved == null) return;

            var effects = saved.GetComponent<EnemyEffects>();
            if (effects == null) return;

            var so = new SerializedObject(effects);
            SerializedProperty array = so.FindProperty("effects");
            if (array == null) return;

            for (int i = 0; i < array.arraySize; i++)
            {
                SerializedProperty slot = array.GetArrayElementAtIndex(i);
                if (slot.FindPropertyRelative("key").stringValue != DeathSlotKey) continue;

                if (slot.FindPropertyRelative("particle").objectReferenceValue == null)
                {
                    Debug.LogError($"[BossDeathEffect] '{saved.name}'의 '{DeathSlotKey}' 슬롯 파티클 참조가 비었습니다. " +
                                   "프리팹을 열어 BossDeath 자식의 ParticleSystem을 슬롯에 직접 끌어다 넣으세요.", saved);
                }

                return;
            }
        }

        // ── 프리팹 조립 ────────────────────────────────────────────────────────────

        private struct Mats
        {
            public Material starburst, glowDot, corona, tubeRing, ripple, streak, shardLarge, shardSmall, smoke;

            public bool IsComplete =>
                starburst != null && glowDot != null && corona != null && tubeRing != null && ripple != null &&
                streak != null && shardLarge != null && shardSmall != null && smoke != null;
        }

        private static void BuildPrefab(Mats mats)
        {
            var root = new GameObject("BossDeath", typeof(ParticleSystem));

            try
            {
                ConfigureRoot(root.GetComponent<ParticleSystem>());

                BuildFlash(root.transform, mats.starburst);
                BuildCoreGlow(root.transform, mats.glowDot);
                BuildCorona(root.transform, mats.corona);
                BuildShockRing(root.transform, mats.tubeRing);
                BuildRipple(root.transform, mats.ripple);
                BuildStreaks(root.transform, mats.streak);
                BuildShardsLarge(root.transform, mats.shardLarge);
                BuildShardsSmall(root.transform, mats.shardSmall);
                BuildSmoke(root.transform, mats.smoke);
                BuildEmbers(root.transform, mats.glowDot);

                EnsureFolder(PrefabPath);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // 루트는 아무것도 뿜지 않는 시계다. duration이 가장 길어야 자식이 중간에 잘리지 않는다.
        private static void ConfigureRoot(ParticleSystem ps)
        {
            var main = ps.main;
            main.duration = 4.2f;          // 가장 오래 사는 Smoke(최대 3.4초)보다 길게
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = 0.01f;
            main.startSize = 0f;
            main.maxParticles = 1;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.stopAction = ParticleSystemStopAction.None;
            // 사망 연출 도중 보스가 화면 밖으로 밀려도(카메라 워크·넉백) 시뮬레이션이 멈추면
            // 다시 보일 때 시간이 튄다.
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = false;

            ps.GetComponent<ParticleSystemRenderer>().enabled = false;
        }

        // 사망 프레임의 다발 섬광. 0.06초에 최대로 벌어졌다가 0.45초 안에 사라진다.
        private static void BuildFlash(Transform parent, Material material)
        {
            ParticleSystem ps = CreateChild(parent, "Flash", material, new Vector3(0f, 1.1f, 0f), Vector3.zero);

            var main = ps.main;
            main.duration = 0.6f;
            main.startLifetime = 0.45f;
            main.startSpeed = 0f;
            main.startSize = 13f;
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f); // 라디안
            main.startColor = CoreWhite;
            main.maxParticles = 2;

            SetBursts(ps, new ParticleSystem.Burst(0f, (short)1));

            SetSizeOverLifetime(ps, new AnimationCurve(
                new Keyframe(0f, 0.12f),
                new Keyframe(0.06f, 1f),
                new Keyframe(0.3f, 0.85f),
                new Keyframe(1f, 0.5f)));

            SetColorOverLifetime(ps,
                new[] { Key(CoreWhite, 0f), Key(EnergyCyan, 1f) },
                new[] { Alpha(1f, 0f), Alpha(1f, 0.12f), Alpha(0f, 1f) });
        }

        // 섬광이 꺼진 자리에 남아 천천히 사그라드는 코어. 섬광보다 오래 살아 '여운'을 만든다.
        private static void BuildCoreGlow(Transform parent, Material material)
        {
            ParticleSystem ps = CreateChild(parent, "CoreGlow", material, new Vector3(0f, 1.1f, 0f), Vector3.zero);

            var main = ps.main;
            main.duration = 1.4f;
            main.startLifetime = 1.1f;
            main.startSpeed = 0f;
            main.startSize = 8f;
            main.startColor = CoreWhite;
            main.maxParticles = 2;

            SetBursts(ps, new ParticleSystem.Burst(0f, (short)1));

            SetSizeOverLifetime(ps, new AnimationCurve(
                new Keyframe(0f, 0.25f),
                new Keyframe(0.1f, 1.1f),
                new Keyframe(1f, 0.2f)));

            SetColorOverLifetime(ps,
                new[] { Key(CoreWhite, 0f), Key(EnergyCyan, 0.55f), Key(AccentMagenta, 1f) },
                new[] { Alpha(1f, 0f), Alpha(1f, 0.2f), Alpha(0f, 1f) });
        }

        // 가시 돋친 폭발 링. 카메라를 향해 서 있어서, 보스가 어느 각도에서 죽어도 폭발이 읽힌다.
        private static void BuildCorona(Transform parent, Material material)
        {
            ParticleSystem ps = CreateChild(parent, "Corona", material, new Vector3(0f, 1.1f, 0f), Vector3.zero);

            var main = ps.main;
            main.duration = 0.9f;
            main.startLifetime = 0.65f;
            main.startSpeed = 0f;
            main.startSize = 12f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = Color.white;
            main.maxParticles = 4;

            SetBursts(ps, new ParticleSystem.Burst(0.02f, (short)1));

            // 빠르게 퍼졌다가 끝에서 느려지는 감속. 등속으로 퍼지면 충격파로 안 읽힌다.
            SetSizeOverLifetime(ps, new AnimationCurve(
                new Keyframe(0f, 0.05f),
                new Keyframe(0.4f, 0.82f),
                new Keyframe(1f, 1f)));

            // 가시가 아주 느리게 돌아 정지 화면처럼 보이지 않게 한다.
            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(0.9f);

            SetColorOverLifetime(ps,
                new[] { Key(Color.white, 0f), Key(EnergyCyan, 1f) },
                new[] { Alpha(0f, 0f), Alpha(1f, 0.07f), Alpha(0f, 1f) });
        }

        // 바닥을 훑는 입체 충격파. 노말맵을 물린 유일한 파티클이라 평평한 데칼이 아니라
        // '빛나는 튜브'가 퍼지는 것처럼 보인다.
        // 쿼드를 눕히려고 오브젝트를 X로 90도 돌리고 렌더러 정렬을 Local로 둔다
        // (Billboard로 두면 카메라를 따라 서 버려 바닥에 깔리지 않는다).
        private static void BuildShockRing(Transform parent, Material material)
        {
            // 바닥에 딱 붙이면 지면과 Z파이팅이 난다. 조금 띄운다.
            ParticleSystem ps = CreateChild(parent, "ShockRing", material,
                new Vector3(0f, 0.14f, 0f), new Vector3(90f, 0f, 0f));

            var main = ps.main;
            main.duration = 1.4f;
            main.startLifetime = 1.0f;
            main.startSpeed = 0f;
            main.startSize = 18f;
            main.startColor = Color.white;
            main.maxParticles = 6;

            // 시차를 둔 두 겹. 한 겹만 퍼지면 밋밋하다.
            SetBursts(ps,
                new ParticleSystem.Burst(0.02f, (short)1),
                new ParticleSystem.Burst(0.22f, (short)1));

            SetSizeOverLifetime(ps, new AnimationCurve(
                new Keyframe(0f, 0.05f),
                new Keyframe(0.35f, 0.75f),
                new Keyframe(1f, 1f)));

            SetColorOverLifetime(ps,
                new[] { Key(Color.white, 0f), Key(EnergyCyan, 0.6f), Key(AccentMagenta, 1f) },
                new[] { Alpha(0f, 0f), Alpha(1f, 0.06f), Alpha(0.8f, 0.5f), Alpha(0f, 1f) });

            ps.GetComponent<ParticleSystemRenderer>().alignment = ParticleSystemRenderSpace.Local;
        }

        // 한 박자 늦게 바닥으로 번지는 파문. 충격파가 지나간 뒤 여진처럼 깔린다.
        private static void BuildRipple(Transform parent, Material material)
        {
            ParticleSystem ps = CreateChild(parent, "Ripple", material,
                new Vector3(0f, 0.10f, 0f), new Vector3(90f, 0f, 0f));

            var main = ps.main;
            main.duration = 1.8f;
            main.startLifetime = 1.4f;
            main.startSpeed = 0f;
            main.startSize = 22f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = Color.white;
            main.maxParticles = 4;

            SetBursts(ps, new ParticleSystem.Burst(0.30f, (short)1));

            SetSizeOverLifetime(ps, new AnimationCurve(
                new Keyframe(0f, 0.08f),
                new Keyframe(0.45f, 0.78f),
                new Keyframe(1f, 1f)));

            // 파문은 충격파보다 흐려야 한다. 같은 세기면 둘이 싸워서 어느 쪽도 안 읽힌다.
            SetColorOverLifetime(ps,
                new[] { Key(EnergyCyan, 0f), Key(AccentMagenta, 1f) },
                new[] { Alpha(0f, 0f), Alpha(0.55f, 0.12f), Alpha(0f, 1f) });

            ps.GetComponent<ParticleSystemRenderer>().alignment = ParticleSystemRenderSpace.Local;
        }

        // 사방으로 뻗는 빛줄기. Stretch 렌더 모드라 날아가는 방향으로 저절로 늘어난다
        // (가로로 긴 텍스처라 늘어나는 축과 그림의 축이 맞는다).
        private static void BuildStreaks(Transform parent, Material material)
        {
            ParticleSystem ps = CreateChild(parent, "Streaks", material, new Vector3(0f, 1.1f, 0f), Vector3.zero);

            var main = ps.main;
            main.duration = 0.5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.30f, 0.55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(14f, 26f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
            main.startColor = CoreWhite;
            main.maxParticles = 48;

            SetBursts(ps, new ParticleSystem.Burst(0f, (short)16));

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.3f;
            shape.radiusThickness = 0f;   // 껍질에서 뿜어야 방향이 고르다

            SetSizeOverLifetime(ps, new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.5f, 0.7f),
                new Keyframe(1f, 0f)));

            SetColorOverLifetime(ps,
                new[] { Key(CoreWhite, 0f), Key(EnergyCyan, 1f) },
                new[] { Alpha(1f, 0f), Alpha(0.9f, 0.4f), Alpha(0f, 1f) });

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.12f;   // 빠를수록 길어진다
            renderer.lengthScale = 2.5f;
        }

        // 큰 파편. 4x4 플립북에서 한 조각을 무작위로 골라 평생 그 모양을 유지한다
        // (프레임을 흐르게 하면 조각이 공중에서 다른 조각으로 변신해 버린다).
        private static void BuildShardsLarge(Transform parent, Material material)
        {
            ParticleSystem ps = CreateChild(parent, "ShardsLarge", material, new Vector3(0f, 1.0f, 0f), Vector3.zero);

            ConfigureShards(ps,
                count: 22,
                speedMin: 6f, speedMax: 14f,
                sizeMin: 0.45f, sizeMax: 1.1f,
                lifeMin: 1.1f, lifeMax: 2.0f,
                gravity: 1.4f,
                emitRadius: 0.55f);
        }

        // 잔파편. 큰 파편보다 많고 작고 빠르게 흩어져 폭발의 '밀도'를 만든다.
        private static void BuildShardsSmall(Transform parent, Material material)
        {
            ParticleSystem ps = CreateChild(parent, "ShardsSmall", material, new Vector3(0f, 1.0f, 0f), Vector3.zero);

            ConfigureShards(ps,
                count: 46,
                speedMin: 9f, speedMax: 20f,
                sizeMin: 0.18f, sizeMax: 0.5f,
                lifeMin: 0.9f, lifeMax: 1.7f,
                gravity: 1.1f,
                emitRadius: 0.4f);
        }

        // 큰/잔 파편이 공유하는 설정. 두 벌을 따로 적으면 한쪽만 고치게 된다.
        private static void ConfigureShards(ParticleSystem ps, int count,
            float speedMin, float speedMax, float sizeMin, float sizeMax,
            float lifeMin, float lifeMax, float gravity, float emitRadius)
        {
            var main = ps.main;
            main.duration = 1.0f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = gravity;
            main.startColor = new ParticleSystem.MinMaxGradient(Color.white, EnergyCyan);
            main.maxParticles = count * 2;

            SetBursts(ps, new ParticleSystem.Burst(0f, (short)count));

            // 구 '껍질'에서 뿜어야 방향이 고르게 퍼진다(radiusThickness 0 = 껍질만).
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = emitRadius;
            shape.radiusThickness = 0f;

            // 공기 저항. 없으면 파편이 끝까지 같은 속도로 날아가 폭발이 아니라 분수처럼 보인다.
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.12f;
            limit.limit = new ParticleSystem.MinMaxCurve(3f);

            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-5f, 5f);

            SetSizeOverLifetime(ps, new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.75f, 0.95f),
                new Keyframe(1f, 0f)));

            SetColorOverLifetime(ps,
                new[] { Key(Color.white, 0f), Key(EnergyCyan, 0.45f), Key(AccentMagenta, 1f) },
                new[] { Alpha(1f, 0f), Alpha(1f, 0.6f), Alpha(0f, 1f) });

            var sheet = ps.textureSheetAnimation;
            sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = 4;
            sheet.numTilesY = 4;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.cycleCount = 1;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);   // 프레임 고정
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 16f); // 16칸 중 무작위
        }

        // 잔연. 이 이펙트에서 유일하게 알파 블렌드라 뒤를 가린다 → 폭발에 무게가 생긴다.
        private static void BuildSmoke(Transform parent, Material material)
        {
            ParticleSystem ps = CreateChild(parent, "Smoke", material, new Vector3(0f, 0.9f, 0f), Vector3.zero);

            var main = ps.main;
            main.duration = 1.2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.0f, 3.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(3.5f, 7f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.05f;   // 아주 천천히 떠오른다
            main.startColor = SmokeGray;
            main.maxParticles = 40;

            SetBursts(ps,
                new ParticleSystem.Burst(0.04f, (short)10),
                new ParticleSystem.Burst(0.35f, (short)6));

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 1.1f;
            shape.radiusThickness = 1f;

            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.6f;
            limit.limit = new ParticleSystem.MinMaxCurve(0.8f);

            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);

            // 연기는 퍼지면서 커진다.
            SetSizeOverLifetime(ps, new AnimationCurve(
                new Keyframe(0f, 0.45f),
                new Keyframe(1f, 1.3f)));

            // 알파가 낮다. 진한 연기는 섬광과 파편을 다 덮어 버린다.
            SetColorOverLifetime(ps,
                new[] { Key(SmokeGray, 0f), Key(SmokeGray * 0.6f, 1f) },
                new[] { Alpha(0f, 0f), Alpha(0.55f, 0.15f), Alpha(0.35f, 0.5f), Alpha(0f, 1f) });

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            // 알파 블렌드는 그리는 순서가 결과를 바꾼다 → 거리순 정렬이 필요하다.
            renderer.sortMode = ParticleSystemSortMode.Distance;
            // 양수 fudge = 더 멀리 있는 것으로 취급 → 가산 발광들보다 먼저 그려져 뒤에 깔린다.
            renderer.sortingFudge = 50f;
        }

        // 뒤늦게 떠오르며 반짝이는 불티. 폭발이 끝난 뒤에도 공기가 식는 시간을 준다.
        private static void BuildEmbers(Transform parent, Material material)
        {
            ParticleSystem ps = CreateChild(parent, "Embers", material, new Vector3(0f, 1.0f, 0f), Vector3.zero);

            var main = ps.main;
            main.duration = 1.8f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.3f, 2.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.5f);
            // 음수 중력 = 천천히 떠오른다. 에너지가 흩어지는 방향감을 준다.
            main.gravityModifier = -0.18f;
            main.startColor = new ParticleSystem.MinMaxGradient(EnergyCyan, AccentMagenta);
            main.maxParticles = 80;

            // 터지는 순간 한 번, 잔불처럼 뒤에 한 번.
            SetBursts(ps,
                new ParticleSystem.Burst(0.03f, (short)26),
                new ParticleSystem.Burst(0.50f, (short)14));

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.9f;
            shape.radiusThickness = 1f;

            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.5f;
            limit.limit = new ParticleSystem.MinMaxCurve(1.2f);

            // 봉우리를 두 번 둬서 '깜빡임'을 만든다. 단조롭게 줄어들면 그냥 사라지는 점으로 보인다.
            SetSizeOverLifetime(ps, new AnimationCurve(
                new Keyframe(0f, 0.2f),
                new Keyframe(0.15f, 1f),
                new Keyframe(0.45f, 0.4f),
                new Keyframe(0.65f, 0.9f),
                new Keyframe(1f, 0f)));

            SetColorOverLifetime(ps,
                new[] { Key(Color.white, 0f), Key(EnergyCyan, 0.35f), Key(AccentMagenta, 1f) },
                new[] { Alpha(0f, 0f), Alpha(1f, 0.1f), Alpha(0.8f, 0.6f), Alpha(0f, 1f) });
        }

        // ── 조립 도우미 ────────────────────────────────────────────────────────────

        // 모든 자식 파티클이 공유하는 기본값. 여기서 빠뜨리면(특히 playOnAwake) 씬을 열자마자
        // 보스 발밑에서 폭발이 재생된다.
        private static ParticleSystem CreateChild(Transform parent, string name, Material material,
            Vector3 localPosition, Vector3 localEuler)
        {
            var go = new GameObject(name, typeof(ParticleSystem));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localEulerAngles = localEuler;

            var ps = go.GetComponent<ParticleSystem>();

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.stopAction = ParticleSystemStopAction.None;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            // 기본 Cone 모양과 시간당 방출은 쓰지 않는다. 전부 Burst로만 뿜는다.
            var shape = ps.shape;
            shape.enabled = false;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sharedMaterial = material;
            // 가산 합성은 그리는 순서가 결과에 영향을 주지 않는다 → 정렬 비용을 아낀다
            // (알파 블렌드인 Smoke만 자기 쪽에서 Distance로 되돌린다).
            renderer.sortMode = ParticleSystemSortMode.None;
            renderer.maxParticleSize = MaxParticleSize;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            return ps;
        }

        private static void SetBursts(ParticleSystem ps, params ParticleSystem.Burst[] bursts)
        {
            var emission = ps.emission;
            emission.SetBursts(bursts);
        }

        private static void SetSizeOverLifetime(ParticleSystem ps, AnimationCurve curve)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        private static void SetColorOverLifetime(ParticleSystem ps, GradientColorKey[] colors, GradientAlphaKey[] alphas)
        {
            var gradient = new Gradient();
            gradient.SetKeys(colors, alphas);

            var color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        private static GradientColorKey Key(Color color, float time) => new GradientColorKey(color, time);

        private static GradientAlphaKey Alpha(float alpha, float time) => new GradientAlphaKey(alpha, time);

        // ── 에셋 도우미 ────────────────────────────────────────────────────────────

        // 이미 있으면 새로 만들지 않고 값만 덮어쓴다 — GUID가 바뀌면 이미 물려 둔 프리팹 참조가 끊긴다.
        private static Material BuildMaterial(Shader shader, string name, string texturePath, Color tint,
            string normalPath = null, float normalStrength = 0f,
            float srcBlend = BlendSrcAlpha, float dstBlend = BlendOne)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture == null)
            {
                Debug.LogError($"[BossDeathEffect] 텍스처를 찾을 수 없습니다: {texturePath}");
                return null;
            }

            string path = $"{MaterialDir}/{name}.mat";
            EnsureFolder(path);

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", tint);
            material.SetFloat("_SrcBlend", srcBlend);
            material.SetFloat("_DstBlend", dstBlend);

            if (!string.IsNullOrEmpty(normalPath))
            {
                var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                if (normal == null)
                {
                    Debug.LogError($"[BossDeathEffect] 노말맵을 찾을 수 없습니다: {normalPath}");
                    return null;
                }

                material.SetTexture("_NormalMap", normal);
            }

            material.SetFloat("_NormalStrength", normalStrength);
            EditorUtility.SetDirty(material);

            return material;
        }

        // 파티클 텍스처 공통 설정. Repeat로 두면 링·플레어의 흐릿한 가장자리가 반대편과 섞여
        // 네모난 이음매가 보인다.
        private static void ConfigureColorTexture(string path)
        {
            if (!TryGetImporter(path, out TextureImporter importer)) return;

            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }

        // 노말맵은 타입을 NormalMap으로 지정해야 Unity가 올바른 포맷으로 압축하고
        // 셰이더의 UnpackNormal이 기대하는 인코딩이 된다. sRGB로 두면 법선이 휘어 음영이 뭉개진다.
        private static void ConfigureNormalTexture(string path)
        {
            if (!TryGetImporter(path, out TextureImporter importer)) return;

            importer.textureType = TextureImporterType.NormalMap;
            importer.sRGBTexture = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }

        private static bool TryGetImporter(string path, out TextureImporter importer)
        {
            importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null) return true;

            Debug.LogWarning($"[BossDeathEffect] 텍스처 임포터를 찾을 수 없습니다: {path}");
            return false;
        }

        private static void EnsureFolder(string assetPath)
        {
            string folder = System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;

            string[] parts = folder.Split('/');
            string current = parts[0]; // "Assets"

            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);

                current = next;
            }
        }

        // ── EnemyEffects 슬롯 조작 ────────────────────────────────────────────────
        // effects 배열은 private이라 SerializedObject로 건드린다. 필드 이름이 바뀌면 여기도 바꿔야 한다.

        private static bool HasSlot(EnemyEffects effects, string key)
        {
            var so = new SerializedObject(effects);
            SerializedProperty array = so.FindProperty("effects");
            if (array == null) return false;

            for (int i = 0; i < array.arraySize; i++)
            {
                SerializedProperty slotKey = array.GetArrayElementAtIndex(i).FindPropertyRelative("key");
                if (slotKey != null && slotKey.stringValue == key) return true;
            }

            return false;
        }

        private static void AddSlot(EnemyEffects effects, string key, ParticleSystem particle)
        {
            var so = new SerializedObject(effects);
            SerializedProperty array = so.FindProperty("effects");

            array.arraySize++;
            SerializedProperty slot = array.GetArrayElementAtIndex(array.arraySize - 1);

            slot.FindPropertyRelative("key").stringValue = key;
            slot.FindPropertyRelative("particle").objectReferenceValue = particle;
            slot.FindPropertyRelative("anchorToWorld").boolValue = true;   // 보스가 사라져도 끝까지 재생
            slot.FindPropertyRelative("stopOnInterrupt").boolValue = false; // DeadState의 StopAll에 걷히지 않게

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
