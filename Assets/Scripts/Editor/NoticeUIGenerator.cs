using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectS.UI;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 레벨업·스킬 해금 알림(기획서 4장 · UI_LV_001 / UI_LV_011)의 계층을 HUD 캔버스 아래에 만드는 에디터 툴.
    /// 메뉴: Tools ▸ ProjectS ▸ Create Notice UI
    ///
    /// 기획서는 "LEVEL UP 텍스트 + 새 레벨 숫자"만 정의하지만, 팀 결정으로 <b>캐릭터 심볼</b>을 위에 얹는다
    /// (2026-08-14). 심볼 → 레벨 숫자 → 문구 순으로 세로로 쌓는다.
    ///
    /// 심볼은 <see cref="LevelUpNotice"/>에 필드를 추가하지 않고 <b>자식 두 개를 두고 하나만 켜는</b> 방식으로 푼다.
    /// 이 프로젝트가 캐릭터별 아트를 이미 그렇게 다루고 있어서다(EXP Haru/EXP Erwin, HaruEmblem/ErwinEmblem,
    /// HaruIcon/ErwinIcon). 알림 스크립트는 심볼의 존재를 알 필요가 없고, 캐릭터 전환 로직도 새로 만들 필요가 없다.
    ///
    /// <see cref="Notice"/> 루트는 Dungeon이 아니라 HUD 직속에 붙인다 — Dungeon 밑에 두면
    /// 마을에서 레벨업할 때 알림이 뜨지 않는다.
    /// </summary>
    public static class NoticeUIGenerator
    {
        private const string UndoLabel = "Create Notice UI";

        private const string HudCanvasName = "HUD";
        private const string RootName = "Notice";
        private const string InsertBeforeName = "QuestFxLayer";

        // 캐릭터 심볼 스프라이트는 씬에 이미 쓰이고 있는 것을 그대로 가져온다.
        // 경로로 에셋을 찾으면 파일이 옮겨질 때 조용히 끊기므로, 실제로 쓰이는 인스턴스에서 복사한다.
        private static readonly string[] HaruSymbolSources = { "HaruEmblem", "HaruIcon" };
        private static readonly string[] ErwinSymbolSources = { "ErwinEmblem", "ErwinIcon" };

        // 폰트 크기는 씬에 이미 자리잡은 값에서 고른다.
        // 새 크기를 만들면 씬 전체에서 그 값 하나만 튀는 이상치가 된다(2026-08-14 점검 결과).
        private const float LevelFontSize = 40f;
        private const float TitleFontSize = 28f;
        private const float SkillNameFontSize = 24f;
        private const float MessageFontSize = 20f;

        private static readonly Color BannerColor = new Color(0.05f, 0.07f, 0.10f, 0.82f);
        private static readonly Color SlotColor = new Color(0.08f, 0.10f, 0.14f, 0.9f);
        private static readonly Color AccentColor = new Color(1f, 0.82f, 0.35f, 1f);
        private static readonly Color MutedTextColor = new Color(0.72f, 0.76f, 0.83f, 1f);

        [MenuItem("Tools/ProjectS/Create Notice UI")]
        public static void CreateNoticeUI()
        {
            Canvas hud = FindHudCanvas();
            if (hud == null)
            {
                EditorUtility.DisplayDialog("HUD 캔버스를 못 찾음",
                    $"현재 씬에 '{HudCanvasName}' 이름의 Canvas가 없습니다.\n" +
                    "HUD가 있는 씬을 열고 다시 실행하세요.",
                    "확인");
                return;
            }

            Transform existing = hud.transform.Find(RootName);
            if (existing != null)
            {
                bool rebuild = EditorUtility.DisplayDialog("Notice UI 다시 만들기",
                    "이미 Notice 루트가 있습니다.\n지우고 새로 만들면 인스펙터에서 손본 배치·문구·연결이 사라집니다.",
                    "지우고 다시 만들기", "취소");
                if (!rebuild) return;

                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            GameObject root = BuildRoot(hud.transform);
            Undo.RegisterCreatedObjectUndo(root, UndoLabel);

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log("[NoticeUIGenerator] Notice UI 생성 완료.\n" +
                      "· 두 알림은 비활성으로 시작합니다. TimedNoticeView.Play()가 켜면서 재생합니다.\n" +
                      "· 'LEVEL UP' 라벨에 GlitchTextFx는 붙이지 않았습니다 — 셰이더 참조가 필요하고 기본 문구가\n" +
                      "  사망 팝업용이라 반쯤 설정된 채로 나옵니다. 원하면 DeathPopup/TitleText의 설정을 복사해\n" +
                      "  직접 붙이세요. LevelUpNotice가 자식에서 자동으로 찾습니다(없어도 정상 동작).\n" +
                      "· 확인은 Assets/Scripts/Debug/NoticeTester.cs 를 아무 오브젝트에 붙이고\n" +
                      "  플레이 → 인스펙터 ⋮ 메뉴로 하세요. PlayerEvents.FireLevelUp은 아직 호출부가 없습니다.");
        }

        private static GameObject BuildRoot(Transform hud)
        {
            GameObject root = new GameObject(RootName, typeof(RectTransform));
            root.transform.SetParent(hud, false);
            EntryUIBuilder.Fill(root.GetComponent<RectTransform>());

            Transform anchor = hud.Find(InsertBeforeName);
            if (anchor != null) root.transform.SetSiblingIndex(anchor.GetSiblingIndex());
            else root.transform.SetAsLastSibling();

            BuildLevelUp(root.transform, hud);
            BuildSkillUnlock(root.transform);

            return root;
        }

        // 심볼 → 레벨 숫자 → "LEVEL UP" 띠. 스케치대로 위아래가 살짝 겹치게 둔다.
        private static void BuildLevelUp(Transform parent, Transform hud)
        {
            GameObject go = new GameObject("LevelUp", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            SetTopCenter(go.GetComponent<RectTransform>(), new Vector2(0f, -110f), new Vector2(420f, 380f));

            // 심볼 두 개 중 하나만 켜서 캐릭터를 가른다. 알림 스크립트는 이 구조를 몰라도 된다.
            GameObject symbol = new GameObject("Symbol", typeof(RectTransform));
            symbol.transform.SetParent(go.transform, false);
            SetTopCenter(symbol.GetComponent<RectTransform>(), Vector2.zero, new Vector2(260f, 196f));

            Image haru = CreateSymbol(symbol.transform, "HaruSymbol", HaruSymbolSources);
            Image erwin = CreateSymbol(symbol.transform, "ErwinSymbol", ErwinSymbolSources);
            haru.gameObject.SetActive(false);   // 기본은 어윈. 씬의 EXP/ClassIcon과 같은 상태로 맞춘다.

            // 띠를 레벨 숫자보다 먼저 만든다 = 뒤에 깔린다. 스케치에서 숫자가 띠 위에 얹혀 있다.
            Image banner = CreateBox(go.transform, "TitleBanner", BannerColor);
            SetTopCenter(banner.rectTransform, new Vector2(0f, -246f), new Vector2(300f, 56f));

            TextMeshProUGUI title = EntryUIBuilder.CreateTMP(banner.transform, "Label", "LEVEL UP", TitleFontSize);
            title.alignment = TextAlignmentOptions.Center;
            title.color = AccentColor;
            EntryUIBuilder.Fill(title.rectTransform);

            Image levelBox = CreateBox(go.transform, "LevelBox", SlotColor);
            SetTopCenter(levelBox.rectTransform, new Vector2(0f, -182f), new Vector2(110f, 84f));

            TextMeshProUGUI levelText = EntryUIBuilder.CreateTMP(levelBox.transform, "LevelText", "1", LevelFontSize);
            levelText.alignment = TextAlignmentOptions.Center;
            levelText.color = Color.white;
            EntryUIBuilder.Fill(levelText.rectTransform);

            LevelUpNotice notice = go.AddComponent<LevelUpNotice>();
            EntryUIBuilder.Wire(notice, ("levelText", levelText));

            go.SetActive(false);
        }

        // 스킬 슬롯 아이콘 → 알림 띠(문구 + 스킬 이름).
        private static void BuildSkillUnlock(Transform parent)
        {
            GameObject go = new GameObject("SkillUnlock", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            SetTopCenter(go.GetComponent<RectTransform>(), new Vector2(0f, -560f), new Vector2(420f, 200f));

            Image banner = CreateBox(go.transform, "Banner", BannerColor);
            SetTopCenter(banner.rectTransform, new Vector2(0f, -84f), new Vector2(380f, 78f));

            TextMeshProUGUI message = EntryUIBuilder.CreateTMP(
                banner.transform, "Message", "새 스킬을 해금했습니다!", MessageFontSize);
            message.alignment = TextAlignmentOptions.Center;
            message.color = MutedTextColor;
            EntryUIBuilder.StretchTop(message.rectTransform, -38f, -4f, 12f);

            TextMeshProUGUI skillName = EntryUIBuilder.CreateTMP(
                banner.transform, "SkillName", "스킬 이름", SkillNameFontSize);
            skillName.alignment = TextAlignmentOptions.Center;
            skillName.color = AccentColor;
            EntryUIBuilder.StretchBottom(skillName.rectTransform, 6f, 42f, 12f);

            // 아이콘을 띠보다 나중에 만들어 위에 그린다(띠 윗변에 얹히는 스케치 구조).
            Image icon = CreateBox(go.transform, "SkillIcon", SlotColor);
            SetTopCenter(icon.rectTransform, Vector2.zero, new Vector2(96f, 96f));

            SkillUnlockBanner notice = go.AddComponent<SkillUnlockBanner>();
            EntryUIBuilder.Wire(notice,
                ("messageText", message),
                ("skillNameText", skillName),
                ("skillIcon", icon));

            go.SetActive(false);
        }

        private static Image CreateSymbol(Transform parent, string name, string[] sourceNames)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            EntryUIBuilder.Fill(go.GetComponent<RectTransform>());

            Image img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;

            Sprite found = FindSpriteOn(sourceNames);
            if (found != null)
            {
                img.sprite = found;
                return img;
            }

            // 못 찾아도 계층은 남긴다. 스프라이트만 나중에 물리면 된다.
            img.color = new Color(1f, 1f, 1f, 0.25f);
            Debug.LogWarning($"[NoticeUIGenerator] {name}: 씬에서 {string.Join(" / ", sourceNames)}을(를) 못 찾아 " +
                             "스프라이트를 비워 뒀습니다. 직접 물려주세요.");
            return img;
        }

        /// <summary>씬에서 주어진 이름의 오브젝트를 찾아 그 Image의 스프라이트를 돌려준다.</summary>
        private static Sprite FindSpriteOn(string[] names)
        {
            foreach (Image image in Object.FindObjectsByType<Image>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                foreach (string name in names)
                {
                    if (image.name == name && image.sprite != null) return image.sprite;
                }
            }
            return null;
        }

        private static Image CreateBox(Transform parent, string name, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            Image img = go.GetComponent<Image>();
            img.sprite = EntryUIBuilder.UISprite;
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>부모 위 모서리 기준으로 위치와 크기를 잡는다(위에서 아래로 쌓는 알림 구조).</summary>
        private static void SetTopCenter(RectTransform rt, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        private static Canvas FindHudCanvas()
        {
            foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (canvas.name == HudCanvasName) return canvas;
            }
            return null;
        }
    }
}
