using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectS.UI;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 진입 화면의 팝업 계층(PopupLayer)을 현재 씬에 만드는 에디터 툴.
    /// 메뉴: Tools ▸ ProjectS ▸ Create Popup Layer
    ///
    /// 페이지들보다 항상 위에 있어야 하므로 UICanvas의 마지막 자식으로 붙인다.
    /// 계층 내부 순서도 의미가 있다: 딤 → 확인 팝업 → 알림 팝업 → 입력 차단.
    /// 뒤에 있을수록 위에 그려지고 클릭도 먼저 먹으므로, 차단막이 팝업보다 뒤여야 한다.
    /// </summary>
    public static class PopupLayerGenerator
    {
        private const string UndoLabel = "Create Popup Layer";

        private const float ConfirmWidth = 720f;
        private const float ConfirmHeight = 360f;
        private const float AlertWidth = 640f;
        private const float AlertHeight = 280f;
        private const float PopupTextInset = 40f;

        private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.65f);
        private static readonly Color BusyColor = new Color(0f, 0f, 0f, 0.35f);
        private static readonly Color PopupColor = new Color(0.11f, 0.13f, 0.17f, 0.98f);
        private static readonly Color AccentButtonColor = new Color(0.16f, 0.5f, 0.42f, 1f);
        private static readonly Color NeutralButtonColor = new Color(0.22f, 0.25f, 0.31f, 1f);
        private static readonly Color WarnTextColor = new Color(0.95f, 0.72f, 0.35f, 1f);
        private static readonly Color MutedTextColor = new Color(0.72f, 0.76f, 0.83f, 1f);

        [MenuItem("Tools/ProjectS/Create Popup Layer")]
        public static void CreatePopupLayer()
        {
            EntryUIBuilder.EnsureEventSystem(UndoLabel);
            Canvas canvas = EntryUIBuilder.EnsureCanvas(UndoLabel);

            Transform existing = canvas.transform.Find("PopupLayer");
            if (existing != null)
            {
                bool rebuild = EditorUtility.DisplayDialog("PopupLayer 다시 만들기",
                    "이미 PopupLayer가 있습니다.\n지우고 새로 만들면 인스펙터에서 손본 배치·문구가 사라집니다.",
                    "지우고 다시 만들기", "취소");
                if (!rebuild) return;

                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            GameObject layer = BuildLayer(canvas.transform);

            Undo.RegisterCreatedObjectUndo(layer, UndoLabel);
            Selection.activeGameObject = layer;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[PopupLayerGenerator] PopupLayer 생성 완료. 팝업들은 비활성 상태로 시작합니다.");
        }

        private static GameObject BuildLayer(Transform canvas)
        {
            GameObject layer = new GameObject("PopupLayer", typeof(RectTransform));
            layer.transform.SetParent(canvas, false);
            layer.transform.SetAsLastSibling();   // 페이지들보다 위
            EntryUIBuilder.Fill(layer.GetComponent<RectTransform>());

            // 딤은 뒤쪽 클릭을 막아야 하므로 raycastTarget을 켠다.
            Image dimmer = EntryUIBuilder.CreateFullScreenImage(layer.transform, "Dimmer", DimColor, true);

            ConfirmPopupView confirm = BuildConfirmPopup(layer.transform);
            AlertPopupView alert = BuildAlertPopup(layer.transform);
            GameObject busy = BuildBusyBlocker(layer.transform);

            PopupLayerView view = layer.AddComponent<PopupLayerView>();
            EntryUIBuilder.Wire(view,
                ("dimmer", dimmer),
                ("confirmPopup", confirm),
                ("alertPopup", alert),
                ("busyBlocker", busy));

            dimmer.gameObject.SetActive(false);
            confirm.gameObject.SetActive(false);
            alert.gameObject.SetActive(false);
            busy.SetActive(false);

            return layer;
        }

        private static ConfirmPopupView BuildConfirmPopup(Transform parent)
        {
            RectTransform box = CreateBox(parent, "ConfirmPopup", ConfirmWidth, ConfirmHeight);

            TextMeshProUGUI message = EntryUIBuilder.CreateTMP(box, "MessageText", "캐릭터를 삭제하시겠습니까?", 30f);
            message.alignment = TextAlignmentOptions.Center;
            message.color = Color.white;
            EntryUIBuilder.StretchTop(message.rectTransform, -140f, -50f, PopupTextInset);

            TextMeshProUGUI sub = EntryUIBuilder.CreateTMP(box, "SubText", "삭제 후 복구 불가", 22f);
            sub.alignment = TextAlignmentOptions.Center;
            sub.color = WarnTextColor;
            EntryUIBuilder.StretchTop(sub.rectTransform, -190f, -150f, PopupTextInset);

            (Button confirmBtn, TextMeshProUGUI confirmLabel) =
                EntryUIBuilder.CreateButton(box, "ConfirmButton", "예", 24f, AccentButtonColor);
            EntryUIBuilder.SetBottomCenter(confirmBtn.GetComponent<RectTransform>(),
                new Vector2(-140f, 82f), new Vector2(260f, 64f));

            (Button cancelBtn, TextMeshProUGUI cancelLabel) =
                EntryUIBuilder.CreateButton(box, "CancelButton", "아니오", 24f, NeutralButtonColor);
            EntryUIBuilder.SetBottomCenter(cancelBtn.GetComponent<RectTransform>(),
                new Vector2(140f, 82f), new Vector2(260f, 64f));

            ConfirmPopupView view = box.gameObject.AddComponent<ConfirmPopupView>();
            EntryUIBuilder.Wire(view,
                ("messageText", message),
                ("subText", sub),
                ("confirmButton", confirmBtn),
                ("cancelButton", cancelBtn),
                ("confirmLabel", confirmLabel),
                ("cancelLabel", cancelLabel));
            return view;
        }

        private static AlertPopupView BuildAlertPopup(Transform parent)
        {
            RectTransform box = CreateBox(parent, "AlertPopup", AlertWidth, AlertHeight);

            TextMeshProUGUI message = EntryUIBuilder.CreateTMP(box, "MessageText", "이미 사용 중인 이름입니다.", 28f);
            message.alignment = TextAlignmentOptions.Center;
            message.color = Color.white;
            EntryUIBuilder.StretchTop(message.rectTransform, -160f, -50f, PopupTextInset);

            (Button okBtn, TextMeshProUGUI okLabel) =
                EntryUIBuilder.CreateButton(box, "OkButton", "확인", 24f, AccentButtonColor);
            EntryUIBuilder.SetBottomCenter(okBtn.GetComponent<RectTransform>(),
                new Vector2(0f, 72f), new Vector2(240f, 64f));

            AlertPopupView view = box.gameObject.AddComponent<AlertPopupView>();
            EntryUIBuilder.Wire(view,
                ("messageText", message),
                ("okButton", okBtn),
                ("okLabel", okLabel));
            return view;
        }

        // 서버 왕복 중 전체 입력 차단. 팝업보다 뒤(=위)에 두어 팝업 버튼도 막는다.
        private static GameObject BuildBusyBlocker(Transform parent)
        {
            Image img = EntryUIBuilder.CreateFullScreenImage(parent, "BusyBlocker", BusyColor, true);

            TextMeshProUGUI label = EntryUIBuilder.CreateTMP(img.transform, "BusyLabel", "잠시만 기다려 주세요", 28f);
            label.alignment = TextAlignmentOptions.Center;
            label.color = MutedTextColor;
            EntryUIBuilder.Fill(label.rectTransform);

            return img.gameObject;
        }

        private static RectTransform CreateBox(Transform parent, string name, float width, float height)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            EntryUIBuilder.SetCenter(rt, Vector2.zero, new Vector2(width, height));

            Image img = go.GetComponent<Image>();
            img.sprite = EntryUIBuilder.UISprite;
            img.type = Image.Type.Sliced;
            img.color = PopupColor;
            img.raycastTarget = true;   // 팝업 몸통을 눌렀을 때 뒤로 새지 않게
            return rt;
        }
    }
}
