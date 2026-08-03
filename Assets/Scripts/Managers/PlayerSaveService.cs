using System.Threading.Tasks;
using UnityEngine;
using ProjectS.Data;
using ProjectS.Players;

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

        /// <summary>
        /// 영구 상태가 바뀌었음을 표시한다(다음 오토세이브/경계에 저장됨). 어디서든 싸게 호출한다.
        /// "즉시 저장할 만큼 중요하지 않지만 유실되면 안 되는" 변화(부분 킬 진행, 드랍 골드 등)에 쓴다.
        /// </summary>
        public static void MarkDirty() => IsDirty = true;

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

            // 현재 상태를 세이브 데이터로 수집(동기).
            Player player = PlayerManager.Instance != null ? PlayerManager.Instance.Player : null;
            if (player != null) player.Stats.WriteTo(save);
            if (InventoryManager.Instance != null) InventoryManager.Instance.WriteTo(save);
            if (QuestManager.Instance != null) QuestManager.Instance.WriteTo(save);

            // 스냅샷을 떴으므로 dirty를 내린다. 업로드가 실패해도 값은 GameSession에 남고,
            // 이후 변경이 다시 dirty로 만들거나 다음 경계 flush가 재시도한다.
            IsDirty = false;

            if (FirebaseManager.Instance == null) return Task.FromResult(false);
            return FirebaseManager.Instance.SaveCharacter(save);
        }

        // 플레이 모드 리로드 후에도 남을 수 있는 static 상태를 초기화한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => IsDirty = false;
    }
}
