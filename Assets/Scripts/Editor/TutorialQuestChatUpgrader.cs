using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// Tutorial 씬의 NPC 대화·퀘스트 UI를 <b>최신 QuestChat 프리팹 구성</b>으로 올리는 일회성 에디터 툴.
    /// 메뉴: Tools ▸ ProjectS ▸ Upgrade Tutorial QuestChat UI
    ///
    /// <b>무엇이 구버전인가</b> — VillageGather는 <c>InteractionUI</c> 아래 DialogueBox·HubPanel·QuestListPanel
    /// 세 패널을 모두 <c>Assets/Prefabs/UI/QuestChat.prefab</c> 인스턴스(이름만 오버라이드)로 쓰는데,
    /// Tutorial은 같은 자리에 <b>손으로 복사한 인라인 사본</b>을 갖고 있다. 사본은 프리팹이 갱신돼도 따라오지 않아
    /// 아트가 한 세대 뒤처져 있다(신: Background·NpcStanding·TalkVignette·DecoL/DecoR / 구: Bg·NpcStaning·Gloss).
    /// 배선도 갈렸다 — <c>NpcQuestListView.rowPrefab</c>이 Tutorial에선 구 QuestEnumSlot.prefab 에셋을,
    /// 마을에선 프리팹 안의 QuestEnumSlot00을 가리킨다.
    ///
    /// <b>왜 마을 씬을 읽어오는가</b> — 새 프리팹은 노드 이름이 바뀌어서(Bg→Background 등) 구 사본의 경로를
    /// 그대로 따라갈 수 없다. 마을은 <b>이미 같은 프리팹으로 올바르게 배선된 정답지</b>이므로, 마을 컴포넌트의
    /// 값을 프로퍼티 단위로 복사하고 씬 오브젝트 참조만 Tutorial 쪽 같은 경로로 바꿔치면
    /// 이름 매핑표 없이 배선이 끝난다. 마을 씬은 읽기만 하고 저장하지 않는다.
    ///
    /// <b>왜 코드가 아니라 에디터 툴인가</b> — 프리팹 인스턴스 교체는 fileID와 m_Modifications를 새로 만드는
    /// 일이라 .unity(YAML) 텍스트 편집으로는 재현되지 않는다. <see cref="HudConventionUnifier"/>와 같은 이유다.
    ///
    /// <b>이 툴이 일부러 건드리지 않는 것</b>
    ///   • <c>NpcRewardView</c> — <b>마을 씬도 아직 구버전 인라인</b>이라 여기만 프리팹화하면 오히려 두 씬이
    ///     갈린다. 마을과 함께 옮겨야 하는 별건이다. (두 씬의 이 서브트리는 경로 88개가 완전히 동일해서,
    ///     이 패널을 가리키는 참조는 경로 재연결로 그대로 살아난다.)
    ///   • 씬의 다른 UI(GuideUI·Tutorialmonologue 등) — 이미 프리팹 인스턴스다.
    ///
    /// <b>안전장치</b> — [검사]가 먼저다. 씬을 바꾸지 않고 무엇이 교체·재연결되는지 리포트만 뽑는다.
    /// [적용]은 Tutorial 씬만 저장한다. 되돌리려면 git으로 Tutorial.unity를 되돌리는 것이 가장 확실하다
    /// (씬을 새로 여는 툴이라 Ctrl+Z에 기대지 말 것). <b>커밋이 깨끗한 상태에서 실행할 것.</b>
    /// </summary>
    public class TutorialQuestChatUpgrader : EditorWindow
    {
        private const string TutorialScenePath = "Assets/Scenes/Tutorial.unity";
        private const string VillageScenePath = "Assets/Scenes/VillageGather.unity";
        private const string QuestChatPrefabPath = "Assets/Prefabs/UI/QuestChat.prefab";

        // 실제 오브젝트 이름 끝에 공백이 붙어 있다('InteractionUI '). 이름 비교는 전부 Trim으로 한다.
        private const string InteractionRootName = "InteractionUI";

        // 교체 대상. NpcRewardView는 위 주석의 이유로 제외한다.
        private static readonly string[] PanelNames = { "DialogueBox", "HubPanel", "QuestListPanel" };

        private Vector2 scroll;
        private string report = "[검사]를 눌러 무엇이 바뀌는지 먼저 확인하세요.";

        [MenuItem("Tools/ProjectS/Upgrade Tutorial QuestChat UI")]
        private static void Open()
        {
            var w = GetWindow<TutorialQuestChatUpgrader>("튜토리얼 대화 UI 최신화");
            w.minSize = new Vector2(640f, 480f);
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Tutorial 씬의 DialogueBox / HubPanel / QuestListPanel 을 최신 QuestChat.prefab 인스턴스로 교체하고,\n" +
                "배선은 VillageGather 씬의 같은 컴포넌트에서 가져옵니다. NpcRewardView는 건드리지 않습니다.\n\n" +
                "두 씬을 모두 열고 Tutorial 씬을 저장합니다. 반드시 커밋이 깨끗한 상태에서 [검사] 후 [적용]하세요.",
                MessageType.Info);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("검사 (변경 없음)", GUILayout.Height(30f))) Run(false);
                Color bg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.75f, 0.55f);
                if (GUILayout.Button("적용", GUILayout.Height(30f))) Run(true);
                GUI.backgroundColor = bg;
            }

            EditorGUILayout.Space();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        // ════════════════════════════════════════════════════════════════════════
        //  실행
        // ════════════════════════════════════════════════════════════════════════

        private void Run(bool apply)
        {
            var log = new StringBuilder();
            log.AppendLine(apply ? "=== 적용 ===" : "=== 검사 (변경 없음) ===");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(QuestChatPrefabPath);
            if (prefab == null)
            {
                report = "[중단] 프리팹을 찾지 못했습니다: " + QuestChatPrefabPath;
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                report = "[중단] 열려 있던 씬 저장이 취소되었습니다.";
                return;
            }

            Scene tutorial;
            Scene village;
            try
            {
                tutorial = EditorSceneManager.OpenScene(TutorialScenePath, OpenSceneMode.Single);
                village = EditorSceneManager.OpenScene(VillageScenePath, OpenSceneMode.Additive);
            }
            catch (Exception e)
            {
                report = "[중단] 씬을 여는 데 실패했습니다.\n" + e.Message;
                return;
            }

            try
            {
                Transform tutorialRoot = FindRoot(tutorial, InteractionRootName);
                Transform villageRoot = FindRoot(village, InteractionRootName);
                if (tutorialRoot == null || villageRoot == null)
                {
                    log.AppendLine($"[중단] '{InteractionRootName}' 루트를 찾지 못했습니다 " +
                                   $"(Tutorial: {tutorialRoot != null}, VillageGather: {villageRoot != null}).");
                    return;
                }

                Upgrade(apply, prefab, tutorialRoot, villageRoot, village, log);

                if (apply)
                {
                    EditorSceneManager.MarkSceneDirty(tutorial);
                    EditorSceneManager.SaveScene(tutorial);
                    log.AppendLine();
                    log.AppendLine("[저장] " + TutorialScenePath);
                    log.AppendLine("Unity에서 Tutorial 씬을 플레이해 NPC 대화 → 퀘스트 목록 → 허브 버튼 → Skip → 보상 수령까지 확인하세요.");
                }
                else
                {
                    log.AppendLine();
                    log.AppendLine("변경된 것은 없습니다. 위 내용이 맞으면 [적용]을 누르세요.");
                }
            }
            catch (Exception e)
            {
                log.AppendLine();
                log.AppendLine("[예외] " + e);
            }
            finally
            {
                // 마을 씬은 참조용으로만 열었다. 저장하지 않고 닫는다.
                if (village.isLoaded) EditorSceneManager.CloseScene(village, true);
                report = log.ToString();
                Repaint();
            }
        }

        private void Upgrade(bool apply, GameObject prefab, Transform tutorialRoot, Transform villageRoot,
                             Scene villageScene, StringBuilder log)
        {
            // ── 1. 대상 패널 확인 ────────────────────────────────────────────────
            var oldPanels = new Dictionary<string, Transform>();
            var villagePanels = new Dictionary<string, Transform>();
            foreach (string name in PanelNames)
            {
                Transform o = FindChild(tutorialRoot, name);
                Transform v = FindChild(villageRoot, name);
                if (o == null) { log.AppendLine($"[중단] Tutorial에 '{name}' 패널이 없습니다."); return; }
                if (v == null) { log.AppendLine($"[중단] VillageGather에 '{name}' 패널이 없습니다."); return; }

                GameObject vSource = PrefabUtility.GetCorrespondingObjectFromSource(v.gameObject);
                bool vIsQuestChat = vSource != null && AssetDatabase.GetAssetPath(vSource) == QuestChatPrefabPath;
                if (!vIsQuestChat)
                {
                    log.AppendLine($"[중단] VillageGather의 '{name}'이 QuestChat.prefab 인스턴스가 아닙니다. " +
                                   "정답지로 쓸 수 없으니 마을 씬 상태를 먼저 확인하세요.");
                    return;
                }
                if (PrefabUtility.GetCorrespondingObjectFromSource(o.gameObject) != null)
                {
                    log.AppendLine($"[건너뜀] Tutorial의 '{name}'은 이미 프리팹 인스턴스입니다 — 교체할 것이 없습니다.");
                    return;
                }

                oldPanels[name] = o;
                villagePanels[name] = v;
                log.AppendLine($"[교체 대상] {name} — 인라인 사본 {CountDescendants(o)}개 노드 → QuestChat.prefab 인스턴스");
            }

            // ── 2. 구 패널 바깥에서 들어오는 참조를 경로로 기록 ───────────────────
            // 패널을 지우면 이 참조들이 null이 된다. 새 인스턴스에서 같은 상대 경로로 되살린다.
            List<ExternalRef> externals = CollectExternalRefs(tutorialRoot, oldPanels, log);

            if (!apply)
            {
                LogPlannedRewire(tutorialRoot, villageRoot, villagePanels, log);
                return;
            }

            // ── 3. 새 인스턴스 생성 (마을 인스턴스의 오버라이드를 그대로 복제) ────
            var newPanels = new Dictionary<string, Transform>();
            foreach (string name in PanelNames)
            {
                Transform oldPanel = oldPanels[name];
                Transform villagePanel = villagePanels[name];

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, tutorialRoot.gameObject.scene);
                Undo.RegisterCreatedObjectUndo(instance, "Create QuestChat instance");
                instance.transform.SetParent(tutorialRoot, false);

                // 마을 인스턴스의 프로퍼티 오버라이드(이름·RectTransform·활성 상태·자식별 조정)를 통째로 가져온다.
                // 이 단계에서 씬 오브젝트를 가리키는 오버라이드는 아직 마을 쪽을 가리킨다 — 5단계에서 일괄 교정한다.
                PropertyModification[] mods = PrefabUtility.GetPropertyModifications(villagePanel.gameObject);
                if (mods != null) PrefabUtility.SetPropertyModifications(instance, mods);

                instance.name = oldPanel.name;                                   // 오버라이드에 이름이 없을 때를 대비
                instance.transform.SetSiblingIndex(oldPanel.GetSiblingIndex());  // 렌더 순서 보존
                newPanels[name] = instance.transform;

                log.AppendLine($"[생성] {name} (sibling {oldPanel.GetSiblingIndex()}, " +
                               $"오버라이드 {(mods == null ? 0 : mods.Length)}개 복제)");
            }

            // ── 4. 구 패널 제거 ─────────────────────────────────────────────────
            foreach (string name in PanelNames)
            {
                Undo.DestroyObjectImmediate(oldPanels[name].gameObject);
                log.AppendLine("[삭제] 구 " + name);
            }

            // ── 5. 배선: 마을 컴포넌트 값 복사 + 씬 참조 재해석 ──────────────────
            CopyViewWiring(tutorialRoot, villageRoot, log);
            int remapped = RemapVillageRefs(tutorialRoot, villageRoot, villageScene, log);
            log.AppendLine($"[재연결] 마을 씬을 가리키던 참조 {remapped}개를 Tutorial 쪽으로 옮겼습니다.");

            // ── 6. 바깥 참조 복구 ───────────────────────────────────────────────
            RestoreExternalRefs(externals, newPanels, log);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  배선
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// InteractionUI 루트에 붙은 뷰 컴포넌트(DialogueManager·NpcHubView·NpcQuestListView 등)의 값을
        /// 마을 쪽에서 프로퍼티 단위로 복사한다. 에셋 참조(스프라이트 등)와 일반 값은 그대로 오고,
        /// 씬 오브젝트 참조는 마을 오브젝트를 가리킨 채 들어와 <see cref="RemapVillageRefs"/>에서 교정된다.
        /// 지금 스크립트에 없는 필드(구 버전 잔재)는 직렬화 프로퍼티로 잡히지 않아 자연히 빠진다.
        /// </summary>
        private static void CopyViewWiring(Transform tutorialRoot, Transform villageRoot, StringBuilder log)
        {
            foreach (Component villageComp in villageRoot.GetComponents<Component>())
            {
                if (villageComp == null) continue;
                if (villageComp is Transform) continue;   // RectTransform도 Transform이다

                Component tutorialComp = tutorialRoot.GetComponent(villageComp.GetType());
                if (tutorialComp == null)
                {
                    log.AppendLine($"[주의] Tutorial 루트에 {villageComp.GetType().Name}가 없어 배선을 건너뜁니다.");
                    continue;
                }

                var src = new SerializedObject(villageComp);
                var dst = new SerializedObject(tutorialComp);
                SerializedProperty p = src.GetIterator();
                int copied = 0;
                bool enterChildren = true;
                while (p.NextVisible(enterChildren))
                {
                    enterChildren = false;                 // 최상위만 돌면 하위 값은 통째로 따라온다
                    if (p.propertyPath == "m_Script") continue;
                    dst.CopyFromSerializedProperty(p);
                    copied++;
                }
                dst.ApplyModifiedPropertiesWithoutUndo();
                log.AppendLine($"[배선] {villageComp.GetType().Name} — 프로퍼티 {copied}개를 마을에서 복사");
            }
        }

        /// <summary>
        /// Tutorial의 InteractionUI 서브트리 안에서 <b>마을 씬 오브젝트를 가리키는 참조</b>를 찾아
        /// 같은 상대 경로의 Tutorial 오브젝트로 바꾼다. 마을과 Tutorial의 InteractionUI는 이제 같은 구조이므로
        /// 경로가 일대일로 맞는다(NpcRewardView는 양쪽 모두 구 사본이라 경로 88개가 동일).
        /// </summary>
        private static int RemapVillageRefs(Transform tutorialRoot, Transform villageRoot, Scene villageScene,
                                            StringBuilder log)
        {
            int count = 0;
            foreach (Component c in tutorialRoot.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var so = new SerializedObject(c);
                SerializedProperty p = so.GetIterator();
                bool changed = false;
                while (p.Next(true))
                {
                    if (p.propertyType != SerializedPropertyType.ObjectReference) continue;
                    UnityEngine.Object v = p.objectReferenceValue;
                    if (v == null || !InScene(v, villageScene)) continue;

                    string why;
                    UnityEngine.Object resolved = Resolve(v, villageRoot, tutorialRoot, out why);
                    if (resolved == null)
                    {
                        log.AppendLine($"[미해결] {Describe(c)} .{p.propertyPath} → 마을의 {v.name} ({why}). 손으로 연결하세요.");
                        continue;
                    }
                    p.objectReferenceValue = resolved;
                    changed = true;
                    count++;
                }
                if (changed) so.ApplyModifiedPropertiesWithoutUndo();
            }
            return count;
        }

        /// <summary>마을 오브젝트를 상대 경로·컴포넌트 타입·같은 타입 내 순번으로 Tutorial 쪽에 대응시킨다.</summary>
        private static UnityEngine.Object Resolve(UnityEngine.Object villageObj, Transform villageRoot,
                                                  Transform tutorialRoot, out string why)
        {
            why = null;
            Transform vt = villageObj is Component vc ? vc.transform
                         : villageObj is GameObject vg ? vg.transform
                         : null;
            if (vt == null) { why = "GameObject/Component가 아님"; return null; }

            string path = RelativePath(villageRoot, vt);
            if (path == null) { why = "InteractionUI 바깥"; return null; }

            Transform tt = ByRelativePath(tutorialRoot, path);
            if (tt == null) { why = $"Tutorial에 경로 '{path}' 없음"; return null; }

            if (villageObj is GameObject) return tt.gameObject;

            var comp = (Component)villageObj;
            Type type = comp.GetType();
            Component[] villageAll = comp.gameObject.GetComponents(type);
            Component[] tutorialAll = tt.GetComponents(type);
            int index = Array.IndexOf(villageAll, comp);
            if (index < 0 || index >= tutorialAll.Length) { why = $"{type.Name} 순번 {index} 없음"; return null; }
            return tutorialAll[index];
        }

        // ════════════════════════════════════════════════════════════════════════
        //  구 패널 바깥에서 들어오던 참조
        // ════════════════════════════════════════════════════════════════════════

        private sealed class ExternalRef
        {
            public Component Owner;
            public string PropertyPath;
            public string Panel;
            public string RelativePath;
            public Type ComponentType;   // null이면 GameObject 참조
            public int ComponentIndex;
        }

        private static List<ExternalRef> CollectExternalRefs(Transform tutorialRoot,
                                                            Dictionary<string, Transform> oldPanels,
                                                            StringBuilder log)
        {
            var result = new List<ExternalRef>();
            var panelOf = new Dictionary<Transform, string>();
            foreach (KeyValuePair<string, Transform> kv in oldPanels)
                foreach (Transform t in kv.Value.GetComponentsInChildren<Transform>(true))
                    panelOf[t] = kv.Key;

            foreach (GameObject root in tutorialRoot.gameObject.scene.GetRootGameObjects())
            {
                foreach (Component c in root.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) continue;
                    if (panelOf.ContainsKey(c.transform)) continue;   // 패널 안쪽은 통째로 사라지므로 대상 아님

                    var so = new SerializedObject(c);
                    SerializedProperty p = so.GetIterator();
                    while (p.Next(true))
                    {
                        if (p.propertyType != SerializedPropertyType.ObjectReference) continue;
                        UnityEngine.Object v = p.objectReferenceValue;
                        if (v == null) continue;

                        Transform vt = v is Component vc ? vc.transform : v is GameObject vg ? vg.transform : null;
                        string panel;
                        if (vt == null || !panelOf.TryGetValue(vt, out panel)) continue;

                        Type type = v is GameObject ? null : v.GetType();
                        int idx = type == null ? 0 : Array.IndexOf(vt.GetComponents(type), (Component)v);
                        string rel = RelativePath(oldPanels[panel], vt);
                        result.Add(new ExternalRef
                        {
                            Owner = c,
                            PropertyPath = p.propertyPath,
                            Panel = panel,
                            RelativePath = rel,
                            ComponentType = type,
                            ComponentIndex = Math.Max(idx, 0),
                        });
                        log.AppendLine($"[바깥 참조] {Describe(c)} .{p.propertyPath} → {panel}/{rel}" +
                                       (type != null ? $" ({type.Name})" : " (GameObject)"));
                    }
                }
            }
            if (result.Count == 0) log.AppendLine("[바깥 참조] 없음");
            return result;
        }

        private static void RestoreExternalRefs(List<ExternalRef> refs, Dictionary<string, Transform> newPanels,
                                                StringBuilder log)
        {
            foreach (ExternalRef r in refs)
            {
                if (r.Owner == null) continue;

                Transform t = ByRelativePath(newPanels[r.Panel], r.RelativePath);
                if (t == null)
                {
                    log.AppendLine($"[미해결] {Describe(r.Owner)} .{r.PropertyPath} — 새 {r.Panel}에 " +
                                   $"경로 '{r.RelativePath}'가 없습니다(프리팹에서 이름이 바뀐 노드일 수 있음). 손으로 연결하세요.");
                    continue;
                }

                UnityEngine.Object value;
                if (r.ComponentType == null)
                {
                    value = t.gameObject;
                }
                else
                {
                    Component[] all = t.GetComponents(r.ComponentType);
                    if (r.ComponentIndex >= all.Length)
                    {
                        log.AppendLine($"[미해결] {Describe(r.Owner)} .{r.PropertyPath} — " +
                                       $"'{r.RelativePath}'에 {r.ComponentType.Name}가 없습니다. 손으로 연결하세요.");
                        continue;
                    }
                    value = all[r.ComponentIndex];
                }

                var so = new SerializedObject(r.Owner);
                SerializedProperty p = so.FindProperty(r.PropertyPath);
                if (p == null)
                {
                    log.AppendLine($"[미해결] {Describe(r.Owner)} .{r.PropertyPath} 프로퍼티를 찾지 못했습니다.");
                    continue;
                }
                p.objectReferenceValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
                log.AppendLine($"[복구] {Describe(r.Owner)} .{r.PropertyPath} → {r.Panel}/{r.RelativePath}");
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  검사 모드 리포트
        // ════════════════════════════════════════════════════════════════════════

        private static void LogPlannedRewire(Transform tutorialRoot, Transform villageRoot,
                                             Dictionary<string, Transform> villagePanels, StringBuilder log)
        {
            log.AppendLine();
            log.AppendLine("--- 마을에서 가져올 배선 ---");
            foreach (Component villageComp in villageRoot.GetComponents<Component>())
            {
                if (villageComp == null || villageComp is Transform) continue;
                Component tutorialComp = tutorialRoot.GetComponent(villageComp.GetType());
                log.AppendLine(tutorialComp == null
                    ? $"[주의] Tutorial 루트에 {villageComp.GetType().Name} 없음 — 건너뜁니다."
                    : $"[배선 예정] {villageComp.GetType().Name}");
            }

            log.AppendLine();
            log.AppendLine("--- 복제할 프리팹 오버라이드 ---");
            foreach (KeyValuePair<string, Transform> kv in villagePanels)
            {
                PropertyModification[] mods = PrefabUtility.GetPropertyModifications(kv.Value.gameObject);
                log.AppendLine($"{kv.Key}: {(mods == null ? 0 : mods.Length)}개");
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  경로 유틸 (이름 끝 공백 때문에 비교는 전부 Trim)
        // ════════════════════════════════════════════════════════════════════════

        private static Transform FindRoot(Scene scene, string name)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name.Trim() == name) return go.transform;
            return null;
        }

        private static Transform FindChild(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform c = parent.GetChild(i);
                if (c.name.Trim() == name) return c;
            }
            return null;
        }

        /// <summary>root 기준 상대 경로. root 자신이면 빈 문자열, root 바깥이면 null.</summary>
        private static string RelativePath(Transform root, Transform t)
        {
            if (t == root) return string.Empty;
            var parts = new List<string>();
            for (Transform c = t; c != null; c = c.parent)
            {
                if (c == root)
                {
                    parts.Reverse();
                    return string.Join("/", parts);
                }
                parts.Add(c.name.Trim());
            }
            return null;
        }

        private static Transform ByRelativePath(Transform root, string path)
        {
            if (string.IsNullOrEmpty(path)) return root;
            Transform cur = root;
            foreach (string seg in path.Split('/'))
            {
                cur = FindChild(cur, seg);
                if (cur == null) return null;
            }
            return cur;
        }

        private static bool InScene(UnityEngine.Object o, Scene scene)
        {
            if (o is Component c) return c.gameObject.scene == scene;
            if (o is GameObject g) return g.scene == scene;
            return false;
        }

        private static int CountDescendants(Transform t) => t.GetComponentsInChildren<Transform>(true).Length;

        private static string Describe(Component c) =>
            c == null ? "(없음)" : $"{c.gameObject.name}/{c.GetType().Name}";
    }
}
