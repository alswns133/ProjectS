using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// HUD 씬의 UI 계층을 <b>Enhance 팝업 컨벤션</b>으로 통일하는 에디터 툴.
    /// 메뉴: Tools ▸ ProjectS ▸ Unify HUD Conventions (Enhance)
    ///
    /// Enhance(가장 최근에 만든 팝업)가 쓰는 규칙을 프로젝트 표준으로 확정하고, 그 이전 세대 화면
    /// (Inventory·Status·Store·Skill·Options·QuestChat 등)을 같은 규칙으로 맞춘다. 규칙은 셋이다.
    ///   1) 배경은 <c>Background</c>, 타이틀 그라디언트는 <c>TitleGradient</c>, 접두사(SPR_/PT_/ICN)는 쓰지 않는다.
    ///   2) 형제 반복 요소는 <c>Base00, Base01…</c> 2자리 제로패딩. Unity 기본 <c>Base (1)</c>은 쓰지 않는다.
    ///   3) 손으로 복사한 반복 요소는 프리팹 인스턴스로 되돌린다.
    ///
    /// <b>2026-08-20 확정된 세 가지 (Enhance를 기준으로 삼되, 배경 이름만 의도적으로 예외)</b>
    ///   • <b>배경은 <c>Background</c></b> — Enhance는 <c>Bg</c>를 쓰지만 그쪽을 표준으로 삼지 않았다.
    ///     Unity가 Slider 등에 자동 생성하는 노드 이름이 <c>Background</c>라, <c>Bg</c>를 택하면
    ///     우리가 손댈 수 없는 Unity 노드와 우리 노드가 <b>영구히 두 이름으로 갈린다</b>.
    ///     <c>Background</c>로 통일하면 예외 없이 한 이름이 된다. 또 Enhance 어휘(Title·Body·Frame·
    ///     Preview·Divider) 중 줄임말은 <c>Bg</c> 하나뿐이라 그것만 튀는 문제도 사라진다.
    ///   • <b><c>Num</c>은 그대로 둔다</b> — CooldownNum·SliderNum·LvNum 등. 관용적으로 읽히고,
    ///     <c>Number</c>로 펼쳐서 얻는 것이 없다. HP·SG·EXP·SP·PCB 같은 도메인 약어도 마찬가지.
    ///   • <b>PascalCase(띄어쓰기·언더바 없음)</b> — 단어 구분은 오직 대소문자로 한다.
    ///     <c>Left_Material</c> → <c>LeftMaterial</c>, <c>Skill_01</c> → <c>Skill01</c>.
    ///     오브젝트 이름은 애니메이션 클립의 커브 경로
    ///     ("Parent/Child")로 쓰이므로 공백이 <b>보이지 않는 버그</b>가 된다. 실제로 이 프로젝트에
    ///     InventoryItemSlot.prefab의 <c>'Text '</c>, QuestEnumSlot.prefab의 <c>'exclamation mark '</c>가
    ///     끝에 공백이 붙은 채 존재한다(Hierarchy에서 구별 불가). 통계로도 띄어쓰기 있는 이름은 27%가
    ///     표기가 무너져 있었고, 없는 이름은 2%였다.
    ///
    /// <b>왜 코드가 아니라 에디터 툴인가</b> — 씬 .unity(YAML)를 밖에서 고치면 Unity가 열어둔 씬을 저장할 때
    /// 통째로 덮어쓴다. 또 프리팹 교체는 fileID와 m_Modifications를 새로 만드는 일이라 텍스트 편집으로는 재현이 안 된다.
    ///
    /// <b>안전장치</b> — [검사]가 먼저다. 실제 변경 없이 리포트만 뽑아 눈으로 확인한 뒤 [적용]을 누른다.
    /// 씬 변경은 Undo 그룹 하나로 묶여 Ctrl+Z 한 번에 되돌아간다.
    /// (다만 '프리팹 에셋에도 적용' 옵션은 에셋 파일을 직접 저장하므로 Undo가 되지 않는다 — 커밋 후 실행할 것.)
    ///
    /// <b>이 툴이 일부러 건드리지 않는 것</b> (자동 판단이 위험한 것들)
    ///   • Slider·Scrollbar가 스스로 만드는 내부 구조(Fill Area / Handle Slide Area / Sliding Area) —
    ///     새 위젯을 만들 때마다 Unity가 같은 이름으로 다시 만들어 어차피 되돌아온다. <see cref="Untouchable"/> 참고.
    ///     (Slider의 Background는 이제 표준 이름과 같아져 손댈 일이 없다.)
    ///   • <c>FlashImage</c> — TV_PowerOn.anim이 이 이름을 <b>경로로</b> 애니메이션한다. 바뀌면 조용히 멈춘다.
    ///   • <c>QuestEnumSlot</c> 프리팹화 — 씬 사본(Image+Button+Mask, 자식 평평)과 QuestEnumSlot.prefab
    ///     (Image+Mask+NpcQuestListRow+Selectable, Quest 래퍼)은 이름만 같고 구조가 다른 별개 물건이다.
    ///   • Left/Right/Center 존 오프셋 정규화 — Center 한 존에 창 4개가 서로 다른 오프셋으로 들어있어
    ///     "0으로 맞춘다"가 정의되지 않는다. 레이아웃 재설계가 필요한 별건이다.
    ///   • <c>Skill_01~04</c> / <c>Potion_00~01</c>의 자기 번호 — 슬롯 번호가 Q/E/R 키 배치와 묶여 있어
    ///     프리팹 이름으로 갈아끼우면 의미가 사라진다. <see cref="KeepOwnBaseName"/> 참고.
    /// </summary>
    public class HudConventionUnifier : EditorWindow
    {
        private const string UndoLabel = "Unify HUD Conventions";
        private const string UiManagerName = "UIManager";
        private const string HudCanvasName = "HUD";

        private const string SkillSlotPrefabPath = "Assets/Prefabs/UI/SkillSlot.prefab";
        private const string XBoxPrefabPath = "Assets/Prefabs/UI/XBox.prefab";
        private const string UiPrefabFolder = "Assets/Prefabs/UI";

        // ── 옵션 ────────────────────────────────────────────────────────────────
        private bool doRename = true;
        private bool doRestructure = true;
        private bool doPrefab = true;
        private bool alsoPrefabAssets = false;
        private bool keepColorOverrides = true;

        private Vector2 scroll;
        private string report = "[검사]를 눌러 무엇이 바뀌는지 먼저 확인하세요.";

        [MenuItem("Tools/ProjectS/Unify HUD Conventions (Enhance)")]
        private static void Open()
        {
            var w = GetWindow<HudConventionUnifier>("HUD 규칙 통일");
            w.minSize = new Vector2(620f, 460f);
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "HUD 씬의 UI를 Enhance 팝업 컨벤션으로 통일합니다.\n" +
                "반드시 [검사]로 리포트를 확인한 뒤 [적용]을 누르세요. 씬 변경은 Ctrl+Z 한 번에 되돌아갑니다.",
                MessageType.Info);

            EditorGUILayout.Space();
            doRename = EditorGUILayout.ToggleLeft("1. 이름 표기 통일 (Bg/BG→Background, 접두사 제거, (1)→00, 공백·언더바 제거)", doRename);
            using (new EditorGUI.DisabledScope(!doRename))
            {
                EditorGUI.indentLevel++;
                alsoPrefabAssets = EditorGUILayout.ToggleLeft(
                    "Assets/Prefabs/UI 의 프리팹 에셋에도 같은 규칙 적용 (Undo 불가)", alsoPrefabAssets);
                EditorGUI.indentLevel--;
            }

            doRestructure = EditorGUILayout.ToggleLeft("2. 구조 재배치 (Notice를 HUD 직속으로, Vignette 순서 교정)", doRestructure);
            doPrefab = EditorGUILayout.ToggleLeft("3. 손복사본 → 프리팹 인스턴스 (Skill_02~04, XBox)", doPrefab);
            using (new EditorGUI.DisabledScope(!doPrefab))
            {
                EditorGUI.indentLevel++;
                keepColorOverrides = EditorGUILayout.ToggleLeft(
                    "교체 시 기존 색상을 오버라이드로 보존 (끄면 프리팹 색으로 통일)", keepColorOverrides);
                EditorGUI.indentLevel--;
            }

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
            Transform hud = FindHudRoot();
            if (hud == null)
            {
                report = $"[중단] 열린 씬에서 '{UiManagerName}/{HudCanvasName}'를 찾지 못했습니다.\n" +
                         "HUD(TH) 2 씬을 연 상태에서 실행하세요.";
                return;
            }

            int undoGroup = Undo.GetCurrentGroup();
            if (apply) Undo.SetCurrentGroupName(UndoLabel);

            try
            {
                // 프리팹 교체를 먼저 한다. 교체로 새로 들어온 프리팹 자식들도 이름 통일 대상이 되어야 하고,
                // 반대 순서로 하면 방금 붙인 이름이 프리팹 구조로 덮여 사라진다.
                if (doPrefab) PrefabPass(hud, apply, log);
                if (doRestructure) RestructurePass(hud, apply, log);
                if (doRename)
                {
                    RenamePass(hud, apply, log, "씬");
                    if (alsoPrefabAssets) PrefabAssetRenamePass(apply, log);
                }

                if (apply)
                {
                    Undo.CollapseUndoOperations(undoGroup);
                    EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
                    log.AppendLine();
                    log.AppendLine("── 적용 완료. 결과를 확인하고 씬을 저장하세요 (Ctrl+S). 되돌리려면 Ctrl+Z.");
                }
                else
                {
                    log.AppendLine();
                    log.AppendLine("── 검사만 했습니다. 실제로는 아무것도 바뀌지 않았습니다.");
                }
            }
            catch (Exception e)
            {
                log.AppendLine();
                log.AppendLine("[예외] " + e);
            }

            report = log.ToString();
            Debug.Log(report);
        }

        private static Transform FindHudRoot()
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name != UiManagerName) continue;
                Transform hud = root.transform.Find(HudCanvasName);
                if (hud != null) return hud;
            }
            return null;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  1. 이름 표기 통일
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 이름을 바꾸면 안 되는 노드.
        ///
        /// 여기 있는 띄어쓰기 이름들은 <b>Slider·Scrollbar 컴포넌트가 스스로 만들어내는 내부 구조</b>다.
        /// PascalCase 규칙에는 어긋나지만, 새 Slider를 추가할 때마다 Unity가 다시 이 이름으로 만들기 때문에
        /// 고쳐봐야 계속 되돌아온다. "이건 Unity가 만든 것"이라고 누구나 알아보는 편이 낫다고 보고 남긴다.
        /// (반면 <c>Scroll View</c>·<c>Scrollbar Horizontal</c>은 컴포넌트가 만드는 내부가 아니라
        ///  우리가 만든 오브젝트의 기본 이름일 뿐이라 규칙대로 <c>ScrollView</c>·<c>ScrollbarHorizontal</c>이 된다.)
        ///
        /// <c>FlashImage</c>는 성격이 다르다 — TV_PowerOn.anim이 이 이름을 <b>커브 경로</b>로 쓴다. 바꾸면 조용히 멈춘다.
        /// </summary>
        private static readonly HashSet<string> Untouchable = new HashSet<string>
        {
            // Slider / Scrollbar 가 스스로 생성하는 내부 구조
            "Fill Area", "Handle Slide Area", "Sliding Area", "Handle", "Fill",
            // ScrollRect 가 참조하는 내부 구조
            "Viewport", "Content",
            // TV_PowerOn.anim 이 경로로 애니메이션하는 이름
            "FlashImage",
        };

        /// <summary>
        /// 프리팹 인스턴스라도 <b>자기 이름을 유지</b>해야 하는 것들.
        /// 번호가 프리팹 종류가 아니라 게임 안의 의미(단축키 슬롯, 미니맵 진영 등)를 나타내기 때문이다.
        ///
        /// 언더바를 <b>선택</b>으로 둔 이유: 이 툴이 언더바를 없애므로(<see cref="Pascalize"/>), 한 번 적용한 뒤
        /// 다시 실행하면 이름이 <c>Enemy_00</c>이 아니라 <c>Enemy00</c>이다. 언더바를 필수로 두면 두 번째 실행에서
        /// 여기 걸리지 않아 프리팹 이름으로 갈아끼워지고, 애써 지킨 번호가 날아간다.
        /// </summary>
        private static readonly Regex[] KeepOwnBaseName =
        {
            new Regex(@"^Skill_?\d+$"), new Regex(@"^Potion_?\d+$"),
            new Regex(@"^Enemy_?\d+$"), new Regex(@"^Neutral_?\d+$"), new Regex(@"^Ally_?\d+$"),
            new Regex(@"^Icon(_|[A-Z])"), new Regex(@"^ChatLog_?Item"), new Regex(@"^Label(_|[A-Z])"),
        };

        private class NameRule
        {
            public string From;
            public string To;
            public string ParentName;               // null이면 부모 무관
            public Func<Transform, bool> Guard;     // 추가 조건
            public string Reason;
        }

        /// <summary>
        /// 이름 치환 규칙. <b>위에서부터 첫 일치 하나만</b> 적용된다.
        /// 판정은 전부 "바뀌기 전 이름" 기준이라, 부모가 먼저 바뀌든 말든 결과가 같다.
        /// </summary>
        private static readonly NameRule[] ExplicitRules =
        {
            // ── 배경 → Background (2026-08-20 확정. 이유는 클래스 주석 참고)
            // Unity가 만드는 Slider의 Background는 이미 이 이름이라 손댈 것이 없다 — 제외 규칙이 필요 없다.
            new NameRule { From = "Bg", To = "Background", Reason = "배경 이름 통일" },
            new NameRule { From = "BG", To = "Background", Reason = "배경 이름 통일" },
            // Potion 슬롯은 배경 래퍼 안에 스프라이트가 또 들어있다.
            // 둘 다 Background가 되면 부모/자식이 같은 이름이라 구분이 안 되므로 안쪽은 BackgroundFill.
            new NameRule { From = "SPR_Background", To = "BackgroundFill", ParentName = "PT_Background", Reason = "Synty 접두사 제거" },
            new NameRule { From = "SPR_Background", To = "Background", Reason = "Synty 접두사 제거" },
            new NameRule { From = "PT_Background", To = "Background", Reason = "접두사 제거" },
            new NameRule { From = "ClassImageBackground", To = "Background", ParentName = "ClassIcon", Reason = "배경 이름 통일" },
            new NameRule { From = "ClassImageBackground (1)", To = "BackgroundPattern", Reason = "역할이 드러나는 이름으로" },

            // ── 프레임 / 쿨다운
            new NameRule { From = "SPR_Frame", To = "Frame", Reason = "Synty 접두사 제거" },
            new NameRule { From = "PT_Frame", To = "Frame", Reason = "접두사 제거" },
            // Background/Frame 짝을 맞춘다. 형제인 ClassImageBackground를 Background로 바꾸면서 혼자 남으면 어긋난다.
            new NameRule { From = "ClassImageFrame", To = "Frame", ParentName = "ClassIcon", Reason = "Background/Frame 짝 맞춤" },
            new NameRule { From = "SPR_CoolDown", To = "CooldownFill", Reason = "SkillSlot과 어휘 통일" },

            // ── 타이틀 그라디언트 (Title 직속만. SkillSlot 등의 Gradient는 성격이 달라 그대로 둔다)
            new NameRule { From = "Gradient", To = "TitleGradient", ParentName = "Title", Reason = "Enhance/Options 표기에 맞춤" },

            // ── 미니맵 플레이어 아이콘: ICN > Icon > ICON 3중첩을 알아볼 수 있게 푼다
            new NameRule { From = "ICN", To = "IconRoot", Reason = "대문자 약어 제거" },
            new NameRule { From = "ViewCone", To = "ViewConeRoot", ParentName = "ICN", Reason = "래퍼/실물 구분" },
            new NameRule { From = "SPR_ViewCone", To = "ViewCone", Reason = "Synty 접두사 제거" },
            new NameRule { From = "Icon", To = "IconPivot", ParentName = "ICN", Reason = "래퍼/실물 구분" },
            new NameRule { From = "ICON", To = "Icon", Reason = "대문자 약어 제거" },

            // ── 핫키 내부 텍스트 (스킬/툴바는 Text, 포션만 Input이었다)
            new NameRule { From = "Input", To = "Text", ParentName = "Hotkey", Reason = "핫키 내부 이름 통일" },

            // ── 표기 예외 (형제 그룹 규칙으로는 안 잡히는 단발성)
            // 공백/언더바 제거만으로는 Gradient0 · Skill4 가 되어 2자리 규칙에 안 맞으므로 따로 지정한다.
            new NameRule { From = "Gradient 0", To = "Gradient00", Reason = "2자리 제로패딩" },
            new NameRule { From = "Gradient 1", To = "Gradient01", Reason = "2자리 제로패딩" },
            new NameRule { From = "Skill 4", To = "Skill04", Reason = "2자리 제로패딩" },
            // 그냥 두면 FXOverlay가 된다. 프로젝트가 이미 Fx 표기로 굳어 있다(FxRoot, QuestFxLayer, BossIntroFx).
            new NameRule { From = "FX_Overlay", To = "FxOverlay", Reason = "프로젝트 Fx 표기에 맞춤" },
        };

        private static readonly Regex NumberedSuffix = new Regex(@"^(?<base>.*?)\s*\((?<n>\d+)\)$");
        private static readonly Regex NumericOnly = new Regex(@"^\d+$");

        private void RenamePass(Transform root, bool apply, StringBuilder log, string scopeLabel)
        {
            // 판정을 "바뀌기 전 이름"으로 하기 위해 먼저 전부 스냅샷한다.
            List<Transform> nodes = Walk(root).ToList();
            var originalName = nodes.ToDictionary(t => t, t => t.name);
            var plan = new List<(Transform t, string from, string to, string why)>();
            var renamed = new Dictionary<Transform, string>();

            // (a) 명시 규칙
            foreach (Transform t in nodes)
            {
                string cur = originalName[t];
                if (Untouchable.Contains(cur)) continue;

                foreach (NameRule r in ExplicitRules)
                {
                    if (r.From != cur) continue;
                    if (r.ParentName != null &&
                        (t.parent == null || !originalName.TryGetValue(t.parent, out string pn) || pn != r.ParentName))
                        continue;
                    if (r.Guard != null && !r.Guard(t)) continue;
                    UpsertPlan(plan, renamed, t, cur, r.To, r.Reason);
                    break;
                }
            }

            Func<Transform, string> nameNow = t => renamed.TryGetValue(t, out string n) ? n : originalName[t];

            // (b) 형제 반복 요소 번호 매기기: Base / Base (1) / Base (2) → Base00, Base01, Base02
            foreach (Transform parent in nodes)
            {
                List<Transform> children = Enumerable.Range(0, parent.childCount).Select(parent.GetChild).ToList();
                if (children.Count < 2) continue;

                var groups = new Dictionary<string, List<Transform>>();
                var hasNumbered = new HashSet<string>();

                foreach (Transform c in children)
                {
                    string cur = nameNow(c);
                    if (Untouchable.Contains(cur)) continue;

                    Match m = NumberedSuffix.Match(cur);
                    string baseName = m.Success ? m.Groups["base"].Value : cur;
                    if (m.Success) hasNumbered.Add(baseName);

                    // 프리팹 인스턴스는 프리팹 에셋 이름을 기준 이름으로 삼는다.
                    // "SkillCard (1)"과 "ActiveSkillCard"가 같은 프리팹인데 따로 놀던 문제를 여기서 흡수한다.
                    string prefabBase = PrefabBaseName(c);
                    if (prefabBase != null && !KeepOwnBaseName.Any(rx => rx.IsMatch(baseName)))
                    {
                        if (prefabBase != baseName) hasNumbered.Add(prefabBase);
                        baseName = prefabBase;
                    }

                    if (!groups.TryGetValue(baseName, out List<Transform> list))
                        groups[baseName] = list = new List<Transform>();
                    list.Add(c);
                }

                foreach (KeyValuePair<string, List<Transform>> kv in groups)
                {
                    if (kv.Value.Count < 2) continue;

                    // 번호를 매기는 경우는 둘이다.
                    //  (1) Unity가 붙인 "(1)" 꼬리표가 있다 → 우리 표기(00)로 바꾼다.
                    //  (2) 형제끼리 이름이 실제로 똑같다 → 사람이 만든 중복이라 구별이 안 된다.
                    //      (2)를 빼면 Label_Message 3형제처럼 완전히 같은 이름이 그대로 남는다.
                    bool collides = kv.Value.Select(c => nameNow(c)).Distinct().Count() != kv.Value.Count;
                    if (!hasNumbered.Contains(kv.Key) && !collides) continue;

                    for (int i = 0; i < kv.Value.Count; i++)
                    {
                        Transform c = kv.Value[i];
                        string want = $"{kv.Key}{i:00}";
                        if (nameNow(c) == want) continue;
                        UpsertPlan(plan, renamed, c, originalName[c], want, "형제 반복 요소 2자리 번호");
                    }
                }
            }

            // (c) 이름이 숫자뿐인 형제 묶음(예: Dot 밑의 1,2,3,4,5)은 부모 이름을 붙여 의미를 준다.
            foreach (Transform parent in nodes)
            {
                List<Transform> children = Enumerable.Range(0, parent.childCount).Select(parent.GetChild).ToList();
                if (children.Count < 2) continue;
                if (!children.All(c => NumericOnly.IsMatch(nameNow(c)))) continue;

                for (int i = 0; i < children.Count; i++)
                    UpsertPlan(plan, renamed, children[i], originalName[children[i]],
                               $"{nameNow(parent)}{i:00}", "숫자뿐인 이름에 의미 부여");
            }

            // (d) 공백·언더바 제거 + 각 단어 첫 글자 대문자 (약어의 기존 대문자는 유지: "HP Bar" → "HPBar")
            foreach (Transform t in nodes)
            {
                string cur = nameNow(t);
                if (Untouchable.Contains(cur) || cur.Length == 0) continue;
                if (cur.IndexOf(' ') < 0 && cur.IndexOf('_') < 0 && !char.IsLower(cur[0])) continue;

                string want = Pascalize(cur);
                if (want == cur) continue;
                UpsertPlan(plan, renamed, t, originalName[t], want, "공백·언더바 제거 / 첫 글자 대문자");
            }

            // ── 리포트
            log.AppendLine($"═══ 1. 이름 표기 통일 ({scopeLabel}) — {plan.Count}건 ═══");
            foreach (var p in plan.OrderBy(p => Path(root, p.t), StringComparer.Ordinal))
                log.AppendLine($"  {Path(root, p.t)}\n      {p.from}  →  {p.to}   ({p.why})");

            // 같은 부모 밑에서 이름이 겹치게 되는지 확인해 알려준다 (Unity는 허용하지만 사람이 헷갈린다).
            foreach (Transform parent in nodes)
            {
                List<string> dup = Enumerable.Range(0, parent.childCount)
                    .Select(parent.GetChild).Select(c => nameNow(c))
                    .GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
                if (dup.Count > 0)
                    log.AppendLine($"  [경고] {Path(root, parent)} 아래 이름 중복: {string.Join(", ", dup)}");
            }

            if (!apply) return;
            foreach (var p in plan)
            {
                Undo.RecordObject(p.t.gameObject, UndoLabel);
                p.t.name = p.to;
                EditorUtility.SetDirty(p.t.gameObject);
            }
        }

        private static void UpsertPlan(List<(Transform t, string from, string to, string why)> plan,
                                       Dictionary<Transform, string> renamed,
                                       Transform t, string from, string to, string why)
        {
            int idx = plan.FindIndex(p => p.t == t);
            if (idx >= 0) plan[idx] = (t, from, to, plan[idx].why + " + " + why);
            else plan.Add((t, from, to, why));
            renamed[t] = to;
        }

        /// <summary>프리팹 인스턴스의 루트면 원본 프리팹 이름, 아니면 null.</summary>
        private static string PrefabBaseName(Transform t)
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(t.gameObject)) return null;
            if (PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject) != t.gameObject) return null;
            GameObject src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(t.gameObject);
            return src != null ? src.name : null;
        }

        /// <summary>
        /// 공백과 언더바를 단어 경계로 보고 붙여서 PascalCase로 만든다.
        /// 각 단어의 <b>둘째 글자부터는 건드리지 않아</b> 약어의 대문자가 살아남는다("HP Bar" → "HPBar").
        /// </summary>
        private static string Pascalize(string s)
        {
            string[] parts = s.Split(new[] { ' ', '_' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            foreach (string p in parts)
                sb.Append(char.ToUpperInvariant(p[0])).Append(p.Substring(1));
            return sb.ToString();
        }

        // ════════════════════════════════════════════════════════════════════════
        //  2. 구조 재배치
        // ════════════════════════════════════════════════════════════════════════

        private void RestructurePass(Transform hud, bool apply, StringBuilder log)
        {
            log.AppendLine("═══ 2. 구조 재배치 ═══");

            // (a) Notice를 Center 밖으로.
            //     Notice는 레벨업/스킬해금 전체화면 알림인데 "가운데 창" 존인 Center 밑에 들어가 있다.
            //     알림 계층 자체가 HUD 직속(QuestFxLayer 앞)을 전제로 만들어져 있다.
            //     Center(640x360) 안에서 stretch + sizeDelta(1280,720) = 정확히 1920x1080이므로,
            //     HUD 직속 full-stretch로 옮겨도 화면상 위치·크기는 그대로다.
            Transform center = hud.Find("Center");
            Transform notice = center != null ? center.Find("Notice") : null;
            if (notice == null)
            {
                log.AppendLine("  [건너뜀] Center/Notice 를 찾지 못했습니다 (이미 옮겼거나 이름이 다름).");
            }
            else
            {
                Transform anchor = hud.Find("QuestFxLayer");
                int index = anchor != null ? anchor.GetSiblingIndex() : hud.childCount;
                log.AppendLine($"  Notice: Center/Notice  →  HUD/Notice (형제 index {index}, QuestFxLayer 앞)");
                log.AppendLine("      전체화면 알림이 '가운데 창' 존에 묶여 있던 것을 푼다. 화면상 위치는 동일.");
                if (apply)
                {
                    Undo.SetTransformParent(notice, hud, UndoLabel);
                    Undo.SetSiblingIndex(notice, index, UndoLabel);
                    var rt = (RectTransform)notice;
                    Undo.RecordObject(rt, UndoLabel);
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                    rt.localScale = Vector3.one;
                }
            }

            // (b) 비네트 3종을 게임플레이 HUD 위, 창 아래로.
            //     지금은 HUD의 첫 형제라 모든 UI 뒤에 그려진다 — 피격 비네트가 HUD에 가려 안 보이는 순서다.
            //     인벤토리/상점 같은 창까지 덮으면 그것대로 이상하므로, Bottom 다음 / Right 앞에 끼운다.
            string[] vignettes = { "Vignette", "HitVignette", "HpVignette" };
            Transform bottom = hud.Find("Bottom");
            if (bottom == null)
            {
                log.AppendLine("  [건너뜀] Bottom 을 찾지 못해 비네트 순서를 조정하지 않았습니다.");
            }
            else
            {
                // 앞의 것 바로 뒤에 하나씩 붙인다. SetSiblingIndex는 "빼고 다시 끼우는" 동작이라,
                // 대상이 기준보다 앞에 있었으면 목표 인덱스가 하나 당겨진다. 그 보정을 하지 않으면
                // 이미 정리된 씬에서 다시 실행했을 때 오히려 Bottom 앞으로 되돌아간다(멱등하지 않음).
                Transform after = bottom;
                foreach (string n in vignettes)
                {
                    Transform v = hud.Find(n);
                    if (v == null) { log.AppendLine($"  [건너뜀] {n} 없음"); continue; }

                    int cur = v.GetSiblingIndex();
                    int anchorIdx = after.GetSiblingIndex();
                    int target = cur < anchorIdx ? anchorIdx : anchorIdx + 1;

                    if (cur == target) log.AppendLine($"  {n}: 이미 {after.name} 뒤 (index {cur}) — 변경 없음");
                    else log.AppendLine($"  {n}: 형제 index {cur}  →  {target} ({after.name} 뒤)");

                    if (apply && cur != target) Undo.SetSiblingIndex(v, target, UndoLabel);
                    after = v;
                }
                log.AppendLine("      게임플레이 HUD 위 / 창 아래. 현재 셋 다 비활성이라 켜기 전까지 화면 변화는 없다.");
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  3. 손복사본 → 프리팹 인스턴스
        // ════════════════════════════════════════════════════════════════════════

        private void PrefabPass(Transform hud, bool apply, StringBuilder log)
        {
            log.AppendLine("═══ 3. 손복사본 → 프리팹 인스턴스 ═══");

            var jobs = new List<(Transform target, string prefabPath)>();

            // SkillSlot: Skill_01만 프리팹 인스턴스고 02~04는 손복사본이었다. 구조·값이 프리팹과 동일해 무손실 교체.
            foreach (Transform t in Walk(hud))
                if (t.name == "Skill_02" || t.name == "Skill_03" || t.name == "Skill_04")
                    jobs.Add((t, SkillSlotPrefabPath));

            // XBox: 창마다 손으로 복사돼 5벌. 스프라이트는 동일하고 RectTransform과 일부 색만 다르다.
            foreach (Transform t in Walk(hud))
                if (t.name == "XBox")
                    jobs.Add((t, XBoxPrefabPath));

            int done = 0;
            foreach ((Transform target, string path) in jobs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { log.AppendLine($"  [실패] 프리팹 없음: {path}"); continue; }

                if (PrefabUtility.IsPartOfPrefabInstance(target.gameObject) &&
                    PrefabUtility.GetCorrespondingObjectFromOriginalSource(target.gameObject) == prefab)
                {
                    log.AppendLine($"  [건너뜀] {Path(hud, target)} — 이미 {prefab.name} 인스턴스");
                    continue;
                }

                List<RefLink> links = CollectIncomingRefs(target, hud);
                log.AppendLine($"  {Path(hud, target)}  →  {prefab.name}.prefab 인스턴스" +
                               (links.Count > 0 ? $"   (외부 참조 {links.Count}건 재연결)" : ""));
                foreach (RefLink l in links)
                    log.AppendLine($"      재연결: {l.Owner.GetType().Name}.{l.PropertyPath} → " +
                                   $"{(l.RelativePath.Length == 0 ? "(루트)" : l.RelativePath)}");

                if (!apply) { done++; continue; }

                var oldRt = (RectTransform)target;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, target.parent);
                Undo.RegisterCreatedObjectUndo(inst, UndoLabel);

                inst.name = target.name;
                inst.SetActive(target.gameObject.activeSelf);
                CopyRect(oldRt, (RectTransform)inst.transform);
                if (keepColorOverrides) CopyGraphicColors(target, inst.transform);
                inst.transform.SetSiblingIndex(target.GetSiblingIndex());

                Reapply(links, inst.transform);
                Undo.DestroyObjectImmediate(target.gameObject);
                done++;
            }

            if (done == 0) log.AppendLine("  대상 없음 (이미 전부 프리팹 인스턴스).");
            log.AppendLine("  [제외] QuestEnumSlot — 씬 사본과 프리팹이 컴포넌트·계층이 다른 별개 물건이라 교체하면 동작이 바뀝니다.");
        }

        private static void CopyRect(RectTransform from, RectTransform to)
        {
            to.anchorMin = from.anchorMin;
            to.anchorMax = from.anchorMax;
            to.pivot = from.pivot;
            to.anchoredPosition3D = from.anchoredPosition3D;
            to.sizeDelta = from.sizeDelta;
            to.localRotation = from.localRotation;
            to.localScale = from.localScale;
        }

        /// <summary>같은 상대 경로에 있는 Graphic의 색만 옮긴다(스프라이트는 프리팹 것을 쓴다).</summary>
        private static void CopyGraphicColors(Transform oldRoot, Transform newRoot)
        {
            foreach (Transform t in Walk(oldRoot))
            {
                var g = t.GetComponent<Graphic>();
                if (g == null) continue;

                string rel = RelPath(oldRoot, t);
                Transform nt = rel.Length == 0 ? newRoot : newRoot.Find(rel);
                Graphic ng = nt != null ? nt.GetComponent<Graphic>() : null;
                if (ng == null || ng.color == g.color) continue;

                Undo.RecordObject(ng, UndoLabel);
                ng.color = g.color;
            }
        }

        // ── 교체 대상으로 들어오는 직렬화 참조를 상대 경로로 기억했다가 새 인스턴스에 다시 꽂는다 ──

        private class RefLink
        {
            public Component Owner;
            public string PropertyPath;
            public string RelativePath;   // 교체 대상 루트 기준. 빈 문자열이면 루트 자신
            public Type ComponentType;    // null이면 GameObject 참조
        }

        private static List<RefLink> CollectIncomingRefs(Transform oldRoot, Transform scope)
        {
            var map = new Dictionary<UnityEngine.Object, (string path, Type type)>();
            foreach (Transform t in Walk(oldRoot))
            {
                string rel = RelPath(oldRoot, t);
                map[t.gameObject] = (rel, null);
                foreach (Component c in t.GetComponents<Component>())
                    if (c != null) map[c] = (rel, c.GetType());
            }

            var links = new List<RefLink>();
            foreach (MonoBehaviour mb in scope.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || mb.transform.IsChildOf(oldRoot)) continue;  // 내부 참조는 프리팹이 스스로 들고 있다

                var so = new SerializedObject(mb);
                SerializedProperty it = so.GetIterator();
                while (it.NextVisible(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                    UnityEngine.Object v = it.objectReferenceValue;
                    if (v == null || !map.TryGetValue(v, out (string path, Type type) info)) continue;

                    links.Add(new RefLink
                    {
                        Owner = mb,
                        PropertyPath = it.propertyPath,
                        RelativePath = info.path,
                        ComponentType = info.type,
                    });
                }
            }
            return links;
        }

        private static void Reapply(List<RefLink> links, Transform newRoot)
        {
            foreach (RefLink l in links)
            {
                Transform t = l.RelativePath.Length == 0 ? newRoot : newRoot.Find(l.RelativePath);
                if (t == null)
                {
                    Debug.LogWarning($"[{UndoLabel}] 재연결 실패(경로 없음): {l.RelativePath}");
                    continue;
                }

                UnityEngine.Object target = l.ComponentType == null
                    ? t.gameObject
                    : t.GetComponent(l.ComponentType);
                if (target == null)
                {
                    Debug.LogWarning($"[{UndoLabel}] 재연결 실패(컴포넌트 없음): {l.RelativePath}/{l.ComponentType?.Name}");
                    continue;
                }

                var so = new SerializedObject(l.Owner);
                SerializedProperty p = so.FindProperty(l.PropertyPath);
                if (p == null) continue;

                Undo.RecordObject(l.Owner, UndoLabel);
                p.objectReferenceValue = target;
                so.ApplyModifiedProperties();
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  프리팹 에셋에도 같은 이름 규칙 적용
        // ════════════════════════════════════════════════════════════════════════

        private void PrefabAssetRenamePass(bool apply, StringBuilder log)
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { UiPrefabFolder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var one = new StringBuilder();
                    RenamePass(root.transform, apply, one, System.IO.Path.GetFileName(path));

                    string text = one.ToString();
                    if (text.Contains("— 0건")) continue;   // 변경 없는 프리팹으로 리포트를 어지럽히지 않는다
                    log.Append(text);
                    if (apply) PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  공용
        // ════════════════════════════════════════════════════════════════════════

        private static IEnumerable<Transform> Walk(Transform root)
        {
            yield return root;
            for (int i = 0; i < root.childCount; i++)
                foreach (Transform t in Walk(root.GetChild(i)))
                    yield return t;
        }

        private static string RelPath(Transform root, Transform t)
        {
            if (t == root) return string.Empty;
            var sb = new StringBuilder(t.name);
            for (Transform p = t.parent; p != null && p != root; p = p.parent)
                sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        private static string Path(Transform root, Transform t) => root.name + "/" + RelPath(root, t);
    }
}
