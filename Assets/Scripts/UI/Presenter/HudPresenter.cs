using UnityEngine;
using UnityEngine.Serialization;
using ProjectS.Events;
using ProjectS.UI.Framework;
using ProjectS.Managers;
using ProjectS.Players;

namespace ProjectS.UI
{
    public class HUDPresenter : BasePresenter
    {
        // FormerlySerializedAs: 언더바 제거 리네임(_view→view) 전에 저장된
        // 인스펙터 연결이 끊기지 않게 유지한다. 씬 재저장 후에는 지워도 된다.
        [FormerlySerializedAs("_view")]
        [SerializeField] private HUDPanel view;

        private void Awake()
        {
            view = GetComponent<HUDPanel>();
        }

        protected override void Subscribe()
        {
            PlayerEvents.OnHpChanged += OnHpChanged;
            PlayerEvents.OnSGChanged += OnSgChanged;
            PlayerEvents.OnStaminaChanged += OnStaminaChanged;
            PlayerEvents.OnExpChanged += OnExpChanged;
            PlayerEvents.OnLevelChanged += OnLevelChanged;
            PlayerEvents.OnSkillUsed += OnSkillUsed;
            PlayerEvents.OnSkillCooldownsReset += OnSkillCooldownsReset;
            PlayerEvents.OnHitComboChanged += OnHitComboChanged;

            // 숨겨져(비활성) 구독이 끊긴 사이 바뀐 스탯을 다시 받는다(예: 상호작용 중 받은 보상).
            PlayerEvents.FireStatsRefreshRequested();

            if (PlayerManager.Instance != null)
            {
                int chatid = PlayerManager.Instance.CurrentCharacterId;
                view.SetSymbol(chatid);
                view.SetLevelColor(chatid);
            }

        }

        protected override void Unsubscribe()
        {
            PlayerEvents.OnHpChanged -= OnHpChanged;
            PlayerEvents.OnSGChanged -= OnSgChanged;
            PlayerEvents.OnStaminaChanged -= OnStaminaChanged;
            PlayerEvents.OnExpChanged -= OnExpChanged;
            PlayerEvents.OnLevelChanged -= OnLevelChanged;
            PlayerEvents.OnSkillUsed -= OnSkillUsed;
            PlayerEvents.OnSkillCooldownsReset -= OnSkillCooldownsReset;
            PlayerEvents.OnHitComboChanged -= OnHitComboChanged;
        }

        // 이벤트 받아서 가공 후 View한테 전달
        // HP/SG는 원본 값을 그대로 넘긴다. 게이지 비율 외에 "150/200" 수치 표기도 그려야 해서,
        // 여기서 비율로 접으면 View가 원본을 되살릴 방법이 없다.
        private void OnHpChanged(float cur, float max)
            => view.SetHp(cur, max);

        private void OnSgChanged(float cur, float max)
            => view.SetSg(cur, max);

        private void OnStaminaChanged(float cur, float max)
            => view.SetStamina(cur / max);

        private void OnExpChanged(int cur, int max)
            => view.SetExp(max > 0 ? (float)cur / max : 1f);   // max 0(최대 레벨 등)이면 가득 참으로

        private void OnLevelChanged(int level)
            => view.SetLevel(level);

        private void OnSkillUsed(int skillNumber, float cooldown)
            => view.StartSkillCooldown(skillNumber, cooldown);

        private void OnSkillCooldownsReset()
            => view.ClearSkillCooldowns();

        private void OnHitComboChanged(int hitCount)
            => view.SetHitCombo(hitCount);

        // [2026.09.15 태하] 콤보 유지 게이지. 남은 시간은 매 프레임 줄어드는 연속 값이라
        // static 이벤트를 매 프레임 쏘는 대신 여기서 직접 읽어 View에 넘긴다.
        // 타이머를 HUD에 따로 두지 않는 이유: 히트스톱·피격 리셋과 어긋나지 않게 판정 원천(PlayerHitCombo)을 하나로 유지하기 위함.
        private void Update()
        {
            // 멀티 레이드에선 PlayerManager.Player가 숨겨진 마을 캐릭터(히트가 안 들어와 비율이 영영 0)라,
            // 조작 중인 아바타를 돌려주는 LocalPlayer.Current를 읽어야 유지 게이지가 실제로 줄어든다.
            var player = LocalPlayer.Current;
            if (player == null || player.HitCombo == null) return;

            view.SetHitComboTimer(player.HitCombo.RemainingRatio);
        }
    }
}
