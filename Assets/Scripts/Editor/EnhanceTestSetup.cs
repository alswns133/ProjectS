// 'Editor' 폴더 필수(UnityEditor 참조). 네임스페이스는 UnityEditor.Editor 가림 회피로 EditorTools.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ProjectS.Managers;
using ProjectS.Debugging;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 현재 씬을 강화 테스트가 가능하도록 세팅한다. HUD(TH) 2처럼 매니저가 없는 UI 씬에서 단독 재생 테스트용.
    /// JsonManager(데이터 로드) · InventoryManager(재화 소유) · EnhanceTester(하네스)를 없을 때만 추가한다.
    /// 정상 부트 흐름(Bootstrap)과 함께 로드돼 매니저가 중복돼도 싱글톤 가드가 자기 자신을 파기하므로 안전하다.
    /// 테스트용이므로 머지 전에 __EnhanceTest 등을 지워도 된다.
    /// (2026-07-23 TH)
    /// </summary>
    public static class EnhanceTestSetup
    {
        [MenuItem("Tools/ProjectS/Setup Enhance Test (current scene)")]
        public static void Setup()
        {
            int added = 0;

            if (Object.FindObjectOfType<JsonManager>() == null)
            {
                var go = new GameObject("JsonManager", typeof(JsonManager));
                Undo.RegisterCreatedObjectUndo(go, "Add JsonManager");
                added++;
            }

            if (Object.FindObjectOfType<InventoryManager>() == null)
            {
                var go = new GameObject("InventoryManager", typeof(InventoryManager));
                Undo.RegisterCreatedObjectUndo(go, "Add InventoryManager");
                added++;
            }

            if (Object.FindObjectOfType<EnhanceTester>() == null)
            {
                var go = new GameObject("__EnhanceTest", typeof(EnhanceTester));
                Undo.RegisterCreatedObjectUndo(go, "Add EnhanceTester");
                added++;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log($"[EnhanceTestSetup] 현재 씬 테스트 세팅 완료 (신규 {added}개 추가).\n" +
                      "· 재생 전 필수: JsonData 5개 JSON이 어드레서블 등록(주소=타입명)돼 있어야 데이터가 로드됩니다.\n" +
                      "· 재생 → __EnhanceTest의 EnhanceTester 컨텍스트 메뉴(⋮)로 Print Info / Try Once / Sample Rate 실행.\n" +
                      "· 테스트용 오브젝트라 머지 전 제거해도 됩니다.");
        }
    }
}
