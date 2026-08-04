using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectS.Managers;
using ProjectS.Players;
using ProjectS.Scenes;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// ★ 테스트용 프로토타입입니다. 정식 사망 팝업은 <see cref="DeathPopup"/>(KimMinjun)입니다.
    /// 이 클래스는 <b>합의된 부활 규칙을 실제로 굴려 본 시안</b>이며, 정식 팝업에 규칙을 옮기고 나면 지워도 됩니다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 만든 배경 — 2026-08-04에 사망/부활을 양쪽에서 병행 구현했습니다(TH·KimMinjun). 정식 구현은
    /// 이미 <c>Bootstrap.unity</c>에 배치된 <see cref="DeathPopup"/> + <see cref="DeathPopupTrigger"/> 쪽이고,
    /// 부활 로직(<c>Player.Revive</c> · <c>PlayerStats.Revive</c> · <c>PlayerAnimation.ResetDeath</c>)도
    /// 그쪽 것을 씁니다. 이 시안은 <b>UI 규칙만</b> 다릅니다.
    /// </para>
    /// <para>
    /// 정식 팝업과 다른 점(= 옮길 가치가 있다고 본 것):
    /// <list type="number">
    /// <item><b>부활 기회를 던전 단위로</b> — <see cref="ReviveBudget"/>. 던전 입장 시 1회 부여,
    ///       부활하면 소모. 정식 쪽 <c>reviveConsumed</c> bool은 플레이 세션 단위라 던전을 다시 들어가도
    ///       기회가 돌아오지 않습니다.</item>
    /// <item><b>기회가 없으면 선택지를 아예 숨기고 자동 복귀</b> — 못 누르는 버튼을 띄워 두면
    ///       "왜 안 눌리지"로 읽힙니다.</item>
    /// <item><b>포기 후 카운트다운</b>(기획서 UI_DG_402) — 즉시 전환이 아니라 N초 뒤 마을로.</item>
    /// <item><b>ESC로 닫히지 않음</b> — <see cref="CanCloseByBack"/>. 닫히면 죽은 채 조작 불가 상태에
    ///       갇히고 창을 다시 부를 방법이 없습니다.</item>
    /// </list>
    /// </para>
    /// <para>
    /// 부활 직후 HP는 정식 규칙(전량 회복)을 따릅니다 — <c>Player.Revive()</c>를 그대로 호출합니다.
    /// 파티 사망 투표(UI_DG_403~405)는 멀티플레이가 없어 범위에서 뺐습니다.
    /// </para>
    /// <para>
    /// 배치: <see cref="DeathPresenter"/>가 사망을 듣고 이 팝업을 엽니다. 팝업은 닫히면 GameObject가 꺼져
    /// 스스로 이벤트를 받지 못하기 때문입니다. 둘 다 UIManager 직속에 두어야 하며, 팝업은 자기 Canvas가
    /// 필요합니다(Canvas는 HUD에 붙어 있어, HUD 밖으로 나가면 렌더링되지 않습니다).
    /// 생성은 Tools ▸ ProjectS ▸ 사망·부활 팝업 생성.
    /// </para>
    /// </remarks>
    public class DeathPopupPrototype : BasePopup
    {
        [Header("버튼")]
        [Tooltip("부활 버튼. 남은 기회가 없으면 통째로 숨겨진다.")]
        [SerializeField] private Button reviveButton;

        [Tooltip("포기 버튼(집 아이콘). 누르면 카운트다운 후 마을로 이동한다. 기회가 없으면 함께 숨겨진다.")]
        [SerializeField] private Button giveUpButton;

        [Header("표시")]
        [Tooltip("부활 잔여 기회 텍스트. 비워도 동작한다(연출 없이 기능만).")]
        [SerializeField] private TMP_Text reviveCountText;

        [Tooltip("사망 안내 문구. 선택이 가능할 때와 자동 복귀일 때 문장이 달라진다.")]
        [SerializeField] private TMP_Text messageText;

        [Tooltip("마을 이동까지 남은 초. 카운트다운 중에만 켜진다.")]
        [SerializeField] private TMP_Text returnTimerText;

        [Tooltip("선택 버튼 묶음. 남은 기회가 없으면 꺼서 자동 복귀임을 분명히 한다. 선택 사항.")]
        [SerializeField] private GameObject choiceGroup;

        [Header("문구")]
        [SerializeField, TextArea] private string chooseMessage = "부활하시겠습니까?";
        [SerializeField, TextArea] private string autoReturnMessage = "부활 기회를 모두 사용했습니다.\n마을로 돌아갑니다.";

        [Header("설정")]
        [Tooltip("마을로 옮겨지기까지의 시간(초). '포기를 누른 뒤' 그리고 '기회가 없어 자동 복귀할 때' 모두 이 값을 쓴다.\n" +
                 "포기하려고 기다려야 하는 시간이 아니다.")]
        [SerializeField, Min(0f)] private float returnDelay = 5f;

        // 마을 복귀 카운트다운 진행 여부. 도는 동안에는 버튼 입력을 받지 않는다
        // (카운트다운 중 부활을 누르면 이동과 부활이 동시에 성립해 마을에서 부활 모션이 도는 사고가 난다).
        private bool isReturning;
        private float returnRemain;

        /// <summary>
        /// ESC로 닫히지 않는다. 부활 / 포기 중 하나를 반드시 고르게 하기 위함이다.
        /// </summary>
        /// <remarks>
        /// 닫히면 플레이어는 죽은 채(<c>PlayerDeadState</c>는 스스로 빠져나오지 않는 막다른 상태) 아무 UI도 없이
        /// 조작 불가 상태에 갇힌다. 특히 카운트다운 중 닫히면 타이머만 사라지고 씬 전환도 일어나지 않는다
        /// (<see cref="Update"/>는 이 오브젝트가 꺼지면 함께 멈춘다).
        /// </remarks>
        public override bool CanCloseByBack => false;

        protected override void OnInit()
        {
            if (reviveButton != null) reviveButton.onClick.AddListener(OnReviveClicked);
            if (giveUpButton != null) giveUpButton.onClick.AddListener(OnGiveUpClicked);
        }

        protected override void OnShow()
        {
            isReturning = false;
            returnRemain = returnDelay;

            RefreshCountView();

            // 죽은 직후에는 커서가 잠겨 있어 버튼을 누를 수 없다(DungeonSelectPopup과 같은 처리).
            SetCursorFree(true);

            if (ReviveBudget.CanRevive)
            {
                ShowChoice();
                return;
            }

            HideChoice();
            BeginReturn();
        }

        protected override void OnHide() => SetCursorFree(false);

        private void Update()
        {
            if (!isReturning) return;

            // 사망 연출로 timeScale을 낮출 수 있어 unscaled로 센다. 스케일을 따라가면 퇴장이 한없이 늘어진다.
            returnRemain -= Time.unscaledDeltaTime;

            if (returnTimerText != null)
                returnTimerText.text = Mathf.CeilToInt(Mathf.Max(0f, returnRemain)).ToString();

            if (returnRemain > 0f) return;

            isReturning = false;   // 씬 전환이 한 프레임 늦어도 두 번 요청되지 않게 먼저 내린다.
            ReturnToVillage();
        }

        private void OnReviveClicked()
        {
            if (isReturning) return;

            // 기회 차감이 곧 부활 성립 조건이다. 실패하면(이미 0) 아무 일도 일어나지 않는다.
            if (!ReviveBudget.TryConsume()) return;

            Player player = ResolvePlayer();
            if (player == null)
            {
                Debug.LogWarning($"{name}: Player를 찾지 못해 부활을 건너뛴다.", this);
                return;
            }

            RefreshCountView();

            // 팝업을 먼저 닫아 커서를 원복(OnHide)한 뒤 부활시킨다.
            // 반대 순서면 살아난 첫 프레임에 커서가 아직 풀려 있어 시점이 튄다.
            RequestClose();
            player.Revive();
        }

        private void OnGiveUpClicked()
        {
            if (isReturning) return;

            HideChoice();
            BeginReturn();
        }

        private void BeginReturn()
        {
            isReturning = true;
            returnRemain = returnDelay;

            if (returnTimerText != null)
            {
                returnTimerText.gameObject.SetActive(true);
                returnTimerText.text = Mathf.CeilToInt(returnDelay).ToString();
            }
        }

        private void ReturnToVillage()
        {
            // 판이 끝났으므로 남은 기회를 정리한다. 안 지우면 마을에서 죽었을 때(던전 입장 없이)
            // 이전 판의 기회로 부활할 수 있게 된다.
            ReviveBudget.Clear();

            RequestClose();

            if (GameSceneManager.Instance != null)
                GameSceneManager.Instance.RequestSceneChange<VillageGather>();
        }

        private void ShowChoice()
        {
            if (choiceGroup != null) choiceGroup.SetActive(true);
            if (reviveButton != null) reviveButton.gameObject.SetActive(true);
            if (giveUpButton != null) giveUpButton.gameObject.SetActive(true);
            if (returnTimerText != null) returnTimerText.gameObject.SetActive(false);
            if (messageText != null) messageText.text = chooseMessage;
        }

        private void HideChoice()
        {
            if (choiceGroup != null) choiceGroup.SetActive(false);
            if (reviveButton != null) reviveButton.gameObject.SetActive(false);
            if (giveUpButton != null) giveUpButton.gameObject.SetActive(false);
            if (messageText != null) messageText.text = autoReturnMessage;
        }

        private void RefreshCountView()
        {
            if (reviveCountText != null)
                reviveCountText.text = $"{ReviveBudget.Remaining} / {ReviveBudget.MaxPerRun}";
        }

        private static Player ResolvePlayer()
        {
            // 매니저를 우선하고, 매니저 없이 단독 실행하는 테스트 씬을 위해 씬 검색으로 폴백한다(Enemy와 동일).
            return PlayerManager.Instance != null
                ? PlayerManager.Instance.Player
                : FindAnyObjectByType<Player>();
        }

        private void SetCursorFree(bool free)
        {
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }
    }
}
