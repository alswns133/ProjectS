using TMPro;
using UnityEngine;

namespace ProjectS.UI
{
    /// <summary>
    /// 레벨업 알림(기획서 4-1 · UI_LV_001). 화면 중앙에 "LEVEL UP"과 도달한 레벨 숫자를 띄우고,
    /// 버튼 없이 <see cref="TimedNoticeView.HoldSeconds"/>초 뒤 스스로 사라진다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>이 뷰는 이벤트를 구독하지 않는다.</b> 지금은 <c>PlayerEvents.FireLevelUp</c>을 부르는 곳이
    /// 한 군데도 없어(2026.08.12 기준) 신호 자체가 발행되지 않는다. 배선이 정해지면
    /// Presenter가 <c>PlayerEvents.OnLevelUp</c>을 받아 <see cref="Show"/>를 부르면 된다.
    /// 뷰가 직접 구독하지 않는 것은 이 프로젝트의 Presenter 규약이기도 하다.
    /// </para>
    /// <para>
    /// <b>한 번에 여러 레벨이 오를 때</b>: <see cref="Show"/>를 연달아 부르면 그때마다 처음부터 다시 재생되어
    /// 마지막 레벨만 남는다(<see cref="TimedNoticeView.Play"/>). 5초짜리 알림이 레벨 수만큼 줄지어 뜨는 것보다
    /// 최종 레벨 하나를 보여주는 편이 낫다는 판단이다. 매 레벨을 다 보여주려면 배선 쪽에서 간격을 두고 불러야 한다.
    /// </para>
    /// <para>
    /// 표시 필드는 전부 선택 사항이다. 비어 있어도 기능은 돈다 — 아트가 붙기 전에도 타이밍을 검증할 수 있게 했다
    /// (<c>DeathPopup</c>과 같은 방침).
    /// </para>
    /// </remarks>
    public class LevelUpNotice : TimedNoticeView
    {
        [Header("표시")]
        [Tooltip("도달한 레벨 숫자. 'LEVEL UP' 타이틀은 씬에 고정 문구로 두고 여기서는 숫자만 갈아끼운다.")]
        [SerializeField] private TMP_Text levelText;

        [Tooltip("{0}에 레벨 숫자가 들어간다.")]
        [SerializeField] private string levelFormat = "{0}";

        [Header("연출")]
        [Tooltip("타이틀 글리치 연출. 비워두면 자식에서 자동으로 찾는다. 없으면 연출 없이 표시만 된다.")]
        [SerializeField] private GlitchTextFx titleFx;

        [Tooltip("숫자 슬롯머신 연출. 비워두면 자식에서 자동으로 찾는다. " +
                 "붙어 있으면 숫자는 릴이 그리므로 levelText·levelFormat은 쓰이지 않는다.")]
        [SerializeField] private NumberReelFx levelReel;

        protected override void Awake()
        {
            base.Awake();

            // 인스펙터에 안 물려도 동작하게 자식에서 찾아 둔다. 알림은 평소 꺼져 있으므로 비활성 포함으로 찾는다.
            if (titleFx == null) titleFx = GetComponentInChildren<GlitchTextFx>(true);
            if (levelReel == null) levelReel = GetComponentInChildren<NumberReelFx>(true);
        }

        /// <summary>
        /// 레벨업 알림을 띄운다. 이미 떠 있으면 새 레벨로 갈아끼우고 처음부터 다시 재생한다.
        /// </summary>
        /// <param name="level">새로 도달한 레벨</param>
        public void Show(int level)
        {
            // 릴이 있으면 숫자는 릴이 그린다. 여기서 텍스트를 덮어쓰면 릴의 원본 라벨과 충돌한다.
            if (levelReel == null && levelText != null) levelText.text = string.Format(levelFormat, level);

            Play();

            // 재생 시작은 Play() 뒤여야 한다. Play()가 오브젝트를 켜는데, 꺼져 있는 동안 부른 Play는
            // GlitchTextFx·NumberReelFx 쪽에서 isActiveAndEnabled 가드에 걸려 조용히 무시된다.
            // Play는 재생 중이어도 처음부터 다시 시작하므로 중복 호출이 안전하다.
            if (titleFx != null) titleFx.Play();
            if (levelReel != null) levelReel.Play(level);
        }
    }
}
