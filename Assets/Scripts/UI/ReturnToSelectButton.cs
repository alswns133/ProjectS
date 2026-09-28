using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Managers;
using ProjectS.Players;
using ProjectS.Scenes;

namespace ProjectS.UI
{
    /// <summary>
    /// 인게임 메뉴의 "캐릭터 선택으로" 버튼. 확인을 한 번 받고 <see cref="SessionReboot"/>에 넘긴다.
    /// <b>던전·레이드 안에서는 같은 자리가 "포기"(던전 중도 포기 → 마을 복귀) 버튼으로 바뀐다.</b>
    ///
    /// 되돌릴 수 없는(플레이 중이던 세션을 끝내는) 행동이라 <see cref="ConfirmDialog"/>를 반드시 끼운다.
    /// 실제 정리 순서(저장 → 네트워크 → 파괴 → 씬 로드)는 이 컴포넌트가 아니라 SessionReboot이 가진다 —
    /// 호출하는 곳이 늘어도 순서가 갈라지지 않게 하기 위해서다.
    /// </summary>
    /// <remarks>
    /// 던전 안에서 캐릭터 선택으로 바로 빠지면 파티 인스턴스·부활 기회·결과 스냅샷 정리를 건너뛰게 되므로,
    /// 던전에서는 캐릭터 선택 대신 마을 복귀 단일 진입점(<see cref="PartyInstanceExit"/>/<see cref="RaidFailFlow"/>)을 탄다.
    /// 모드 판정은 창이 열릴 때(OnEnable)마다 다시 한다 — 옵션 팝업은 열고 닫을 때 GameObject를 켜고 끄므로
    /// 마을↔던전 이동 후 처음 여는 순간 라벨·동작이 맞춰진다. (2026-09-28 TH)
    /// </remarks>
    [RequireComponent(typeof(Button))]
    public class ReturnToSelectButton : MonoBehaviour
    {
        [Tooltip("돌아갈 캐릭터 선택 씬 이름. Build Settings에 등록돼 있어야 한다.")]
        [SerializeField] private string characterSelectScene = "CharacterSelect";

        [Tooltip("확인 대화상자 문구. 저장된다는 사실을 함께 알려 준다.")]
        [SerializeField, TextArea] private string confirmMessage = "캐릭터 선택 화면으로 돌아갈까요?\n진행 상황은 저장됩니다.";

        [Tooltip("복귀 중 화면을 덮을 베일 프리팹(EntryVeil). 비우면 SessionReboot이 단색 베일을 만든다.")]
        [SerializeField] private GameObject veilPrefab;

        [Header("던전 안 (포기)")]
        [Tooltip("버튼 라벨. 비우면 자식에서 찾는다. 마을에서는 원래 문구, 던전에서는 아래 문구로 바뀐다.")]
        [SerializeField] private TMP_Text label;

        [Tooltip("던전·레이드 안에서 보일 버튼 문구.")]
        [SerializeField] private string giveUpLabel = "포기";

        [Tooltip("던전 포기 확인 문구.")]
        [SerializeField, TextArea] private string giveUpConfirmMessage = "던전을 포기하고 마을로 돌아갈까요?";

        private Button button;
        private string defaultLabel;
        private bool isGiveUpMode;

        /// <summary>
        /// 지금 던전·레이드 안인지. 호스트는 파티 레이드에서 <see cref="DungeonContext"/>가 0으로 남을 수 있어
        /// (<see cref="RaidFailFlow.IsRaidRun"/> 주석 참고) 파티 인스턴스 여부도 함께 본다.
        /// </summary>
        private static bool IsInDungeon =>
            DungeonContext.CurrentDungeonId != 0
            || RaidFailFlow.IsRaidRun
            || PartyInstanceExit.IsInNetworkInstance;

        private void Awake()
        {
            button = GetComponent<Button>();

            if (label == null) label = GetComponentInChildren<TMP_Text>(true);
            if (label != null) defaultLabel = label.text;

            // 캐릭터 선택 씬의 베일과 같은 프리팹을 쓰면, 인게임 → 선택 전환도 로그인 경로와 똑같이 보인다.
            // SessionReboot은 static이라 인스펙터가 없어, 프리팹을 들고 있는 이쪽에서 넘겨 준다.
            if (veilPrefab != null) SessionReboot.VeilPrefab = veilPrefab;
        }

        private void OnEnable()
        {
            RefreshMode();
            button.onClick.AddListener(OnClick);
        }

        private void OnDisable() => button.onClick.RemoveListener(OnClick);

        // 창이 열릴 때마다 현재 위치(마을/던전)에 맞춰 라벨과 동작을 고른다.
        private void RefreshMode()
        {
            isGiveUpMode = IsInDungeon;
            if (label != null) label.text = isGiveUpMode ? giveUpLabel : defaultLabel;
        }

        private void OnClick()
        {
            if (SessionReboot.IsRebooting) return;   // 이미 나가는 중이면 무시(중복 클릭)

            // 창이 열린 채 위치가 바뀌는 경우(로딩 중 등)를 대비해 누르는 순간 한 번 더 맞춘다.
            RefreshMode();

            System.Action action = isGiveUpMode ? GiveUp : Leave;
            string message = isGiveUpMode ? giveUpConfirmMessage : confirmMessage;

            // 대화상자가 없는 씬(단독 테스트)에서는 확인 없이 진행한다 — 버튼이 먹통으로 보이는 편이 더 나쁘다.
            if (ConfirmDialog.Instance == null)
            {
                action();
                return;
            }

            ConfirmDialog.Instance.Show(message, action);
        }

        private void Leave() => SessionReboot.ToCharacterSelect(characterSelectScene, ShowAborted);

        // 던전 중도 포기. 판 정리는 사망 팝업·결과창의 마을 복귀와 같은 순서를 따른다.
        private void GiveUp()
        {
            // 옵션창을 먼저 닫아 커서를 원복(OnHide)한다. 호스트는 씬을 다시 불러오지 않아(PartyInstanceExit)
            // 닫지 않으면 마을에 도착해서도 옵션창이 떠 있다.
            if (UIManager.Instance != null && GetComponentInParent<OptionsPopup>(true) != null)
                UIManager.Instance.ClosePopup<OptionsPopup>();

            // 판이 끝났으므로 결과 스냅샷을 지운다(안 지우면 다음 결과 화면에 옛 점수가 남는다).
            DungeonResultContext.Clear();

            // 레이드는 관전 카메라 해제·양쪽 부활까지 챙기는 전용 경로를 탄다.
            if (RaidFailFlow.IsRaidRun)
            {
                RaidFailFlow.ReturnToVillage();
                return;
            }

            // 남은 부활 기회 정리 — 안 지우면 마을에서 죽었을 때 이전 판의 기회로 부활한다.
            ReviveBudget.Clear();

            // 죽은 채로 보내면 VillageGather가 회복을 건너뛰어 HP 0으로 도착한다. 멀티면 아바타·지속 캐릭터 둘 다 본다.
            ReviveIfDead(LocalPlayer.Current);
            ReviveIfDead(PlayerManager.Instance != null ? PlayerManager.Instance.Player : null);

            // 멀티(파티 인스턴스)면 서버에 이탈도 알린다 — 씬만 바꾸면 서버에 아바타가 던전에 남는다.
            PartyInstanceExit.ReturnToVillage();
        }

        private static void ReviveIfDead(Player player)
        {
            if (player != null && player.Stats != null && player.Stats.IsDead) player.Revive();
        }

        // 저장 실패 등으로 나가지 못했을 때. 아무 일도 일어나지 않은 것처럼 보이면 사용자가 계속 누르므로
        // 반드시 사유를 띄운다(확인만 있는 알림으로 재사용).
        private void ShowAborted(string reason)
        {
            Debug.LogWarning($"[ReturnToSelectButton] 복귀 중단: {reason}");

            if (ConfirmDialog.Instance != null) ConfirmDialog.Instance.Show(reason, null);
        }
    }
}
