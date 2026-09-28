using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// UI 오브젝트 이름 규칙(CLAUDE.md "씬/UI 오브젝트 이름 규칙")을 UI 프리팹과 주요 씬 전체에 일괄 적용하는 배치 진입점.
    /// <see cref="HudConventionUnifier"/> 창 툴은 HUD 씬의 UIManager/HUD 하나만 다루므로, 로그인·캐릭터 선택·부트스트랩
    /// 씬까지 같은 규칙을 한 번에 돌리려고 둔다. 규칙 자체는 <see cref="HudConventionUnifier.RenamePass"/>를 그대로 쓴다.
    ///
    /// 실행: Unity -batchmode -quit -projectPath . -executeMethod ProjectS.EditorTools.UiNamingBatch.Inspect
    ///       (실제 적용은 .Apply). 리포트는 Logs/UiNamingReport.txt 에 남는다.
    ///
    /// 프리팹 에셋을 먼저 고친다 — 씬의 프리팹 인스턴스 내부 노드는 에셋 쪽 변경을 그대로 물려받게 하고,
    /// 씬에서는 인스턴스 루트와 씬 고유 노드만 바꾼다. 씬에서 내부 노드를 바꾸면 이름 오버라이드가 쌓여
    /// 이후 프리팹을 고쳐도 따라오지 않게 된다.
    /// </summary>
    public static class UiNamingBatch
    {
        private const string ReportPath = "Logs/UiNamingReport.txt";
        private const string UiPrefabFolder = "Assets/Prefabs/UI";

        private static readonly string[] Scenes =
        {
            "Assets/Scenes/Bootstrap.unity",
            "Assets/Scenes/Login.unity",
            "Assets/Scenes/CharacterSelect.unity",
            "Assets/Scenes/TH/HUD(TH) 2.unity",
        };

        /// <summary>변경 없이 리포트만 만든다.</summary>
        public static void Inspect() => Run(false);

        /// <summary>프리팹 에셋과 씬에 실제로 적용하고 저장한다. 되돌리기는 git으로 한다.</summary>
        public static void Apply() => Run(true);

        /// <summary>
        /// 빌드 설정의 모든 씬을 검사만 한다(적용 없음). <see cref="Scenes"/> 밖의 씬은 다른 팀원 작업 영역이라
        /// 일괄 적용 대상에서 뺐고, 여기서 리포트로만 확인한 뒤 담당자가 직접 정리한다.
        /// </summary>
        public static void InspectBuildScenes()
        {
            var log = new StringBuilder();
            int total = 0;
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            {
                if (Scenes.Contains(s.path)) continue;
                Scene scene = EditorSceneManager.OpenScene(s.path, OpenSceneMode.Single);
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    var one = new StringBuilder();
                    int n = HudConventionUnifier.RenamePass(root.transform, false, one, $"{s.path} :: {root.name}", IsOwnedNode);
                    if (n == 0) continue;
                    total += n;
                    log.Append(one);
                }
            }
            log.Insert(0, $"UI 이름 규칙 검사(그 외 빌드 씬) — 총 {total}건\n\n");
            System.IO.File.WriteAllText(ReportPath, log.ToString(), new UTF8Encoding(false));
            Debug.Log($"[UiNamingBatch] 그 외 빌드 씬 검사 완료: {total}건 → {ReportPath}");
        }

        private static void Run(bool apply)
        {
            var log = new StringBuilder();
            int total = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { UiPrefabFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var one = new StringBuilder();
                    // 루트 이름은 에셋 파일 이름을 따라가므로 건드리지 않는다.
                    int n = HudConventionUnifier.RenamePass(root.transform, apply, one, path,
                                                            t => t != root.transform && IsOwnedNode(t));
                    if (n == 0 && !one.ToString().Contains("[보호]")) continue;
                    total += n;
                    log.Append(one);
                    if (apply && n > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            foreach (string scenePath in Scenes)
            {
                Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                int sceneCount = 0;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    var one = new StringBuilder();
                    int n = HudConventionUnifier.RenamePass(root.transform, apply, one, $"{scenePath} :: {root.name}",
                                                            IsOwnedNode);
                    if (n == 0 && !one.ToString().Contains("[보호]")) continue;
                    sceneCount += n;
                    log.Append(one);
                }
                total += sceneCount;
                if (apply && sceneCount > 0) EditorSceneManager.SaveScene(scene);
            }

            log.Insert(0, $"UI 이름 규칙 {(apply ? "적용" : "검사")} — 총 {total}건\n\n");
            System.IO.File.WriteAllText(ReportPath, log.ToString(), new UTF8Encoding(false));
            Debug.Log($"[UiNamingBatch] {(apply ? "적용" : "검사")} 완료: {total}건 → {ReportPath}");
        }

        /// <summary>
        /// 이 파일(씬/프리팹)이 이름을 직접 소유한 노드인가. 중첩 프리팹 내부 노드는 그 프리팹 에셋이 소유하므로 제외하고,
        /// 인스턴스 루트는 배치한 쪽이 이름을 정하므로 포함한다.
        /// </summary>
        private static bool IsOwnedNode(Transform t)
        {
            GameObject go = t.gameObject;
            if (!PrefabUtility.IsPartOfPrefabInstance(go)) return true;
            return PrefabUtility.GetNearestPrefabInstanceRoot(go) == go;
        }
    }
}
