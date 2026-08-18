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
    /// 사망 시 뜨는 팝업. 부활 / 마을 복귀 중 하나를 반드시 고르게 하는 모달이며, 두 선택 모두
    /// <b>카운트다운을 거친 뒤</b> 실행된다.
    /// 표시는 <see cref="DeathPopupTrigger"/>가 <see cref="ProjectS.Events.PlayerEvents.OnPlayerDied"/>를 받아 띄운다
    /// (팝업 자신은 비활성이라 이벤트를 못 받으므로).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 규칙 (2026-08-05 TH — <see cref="DeathPopupPrototype"/>에서 검증한 시안 규칙을 이 정식 팝업으로 이관):
    /// <list type="number">
    /// <item><b>부활 기회는 던전 한 판 단위</b> — <see cref="ReviveBudget"/>. 던전 입장 시 1회 부여되고
    ///       (<c>DungeonGather.Enter</c>), 부활할 때 소모된다. 플레이 세션 단위 bool이 아니라서
    ///       던전을 다시 들어가면 기회가 돌아온다.</item>
    /// <item><b>기회가 없으면 선택지를 아예 숨기고 자동으로 마을 복귀</b> — 못 누르는 버튼을 띄워 두면
    ///       "왜 안 눌리지"로 읽힌다.</item>
    /// <item><b>부활도 즉시가 아니라 <see cref="reviveDelay"/>초 뒤에 성립한다</b>. 죽은 프레임에 곧바로
    ///       되살아나면 사망 연출·피격 판정·남아 있는 적 공격과 겹쳐 상태가 꼬이고, 플레이어도 무슨 일이
    ///       일어났는지 못 읽는다. 남은 시간은 <see cref="countdownText"/>로 보여 준다.</item>
    /// <item><b>마을 복귀도 카운트다운 뒤 전환</b>(기획서 UI_DG_402).</item>
    /// <item><b>ESC로 닫히지 않는다</b> — <see cref="CanCloseByBack"/>.</item>
    /// </list>
    /// </para>
    /// <para>
    /// 부활 처리 자체는 <c>Player.Revive()</c>(HP 전량 회복 · 사망 모션 정리 · 자유 상태 복귀)에 맡긴다.
    /// <b>마을 복귀 경로에서도 <c>Revive()</c>를 먼저 부르는 것이 중요하다</b> —
    /// <c>VillageGather.Enter</c>의 <c>RefillOnSceneEnter</c>는 죽어 있으면 회복을 건너뛰므로,
    /// 살려 놓지 않고 씬만 바꾸면 마을에 HP 0인 시체로 도착한다.
    /// </para>
    /// <para>
    /// 표시용 필드(<see cref="messageText"/> 등)는 전부 선택 사항이다. 비어 있으면 연출 없이 기능만 돈다
    /// — 아트가 붙기 전 씬에서도 흐름을 검증할 수 있게 하기 위함이다.
    /// 다른 창과 공존하지 않지만, 스택형 패널 흐름(HUD Pause/Resume)에 끼어들지 않도록 리스트형
    /// <see cref="BasePopup"/>으로 둔다(DungeonEntryPopup과 같은 방침).
    /// </para>
    /// </remarks>
    public class DeathPopup : BasePopup
    {
        [Header("버튼")]
        [Tooltip("부활 버튼. 남은 기회가 없으면 통째로 숨겨진다.")]
        [SerializeField] private Button reviveButton;

        [Tooltip("마을 복귀 버튼. 누르면 카운트다운 후 마을로 이동한다. 기회가 없으면 함께 숨겨진다.")]
        [SerializeField] private Button returnToVillageButton;

        [Header("표시")]
        [Tooltip("사망 안내 문구. 선택 중 / 부활 대기 중 / 복귀 대기 중에 따라 문장이 바뀐다. 비워도 동작한다.")]
        [SerializeField] private TMP_Text messageText;

        [Tooltip("부활 잔여 기회 텍스트. 비워도 동작한다(연출 없이 기능만).")]
        [SerializeField] private TMP_Text countText;

        [Tooltip("부활/마을 복귀까지 남은 초. 카운트다운 중에만 켜지며 두 경우에 공용으로 쓴다.")]
        [SerializeField] private TMP_Text countdownText;

        [Tooltip("선택 버튼 묶음. 선택이 끝나거나 기회가 없으면 꺼서 '이제 기다리는 중'임을 분명히 한다. 선택 사항.")]
        [SerializeField] private GameObject choiceGroup;

        [Header("연출")]
        [Tooltip("타이틀('사망') 글리치 연출. 비워두면 자식에서 자동으로 찾는다.\n" +
                 "팝업이 열릴 때마다 처음부터 다시 재생한다.")]
        [SerializeField] private GlitchTextFx titleFx;

        [Header("문구")]
        [SerializeField, TextArea] private string chooseMessage = "부활하시겠습니까?";
        [SerializeField, TextArea] private string revivingMessage = "잠시 후 부활합니다.";
        [SerializeField, TextArea] private string returningMessage = "마을로 돌아갑니다.";
        [SerializeField, TextArea] private string autoReturnMessage = "부활 기회를 모두 사용했습니다.\n마을로 돌아갑니다.";

        [Header("설정")]
        [Tooltip("부활 버튼을 누른 뒤 실제로 살아나기까지의 시간(초).\n" +
                 "죽은 자리에서 곧바로 되살아나지 않게 두는 간격이다. 0으로 두면 즉시 부활한다.")]
        [SerializeField, Min(0f)] private float reviveDelay = 3f;

        [Tooltip("마을로 옮겨지기까지의 시간(초). '마을 복귀를 누른 뒤'와 '기회가 없어 자동 복귀할 때' 모두 이 값을 쓴다.")]
        [SerializeField, Min(0f)] private float returnDelay = 5f;

        // 카운트다운이 끝나면 무엇을 할지. 도는 동안에는 버튼 입력을 받지 않는다
        // (부활 대기 중 마을 복귀를 누르면 이동과 부활이 동시에 성립해 마을에서 부활 모션이 도는 사고가 난다).
        private enum Pending
        {
            None,
            Revive,
            Return
        }

        private Pending pending;
        private float remain;

        /// <summary>
        /// ESC로 닫히지 않는다. 부활 / 마을 복귀 중 하나를 반드시 고르게 하기 위함이다.
        /// </summary>
        /// <remarks>
        /// 닫히면 플레이어는 죽은 채(<c>PlayerDeadState</c>는 스스로 빠져나오지 않는 막다른 상태) 아무 UI도 없이
        /// 조작 불가 상태에 갇힌다 — 창을 다시 부를 방법이 없어 재시작 말고는 빠져나올 수 없다.
        /// 특히 카운트다운 중 닫히면 타이머만 사라지고 부활도 씬 전환도 일어나지 않는다
        /// (<see cref="Update"/>는 이 오브젝트가 꺼지면 함께 멈춘다).
        /// </remarks>
        public override bool CanCloseByBack => false;

        protected override void OnInit()
        {
            // 버튼 배선은 최초 1회만(BasePopup이 OnInit을 1회만 호출 → 중복 구독이 쌓이지 않는다).
            if (reviveButton != null) reviveButton.onClick.AddListener(OnReviveClicked);
            if (returnToVillageButton != null) returnToVillageButton.onClick.AddListener(OnReturnToVillageClicked);

            // 인스펙터에 안 물려도 동작하게 자식에서 찾아 둔다. 타이틀은 꺼져 있을 수 있어 비활성 포함.
            if (titleFx == null) titleFx = GetComponentInChildren<GlitchTextFx>(true);
        }

        protected override void OnShow()
        {
            pending = Pending.None;
            remain = 0f;

            // 연출 시작은 '팝업이 열리는 시점'이어야 한다. GlitchTextFx의 playOnEnable(=OnEnable)에만
            // 맡기면 오브젝트가 켜지는 시점과 실제로 보이는 시점이 어긋나, 창이 뜰 때는 연출이 이미
            // 끝나 있는 상태가 된다. Play는 재생 중이어도 처음부터 다시 시작하므로 중복 호출이 안전하다.
            if (titleFx != null) titleFx.Play();

            RefreshCountView();

            // 마우스로 버튼을 누르려면 커서를 풀어야 한다(플레이 중엔 커서가 잠겨 숨겨져 있음).
            SetCursorFree(true);

            if (ReviveBudget.CanRevive)
            {
                ShowChoice();
                return;
            }

            HideChoice(autoReturnMessage);
            BeginCountdown(Pending.Return, returnDelay);
        }

        // 닫힐 때(부활/마을 복귀 모두) 커서를 다시 잠가 플레이 조작으로 복귀시킨다.
        // 진행 중이던 카운트다운도 함께 내린다 — 남겨 두면 다음에 열릴 때 이전 타이머가 이어서 돈다.
        protected override void OnHide()
        {
            pending = Pending.None;
            SetCursorFree(false);
        }

        private void Update()
        {
            if (pending == Pending.None) return;

            // 사망 연출로 timeScale을 낮출 수 있어 unscaled로 센다. 스케일을 따라가면 대기가 한없이 늘어진다.
            remain -= Time.unscaledDeltaTime;

            if (countdownText != null)
                countdownText.text = Mathf.CeilToInt(Mathf.Max(0f, remain)).ToString();

            if (remain > 0f) return;

            // 실행이 한 프레임 늦어도 두 번 돌지 않게 먼저 내린다.
            Pending done = pending;
            pending = Pending.None;

            if (done == Pending.Revive) ReviveNow();
            else ReturnToVillage();
        }

        // 부활 선택. 여기서는 기회만 확정하고, 실제 부활은 카운트다운이 끝난 뒤 ReviveNow가 처리한다.
        private void OnReviveClicked()
        {
            if (pending != Pending.None) return;

            // 살릴 대상이 없으면 기회부터 날리지 않도록 소모 전에 확인한다.
            if (ResolvePlayer() == null)
            {
                Debug.LogWarning($"{name}: Player를 찾지 못해 부활을 건너뛴다.", this);
                return;
            }

            // 기회 차감이 곧 부활 성립 조건이다. 실패하면(이미 0) 아무 일도 일어나지 않는다.
            if (!ReviveBudget.TryConsume()) return;

            RefreshCountView();
            HideChoice(revivingMessage);
            BeginCountdown(Pending.Revive, reviveDelay);
        }

        private void OnReturnToVillageClicked()
        {
            if (pending != Pending.None) return;

            HideChoice(returningMessage);
            BeginCountdown(Pending.Return, returnDelay);
        }

        private void BeginCountdown(Pending next, float delay)
        {
            pending = next;
            remain = delay;

            if (countdownText != null)
            {
                countdownText.gameObject.SetActive(true);
                countdownText.text = Mathf.CeilToInt(delay).ToString();
            }
        }

        // 죽은 자리에서 부활. 팝업을 먼저 닫아 커서를 원복(OnHide)한 뒤 살린다.
        // 반대 순서면 살아난 첫 프레임에 커서가 아직 풀려 있어 시점이 튄다.
        private void ReviveNow()
        {
            Player player = ResolvePlayer();

            RequestClose();

            if (player != null) player.Revive();
            else Debug.LogWarning($"{name}: Player를 찾지 못해 부활하지 못했다.", this);
        }

        private void ReturnToVillage()
        {
            // 판이 끝났으므로 남은 기회를 정리한다. 안 지우면 마을에서 죽었을 때(던전 입장 없이)
            // 이전 판의 기회로 부활할 수 있게 된다.
            ReviveBudget.Clear();

            // 살려 놓고 보내야 한다. VillageGather의 RefillOnSceneEnter는 죽어 있으면 회복을 건너뛰므로,
            // 이걸 빠뜨리면 마을에 HP 0인 채로 도착해 조작이 막힌다.
            ResolvePlayer()?.Revive();

            RequestClose();

            if (GameSceneManager.Instance != null)
                GameSceneManager.Instance.RequestSceneChange<VillageGather>();
        }

        private void ShowChoice()
        {
            if (choiceGroup != null) choiceGroup.SetActive(true);
            if (reviveButton != null) reviveButton.gameObject.SetActive(true);
            if (returnToVillageButton != null) returnToVillageButton.gameObject.SetActive(true);
            if (countdownText != null) countdownText.gameObject.SetActive(false);
            if (messageText != null) messageText.text = chooseMessage;
        }

        private void HideChoice(string message)
        {
            if (choiceGroup != null) choiceGroup.SetActive(false);
            if (reviveButton != null) reviveButton.gameObject.SetActive(false);
            if (returnToVillageButton != null) returnToVillageButton.gameObject.SetActive(false);
            if (messageText != null) messageText.text = message;
        }

        private void RefreshCountView()
        {
            if (countText != null)
                countText.text = $"{ReviveBudget.Remaining} / {ReviveBudget.MaxPerRun}";
        }

        // 매니저를 우선하고, 매니저 없이 단독 실행하는 테스트 씬을 위해 씬 검색으로 폴백한다(Enemy와 동일).
        private static Player ResolvePlayer()
        {
            return PlayerManager.Instance != null
                ? PlayerManager.Instance.Player
                : Object.FindAnyObjectByType<Player>();
        }

        private void SetCursorFree(bool free)
        {
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }
    }
}
