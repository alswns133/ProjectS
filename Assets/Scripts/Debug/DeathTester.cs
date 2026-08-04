using UnityEngine;
using ProjectS.Core;
using ProjectS.Managers;
using ProjectS.Players;
using ProjectS.UI;

namespace ProjectS.Debugging
{
    /// <summary>
    /// 사망/부활 흐름(<see cref="DeathPopup"/> · <see cref="ReviveBudget"/>)을 플레이 모드에서 수동 검증하는 테스트 하네스.
    /// 씬 아무 GameObject에 붙이고 플레이 → 인스펙터 컨텍스트 메뉴(⋮)로 실행한다.
    /// </summary>
    /// <remarks>
    /// 이게 필요한 이유: 부활 기회는 <c>DungeonGather.Enter</c>에서만 채워진다. 그래서 HUD 씬을 단독으로
    /// 재생하면 기회가 0이라 첫 사망부터 "자동 마을 복귀" 분기로 빠지고, 부활 버튼을 볼 수가 없다.
    /// 실제로는 정상 동작인데 고장난 것처럼 보이는 구간이라, 던전까지 가지 않고 두 분기를 모두 확인할 수단을 둔다.
    ///
    /// 사망은 이벤트를 직접 쏘지 않고 <b>치명타 데미지를 먹여서</b> 만든다. <c>PlayerEvents.FirePlayerDied</c>만
    /// 부르면 HP가 남아 IsDead가 false이고, 그러면 <c>Player.Revive</c>가 가드에 걸려 아무 일도 하지 않아
    /// "부활 버튼이 안 먹는" 가짜 증상이 나온다.
    /// (2026-08-04 TH)
    /// </remarks>
    public class DeathTester : MonoBehaviour
    {
        [Header("대상")]
        [Tooltip("비워두면 PlayerManager → 씬 검색 순으로 찾는다.")]
        [SerializeField] private Player player;

        [Header("설정")]
        [Tooltip("Kill Player가 먹이는 피해량. 최대 HP보다 넉넉히 크게 둬서 방어도와 무관하게 즉사시킨다.")]
        [SerializeField, Min(1)] private int lethalDamage = 999999;

        /// <summary>
        /// HUD 패널을 띄운다. 단독 씬 테스트에서는 <b>반드시 죽이기 전에</b> 실행해야 한다.
        /// </summary>
        /// <remarks>
        /// 안 부르면 첫 HP 변화에서 <c>FillGauge.SetRatio</c>가 NullReferenceException으로 터지고,
        /// <b>그 예외가 <c>PlayerStats.TakeDamage</c> 한가운데서 터지기 때문에 바로 뒤의
        /// <c>FirePlayerDied()</c>가 실행되지 않는다</b> — HP는 0인데 아무도 사망을 통지받지 못해
        /// 사망 팝업이 뜨지 않는다. "죽었는데 창이 안 뜬다"의 진짜 원인이 이것이다.
        ///
        /// FillGauge는 코루틴을 돌려줄 주인(runner)을 <c>Init</c>에서 받는데, 그 Init은
        /// <c>HUDPanel.OnInit</c> → <c>BasePanel.Show</c> → <c>UIManager.ShowPanel</c> 순으로만 도달한다.
        /// 실제 게임에서는 <c>DungeonGather.Enter</c>가 ShowPanel을 먼저 부르지만, 단독 씬에는 그 흐름이 없다.
        ///
        /// 등록까지 여기서 하는 이유: UIManager는 자기 자식에서만 패널을 수집하는데 HUDPanel은
        /// HUD 루트 아래에 있어 panelMap에 없다. 등록 없이 ShowPanel을 부르면 "패널이 없음" 경고만 나고
        /// 초기화는 일어나지 않는다.
        /// </remarks>
        [ContextMenu("0. HUD 패널 열기 (단독 씬 필수)")]
        public void ShowHud()
        {
            if (UIManager.Instance == null)
            {
                Debug.LogWarning("[DeathTester] UIManager가 없어 HUD 패널을 열 수 없습니다. " +
                                 "Bootstrap에서 시작하거나 이 씬에 UIManager를 추가하세요.", this);
                return;
            }

            // 꺼져 있을 수 있으므로 비활성 포함으로 찾는다.
            HUDPanel hud = FindAnyObjectByType<HUDPanel>(FindObjectsInactive.Include);
            if (hud == null)
            {
                Debug.LogWarning("[DeathTester] 씬에 HUDPanel이 없습니다. HUD가 있는 씬에서 실행하세요.", this);
                return;
            }

            UIManager.Instance.RegisterPanel(hud);
            UIManager.Instance.ShowPanel<HUDPanel>();

            Debug.Log($"[DeathTester] HUD 패널을 열었습니다('{hud.name}'). 게이지가 초기화돼 HP 변화가 안전합니다.", this);
        }

        /// <summary>던전에 입장한 것처럼 부활 기회를 채운다. 부활 분기를 보려면 죽기 전에 실행한다.</summary>
        [ContextMenu("1. 부활 기회 채우기 (던전 입장 흉내)")]
        public void GrantRevive()
        {
            ReviveBudget.GrantOnDungeonEnter();
            PrintState();
        }

        /// <summary>부활 기회를 0으로 만든다. 자동 마을 복귀 분기를 보려면 죽기 전에 실행한다.</summary>
        [ContextMenu("2. 부활 기회 비우기 (자동 복귀 분기)")]
        public void ClearRevive()
        {
            ReviveBudget.Clear();
            PrintState();
        }

        /// <summary>플레이어를 즉사시킨다. 사망 팝업이 뜨는 것까지가 이 메뉴의 범위다.</summary>
        [ContextMenu("3. 플레이어 죽이기")]
        public void KillPlayer()
        {
            Player target = ResolvePlayer();
            if (target == null)
            {
                Debug.LogWarning("[DeathTester] Player를 찾지 못했습니다. 씬에 플레이어가 있는지 확인하세요.", this);
                return;
            }

            if (target.Stats.IsDead)
            {
                Debug.Log("[DeathTester] 이미 죽어 있습니다. 부활시키거나 재생을 다시 시작하세요.", this);
                return;
            }

            // 무적(구르기 등)에 씹히지 않게 ignoreInvincibility로 넣는다. 테스트가 조용히 실패하면
            // "왜 안 죽지"로 시간을 버리게 된다.
            DamageResult lethal = new DamageResult { Amount = lethalDamage, IsCritical = false };
            target.Stats.TakeDamage(in lethal, true);

            PrintState();
        }

        /// <summary>팝업을 거치지 않고 곧바로 되살린다. 부활 로직만 따로 볼 때 쓴다.</summary>
        [ContextMenu("4. 즉시 부활 (팝업 무시)")]
        public void ForceRevive()
        {
            Player target = ResolvePlayer();
            if (target == null) return;

            target.Revive(1f);
            PrintState();
        }

        /// <summary>현재 HP와 부활 기회를 콘솔에 찍는다.</summary>
        [ContextMenu("현재 상태 출력")]
        public void PrintState()
        {
            Player target = ResolvePlayer();
            string hp = target != null
                ? $"IsDead={target.Stats.IsDead}"
                : "Player 없음";

            Debug.Log($"[DeathTester] {hp} · 부활 기회 {ReviveBudget.Remaining}/{ReviveBudget.MaxPerRun}", this);
        }

        private Player ResolvePlayer()
        {
            if (player != null) return player;

            player = PlayerManager.Instance != null
                ? PlayerManager.Instance.Player
                : FindAnyObjectByType<Player>();

            return player;
        }
    }
}
