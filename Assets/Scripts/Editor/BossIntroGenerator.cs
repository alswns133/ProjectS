using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectS.UI;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 보스 등장 연출(HUD 기획서 6절 · UI_DG_BossText · UI_DG_Warning)의 계층을 HUD 캔버스 아래에 만드는 에디터 툴.
    /// 메뉴: Tools ▸ ProjectS ▸ Create Boss Intro
    ///
    /// 경고 배너가 흐르며 위험 표시가 깜박이다가, 그 경고가 부서지면서 "BOSS"가 박히는 순서다.
    /// 사망 팝업의 글리치를 재사용하지 않기로 한 결정에 따라 흐름·깜박임·파괴·충돌로만 구성한다(2026-08-14).
    ///
    /// 밴드는 기울어져 화면을 가로지르므로 <b>화면보다 넉넉히 길게</b> 만든다. 회전한 사각형은 가로폭이
    /// 1920/cos(기울기)보다 커야 양 끝이 화면 밖에 머문다. 짧으면 흐르다가 끝이 화면에 들어온다.
    /// </summary>
    public static class BossIntroGenerator
    {
        private const string UndoLabel = "Create Boss Intro";

        private const string HudCanvasName = "HUD";
        private const string RootName = "BossIntro";

        // 파편(QuestFxLayer = HexFragmentSpawner)이 연출 위에 그려지도록 그 앞에 끼운다.
        private const string InsertBeforeName = "QuestFxLayer";

        private const float BandWidth = 2400f;   // 1920 / cos(8°) ≈ 1939. 여유를 더 둔다.
        private const float BandHeight = 88f;
        private const float BandTilt = -8f;      // 스케치대로 좌상 → 우하 방향
        private const float BandOffsetY = 300f;

        // 폰트 크기는 씬에 이미 자리잡은 값에서 고른다(2026-08-14 점검 기준).
        private const float BossFontSize = 100f;   // DeathPopup 타이틀과 같은 급
        private const float BossNameFontSize = 40f;
        private const float WarnFontSize = 30f;

        private static readonly Color WarnRed = new(0.92f, 0.16f, 0.20f, 1f);
        private static readonly Color BandBackColor = new(0.06f, 0.02f, 0.03f, 0.55f);
        private static readonly Color DimColor = new(0f, 0f, 0f, 0.35f);

        [MenuItem("Tools/ProjectS/Create Boss Intro")]
        public static void CreateBossIntro()
        {
            Canvas hud = FindHudCanvas();
            if (hud == null)
            {
                EditorUtility.DisplayDialog("HUD 캔버스를 못 찾음",
                    $"현재 씬에 '{HudCanvasName}' 이름의 Canvas가 없습니다.\nHUD가 있는 씬을 열고 다시 실행하세요.",
                    "확인");
                return;
            }

            Transform existing = hud.transform.Find(RootName);
            if (existing != null)
            {
                bool rebuild = EditorUtility.DisplayDialog("Boss Intro 다시 만들기",
                    "이미 BossIntro 루트가 있습니다.\n지우고 새로 만들면 인스펙터에서 손본 배치·문구·연결이 사라집니다.",
                    "지우고 다시 만들기", "취소");
                if (!rebuild) return;

                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            GameObject root = Build(hud.transform, out bool foundSpawner);
            Undo.RegisterCreatedObjectUndo(root, UndoLabel);

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log("[BossIntroGenerator] Boss Intro 생성 완료.\n" +
                      "· 비활성으로 시작합니다. BossIntroFx.Play(보스이름)이 켜면서 재생합니다.\n" +
                      (foundSpawner
                          ? "· 씬의 HexFragmentSpawner를 찾아 물렸습니다.\n"
                          : "· HexFragmentSpawner를 못 찾아 비워 뒀습니다 — 파괴 단계의 파편이 생략됩니다.\n" +
                            "  HUD 최상위(마스크 바깥)에 HexFragmentSpawner를 두면 재생 시 자동으로 찾습니다.\n") +
                      "· 위험 표시·밴드는 유니티 기본 스프라이트라 실제 아트로 교체가 필요합니다.\n" +
                      "· 밴드 기울기는 각 밴드 RectTransform의 Z 회전으로 조절합니다(현재 -8°).");
        }

        private static GameObject Build(Transform hud, out bool foundSpawner)
        {
            GameObject root = new(RootName, typeof(RectTransform), typeof(CanvasGroup));
            root.transform.SetParent(hud, false);
            EntryUIBuilder.Fill(root.GetComponent<RectTransform>());

            Transform anchor = hud.Find(InsertBeforeName);
            if (anchor != null) root.transform.SetSiblingIndex(anchor.GetSiblingIndex());
            else root.transform.SetAsLastSibling();

            GameObject warning = BuildWarning(root.transform,
                out ScrollingBand topBand, out ScrollingBand bottomBand, out Image icon);

            RectTransform bossRoot = BuildBoss(root.transform, out TextMeshProUGUI bossName);

            HexFragmentSpawner spawner = Object.FindAnyObjectByType<HexFragmentSpawner>(FindObjectsInactive.Include);
            foundSpawner = spawner != null;

            BossIntroFx fx = root.AddComponent<BossIntroFx>();
            EntryUIBuilder.Wire(fx,
                ("warningRoot", warning),
                ("warningIcon", icon),
                ("fragmentSpawner", spawner),
                ("bossRoot", bossRoot),
                ("bossNameText", bossName));
            EntryUIBuilder.WireList(fx, "bands", topBand, bottomBand);

            root.SetActive(false);
            return root;
        }

        private static GameObject BuildWarning(Transform parent,
            out ScrollingBand topBand, out ScrollingBand bottomBand, out Image icon)
        {
            GameObject warning = new("Warning", typeof(RectTransform));
            warning.transform.SetParent(parent, false);
            EntryUIBuilder.Fill(warning.GetComponent<RectTransform>());

            // 경고가 도는 동안 뒤 화면을 살짝 눌러 둔다. 파괴와 함께 통째로 사라진다.
            EntryUIBuilder.CreateFullScreenImage(warning.transform, "Dim", DimColor, false);

            topBand = BuildBand(warning.transform, "TopBand", BandOffsetY, 160f);
            bottomBand = BuildBand(warning.transform, "BottomBand", -BandOffsetY, -160f);

            icon = CreateBox(warning.transform, "WarningIcon", WarnRed);
            EntryUIBuilder.SetCenter(icon.rectTransform, Vector2.zero, new Vector2(220f, 200f));

            return warning;
        }

        private static ScrollingBand BuildBand(Transform parent, string name, float offsetY, float speed)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            EntryUIBuilder.SetCenter(rt, new Vector2(0f, offsetY), new Vector2(BandWidth, BandHeight));
            rt.localEulerAngles = new Vector3(0f, 0f, BandTilt);

            Image back = go.GetComponent<Image>();
            back.color = BandBackColor;
            back.raycastTarget = false;

            // ScrollingBand가 이 라벨을 원본 삼아 반복 문자열을 만들고 복제한다.
            TextMeshProUGUI label = EntryUIBuilder.CreateTMP(go.transform, "Word", "warning", WarnFontSize);
            label.color = WarnRed;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            EntryUIBuilder.Fill(label.rectTransform);

            ScrollingBand band = go.AddComponent<ScrollingBand>();
            EntryUIBuilder.Wire(band, ("template", label));
            SetFloat(band, "speed", speed);

            return band;
        }

        private static RectTransform BuildBoss(Transform parent, out TextMeshProUGUI bossName)
        {
            GameObject go = new("Boss", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            EntryUIBuilder.SetCenter(rt, Vector2.zero, new Vector2(1200f, 320f));

            TextMeshProUGUI label = EntryUIBuilder.CreateTMP(go.transform, "Label", "BOSS", BossFontSize);
            label.alignment = TextAlignmentOptions.Center;
            label.color = WarnRed;
            EntryUIBuilder.StretchTop(label.rectTransform, -190f, -10f);

            bossName = EntryUIBuilder.CreateTMP(go.transform, "BossName", "보스 이름", BossNameFontSize);
            bossName.alignment = TextAlignmentOptions.Center;
            bossName.color = Color.white;
            EntryUIBuilder.StretchBottom(bossName.rectTransform, 20f, 90f);

            return rt;
        }

        /// <summary>private [SerializeField] float 값을 넣는다. EntryUIBuilder.Wire는 오브젝트 참조만 다룬다.</summary>
        private static void SetFloat(Component comp, string prop, float value)
        {
            SerializedObject so = new(comp);
            SerializedProperty p = so.FindProperty(prop);

            if (p != null) p.floatValue = value;
            else Debug.LogWarning($"[BossIntroGenerator] {comp.GetType().Name}에 '{prop}' 필드가 없습니다.");

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Image CreateBox(Transform parent, string name, Color color)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            Image img = go.GetComponent<Image>();
            img.sprite = EntryUIBuilder.UISprite;
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = false;
            return img;
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
