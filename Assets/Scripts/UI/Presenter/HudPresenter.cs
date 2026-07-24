using UnityEngine;
using UnityEngine.Serialization;
using ProjectS.Events;
using ProjectS.UI.Framework;

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
        private void OnHpChanged(float cur, float max)
            => view.SetHp(cur / max);         // 비율 계산은 P가!

        private void OnSgChanged(float cur, float max)
            => view.SetSg(cur / max);

        private void OnStaminaChanged(float cur, float max)
            => view.SetStamina(cur / max);

        private void OnExpChanged(int cur, int max)
            => view.SetExp((float)cur / max);

        private void OnLevelChanged(int level)
            => view.SetLevel(level);

        private void OnSkillUsed(int skillNumber, float cooldown)
            => view.StartSkillCooldown(skillNumber, cooldown);
    }
}
