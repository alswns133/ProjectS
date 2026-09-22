// 'Editor' 폴더 필수(UnityEditor 참조). 네임스페이스는 UnityEditor.Editor 가림 회피로 EditorTools.
using System.Collections.Generic;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 씬에 놓인 <see cref="NetworkIdentity"/>의 <c>sceneId</c>가 비어 있는 것을 찾아 채운다.
    /// 메뉴: Tools ▸ ProjectS ▸ Fix Scene Network Ids
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>무엇을 고치나.</b> 빌드할 때 나오는
    /// <c>"Scene ... needs to be opened and resaved, because the scene object X has no valid sceneId yet"</c> 에러다.
    /// </para>
    /// <para>
    /// <b>왜 '열고 저장'만으로는 안 되나(2026-09-22).</b> Mirror는 <c>NetworkIdentity.OnValidate</c>가 돌 때
    /// sceneId를 부여하는데, 씬을 열어도 <b>꺼져 있는 오브젝트</b>에서는 그게 돌지 않는다. 그래서 씬을 열고
    /// Ctrl+S를 눌러도 값이 계속 비어 있고, 에러 메시지는 "열고 저장하라"고만 해서 같은 자리를 맴돌게 된다.
    /// 실제로 캐릭터 선택 씬의 프리뷰 모델(꺼 둔 Erwin)이 이 경우였다 — 켜져 있던 다른 모델만 값을 받았다.
    /// </para>
    /// <para>
    /// <b>어떻게 고치나.</b> 값이 빈 것만 골라 <c>serverOnly</c>를 껐다 켜는(원래 값으로 되돌리는) 변경을
    /// 넣는다. <c>SerializedObject.ApplyModifiedProperties</c>가 <c>OnValidate</c>를 부르므로 Mirror가
    /// 자기 규칙대로(중복 검사 포함) sceneId를 만들어 넣는다. 우리가 임의의 숫자를 써 넣지 않는 이유다.
    /// </para>
    /// <para>
    /// Build Settings에 등록된 씬을 차례로 열어 훑고, 고친 씬만 저장한 뒤 원래 열려 있던 씬으로 돌아온다.
    /// </para>
    /// </remarks>
    public static class SceneNetworkIdFixer
    {
        [MenuItem("Tools/ProjectS/Fix Scene Network Ids")]
        public static void FixAll()
        {
            // 씬을 갈아 끼우므로 저장 안 된 작업이 날아가지 않게 먼저 묻는다.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            string originalScenePath = SceneManager.GetActiveScene().path;
            List<string> repaired = new();
            int total = 0;

            foreach (EditorBuildSettingsScene entry in EditorBuildSettings.scenes)
            {
                if (entry == null || !entry.enabled || string.IsNullOrEmpty(entry.path)) continue;

                Scene scene = EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Single);
                int fixedHere = FixOpenScene(scene);
                if (fixedHere <= 0) continue;

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);

                repaired.Add($"{scene.name}({fixedHere}개)");
                total += fixedHere;
            }

            // 작업 전 씬으로 복귀. 빈 경로(새 씬)면 그대로 둔다.
            if (!string.IsNullOrEmpty(originalScenePath)) EditorSceneManager.OpenScene(originalScenePath, OpenSceneMode.Single);

            Debug.Log(total > 0
                ? $"[SceneNetworkIdFixer] sceneId {total}개를 채우고 저장했습니다 — {string.Join(", ", repaired)}"
                : "[SceneNetworkIdFixer] 비어 있는 sceneId가 없습니다. 빌드 에러가 계속 난다면 Build Settings에 없는 씬일 수 있습니다.");
        }

        // 열려 있는 씬 하나를 훑어 빈 sceneId를 채운다. 채운 개수를 돌려준다.
        private static int FixOpenScene(Scene scene)
        {
            int count = 0;

            // 꺼진 오브젝트가 바로 이 문제의 주인공이라 반드시 비활성 포함으로 찾는다.
            foreach (NetworkIdentity identity in Object.FindObjectsByType<NetworkIdentity>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (identity == null || identity.gameObject.scene != scene) continue;
                if (identity.sceneId != 0) continue;

                if (!TouchToAssignSceneId(identity)) continue;

                Debug.Log($"[SceneNetworkIdFixer] {scene.name} · {identity.name} → sceneId {identity.sceneId:X}", identity);
                count++;
            }

            return count;
        }

        // OnValidate를 돌리기 위한 '값 없는 변경'. serverOnly를 뒤집었다 되돌려 두 번 호출한다
        // (첫 번째에서 Mirror가 sceneId를 만들고, 두 번째는 원래 값으로 복구하면서 그 값을 유지한다).
        private static bool TouchToAssignSceneId(NetworkIdentity identity)
        {
            SerializedObject so = new(identity);
            SerializedProperty serverOnly = so.FindProperty("serverOnly");

            if (serverOnly == null)
            {
                Debug.LogWarning($"[SceneNetworkIdFixer] {identity.name}: NetworkIdentity에 serverOnly 필드가 없습니다 — Mirror 버전을 확인하세요.", identity);
                return false;
            }

            bool original = serverOnly.boolValue;

            serverOnly.boolValue = !original;
            so.ApplyModifiedProperties();

            serverOnly.boolValue = original;
            so.ApplyModifiedProperties();

            so.Update();

            if (identity.sceneId != 0) return true;

            Debug.LogWarning($"[SceneNetworkIdFixer] {identity.name}: sceneId를 채우지 못했습니다. " +
                             "프리팹 편집 모드에서 열려 있거나 프리팹 에셋일 수 있습니다.", identity);
            return false;
        }
    }
}
