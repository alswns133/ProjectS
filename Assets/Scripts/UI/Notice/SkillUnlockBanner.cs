using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.Events;
using ProjectS.Items;
using ProjectS.Managers;

namespace ProjectS.UI
{
    /// <summary>
    /// 스킬 해금 알림 배너(기획서 4-2 · UI_LV_011 · UI_LV_012).
    /// "새 스킬을 해금했습니다!" 문구와 함께 해금된 스킬의 이름·아이콘을 보여주고,
    /// 버튼 없이 <see cref="TimedNoticeView.HoldSeconds"/>초 뒤 스스로 사라진다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>레벨업 알림과 달리 큐를 둔다.</b> 한 번에 여러 레벨이 오르면 스킬도 여러 개가 같은 순간에 열릴 수 있는데,
    /// 레벨은 "최종 레벨 하나"로 접어도 되지만 <b>어떤 스킬이 열렸는지는 접으면 정보가 사라진다.</b>
    /// 그래서 하나가 끝나면 다음을 이어 재생한다.
    /// </para>
    /// <para>
    /// <b>대기열은 <see cref="maxQueued"/>개로 막아 둔다.</b> 큰 경험치 보상 한 방에 여러 레벨이 오르면
    /// 대기열이 그만큼 쌓이고, 하나당 5초가 넘으므로 배너가 수십 초 이상 화면을 차지하게 된다.
    /// 넘치면 가장 오래된 것부터 버려 최근 해금이 반드시 보이게 한다.
    /// </para>
    /// <para>
    /// <b>이 뷰는 이벤트를 구독하지 않는다.</b> 애초에 지금은 스킬 해금 시스템 자체가 없다 —
    /// <c>QuestRewardGranter</c>의 <c>SkillUnlock</c>은 로그만 찍는 스텁이고,
    /// 세이브에 배운 스킬 목록이 없으며 <c>SkillTable</c>에도 해금 레벨 열이 없다(2026.08.12 기준).
    /// 시스템이 생기면 그쪽에서 <see cref="Show"/>를 부르면 된다.
    /// </para>
    /// <para>
    /// 표시 필드는 전부 선택 사항이다. 비어 있어도 기능은 돈다.
    /// </para>
    /// </remarks>
    public class SkillUnlockBanner : TimedNoticeView
    {
        [Header("표시")]
        [Tooltip("'새 스킬을 해금했습니다!' 안내 문구. 씬에 적어 둔 값을 그대로 쓰려면 비워 둔다.")]
        [SerializeField] private TMP_Text messageText;

        [Tooltip("해금된 스킬 이름.")]
        [SerializeField] private TMP_Text skillNameText;

        [Tooltip("해금된 스킬 아이콘. 넘어온 스프라이트가 없으면 통째로 숨긴다.")]
        [SerializeField] private Image skillIcon;

        [Header("문구")]
        [Tooltip("배너에 띄울 안내 문구. messageText가 비어 있으면 쓰이지 않는다.")]
        [SerializeField, TextArea] private string unlockMessage = "새 스킬을 해금했습니다!";

        [Header("대기열")]
        [Tooltip("동시에 밀려들 수 있는 해금 알림의 최대 대기 수. 넘치면 가장 오래된 것부터 버린다.")]
        [SerializeField, Min(1)] private int maxQueued = 3;

        [Tooltip("뒤에 대기 중인 해금이 남아 있을 때 쓸 유지 시간(초). 마지막 한 건은 평소 유지 시간을 그대로 쓴다.")]
        [SerializeField, Min(0f)] private float queuedHoldSeconds = 2f;

        // 표시할 내용 한 건. 아이콘이 아직 없을 수 있어 이름만으로도 성립한다.
        private readonly struct Entry
        {
            public readonly string SkillName;
            public readonly Sprite Icon;

            public Entry(string skillName, Sprite icon)
            {
                SkillName = skillName;
                Icon = icon;
            }
        }

        private readonly Queue<Entry> pending = new();

        // 구독은 GameObject 활성 상태에 묶지 않는다(재생 후 스스로 비활성되므로 — ToastNotice와 같은 이유).
        // Awake에서 구독하고 OnDestroy에서만 해제한다. 해금 요청이 오면 Show가 Play로 오브젝트를 다시 켠다.
        protected override void Awake()
        {
            base.Awake();
            SkillEvents.OnSkillUnlocked += OnSkillUnlocked;
        }

        private void OnDestroy()
        {
            SkillEvents.OnSkillUnlocked -= OnSkillUnlocked;
        }

        // 스킬 해금 이벤트를 받아 이름·아이콘을 SkillGrowthTable에서 조회해 배너로 띄운다.
        private async void OnSkillUnlocked(int skillId)
        {
            SkillGrowthTable row = JsonManager.Instance != null ? JsonManager.Instance.Get<SkillGrowthTable>(skillId) : null;
            string skillName = row != null ? row.Name : "새 스킬";

            Sprite icon = (row != null && !string.IsNullOrEmpty(row.IconAddress))
                ? await ItemIconLoader.LoadAsync(row.IconAddress)
                : null;
            if (this == null) return;   // 아이콘 로드 대기 중 파괴됐을 수 있다.

            Show(skillName, icon);
        }

        /// <summary>
        /// 스킬 해금 알림을 예약한다. 배너가 비어 있으면 즉시 재생하고, 재생 중이면 끝난 뒤 이어서 보여준다.
        /// </summary>
        /// <param name="skillName">해금된 스킬 이름</param>
        /// <param name="icon">스킬 아이콘. null이면 아이콘 칸을 숨긴다.</param>
        public void Show(string skillName, Sprite icon)
        {
            // 오래된 것부터 버려 최근 해금이 반드시 보이게 한다.
            while (pending.Count >= maxQueued) pending.Dequeue();

            pending.Enqueue(new Entry(skillName, icon));

            // 재생 중이면 건드리지 않는다. 지금 떠 있는 배너가 끝나면 OnNoticeFinished가 다음을 꺼낸다.
            if (IsPlaying) return;

            PlayNext();
        }

        /// <summary>
        /// 대기 중인 알림을 모두 버리고 배너를 즉시 내린다. 씬 전환·사망처럼 화면이 통째로 바뀔 때 호출한다.
        /// </summary>
        /// <remarks>
        /// <see cref="TimedNoticeView.Dismiss"/>만 부르면 큐가 남아, 다음에 알림이 하나 들어올 때
        /// 지난 판의 해금까지 줄줄이 따라 뜬다.
        /// </remarks>
        public void ClearQueue()
        {
            pending.Clear();
            Dismiss();
        }

        /// <remarks>
        /// 대기열이 남아 있으면 짧게 끊어 간다. 한 건당 온전한 유지 시간을 다 쓰면
        /// <see cref="maxQueued"/>개가 밀렸을 때 배너가 십수 초 동안 화면을 차지한다.
        /// 마지막 한 건은 평소 시간을 그대로 써서, 가장 최근에 열린 스킬은 제대로 읽히게 한다.
        /// <para>
        /// <see cref="PlayNext"/>가 현재 건을 꺼낸 뒤 <c>Play()</c>를 부르므로,
        /// 이 시점의 <c>pending.Count</c>는 "이번 것 다음에 남은 개수"다.
        /// </para>
        /// </remarks>
        protected override float ResolveHoldSeconds()
            => pending.Count > 0 ? queuedHoldSeconds : base.ResolveHoldSeconds();

        protected override void OnNoticeFinished() => PlayNext();

        private void PlayNext()
        {
            if (pending.Count == 0) return;

            Entry entry = pending.Dequeue();

            if (messageText != null) messageText.text = unlockMessage;
            if (skillNameText != null) skillNameText.text = entry.SkillName;

            // 아이콘이 없는 스킬은 빈 사각형이 남지 않게 칸째로 숨긴다.
            if (skillIcon != null)
            {
                skillIcon.sprite = entry.Icon;
                skillIcon.gameObject.SetActive(entry.Icon != null);
            }

            Play();
        }
    }
}
