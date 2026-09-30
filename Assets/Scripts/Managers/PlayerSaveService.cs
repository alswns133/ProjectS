using System.Threading.Tasks;
using UnityEngine;
using ProjectS.Data;
using ProjectS.Players;
using ProjectS.Skills;
using System;

namespace ProjectS.Managers
{
    /// <summary>
    /// 세이브 정책의 단일 소유자(3층 모델). 저장 시점이 코드 곳곳에 흩어지지 않게 한 곳에 모은다.
    ///  ① 즉시 <see cref="SaveNow"/> — 커밋: 캐릭터 생성 · 퀘스트 수락/반납 · 목표 완주 · 구매/강화 · 레벨업.
    ///  ② 오토세이브 — dirty면 <see cref="AutoSaveTicker"/>가 <see cref="AutoSaveInterval"/>마다 flush
    ///     (부분 킬 카운트 · 드랍 골드 등 잦은 누적을 묶어 최대 N초 지연으로 저장).
    ///  ③ 경계 flush — 씬 전환(GameSceneManager) · 앱 종료/포커스아웃(AutoSaveTicker).
    /// 규칙: 영구 상태를 바꾼 쪽은 <see cref="MarkDirty"/>만 부르면, 늦어도 N초 안 또는 다음 경계에 저장된다.
    /// </summary>
    public static class PlayerSaveService
    {
        /// <summary>오토세이브 간격(초). 짧을수록 부분 킬 손실이 줄고 쓰기가 잦아진다.</summary>
        public static float AutoSaveInterval = 20f;

        /// <summary>저장이 필요한 변경이 쌓여 있는지. AutoSaveTicker가 이 값을 보고 flush한다.</summary>
        public static bool IsDirty { get; private set; }

        /// <summary>전투 스탯 변경이 아직 서버에 커밋되지 않았는지. 다음 저장의 스냅샷이 가져간다.</summary>
        public static bool StatsDirty { get; private set; }

        /// <summary>
        /// 전투 스탯 변경을 담은 저장이 Firebase에 올라간 직후 발행. 서버가 이제 새 세이브를 읽을 수 있다는 신호라,
        /// 구독자(NetworkCombatStats)가 여기서 재도출을 요청한다. 이벤트 시점에 바로 요청하면 서버가 옛 세이브를 읽는다.
        /// </summary>
        public static event Action OnStatsCommitted;
        /// <summary>
        /// 영구 상태가 바뀌었음을 표시한다(다음 오토세이브/경계에 저장됨). 어디서든 싸게 호출한다.
        /// "즉시 저장할 만큼 중요하지 않지만 유실되면 안 되는" 변화(부분 킬 진행, 드랍 골드 등)에 쓴다.
        /// </summary>
        public static void MarkDirty() => IsDirty = true;
        /// <summary>전투 스탯이 바뀌었음을 표시한다. 다음 저장이 성공하면 <see cref="OnStatsCommitted"/>로 이어진다.</summary>
        public static void MarkStatsDirty() => StatsDirty = true;

        /// <summary>
        /// 지금 즉시 저장한다(커밋·경계·오토세이브 flush 공통 경로). 값 수집(WriteTo)은 이 호출 시점에
        /// 동기로 끝나므로 반환 Task를 기다리지 않아도(fire-and-forget) 저장되는 스냅샷은 정확하다.
        /// 세션/매니저가 없으면(로그인 없이 직접 씬 테스트) 조용히 건너뛴다.
        /// </summary>
        /// <returns>업로드 성공 시 true. 저장 대상이 없으면 false.</returns>
        public static Task<bool> SaveNow()
        {
            CharacterSaveData save = GameSession.SelectedCharacter;
            if (save == null)
            {
                IsDirty = false;   // 저장 대상 없음 → dirty도 의미 없다
                return Task.FromResult(false);
            }

            // ★ 정의 테이블이 온전하지 않으면 저장하지 않는다. 인벤토리 복원이 정의 없는 장비를 전부 건너뛴 상태라,
            //   여기서 WriteTo가 돌면 빈 인벤토리가 Firebase 세이브를 덮어쓴다(2026-09-18 Addressables가 빠진 개발 빌드에서
            //   장비가 실제로 소실됨). 로딩 전(!IsReady)도 같은 이유로 막는다 — 아직 RestoreFrom 전이라 인벤토리가 비어 있다.
            //   dirty는 내리지 않는다: 로딩 전 차단이면 로딩 뒤 다음 오토세이브가 이어서 저장한다.
            JsonManager json = JsonManager.Instance;
            if (json == null || !json.IsReady || json.HasLoadFailures)
            {
                string reason = json == null ? "JsonManager 없음"
                    : !json.IsReady ? "테이블 로딩 전"
                    : $"테이블 로드 실패({string.Join(", ", json.FailedTables)})";
                Debug.LogError($"[PlayerSaveService] 저장 차단 — {reason}. 세이브 덮어쓰기(장비·아이템 소실)를 막기 위해 건너뜁니다.");
                return Task.FromResult(false);
            }

            // 현재 상태를 세이브 데이터로 수집(동기).
            Player player = PlayerManager.Instance != null ? PlayerManager.Instance.Player : null;
            if (player != null) player.Stats.WriteTo(save);
            if (InventoryManager.Instance != null) InventoryManager.Instance.WriteTo(save);
            if (QuestManager.Instance != null) QuestManager.Instance.WriteTo(save);
            SkillState.WriteTo(save);   // 스킬창 배운 레벨 + 단축키 로드아웃(static 상태라 인스턴스 없음)

            // 스냅샷을 떴으므로 dirty를 내린다. 업로드가 실패해도 값은 GameSession에 남고,
            // 이후 변경이 다시 dirty로 만들거나 다음 경계 flush가 재시도한다.
            IsDirty = false;

            // 스냅샷과 같은 순간에 옮겨 담는다: 업로드 도중 생긴 스탯 변경은 이번이 아니라 다음 저장 몫이다.
            bool includesStats = StatsDirty;
            StatsDirty = false;

            return UploadAsync(save, includesStats);
        }

        private static async Task<bool> UploadAsync(CharacterSaveData save, bool includesStats)
        {
            bool ok = FirebaseManager.Instance != null && await FirebaseManager.Instance.SaveCharacter(save);

            if (includesStats)
            {
                if (ok) OnStatsCommitted?.Invoke();
                else StatsDirty = true; // 실패 → 다음 저장이 다시 가져간다.
            }
            return ok;
        }

        // 플레이 모드 리로드 후에도 남을 수 있는 static 상태를 초기화한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            IsDirty = false;
            StatsDirty = false;
            OnStatsCommitted = null;
        }
    }
}
