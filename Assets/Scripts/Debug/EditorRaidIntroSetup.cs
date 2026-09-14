#if UNITY_EDITOR
using UnityEngine;
using ProjectS.Enemies;
using ProjectS.Managers;
using ProjectS.Scenes;

namespace ProjectS.Debugging
{
    /// <summary>
    /// 에디터에서 Bootstrap 씬을 거치지 않고 레이드 씬을 직접 재생했을 때, <b>등장 연출만</b> 제대로 보이도록
    /// 씬을 정리해 주는 연출 작업용 편의 장치. <see cref="EditorAutoBootstrap"/>(JsonManager 대타)와 같은 성격이다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>무엇을 정리하나</b>: 레이드 씬에는 1페이즈(망치)와 2페이즈(쌍검) 보스가 같은 자리에 겹쳐 놓여 있다.
    /// 2페이즈는 실제로는 <see cref="BossPhaseTransition"/>이 런타임에 스폰하는 것이라 등장 연출 시점에는
    /// 있어서는 안 되는데, 씬에 놓여 있으면 ① 모든 컷에 보스가 두 겹으로 찍히고 ② 등장 신호
    /// (Boss.Start)를 먼저 발행해 연출을 가로챈다. 그래서 <b>연출의 주인공이 아닌 보스를 재생 시작 전에 끈다</b>.
    /// </para>
    /// <para>
    /// 주인공 판정은 <see cref="BossIntroDirector.IsIntroBoss"/>에 맡긴다 — 어느 보스가 주인공인지의 근거는
    /// 타임라인 트랙 구성 하나뿐이라, 여기서 이름을 따로 적어 두면 연출을 고칠 때마다 두 곳이 어긋난다.
    /// </para>
    /// <para>
    /// <b>씬 파일은 건드리지 않는다.</b> 재생 중에만 끄므로 하이어라키 구성은 그대로 남고, 부트스트랩을 거친
    /// 정상 경로(=<see cref="PlayerManager"/>가 있는 경로)에서는 아무것도 하지 않는다.
    /// </para>
    /// <para>
    /// 플레이어가 없어 씬의 진입 트리거(PlayerZoneTrigger)는 발동하지 않지만, 등장 연출은 보스 등장 신호로도
    /// 재생되므로 직접 재생하면 첫 프레임에 바로 시작한다(연출 반복 확인에 오히려 편하다).
    /// </para>
    /// UNITY_EDITOR 전용이라 빌드에는 포함되지 않는다.
    /// </remarks>
    public static class EditorRaidIntroSetup
    {
        // AfterSceneLoad는 씬의 모든 Awake 뒤, 모든 Start 앞이다. 등장 신호는 Boss.Start에서 나오므로
        // 이 시점에 꺼야 "가로채기"가 아예 일어나지 않는다(Start 뒤에 끄면 이미 늦는다).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void KeepOnlyIntroBoss()
        {
            // 부트스트랩을 거친 정상 경로면 손대지 않는다(실제 진입 흐름을 테스트 장치가 바꾸지 않게).
            if (PlayerManager.Instance != null) return;

            BossIntroDirector intro = Object.FindAnyObjectByType<BossIntroDirector>();
            if (intro == null) return;   // 등장 연출이 없는 씬 — 정리할 것도 없다

            Boss[] bosses = Object.FindObjectsByType<Boss>(FindObjectsSortMode.None);
            if (bosses.Length == 0)
            {
                Debug.LogWarning("[EditorRaidIntroSetup] 씬에 보스가 없어 등장 연출이 재생되지 않습니다. " +
                                 "연출을 보려면 보스 프리팹을 씬에 배치하세요(직접 재생은 스포너를 돌리지 않습니다).");
                return;
            }

            foreach (Boss boss in bosses)
            {
                if (intro.IsIntroBoss(boss, out string missingPart)) continue;

                boss.gameObject.SetActive(false);
                Debug.Log($"[EditorRaidIntroSetup] '{boss.name}'은 등장 연출의 주인공이 아니라('{missingPart}' 없음) " +
                          $"재생 동안 꺼 둡니다. 씬 파일은 그대로입니다.", boss);
            }
        }
    }
}
#endif
