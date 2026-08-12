using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectS.UI;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 클래스 선택 페이지(Page_Class)를 현재 씬에 만드는 에디터 툴.
    /// 메뉴: Tools ▸ ProjectS ▸ Create Page Class
    ///
    /// 소개 패널의 "TV 켜지는" 등장 연출에 쓸 애니메이션 클립과 컨트롤러도 함께 만든다(없을 때만).
    /// 클립은 localScale 세 키(점 → 가로선 → 완전히 열림)와 흰 잔광 알파가 전부라
    /// 손으로 찍기보다 수치를 코드에 박아두는 편이 다시 뽑기 쉽다.
    ///
    /// 일러스트는 아직 아트가 없어 자리만 잡은 단색 이미지다. 대각선 경계는 UI가 아니라
    /// <b>PNG의 알파</b>로 만든다 — 실제 아트를 넣을 때 스프라이트만 꽂으면 된다.
    /// </summary>
    public static class ClassSelectPageGenerator
    {
        private const string UndoLabel = "Create Page Class";
        private const string AnimationFolder = "Assets/Animator/UI";
        private const string ClipPath = "Assets/Animator/UI/TV_PowerOn.anim";
        private const string ControllerPath = "Assets/Animator/UI/TV_PowerOn.controller";

        // 일러스트 두 장. 화면 가운데에 맞붙어 서고 대각선 경계는 아트의 알파가 만든다.
        private const float IllustWidth = 420f;
        private const float IllustHeight = 740f;
        private const float IllustOffsetX = 215f;
        private const float IllustOffsetY = 20f;

        // 소개 패널 자리. 고른 쪽의 반대편 — 상대 일러스트 자리 + 바깥 여백까지 덮는다.
        private const float IntroWidth = 864f;
        private const float IntroOffsetX = 470f;
        private const float IntroHeight = 720f;

        // TV 전원 연출 타이밍(초). 가로선까지 빠르게, 세로로 열리며 잔광이 사라진다.
        private const float LineTime = 0.05f;
        private const float OpenTime = 0.15f;
        private const float FlashStart = 0.10f;
        private const float FlashEnd = 0.18f;

        private static readonly Color IllustPlaceholderColor = new Color(0.19f, 0.22f, 0.28f, 1f);
        private static readonly Color PanelColor = new Color(0.1f, 0.12f, 0.16f, 0.96f);
        private static readonly Color VideoAreaColor = new Color(0.07f, 0.08f, 0.11f, 1f);
        private static readonly Color ButtonColor = new Color(0.18f, 0.21f, 0.27f, 1f);
        private static readonly Color AccentButtonColor = new Color(0.16f, 0.5f, 0.42f, 1f);
        private static readonly Color MutedTextColor = new Color(0.72f, 0.76f, 0.83f, 1f);

        [MenuItem("Tools/ProjectS/Create Page Class")]
        public static void CreatePageClass()
        {
            EntryUIBuilder.EnsureEventSystem(UndoLabel);
            Canvas canvas = EntryUIBuilder.EnsureCanvas(UndoLabel);

            Transform existing = canvas.transform.Find("Page_Class");
            if (existing != null)
            {
                bool rebuild = EditorUtility.DisplayDialog("Page_Class 다시 만들기",
                    "이미 Page_Class가 있습니다.\n지우고 새로 만들면 인스펙터에서 꽂은 일러스트·문구가 사라집니다.",
                    "지우고 다시 만들기", "취소");
                if (!rebuild) return;

                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            RuntimeAnimatorController controller = EnsureTvPowerOnController();
            GameObject page = BuildPage(canvas.transform, controller);

            Undo.RegisterCreatedObjectUndo(page, UndoLabel);
            Selection.activeGameObject = page;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[ClassSelectPageGenerator] Page_Class 생성 완료. 일러스트는 자리만 잡힌 단색 이미지입니다.");
        }

        // ── 페이지 ─────────────────────────────────────────────────

        private static GameObject BuildPage(Transform canvas, RuntimeAnimatorController controller)
        {
            GameObject page = new GameObject("Page_Class", typeof(RectTransform));
            page.transform.SetParent(canvas, false);
            EntryUIBuilder.Fill(page.GetComponent<RectTransform>());

            (Button warriorBtn, Image warriorImg) = BuildIllust(page.transform, "IllustWarrior", -IllustOffsetX);
            (Button gunnerBtn, Image gunnerImg) = BuildIllust(page.transform, "IllustGunner", IllustOffsetX);

            RectTransform slotLeft = BuildIntroSlot(page.transform, "IntroSlot_Left", -IntroOffsetX);
            RectTransform slotRight = BuildIntroSlot(page.transform, "IntroSlot_Right", IntroOffsetX);

            // 패널은 오른쪽 슬롯에서 시작한다(전사가 첫 선택일 때 가는 자리).
            (RectTransform panel, RawImage video, Image fallback, TextMeshProUGUI info) =
                BuildIntroPanel(slotRight, controller);

            (Button prev, Button select) = BuildBottomBar(page.transform);

            ClassSelectPageView view = page.AddComponent<ClassSelectPageView>();
            EntryUIBuilder.Wire(view,
                ("warriorButton", warriorBtn),
                ("gunnerButton", gunnerBtn),
                ("warriorImage", warriorImg),
                ("gunnerImage", gunnerImg),
                ("introSlotLeft", slotLeft),
                ("introSlotRight", slotRight),
                ("introPanel", panel),
                ("videoArea", video),
                ("fallbackImage", fallback),
                ("infoText", info),
                ("prevButton", prev),
                ("selectButton", select));

            panel.gameObject.SetActive(false);
            select.interactable = false;

            return page;
        }

        // 일러스트 한 장. 지금은 단색 자리표시자이고, 실제 아트는 대각선으로 잘린 PNG를
        // 이 Image의 Source Image에 꽂으면 된다.
        private static (Button button, Image image) BuildIllust(Transform parent, string name, float offsetX)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            EntryUIBuilder.SetCenter(go.GetComponent<RectTransform>(),
                new Vector2(offsetX, IllustOffsetY), new Vector2(IllustWidth, IllustHeight));

            Image img = go.GetComponent<Image>();
            img.color = IllustPlaceholderColor;

            // 아트를 꽂은 뒤 alphaHitTestMinimumThreshold를 0.1로 올려야 대각선 경계에서
            // 클릭이 옆 일러스트로 새지 않는다. 스프라이트 텍스처의 Read/Write Enabled가 필수라
            // 여기서는 켜지 않고 0으로 둔다(스프라이트 없이 켜면 클릭 시 예외가 날 수 있다).
            img.alphaHitTestMinimumThreshold = 0f;

            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;

            TextMeshProUGUI label = EntryUIBuilder.CreateTMP(go.transform, "PlaceholderLabel",
                name == "IllustWarrior" ? "전사 일러스트" : "거너 일러스트", 24f);
            label.alignment = TextAlignmentOptions.Center;
            label.color = MutedTextColor;
            EntryUIBuilder.Fill(label.rectTransform);

            return (btn, img);
        }

        // 패널이 놓일 빈 자리. 레이아웃 컴포넌트를 붙이면 패널의 스케일 애니메이션과 값이 부딪힌다.
        private static RectTransform BuildIntroSlot(Transform parent, string name, float offsetX)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            EntryUIBuilder.SetCenter(rt, new Vector2(offsetX, IllustOffsetY), new Vector2(IntroWidth, IntroHeight));
            return rt;
        }

        private static (RectTransform panel, RawImage video, Image fallback, TextMeshProUGUI info)
            BuildIntroPanel(Transform slot, RuntimeAnimatorController controller)
        {
            GameObject go = new GameObject("IntroPanel", typeof(RectTransform), typeof(Image), typeof(Animator));
            go.transform.SetParent(slot, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            EntryUIBuilder.Fill(rt);
            rt.pivot = new Vector2(0.5f, 0.5f);   // 가운데에서 열려야 TV 느낌이 난다

            Image bg = go.GetComponent<Image>();
            bg.sprite = EntryUIBuilder.UISprite;
            bg.type = Image.Type.Sliced;
            bg.color = PanelColor;
            bg.raycastTarget = true;

            Animator animator = go.GetComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;   // 로딩·정지 중에도 연출이 돈다

            GameObject videoGo = new GameObject("VideoArea", typeof(RectTransform), typeof(RawImage));
            videoGo.transform.SetParent(go.transform, false);
            EntryUIBuilder.StretchTop(videoGo.GetComponent<RectTransform>(), -480f, -40f, 40f);

            RawImage video = videoGo.GetComponent<RawImage>();
            video.color = Color.white;
            video.raycastTarget = false;
            video.enabled = false;   // 영상이 없으면 대체 이미지가 대신 보인다

            Image fallback = EntryUIBuilder.CreateFullScreenImage(videoGo.transform, "FallbackImage", VideoAreaColor, false);

            TextMeshProUGUI fallbackLabel = EntryUIBuilder.CreateTMP(fallback.transform, "Label", "클래스 소개 영상 (자리 예약)", 22f);
            fallbackLabel.alignment = TextAlignmentOptions.Center;
            fallbackLabel.color = MutedTextColor;
            EntryUIBuilder.Fill(fallbackLabel.rectTransform);

            TextMeshProUGUI info = EntryUIBuilder.CreateTMP(go.transform, "InfoText",
                "이름 · 이명\n나이 · 생일 · 사용 무기\n시작 위치", 24f);
            info.alignment = TextAlignmentOptions.TopLeft;
            info.color = Color.white;
            EntryUIBuilder.StretchTop(info.rectTransform, -680f, -510f, 40f);

            // 전원 들어올 때 번쩍이는 잔광. 클립이 알파를 0.6 → 0으로 떨어뜨린다.
            Image flash = EntryUIBuilder.CreateFullScreenImage(go.transform, "FlashImage", new Color(1f, 1f, 1f, 0f), false);
            flash.transform.SetAsLastSibling();

            return (rt, video, fallback, info);
        }

        private static (Button prev, Button select) BuildBottomBar(Transform parent)
        {
            GameObject bar = new GameObject("BottomBar", typeof(RectTransform));
            bar.transform.SetParent(parent, false);
            EntryUIBuilder.StretchBottom(bar.GetComponent<RectTransform>(), 40f, 116f, 60f);

            Button prev = EntryUIBuilder.CreateButton(bar.transform, "PrevButton", "이전", 24f, ButtonColor).button;
            RectTransform prevRt = prev.GetComponent<RectTransform>();
            prevRt.anchorMin = prevRt.anchorMax = new Vector2(0f, 0.5f);
            prevRt.pivot = new Vector2(0f, 0.5f);
            prevRt.anchoredPosition = Vector2.zero;
            prevRt.sizeDelta = new Vector2(200f, 64f);

            Button select = EntryUIBuilder.CreateButton(bar.transform, "SelectButton", "선택", 24f, AccentButtonColor).button;
            RectTransform selectRt = select.GetComponent<RectTransform>();
            selectRt.anchorMin = selectRt.anchorMax = new Vector2(1f, 0.5f);
            selectRt.pivot = new Vector2(1f, 0.5f);
            selectRt.anchoredPosition = Vector2.zero;
            selectRt.sizeDelta = new Vector2(200f, 64f);

            return (prev, select);
        }

        // ── TV 전원 연출 클립 ───────────────────────────────────────

        private static RuntimeAnimatorController EnsureTvPowerOnController()
        {
            AnimatorController existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (existing != null) return existing;

            EntryUIBuilder.EnsureFolder(AnimationFolder);

            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            if (clip == null)
            {
                clip = BuildTvPowerOnClip();
                AssetDatabase.CreateAsset(clip, ClipPath);
            }

            // 기본 상태 하나뿐인 컨트롤러. Entry가 이 상태를 가리키므로 오브젝트가
            // 비활성→활성될 때 처음부터 다시 재생된다(재생 코드 불필요).
            return AnimatorController.CreateAnimatorControllerAtPathWithClip(ControllerPath, clip);
        }

        private static AnimationClip BuildTvPowerOnClip()
        {
            AnimationClip clip = new AnimationClip { frameRate = 60f };

            // 점 → 가로선 → 세로로 열림. 가로가 먼저 다 펴진 뒤 세로가 열려야 브라운관처럼 보인다.
            clip.SetCurve("", typeof(Transform), "m_LocalScale.x", new AnimationCurve(
                new Keyframe(0f, 0.05f),
                new Keyframe(LineTime, 1f),
                new Keyframe(OpenTime, 1f)));

            clip.SetCurve("", typeof(Transform), "m_LocalScale.y", new AnimationCurve(
                new Keyframe(0f, 0.02f),
                new Keyframe(LineTime, 0.02f),
                new Keyframe(OpenTime, 1f)));

            clip.SetCurve("", typeof(Transform), "m_LocalScale.z", AnimationCurve.Constant(0f, OpenTime, 1f));

            // 잔광은 다 열릴 무렵 번쩍였다 사라진다.
            clip.SetCurve("FlashImage", typeof(Image), "m_Color.a", new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(FlashStart, 0.6f),
                new Keyframe(FlashEnd, 0f)));

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;   // 반복되면 패널이 계속 깜빡인다
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            return clip;
        }
    }
}
