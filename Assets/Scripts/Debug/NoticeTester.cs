using UnityEngine;
using ProjectS.UI;

namespace ProjectS.Debugging
{
    /// <summary>
    /// 화면 연출(<see cref="LevelUpNotice"/> · <see cref="SkillUnlockBanner"/> · <see cref="BossIntroFx"/>)을
    /// 플레이 모드에서 눈으로 확인하는 테스트 하네스.
    /// 씬 아무 GameObject에 붙이고 플레이 → 인스펙터 컨텍스트 메뉴(⋮)로 실행한다.
    /// </summary>
    /// <remarks>
    /// 이게 필요한 이유: 셋 다 아직 <b>아무 데서도 호출되지 않는다</b>.
    /// <c>PlayerEvents.FireLevelUp</c>은 호출부가 없고, 스킬 해금 시스템은 스텁이며,
    /// 보스 등장 연출도 <c>EnemyStats</c> 쪽 배선이 아직 없다(2026.08.14 기준).
    /// 실제로 플레이해서는 이 연출들이 뜨는 것을 볼 방법이 없다.
    /// 배선이 붙으면 이 하네스는 타이밍 조정용으로만 남는다.
    /// </remarks>
    public class NoticeTester : MonoBehaviour
    {
        [Header("대상")]
        [Tooltip("비워두면 씬에서 찾는다(비활성 포함).")]
        [SerializeField] private LevelUpNotice levelUpNotice;

        [Tooltip("비워두면 씬에서 찾는다(비활성 포함).")]
        [SerializeField] private SkillUnlockBanner skillUnlockBanner;

        [Tooltip("비워두면 씬에서 찾는다(비활성 포함).")]
        [SerializeField] private BossIntroFx bossIntro;

        [Header("테스트 값")]
        [SerializeField, Min(1)] private int testLevel = 7;

        [SerializeField] private string testSkillName = "블레이드 스톰";

        [Tooltip("비워두면 아이콘 칸이 숨겨진 상태로 뜬다(아이콘 없는 스킬과 같은 표시).")]
        [SerializeField] private Sprite testSkillIcon;

        [Tooltip("연속 해금 테스트에서 띄울 스킬 이름들. 대기열이 순서대로 소화되는지 본다.")]
        [SerializeField]
        private string[] testSkillNames = { "블레이드 스톰", "섬광 베기", "폭풍 일격", "각성" };

        [Tooltip("보스 등장 연출에 표시할 이름. 비우면 이름 줄이 숨겨진 상태로 뜬다.")]
        [SerializeField] private string testBossName = "강철 파수꾼";

        [ContextMenu("1. 레벨업 알림")]
        public void ShowLevelUp()
        {
            LevelUpNotice notice = Resolve(ref levelUpNotice);
            if (notice == null) return;

            notice.Show(testLevel);
        }

        [ContextMenu("2. 스킬 해금 알림")]
        public void ShowSkillUnlock()
        {
            SkillUnlockBanner banner = Resolve(ref skillUnlockBanner);
            if (banner == null) return;

            banner.Show(testSkillName, testSkillIcon);
        }

        /// <summary>
        /// 레벨업과 스킬 해금을 같은 프레임에 띄운다. 실제로 가장 흔한 조합이라(레벨업으로 스킬이 열린다)
        /// 두 알림이 화면에서 겹치지 않는지 확인하는 용도다.
        /// </summary>
        [ContextMenu("3. 둘 동시에 (레벨업 → 스킬 해금)")]
        public void ShowBoth()
        {
            ShowLevelUp();
            ShowSkillUnlock();
        }

        /// <summary>
        /// 해금 알림을 한꺼번에 밀어 넣어 대기열 동작을 확인한다.
        /// 대기열 상한(<c>maxQueued</c>)을 넘기면 오래된 것부터 버려지는 것도 여기서 보인다.
        /// </summary>
        [ContextMenu("4. 스킬 해금 연속 (대기열 확인)")]
        public void ShowSkillUnlockBurst()
        {
            SkillUnlockBanner banner = Resolve(ref skillUnlockBanner);
            if (banner == null) return;

            foreach (string skillName in testSkillNames)
                banner.Show(skillName, testSkillIcon);
        }

        /// <summary>
        /// 보스 등장 연출을 처음부터 재생한다.
        /// 경고 배너 → 위험 표시 깜박임 → 파편 파괴 → BOSS 슬램까지 한 번에 이어진다.
        /// </summary>
        [ContextMenu("5. 보스 등장 연출")]
        public void ShowBossIntro()
        {
            BossIntroFx intro = Resolve(ref bossIntro);
            if (intro == null) return;

            intro.Play(testBossName);
        }

        [ContextMenu("6. 모두 즉시 내리기")]
        public void DismissAll()
        {
            LevelUpNotice notice = Resolve(ref levelUpNotice);
            if (notice != null) notice.Dismiss();

            SkillUnlockBanner banner = Resolve(ref skillUnlockBanner);
            if (banner != null) banner.ClearQueue();

            BossIntroFx intro = Resolve(ref bossIntro);
            if (intro != null) intro.Dismiss();
        }

        // 알림은 평소 꺼져 있으므로 비활성 포함으로 찾는다.
        private T Resolve<T>(ref T cached) where T : MonoBehaviour
        {
            if (cached != null) return cached;

            cached = FindAnyObjectByType<T>(FindObjectsInactive.Include);

            if (cached == null)
                Debug.LogWarning($"[NoticeTester] 씬에서 {typeof(T).Name}을(를) 찾지 못했습니다.", this);

            return cached;
        }
    }
}
