using UnityEngine;
using UnityEngine.Serialization;
using ProjectS.Events;
using ProjectS.UI.Framework;
using ProjectS.Managers;

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

            // 숨겨져(비활성) 구독이 끊긴 사이 바뀐 스탯을 다시 받는다(예: 상호작용 중 받은 보상).
            PlayerEvents.FireStatsRefreshRequested();

            if (PlayerManager.Instance != null)
                view.SetSymbol(PlayerManager.Instance.CurrentCharacterId);
        }

        protected override void Unsubscribe()
        {
            PlayerEvents.OnHpChanged -= OnHpChanged;
            PlayerEvents.OnSGChanged -= OnSgChanged;
            PlayerEvents.OnStaminaChanged -= OnStaminaChanged;
            PlayerEvents.OnExpChanged -= OnExpChanged;
            PlayerEvents.OnLevelChanged -= OnLevelChanged;
            PlayerEvents.OnSkillUsed -= OnSkillUsed;
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
    }
}
