using System.Collections.Generic;
using UnityEngine;
using ProjectS.Events;
using ProjectS.Skills;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 스킬창의 흐름 제어자. View 이벤트를 받아 배치 편집 모델(<see cref="SkillAllocation"/>)을 갱신하고,
    /// 그 결과를 View에 반영한다. [확인]에서만 소스로 커밋한다(일괄 커밋 모델).
    /// </summary>
    /// <remarks>
    /// 소스는 <see cref="TableSkillSource"/>(SkillGrowthTable + 현재 캐릭터)다. 배운 레벨 저장·SP 스탯이
    /// 아직 없어 그 부분만 소스 안에서 자리표시자로 두고, View/Presenter는 진짜 시스템이 생겨도 그대로 둔다.
    /// (2026-08-26 신설)
    /// </remarks>
    public class SkillPresenter : BasePresenter
    {
        [SerializeField] private SkillPopup view;

        private ISkillWindowSource source;
        private SkillAllocation allocation;

        private void Awake()
        {
            if (view == null) view = GetComponent<SkillPopup>();
        }

        protected override void Subscribe()
        {
            view.OnOpened += HandleOpened;
            view.OnIncreaseRequested += HandleIncrease;
            view.OnDecreaseRequested += HandleDecrease;
            view.OnSlotFocused += HandleSlotFocused;
            view.OnResetRequested += HandleReset;
            view.OnConfirmRequested += HandleConfirm;
            view.OnCancelRequested += HandleCancel;
        }

        protected override void Unsubscribe()
        {
            view.OnOpened -= HandleOpened;
            view.OnIncreaseRequested -= HandleIncrease;
            view.OnDecreaseRequested -= HandleDecrease;
            view.OnSlotFocused -= HandleSlotFocused;
            view.OnResetRequested -= HandleReset;
            view.OnConfirmRequested -= HandleConfirm;
            view.OnCancelRequested -= HandleCancel;
        }

        // 창을 열 때마다 소스의 현재 상태에서 편집 세션을 새로 연다(직전 배치가 남지 않게).
        private void HandleOpened()
        {
            RebuildSource();
            allocation = new SkillAllocation(source);

            view.SetSlots(source.GetActiveSlots(), source.GetPassiveSlots());
            RefreshAll();

            // 첫 슬롯을 프리뷰에 띄운다(빈 프리뷰로 시작하지 않게).
            ShowFirstPreview();
        }

        private void HandleIncrease(int skillId)
        {
            if (allocation == null) return;

            if (!allocation.Increase(skillId))
            {
                // 왜 안 되는지 알린다(조용히 무시하면 SP가 없는지 최대인지 알 수 없다).
                UIEvents.FireToast(allocation.RemainingSp <= 0 ? "SP가 부족합니다." : "이미 최대 레벨입니다.");
                return;
            }

            RefreshAll();
        }

        private void HandleDecrease(int skillId)
        {
            if (allocation == null) return;
            if (!allocation.Decrease(skillId)) return;   // 바닥(배운 레벨)이면 조용히 무시
            RefreshAll();
        }

        private void HandleSlotFocused(int skillId)
        {
            if (allocation != null && allocation.TryGetInfo(skillId, out SkillSlotInfo info))
                view.SetPreview(info);
        }

        private void HandleReset()
        {
            if (allocation == null) return;
            allocation.Reset();
            RefreshAll();
        }

        private void HandleConfirm()
        {
            if (allocation == null)
            {
                view.Close();
                return;
            }

            IReadOnlyList<SkillLevelChange> changes = allocation.BuildChanges();
            if (changes.Count > 0)
            {
                source.Apply(changes);
                allocation.Commit();
                UIEvents.FireToast("스킬을 적용했습니다.");
            }

            view.Close();
        }

        // 취소·X: 적용하지 않고 닫는다. 배치는 다음 오픈 때 새 세션으로 버려지므로 되돌릴 필요가 없다.
        private void HandleCancel() => view.Close();

        // 전 슬롯 레벨·스테퍼 상태 + SP 표기를 한 번에 갱신한다.
        private void RefreshAll()
        {
            RefreshGroup(source.GetActiveSlots());
            RefreshGroup(source.GetPassiveSlots());
            view.SetSp(allocation.UsedSp, allocation.TotalSp);
        }

        private void RefreshGroup(IReadOnlyList<SkillSlotInfo> slots)
        {
            if (slots == null) return;

            foreach (SkillSlotInfo info in slots)
            {
                view.SetSlotLevel(
                    info.SkillId,
                    allocation.PendingLevel(info.SkillId),
                    info.MaxLevel,
                    allocation.CanIncrease(info.SkillId),
                    allocation.CanDecrease(info.SkillId));
            }
        }

        private void ShowFirstPreview()
        {
            IReadOnlyList<SkillSlotInfo> active = source.GetActiveSlots();
            if (active != null && active.Count > 0)
            {
                view.SetPreview(active[0]);
                return;
            }

            IReadOnlyList<SkillSlotInfo> passive = source.GetPassiveSlots();
            if (passive != null && passive.Count > 0) view.SetPreview(passive[0]);
        }

        // 소스는 창을 열 때마다 새로 만든다(그때의 현재 캐릭터·테이블 상태를 반영하기 위함).
        private void RebuildSource()
        {
            source = new TableSkillSource();
        }
    }
}
