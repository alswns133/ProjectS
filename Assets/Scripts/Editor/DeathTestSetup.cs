// 'Editor' 폴더 필수(UnityEditor 참조). 네임스페이스는 UnityEditor.Editor 가림 회피로 EditorTools.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ProjectS.Debugging;
using ProjectS.Managers;
using ProjectS.Players;
using ProjectS.UI;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 현재 씬을 사망/부활 테스트가 가능하도록 세팅한다. HUD(TH) 2처럼 던전을 거치지 않는 UI 씬에서
    /// 단독 재생 테스트용. <see cref="DeathTester"/> 하네스를 없을 때만 추가하고, 부족한 연결을 점검해 알린다.
    /// 메뉴: Tools ▸ ProjectS ▸ Setup Death Test (current scene)
    /// </summary>
    /// <remarks>
    /// 매니저를 억지로 만들어 넣지 않는 이유: 사망 팝업은 <c>UIManager</c>에 등록돼야 열리고, 자동 복귀는
    /// <c>GameSceneManager</c>가 있어야 마을로 넘어간다. 이 둘은 정상 부트(Bootstrap)에서 오는 것이라
    /// 빈 GameObject로 흉내 내면 오히려 "테스트에선 되는데 실제론 안 되는" 상태를 만든다.
    /// 그래서 여기서는 <b>없으면 만들지 않고 무엇이 없는지 알려주기만</b> 한다.
    /// 테스트용이므로 머지 전에 __DeathTest를 지워도 된다.
    /// (2026-08-04 TH)
    /// </remarks>
    public static class DeathTestSetup
    {
        private const string HostName = "__DeathTest";

        [MenuItem("Tools/ProjectS/Setup Death Test (current scene)", false, 111)]
        public static void Setup()
        {
            bool added = false;

            if (Object.FindAnyObjectByType<DeathTester>() == null)
            {
                var go = new GameObject(HostName, typeof(DeathTester));
                Undo.RegisterCreatedObjectUndo(go, "Add DeathTester");
                Selection.activeGameObject = go;
                added = true;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            // 재생 전에 알 수 있는 문제를 미리 알린다. 죽고 나서야 "아무 창도 안 뜬다"를 발견하면
            // 원인이 팝업인지 등록인지 매니저인지 구분하기 어렵다.
            string report = added ? $"'{HostName}' 오브젝트를 추가했습니다.\n" : "이미 하네스가 있어 그대로 둡니다.\n";
            report += Diagnose();

            Debug.Log("[DeathTestSetup] " + report +
                      "\n· 재생 → __DeathTest의 DeathTester 컨텍스트 메뉴(⋮)를 번호 순서대로 실행하세요." +
                      "\n  ★ 0(HUD 패널 열기)을 먼저 하지 않으면 3에서 FillGauge가 NullReference로 터집니다." +
                      "\n  부활 분기: 0 → 1(기회 채우기) → 3(죽이기) → 팝업에서 '부활'" +
                      "\n  자동 복귀 분기: 0 → 2(기회 비우기) → 3(죽이기) → 카운트다운 확인" +
                      "\n· 테스트용 오브젝트라 머지 전 제거해도 됩니다.");
        }

        // 실행 전 점검. 여기서 걸리는 것들은 전부 "죽어도 아무 일이 없다"로 나타나는 원인들이다.
        private static string Diagnose()
        {
            string result = string.Empty;

            if (Object.FindAnyObjectByType<Player>() == null)
                result += "· ⚠ 씬에 Player가 없습니다 → 죽일 대상이 없어 3번 메뉴가 동작하지 않습니다.\n";

            DeathPopup popup = Object.FindAnyObjectByType<DeathPopup>(FindObjectsInactive.Include);
            if (popup == null)
                result += "· ⚠ DeathPopup이 없습니다 → 'Tools ▸ ProjectS ▸ 사망·부활 팝업 생성'을 먼저 실행하세요.\n";

            DeathPresenter presenter = Object.FindAnyObjectByType<DeathPresenter>(FindObjectsInactive.Include);
            if (presenter == null)
            {
                result += "· ⚠ DeathPresenter가 없습니다 → 사망 이벤트를 들을 것이 없어 창이 뜨지 않습니다.\n";
            }
            else if (!presenter.gameObject.activeInHierarchy)
            {
                // 이 경우가 가장 찾기 어렵다. 컴포넌트는 있는데 꺼져 있어 OnEnable이 안 돌고, 구독이 성립하지 않는다.
                result += "· ⚠ DeathPresenter가 꺼진 오브젝트에 있습니다 → 항상 켜져 있는 곳으로 옮기세요.\n";
            }

            if (Object.FindAnyObjectByType<UIManager>() == null)
                result += "· ⚠ UIManager가 없습니다 → 팝업 등록/표시가 되지 않습니다. Bootstrap에서 시작하거나 씬에 추가하세요.\n";

            // HUD 패널은 존재 여부가 아니라 '표시됐는지'가 문제라 에디터에서는 판정할 수 없다(초기화는 재생 중에 일어난다).
            // 그래서 있으면 있다고만 알리고, 실행 순서를 강조한다. 이걸 놓치면 죽이는 순간
            // FillGauge.SetRatio가 코루틴 주인(runner) 없이 불려 NullReferenceException이 난다.
            if (Object.FindAnyObjectByType<HUDPanel>(FindObjectsInactive.Include) != null)
                result += "· ℹ HUD 패널이 있습니다 → 재생 후 반드시 0번(HUD 패널 열기)을 먼저 실행하세요. " +
                          "안 하면 게이지가 초기화되지 않아 3번에서 예외가 납니다.\n";

            if (Object.FindAnyObjectByType<GameSceneManager>() == null)
                result += "· ℹ GameSceneManager가 없습니다 → 카운트다운은 돌지만 마을로 넘어가지는 않습니다(단독 씬에선 정상).\n";

            return result.Length == 0 ? "· 점검 통과: 사망 → 팝업 흐름에 필요한 요소가 모두 있습니다.\n" : result;
        }
    }
}
