using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Playables;
using ProjectS.Enemies;
using ProjectS.Scenes;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// <see cref="BossIntroDirector"/> 인스펙터에 <b>[트랙 자동 할당]</b> 버튼을 단다. 플레이하지 않고 씬에서 바로
    /// Timeline 창을 스크럽해 등장·페이즈 전환 연출을 확인하기 위한 도구다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 필요한가</b>: 보스는 런타임에 스폰되므로 트랙 바인딩이 편집 시점엔 비어 있다(런타임에 RebindTo가 채운다).
    /// 그래서 Timeline 창에서 스크럽해도 보스가 안 움직인다. 이 버튼이 미리보기용 보스를 씬에 임시로 띄우고
    /// 런타임과 <b>같은 규칙(trackBindings)</b>으로 트랙에 꽂는다.
    /// </para>
    /// <para>
    /// 보스 프리팹은 자동으로 찾는다: 씬의 <see cref="EnemySpawnPoint"/> 중 <see cref="Boss"/>가 달린 프리팹.
    /// 전환 연출(Role=PhaseTransition)이면 그 보스의 <see cref="BossPhaseTransition"/>에 연결된 다음 페이즈도 함께 띄운다.
    /// 인스펙터의 "보스 프리팹(수동)"을 채우면 그걸 우선한다.
    /// </para>
    /// <para>
    /// <b>미리보기 보스는 씬에 저장되지 않는다</b>(HideFlags.DontSave). 씬 저장·플레이 진입 직전에 바인딩과 함께 자동으로
    /// 걷어내, 미리보기 바인딩이 씬에 남아 런타임 재바인딩(빈 트랙만 채움)을 가로막는 일이 없게 한다.
    /// </para>
    /// </remarks>
    [CustomEditor(typeof(BossIntroDirector))]
    public class BossIntroDirectorEditor : UnityEditor.Editor
    {
        // 미리보기 오브젝트 표식. HideFlags만으로도 가르지만, 하이어라키에서 눈으로 구분되게 이름에도 붙인다.
        private const string PreviewPrefix = "[BossPreview] ";

        // 수동 지정 프리팹. 인스펙터 세션 동안만 유지한다(씬·프리팹에 저장하지 않는 테스트 입력이라).
        private GameObject manualBossPrefab;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("연출 미리보기 (에디터 전용)", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                manualBossPrefab = (GameObject)EditorGUILayout.ObjectField(
                    new GUIContent("보스 프리팹(수동)", "비우면 씬의 EnemySpawnPoint에서 Boss 프리팹을 자동으로 찾는다."),
                    manualBossPrefab, typeof(GameObject), false);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("트랙 자동 할당")) BindPreview((BossIntroDirector)target);
                    if (GUILayout.Button("할당 해제")) ClearAllPreviews();
                }
            }

            EditorGUILayout.HelpBox("할당 후 Timeline 창에서 이 디렉터를 선택해 스크럽하세요. " +
                                    "미리보기 보스는 저장되지 않으며, 씬 저장·플레이 진입 시 자동으로 해제됩니다.", MessageType.None);
        }

        // ── 할당 ────────────────────────────────────────────────

        private void BindPreview(BossIntroDirector director)
        {
            // 이전 미리보기가 남아 있으면 먼저 걷어낸다(버튼을 여러 번 눌러도 보스가 쌓이지 않게).
            ClearAllPreviews();

            if (!TryFindBossPrefab(director, out GameObject bossPrefab, out Vector3 position, out Quaternion rotation))
            {
                Debug.LogWarning("[BossPreview] 보스 프리팹을 찾지 못했습니다 — 씬의 EnemySpawnPoint에 Boss 프리팹이 연결돼 있는지 보거나, " +
                                 "'보스 프리팹(수동)'을 채우세요.", director);
                return;
            }

            Boss boss = SpawnPreview(bossPrefab, director, position, rotation);
            if (boss == null) return;

            Boss next = null;
            if (director.Role == BossIntroDirector.DirectorRole.PhaseTransition)
            {
                GameObject nextPrefab = FindNextPhasePrefab(bossPrefab);
                if (nextPrefab != null) next = SpawnPreview(nextPrefab, director, position, rotation);
                else Debug.LogWarning($"[BossPreview] '{bossPrefab.name}'의 BossPhaseTransition에 다음 페이즈 프리팹이 없어 " +
                                      "NextPhaseBoss 트랙은 비운 채 둡니다.", bossPrefab);
            }

            director.EditorBindPreview(boss, next);

            // 바뀐 바인딩으로 그래프를 다시 만들고 0초 포즈를 한 번 그려, Timeline 창을 열자마자 결과가 보이게 한다.
            PlayableDirector playable = director.GetComponent<PlayableDirector>();
            if (playable != null)
            {
                playable.RebuildGraph();
                playable.time = 0;
                playable.Evaluate();
            }

            Debug.Log($"[BossPreview] '{director.name}'({director.Role}) 트랙 할당 완료 — 보스='{bossPrefab.name}'" +
                      $"{(next != null ? $", 다음 페이즈='{next.name}'" : "")}.", director);
        }

        // 미리보기 보스를 디렉터와 같은 씬에 띄운다. 프리팹 연결은 유지해 수정 사항이 그대로 반영된 모습으로 본다.
        private static Boss SpawnPreview(GameObject prefab, BossIntroDirector director, Vector3 position, Quaternion rotation)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, director.gameObject.scene);
            instance.name = PreviewPrefix + prefab.name;
            instance.transform.SetPositionAndRotation(position, rotation);

            // 자식까지 전부 표시한다 — 트랙에는 자식(무기·이펙트)이 꽂히므로, 해제할 때 그 대상도 미리보기로 알아봐야 한다.
            foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
                t.gameObject.hideFlags = HideFlags.DontSave;

            Boss boss = instance.GetComponent<Boss>();
            if (boss == null)
            {
                Debug.LogWarning($"[BossPreview] '{prefab.name}'에 Boss 컴포넌트가 없습니다.", prefab);
                Object.DestroyImmediate(instance);
            }

            return boss;
        }

        // ── 보스 프리팹 찾기 ─────────────────────────────────────────

        private bool TryFindBossPrefab(BossIntroDirector director, out GameObject prefab, out Vector3 position, out Quaternion rotation)
        {
            prefab = null;
            position = director.transform.position;
            rotation = director.transform.rotation;

            EnemySpawnPoint bossPoint = null;
            foreach (EnemySpawnPoint point in Object.FindObjectsByType<EnemySpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (point.gameObject.scene != director.gameObject.scene) continue;

                GameObject candidate = EditorAssetOf(point.EnemyRef);
                if (candidate == null || candidate.GetComponent<Boss>() == null) continue;

                bossPoint = point;
                if (manualBossPrefab == null) prefab = candidate;
                break;
            }

            // 위치는 스폰 포인트가 있으면 거기(런타임 스폰 자리), 없으면 디렉터 자리.
            if (bossPoint != null)
            {
                position = bossPoint.Position;
                rotation = bossPoint.Rotation;
            }

            if (manualBossPrefab != null) prefab = manualBossPrefab;
            return prefab != null;
        }

        // 1페이즈 프리팹의 BossPhaseTransition에서 다음 페이즈를 꺼낸다. 필드가 private이라 SerializedObject로 읽는다.
        // 솔로용 어드레서블(nextPhasePrefab)을 우선하고, 없으면 네트워크용 직접 참조로 폴백한다.
        private static GameObject FindNextPhasePrefab(GameObject bossPrefab)
        {
            BossPhaseTransition transition = bossPrefab.GetComponent<BossPhaseTransition>();
            if (transition == null) return null;

            SerializedObject so = new SerializedObject(transition);

            SerializedProperty guid = so.FindProperty("nextPhasePrefab.m_AssetGUID");
            if (guid != null && !string.IsNullOrEmpty(guid.stringValue))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid.stringValue);
                GameObject next = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (next != null) return next;
            }

            SerializedProperty networked = so.FindProperty("networkedNextPhasePrefab");
            return networked != null && networked.objectReferenceValue is Boss nextBoss ? nextBoss.gameObject : null;
        }

        private static GameObject EditorAssetOf(AssetReference reference)
            => reference != null && reference.RuntimeKeyIsValid() ? reference.editorAsset as GameObject : null;

        // ── 해제 ────────────────────────────────────────────────

        /// <summary>열린 씬의 모든 디렉터에서 미리보기 바인딩을 비우고 미리보기 보스를 지운다.</summary>
        private static void ClearAllPreviews()
        {
            foreach (BossIntroDirector director in Object.FindObjectsByType<BossIntroDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                director.EditorUnbindWhere(IsPreview);

            // 루트만 지우면 자식도 함께 사라진다. 순회 중 삭제를 피하려고 먼저 모은다.
            List<GameObject> roots = new();
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.parent == null && IsPreview(t.gameObject) && t.name.StartsWith(PreviewPrefix)) roots.Add(t.gameObject);

            foreach (GameObject root in roots) Object.DestroyImmediate(root);
        }

        // 꽂힌 대상(GameObject 또는 컴포넌트)이 미리보기 보스의 일부인지.
        private static bool IsPreview(Object bound)
        {
            GameObject go = bound as GameObject;
            if (go == null && bound is Component component) go = component.gameObject;
            return go != null && (go.hideFlags & HideFlags.DontSaveInEditor) != 0;
        }

        // 저장·플레이 진입 직전에 자동 해제한다. 미리보기 바인딩이 씬에 저장되거나 플레이 모드로 넘어가면
        // 런타임 재바인딩이 "이미 꽂힌 트랙"으로 보고 건너뛰어, 실제 스폰 보스가 연출대로 안 움직인다.
        [InitializeOnLoadMethod]
        private static void RegisterAutoClear()
        {
            EditorSceneManager.sceneSaving += (_, _) => ClearAllPreviews();
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode) ClearAllPreviews();
            };
        }
    }
}
