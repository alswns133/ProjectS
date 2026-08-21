using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectS.UI;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 보스(레이드 포함) HP 바 오버레이를 현재 씬에 만드는 에디터 툴.
    /// 메뉴: Tools ▸ ProjectS ▸ Create Boss HP Bar
    ///
    /// HUD와 별개의 전용 Canvas(높은 sortingOrder)로 만들어 화면 상단중앙에 띄운다 — 스택 패널이 아니라
    /// 오버레이라 HUD를 멈추지 않고 위에 겹친다(토스트·레벨업 알림과 같은 결). 루트에는
    /// <see cref="BossHpView"/> + <see cref="BossHpPresenter"/>를 얹고, 실제 바 계층(Bar)만 표시/숨김한다
    /// (루트는 이벤트 수신용으로 항상 활성이어야 한다).
    ///
    /// 색·트레일 속도 등 연출값은 <see cref="BossHpView"/>의 필드 기본값으로 들어가므로, 이 툴은 오브젝트
    /// 참조만 배선한다. 배치·색은 생성 후 인스펙터에서 다듬는다.
    /// </summary>
    public static class BossHpBarGenerator
    {
        private const string UndoLabel = "Create Boss HP Bar";
        private const string CanvasName = "BossHpCanvas";
        private const int SortingOrder = 100;   // HUD보다 위에 그린다

        private static readonly Color TrackColor = new Color(0.05f, 0.05f, 0.07f, 0.85f);
        private static readonly Color TrailColor = new Color(1f, 1f, 1f, 0.5f);
        private static readonly Color HpColor = new Color(0.85f, 0.18f, 0.18f, 1f);
        private static readonly Color GroggyColor = new Color(0.55f, 0.75f, 1f, 1f);
        private static readonly Color LockColor = new Color(0.75f, 0.78f, 0.85f, 1f);

        [MenuItem("Tools/ProjectS/Create Boss HP Bar")]
        public static void CreateBossHpBar()
        {
            Canvas canvas = FindCanvas();
            if (canvas != null)
            {
                bool rebuild = EditorUtility.DisplayDialog("Boss HP Bar 다시 만들기",
                    $"이미 '{CanvasName}'가 있습니다.\n지우고 새로 만들면 인스펙터에서 손본 배치·색·연결이 사라집니다.",
                    "지우고 다시 만들기", "취소");
                if (!rebuild) return;

                Undo.DestroyObjectImmediate(canvas.gameObject);
            }

            GameObject canvasGo = BuildCanvas();
            Undo.RegisterCreatedObjectUndo(canvasGo, UndoLabel);

            Selection.activeGameObject = canvasGo;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log("[BossHpBarGenerator] 보스 HP 바 생성 완료. 남은 수동 작업:\n" +
                      "· 바(Bar)는 비활성으로 시작합니다 — BossEvents.FireBossAppeared(boss)가 켭니다.\n" +
                      "  보스방 진입(EnemyRoom 콜백)/레이드 입장에서 호출하세요.\n" +
                      "· 보스 프리팹에 EnemyGroggy를 붙이고, MonsterStatTable에 SegmentCount(줄 수)/GroggyMax를 채우세요.\n" +
                      "· 보스 애니메이터에 'Groggy' State(루프 권장) + 로코모션 복귀 전이를 만드세요.\n" +
                      "· 스킬이 그로기를 깎으려면 AttackContext.GroggyDamage를 채워야 합니다(평타=0).");
        }

        private static GameObject BuildCanvas()
        {
            GameObject canvasGo = new GameObject(CanvasName,
                typeof(Canvas), typeof(CanvasScaler));

            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            BuildRoot(canvasGo.transform);
            return canvasGo;
        }

        // 루트(뷰+프레젠터, 항상 활성) → Bar(표시/숨김) → 이름/HP/줄수/그로기.
        private static void BuildRoot(Transform canvas)
        {
            GameObject root = new GameObject("BossHpRoot",
                typeof(RectTransform), typeof(BossHpView), typeof(BossHpPresenter));
            root.transform.SetParent(canvas, false);
            EntryUIBuilder.Fill(root.GetComponent<RectTransform>());

            GameObject bar = new GameObject("Bar", typeof(RectTransform));
            bar.transform.SetParent(root.transform, false);
            SetTop(bar.GetComponent<RectTransform>(), new Vector2(0f, -30f), new Vector2(1000f, 140f));

            // 보스 이름(상단).
            TextMeshProUGUI nameText = EntryUIBuilder.CreateTMP(bar.transform, "NameText", "보스 이름", 30f);
            nameText.alignment = TextAlignmentOptions.Center;
            nameText.color = Color.white;
            SetTop(nameText.rectTransform, new Vector2(0f, 0f), new Vector2(700f, 34f));

            // HP 바: Track(배경) → Trail(지연) → Fill(앞면). Fill을 마지막에 만들어 위에 그린다.
            Image hpTrack = MakeBox(bar.transform, "HpTrack", TrackColor);
            SetTop(hpTrack.rectTransform, new Vector2(0f, -42f), new Vector2(920f, 48f));

            Image hpTrail = MakeFilled(hpTrack.transform, "HpTrail", TrailColor);
            EntryUIBuilder.Fill(hpTrail.rectTransform);

            Image hpFill = MakeFilled(hpTrack.transform, "HpFill", HpColor);
            EntryUIBuilder.Fill(hpFill.rectTransform);

            // HP 수치(바 위 중앙).
            TextMeshProUGUI hpValue = EntryUIBuilder.CreateTMP(hpTrack.transform, "HpValue", "0/0", 22f);
            hpValue.alignment = TextAlignmentOptions.Center;
            hpValue.color = Color.white;
            EntryUIBuilder.Fill(hpValue.rectTransform);

            // 남은 줄 수(바 오른쪽).
            TextMeshProUGUI segCount = EntryUIBuilder.CreateTMP(bar.transform, "SegmentCount", "X 0", 26f);
            segCount.alignment = TextAlignmentOptions.Center;
            segCount.color = new Color(1f, 0.9f, 0.5f, 1f);
            SetTop(segCount.rectTransform, new Vector2(530f, -42f), new Vector2(140f, 48f));

            // 그로기 바(HP 바 아래). Track → Fill.
            Image groggyTrack = MakeBox(bar.transform, "GroggyTrack", TrackColor);
            SetTop(groggyTrack.rectTransform, new Vector2(0f, -96f), new Vector2(920f, 16f));

            Image groggyFill = MakeFilled(groggyTrack.transform, "GroggyFill", GroggyColor);
            EntryUIBuilder.Fill(groggyFill.rectTransform);

            // 잠금 자물쇠(그로기 바 왼쪽). 특수 패턴 중에만 켜진다.
            Image lockIcon = MakeBox(bar.transform, "GroggyLock", LockColor);
            SetTop(lockIcon.rectTransform, new Vector2(-486f, -94f), new Vector2(22f, 22f));
            TextMeshProUGUI lockLabel = EntryUIBuilder.CreateTMP(lockIcon.transform, "Label", "L", 14f);
            lockLabel.alignment = TextAlignmentOptions.Center;
            lockLabel.color = Color.black;
            EntryUIBuilder.Fill(lockLabel.rectTransform);
            lockIcon.gameObject.SetActive(false);   // 기본 꺼짐(잠금 시에만 켜짐)

            BossHpView view = root.GetComponent<BossHpView>();
            EntryUIBuilder.Wire(view,
                ("barRoot", bar),
                ("nameText", nameText),
                ("hpValueText", hpValue),
                ("segmentCountText", segCount),
                ("hpBackground", hpTrack),
                ("hpFill", hpFill),
                ("hpTrail", hpTrail),
                ("groggyFill", groggyFill),
                ("groggyLockIcon", lockIcon.gameObject));

            EntryUIBuilder.Wire(root.GetComponent<BossHpPresenter>(), ("view", view));

            // 바는 등장 이벤트가 켤 때까지 숨겨 둔다(루트는 이벤트 수신용으로 활성 유지).
            bar.SetActive(false);
        }

        // Filled(가로) 이미지. fillAmount로 바를 그린다.
        private static Image MakeFilled(Transform parent, string name, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            Image img = go.GetComponent<Image>();
            img.sprite = EntryUIBuilder.UISprite;
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = (int)Image.OriginHorizontal.Left;
            img.fillAmount = 1f;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        // 9-slice 배경 박스.
        private static Image MakeBox(Transform parent, string name, Color color)
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

        // 부모 위 모서리 기준 배치(위에서 아래로 쌓는 구조).
        private static void SetTop(RectTransform rt, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        private static Canvas FindCanvas()
        {
            foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (canvas.name == CanvasName) return canvas;
            }
            return null;
        }
    }
}
