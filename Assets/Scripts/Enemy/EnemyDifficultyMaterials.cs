using System;
using UnityEngine;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 던전 난이도에 맞춰 몬스터 메테리얼을 하드/매니악용으로 갈아끼운다. 노말은 렌더러에 원래 꽂혀 있는 메테리얼이다.
    /// </summary>
    /// <remarks>
    /// 프리팹 하나가 세 난이도를 모두 굴리는 구조(<see cref="ProjectS.Scenes.DungeonContext.ResolveMonsterId(int)"/>)라,
    /// 외형도 스탯과 같은 시점·같은 기준(<see cref="EnemyStats.Difficulty"/>)으로 갈라야 "하드 스탯인데 노말 색" 같은 어긋남이 없다.
    /// 그래서 <see cref="EnemyStats.StatsReady"/>(난이도 ID 확정 시점)를 받아 한 번 적용한다.
    ///
    /// <b>왜 <c>sharedMaterials</c>로 교체하는가</b>: <c>renderer.material(s)</c>는 접근하는 순간 몬스터마다 메테리얼 사본을 만들어
    /// 메모리가 새고 SRP Batcher 묶음이 깨진다. 에셋 메테리얼을 그대로 꽂으면 같은 난이도 몬스터끼리 한 메테리얼을 공유한다.
    ///
    /// <b>메모리</b>: 여기 꽂은 하드/매니악 메테리얼과 그 텍스처는 현재 난이도와 무관하게 프리팹과 함께 전부 로드된다(직접 참조).
    /// 던전2의 팔레트 텍스처(32×32)처럼 작으면 문제없고, 큰 텍스처가 생기면 이 두 칸만 AssetReference로 바꾸는 것을 검토한다.
    /// </remarks>
    [RequireComponent(typeof(EnemyStats))]
    public class EnemyDifficultyMaterials : MonoBehaviour
    {
        // 난이도 값(docs/ID_NUMBERING.md — 던전 ID 뒷자리). 노말(1)과 그 밖의 값(레이드 9 등)은 원래 메테리얼을 유지한다.
        private const int DifficultyHard = 2;
        private const int DifficultyManiac = 3;

        /// <summary>
        /// 렌더러의 메테리얼 칸 하나와 그 칸의 난이도별 대체 메테리얼.
        /// 서브메시가 여러 개인 렌더러는 칸마다 슬롯을 하나씩 만든다.
        /// </summary>
        [Serializable]
        private class Slot
        {
            [Tooltip("메테리얼을 바꿀 렌더러(보통 SkinnedMeshRenderer).")]
            public Renderer targetRenderer;

            [Tooltip("렌더러의 Materials 배열 중 바꿀 칸 번호. 메테리얼이 하나뿐이면 0.")]
            [Min(0)] public int materialIndex;

            [Tooltip("하드 난이도용 메테리얼. 비워 두면 노말 메테리얼을 유지한다.")]
            public Material hard;

            [Tooltip("매니악 난이도용 메테리얼. 비워 두면 노말 메테리얼을 유지한다.")]
            public Material maniac;

            // Awake에서 렌더러에 꽂혀 있던 메테리얼을 기억해 둔 것. 노말 칸을 따로 두지 않는 이유는 클래스 주석 참고.
            [NonSerialized] public Material normal;
        }

        [Header("난이도별 메테리얼")]
        [Tooltip("노말은 렌더러에 꽂힌 메테리얼을 그대로 쓴다. 하드/매니악만 채우면 된다.")]
        [SerializeField] private Slot[] slots = Array.Empty<Slot>();

        private EnemyStats stats;

        private void Awake()
        {
            stats = GetComponent<EnemyStats>();
            CaptureNormalMaterials();
        }

        private void OnEnable()
        {
            // 이미 확정됐으면(재활성화 등) 바로 적용, 아니면 확정되는 순간을 기다린다.
            if (stats.IsStatsReady) ApplyDifficulty();
            else stats.StatsReady += OnStatsReady;
        }

        private void OnDisable()
        {
            stats.StatsReady -= OnStatsReady;
        }

        private void OnStatsReady()
        {
            // StatsReady는 1회성이라 바로 해제한다. 해제를 빠뜨려도 OnDisable이 지우지만, 구독을 오래 쥐고 있을 이유가 없다.
            stats.StatsReady -= OnStatsReady;
            ApplyDifficulty();
        }

        // 노말 메테리얼을 인스펙터 칸으로 따로 받지 않고 렌더러에서 읽어 온다.
        // 렌더러에 이미 꽂혀 있는 것을 또 적게 하면 두 곳이 어긋날 수 있고(렌더러만 바꾸고 칸은 옛 것), 원본은 한 곳이어야 한다.
        private void CaptureNormalMaterials()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                Slot slot = slots[i];
                if (slot == null || slot.targetRenderer == null)
                {
                    Debug.LogWarning($"[EnemyDifficultyMaterials] '{name}' 슬롯 {i}에 렌더러가 비어 있습니다. 이 슬롯은 건너뜁니다.", this);
                    continue;
                }

                Material[] current = slot.targetRenderer.sharedMaterials;
                if (slot.materialIndex >= current.Length)
                {
                    Debug.LogWarning($"[EnemyDifficultyMaterials] '{name}' 슬롯 {i}: materialIndex {slot.materialIndex}가 " +
                                     $"'{slot.targetRenderer.name}'의 메테리얼 수({current.Length})를 넘습니다. 이 슬롯은 건너뜁니다.", this);
                    continue;
                }

                slot.normal = current[slot.materialIndex];
            }
        }

        // 현재 난이도에 맞는 메테리얼을 각 칸에 꽂는다. 이미 같은 것이 꽂혀 있으면 건드리지 않는다.
        private void ApplyDifficulty()
        {
            int difficulty = stats.Difficulty;

            foreach (Slot slot in slots)
            {
                // Capture 단계에서 걸러진 슬롯(렌더러 없음/칸 번호 초과)은 normal이 비어 있다.
                if (slot == null || slot.targetRenderer == null || slot.normal == null) continue;

                Material chosen = PickMaterial(slot, difficulty);

                // sharedMaterials는 배열 사본을 돌려주므로, 칸 하나를 바꾸고 배열째 다시 넣어야 반영된다.
                Material[] materials = slot.targetRenderer.sharedMaterials;
                if (materials[slot.materialIndex] == chosen) continue;

                materials[slot.materialIndex] = chosen;
                slot.targetRenderer.sharedMaterials = materials;
            }
        }

        // 비어 있는 칸은 노말로 떨어진다. 하드만 먼저 만들어 두고 매니악은 나중에 채워도 깨지지 않게 하기 위함이다.
        // (UnityEngine.Object에 ??를 쓰면 파괴된 참조를 null로 못 걸러서 명시적으로 비교한다.)
        private static Material PickMaterial(Slot slot, int difficulty)
        {
            if (difficulty == DifficultyHard && slot.hard != null) return slot.hard;
            if (difficulty == DifficultyManiac && slot.maniac != null) return slot.maniac;
            return slot.normal;
        }
    }
}
