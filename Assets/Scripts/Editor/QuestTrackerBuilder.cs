using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectS.UI;
using ProjectS.UI.Framework;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 퀘스트 트래커(QuestList) 계층을 한 번에 만들어 주는 에디터 도구.
    /// 창·카드 프리팹·파편 프리팹·FX 오버레이·상세 팝업을 만들고 인스펙터 참조까지 연결한다.
    ///
    /// 손으로 조립하면 Layout Group 옵션 하나(특히 Child Force Expand Height)만 어긋나도
    /// 카드가 안 늘어나거나 간격이 벌어지는데, 원인을 찾기가 매우 번거롭다. 그 조합을 코드로 고정한다.
    ///
    /// 스프라이트는 프로젝트마다 고르는 것이 달라 비워 둔다(흰 사각형으로 보인다). 만든 뒤 채우면 된다.
    /// 폴더 이름이 Editor인 것은 Unity가 에디터 전용 어셈블리로 인식하는 규칙 때문이고,
    /// 네임스페이스를 EditorTools로 둔 것은 UnityEditor.Editor 타입이 가려지는 것을 막기 위함이다.
    /// </summary>
    public static class QuestTrackerBuilder
    {
        private const string CardPrefabPath = "Assets/Prefabs/UI/QuestCard.prefab";
        private const string FragmentPrefabPath = "Assets/Prefabs/UI/HexFragment.prefab";
        private const string TogglePrefabPath = "Assets/Prefabs/UI/Toggle.prefab";
        private const string SweepPrefabPath = "Assets/Prefabs/UI/SweepLight.prefab";
        private const string SweepSpritePath = "Assets/Textures/UI/SPR_UI_SweepGradient.png";
        private const string SweepMaterialPath = "Assets/Materials/UI_SweepAdditive.mat";
        private const string AdditiveShaderName = "ProjectS/UI Additive";

        // 창 폭에서 카드가 차지하지 않고 비워 두는 왼쪽 여유. 선택 연출로 카드가 왼쪽으로 튀어나올 때
        // Viewport의 RectMask2D에 잘리지 않게 하려는 공간이다(QuestTrackerEntry.selectedOffset와 짝).
        private const float PopMargin = 100f;

        // 눈에 보이는 카드의 폭. 미니맵 가로(230)와 맞춰 HUD 우측 열의 세로선을 하나로 만든다.
        private const float CardWidth = 230f;

        // 창 rect는 카드보다 PopMargin만큼 넓다. 그 왼쪽 여백은 투명하며, 선택 연출로 카드가
        // 왼쪽으로 튀어나올 때 Viewport의 RectMask2D에 잘리지 않게 하는 자리다.
        // PopMargin을 줄이면 QuestTrackerEntry.selectedOffset.x도 그 안으로 들어와야 한다.
        private const float WindowWidth = CardWidth + PopMargin;
        private const float ScrollMaxHeight = 230f;

        private static readonly Color TitleBlue = new Color(0.11f, 0.36f, 0.62f, 1f);
        private static readonly Color TitleOrange = new Color(0.93f, 0.53f, 0.13f, 1f);
        private static readonly Color TitleGreen = new Color(0.24f, 0.70f, 0.32f, 1f);
        private static readonly Color CardBg = new Color(0.06f, 0.13f, 0.22f, 0.85f);
        private static readonly Color DetailBg = new Color(0.08f, 0.18f, 0.30f, 0.75f);

        /// <summary>
        /// 선택한 RectTransform 아래에 퀘스트 트래커 전체 구조를 만든다.
        /// HUD의 TopRight처럼 트래커가 놓일 자리를 선택하고 실행한다.
        /// </summary>
        [MenuItem("Tools/ProjectS/퀘스트 트래커 구조 생성", false, 100)]
        public static void Build()
        {
            if (Selection.activeGameObject == null ||
                Selection.activeGameObject.GetComponent<RectTransform>() == null)
            {
                EditorUtility.DisplayDialog("퀘스트 트래커 생성",
                    "트래커를 놓을 부모를 먼저 선택하세요.\n(HUD의 TopRight 같은 RectTransform)", "확인");
                return;
            }

            RectTransform parent = (RectTransform)Selection.activeGameObject.transform;

            Canvas canvas = parent.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("퀘스트 트래커 생성",
                    "선택한 오브젝트가 Canvas 아래에 있지 않습니다.", "확인");
                return;
            }

            if (parent.GetComponentInChildren<ExpandableScrollList>(true) != null &&
                !EditorUtility.DisplayDialog("퀘스트 트래커 생성",
                    "이 아래에 이미 트래커로 보이는 것이 있습니다. 그래도 만들까요?", "만든다", "취소"))
            {
                return;
            }

            GameObject fragmentPrefab = BuildFragmentPrefab();
            HexFragmentSpawner spawner = BuildFxLayer(canvas, fragmentPrefab);
            QuestDetailPopup popup = BuildDetailPopup(canvas.transform);
            GameObject cardPrefab = BuildCardPrefab();

            BuildWindow(parent, cardPrefab, popup);

            EditorSceneManager.MarkSceneDirty(parent.gameObject.scene);
            Debug.Log($"[QuestTrackerBuilder] 생성 완료. 카드 프리팹={CardPrefabPath}, 파편 프리팹={FragmentPrefabPath}, " +
                      $"FX 레이어={spawner.name}. 스프라이트와 스윕 프리팹은 인스펙터에서 채우세요.");
        }

        /// <summary>
        /// 스윕 라이트(카드 분해 때 왼쪽으로 흐르는 빛) 프리팹을 만들고 카드 프리팹에 연결한다.
        /// 그라디언트 스프라이트의 임포트 설정과 가산 합성 머티리얼까지 함께 맞춘다 —
        /// 셋 중 하나만 어긋나도 빛이 안 보이거나 흰 사각형으로 나온다.
        /// </summary>
        [MenuItem("Tools/ProjectS/스윕 라이트 프리팹 만들기", false, 104)]
        public static void BuildSweepPrefab()
        {
            Sprite sprite = ImportSweepSprite();
            if (sprite == null)
            {
                EditorUtility.DisplayDialog("스윕 라이트",
                    $"그라디언트 스프라이트를 찾지 못했습니다:\n{SweepSpritePath}", "확인");
                return;
            }

            Material material = EnsureAdditiveMaterial();

            RectTransform sweep = NewRect("SweepLight", null);
            // 오른쪽 끝(밝은 쪽)이 카드의 왼쪽 가장자리에 붙고, 꼬리가 왼쪽으로 흐르도록 피벗을 오른쪽에 둔다.
            sweep.pivot = new Vector2(1f, 0.5f);
            sweep.sizeDelta = new Vector2(160f, 64f);   // 높이는 재생 시 카드 높이로 덮어쓴다

            Image image = sweep.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.color = new Color(0.45f, 1f, 0.6f, 1f);   // 완료 녹색보다 밝게 — 가산이라 흰빛으로 번진다
            image.raycastTarget = false;
            if (material != null) image.material = material;

            GameObject asset = SaveAsPrefab(sweep.gameObject, SweepPrefabPath);
            Object.DestroyImmediate(sweep.gameObject);

            if (asset == null) return;

            int wired = WireSweepIntoCard(asset.GetComponent<RectTransform>());
            Debug.Log($"[QuestTrackerBuilder] 스윕 라이트 프리팹 생성: {SweepPrefabPath} " +
                      $"(머티리얼={(material != null ? material.name : "기본")}, 카드 연결 {wired}건)");
        }

        // PNG를 UI 스프라이트로 임포트되게 맞춘다. 기본 임포트 설정이면 Texture로 들어와 Image에 못 넣는다.
        private static Sprite ImportSweepSprite()
        {
            var importer = AssetImporter.GetAtPath(SweepSpritePath) as TextureImporter;
            if (importer == null) return null;

            bool dirty = importer.textureType != TextureImporterType.Sprite ||
                         importer.spriteImportMode != SpriteImportMode.Single ||
                         importer.mipmapEnabled ||
                         !importer.alphaIsTransparency ||
                         importer.wrapMode != TextureWrapMode.Clamp;

            if (dirty)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;              // UI라 밉맵은 흐려지기만 한다
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;   // 반복되면 꼬리 끝에 밝은 띠가 생긴다
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(SweepSpritePath);
        }

        // 가산 셰이더가 있으면 그것으로 머티리얼을 만든다. 없으면 null(=UI 기본 머티리얼)로 두고 경고만 남긴다.
        private static Material EnsureAdditiveMaterial()
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(SweepMaterialPath);
            if (existing != null) return existing;

            Shader shader = Shader.Find(AdditiveShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[QuestTrackerBuilder] '{AdditiveShaderName}' 셰이더를 찾지 못했습니다. " +
                                 "기본 UI 머티리얼로 만듭니다(빛이 겹쳐도 밝아지지 않습니다).");
                return null;
            }

            string directory = System.IO.Path.GetDirectoryName(SweepMaterialPath);
            if (!AssetDatabase.IsValidFolder(directory))
            {
                Debug.LogWarning($"[QuestTrackerBuilder] 폴더가 없어 머티리얼을 만들지 못했습니다: {directory}");
                return null;
            }

            Material material = new Material(shader);
            AssetDatabase.CreateAsset(material, SweepMaterialPath);
            return material;
        }

        // 카드 프리팹 원본의 QuestCardDisintegrateFx에 스윕 프리팹을 연결한다.
        private static int WireSweepIntoCard(RectTransform sweepPrefab)
        {
            GameObject card = PrefabUtility.LoadPrefabContents(CardPrefabPath);
            if (card == null) return 0;

            int wired = 0;
            foreach (var fx in card.GetComponentsInChildren<QuestCardDisintegrateFx>(true))
            {
                new Wire(fx).Ref("sweepPrefab", sweepPrefab).Apply();
                wired++;
            }

            if (wired > 0) PrefabUtility.SaveAsPrefabAsset(card, CardPrefabPath);
            PrefabUtility.UnloadPrefabContents(card);
            return wired;
        }

        /// <summary>
        /// 상세 팝업만 다시 만든다. 전체 생성(<see cref="Build"/>)을 다시 돌리면 씬에 트래커가 하나 더 생기고
        /// 카드 프리팹까지 덮어써지므로, 팝업에 필드가 추가됐을 때 이쪽만 갈아끼우기 위한 메뉴다.
        /// 기존 팝업의 부모·위치·크기는 그대로 이어받고, QuestTrackerHud의 참조도 다시 연결한다.
        /// </summary>
        [MenuItem("Tools/ProjectS/퀘스트 상세 팝업 다시 만들기", false, 103)]
        public static void RebuildDetailPopup()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("상세 팝업 다시 만들기",
                    "플레이 모드에서는 변경이 저장되지 않습니다. 정지 후 실행하세요.", "확인");
                return;
            }

            QuestDetailPopup[] found =
                Object.FindObjectsByType<QuestDetailPopup>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            QuestDetailPopup old = found.Length > 0 ? found[0] : null;

            if (old != null && !EditorUtility.DisplayDialog("상세 팝업 다시 만들기",
                    "기존 팝업을 지우고 새로 만듭니다.\n" +
                    "부모·위치·크기는 유지되지만 스프라이트·폰트·색 같은 수동 편집은 사라집니다.\n\n계속할까요?",
                    "다시 만든다", "취소"))
            {
                return;
            }

            Transform parent = null;
            Vector2 position = Vector2.zero;
            Vector2 size = Vector2.zero;
            bool inherit = false;

            if (old != null)
            {
                RectTransform oldRect = (RectTransform)old.transform;
                parent = oldRect.parent;
                position = oldRect.anchoredPosition;
                size = oldRect.sizeDelta;
                inherit = true;

                Undo.DestroyObjectImmediate(old.gameObject);
            }

            if (parent == null)
            {
                QuestTrackerHud existing = Object.FindAnyObjectByType<QuestTrackerHud>();
                Canvas canvas = existing != null ? existing.GetComponentInParent<Canvas>() : Object.FindAnyObjectByType<Canvas>();
                if (canvas == null)
                {
                    EditorUtility.DisplayDialog("상세 팝업 다시 만들기", "Canvas를 찾지 못했습니다.", "확인");
                    return;
                }

                parent = canvas.rootCanvas.transform;
            }

            QuestDetailPopup created = BuildDetailPopup(parent);

            if (inherit)
            {
                RectTransform rect = (RectTransform)created.transform;
                rect.anchoredPosition = position;
                rect.sizeDelta = size;
            }

            QuestTrackerHud hud = Object.FindAnyObjectByType<QuestTrackerHud>();
            if (hud != null)
            {
                Undo.RecordObject(hud, "상세 팝업 다시 만들기");
                new Wire(hud).Ref("detailPopup", created).Apply();
                EditorUtility.SetDirty(hud);
            }
            else
            {
                Debug.LogWarning("[QuestTrackerBuilder] QuestTrackerHud를 찾지 못해 detailPopup 연결은 직접 해야 합니다.");
            }

            EditorSceneManager.MarkSceneDirty(created.gameObject.scene);
            Selection.activeGameObject = created.gameObject;
            Debug.Log("[QuestTrackerBuilder] 상세 팝업을 다시 만들고 QuestTrackerHud에 연결했습니다.");
        }

        /// <summary>
        /// 씬의 트래커와 카드 프리팹에서 Layout Group 옵션을 올바른 조합으로 되돌린다.
        ///
        /// 세로 목록은 두 축의 규칙이 반대다. Height는 Force Expand를 끄고(켜면 항목이 적을 때 간격이 벌어짐),
        /// Width는 켜야 한다(끄면 자식 폭이 preferred width가 되는데, 슬롯처럼 Graphic도 폭 값도 없는
        /// 오브젝트는 그게 0이라 카드가 계층에는 있는데 화면에는 안 보인다).
        /// 손으로 체크하면 빠뜨리기 쉬워서 한 번에 맞춘다.
        /// </summary>
        [MenuItem("Tools/ProjectS/퀘스트 트래커 레이아웃 점검", false, 102)]
        public static void FixLayoutFlags()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("레이아웃 점검",
                    "플레이 모드에서는 변경이 저장되지 않습니다. 정지 후 실행하세요.", "확인");
                return;
            }

            int changed = 0;

            QuestTrackerHud hud = Object.FindAnyObjectByType<QuestTrackerHud>();
            if (hud != null)
            {
                foreach (var group in hud.GetComponentsInChildren<LayoutGroup>(true))
                {
                    if (!FixGroup(group)) continue;

                    Undo.RecordObject(group, "레이아웃 점검");
                    EditorUtility.SetDirty(group);
                    changed++;
                }

                EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
            }
            else
            {
                Debug.LogWarning("[QuestTrackerBuilder] 씬에서 QuestTrackerHud를 찾지 못해 씬 쪽은 건너뜁니다.");
            }

            // 프리팹은 인스턴스가 아니라 원본을 고쳐야 다음에 만들어지는 카드에도 적용된다.
            GameObject card = PrefabUtility.LoadPrefabContents(CardPrefabPath);
            if (card != null)
            {
                int cardChanged = 0;
                foreach (var group in card.GetComponentsInChildren<LayoutGroup>(true))
                {
                    if (FixGroup(group)) cardChanged++;
                }

                if (cardChanged > 0) PrefabUtility.SaveAsPrefabAsset(card, CardPrefabPath);
                PrefabUtility.UnloadPrefabContents(card);
                changed += cardChanged;
            }

            Debug.Log($"[QuestTrackerBuilder] Layout Group {changed}개를 수정했습니다. " +
                      "0개면 이미 올바른 상태입니다 — 다른 원인을 봐야 합니다.");
        }

        // 올바른 조합으로 되돌리고, 실제로 바뀐 게 있으면 true.
        private static bool FixGroup(LayoutGroup group)
        {
            if (group is VerticalLayoutGroup vertical)
            {
                bool dirty = !vertical.childControlWidth || !vertical.childControlHeight ||
                             !vertical.childForceExpandWidth || vertical.childForceExpandHeight;
                if (!dirty) return false;

                vertical.childControlWidth = true;
                vertical.childControlHeight = true;
                vertical.childForceExpandWidth = true;    // 가로지르는 축 → 자식이 폭을 채워야 한다
                vertical.childForceExpandHeight = false;  // 쌓이는 축 → 켜면 간격이 벌어진다
                return true;
            }

            if (group is HorizontalLayoutGroup horizontal)
            {
                bool dirty = !horizontal.childControlWidth || !horizontal.childControlHeight ||
                             horizontal.childForceExpandWidth || horizontal.childForceExpandHeight;
                if (!dirty) return false;

                horizontal.childControlWidth = true;
                horizontal.childControlHeight = true;
                horizontal.childForceExpandWidth = false;   // 가로 줄에서는 Width가 나눠 갖는 축
                horizontal.childForceExpandHeight = false;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 파편 스포너를 하나로 합치고, 연출 레이어를 올바른 자리(Canvas 직속 마지막 자식)로 옮긴다.
        /// 빌더를 여러 번 돌리거나 손으로 스포너를 추가하면 씬에 여러 개가 생기는데,
        /// <c>QuestCardDisintegrateFx</c>가 FindAnyObjectByType으로 찾기 때문에 어느 것을 집을지 알 수 없다.
        /// 또 레이어가 Layout Group 안이나 마스크 안에 있으면 파편 좌표가 어긋나거나 잘린다.
        /// </summary>
        [MenuItem("Tools/ProjectS/퀘스트 FX 레이어 정리", false, 101)]
        public static void CleanUpFxLayer()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("FX 레이어 정리",
                    "플레이 모드에서는 변경이 저장되지 않습니다. 정지 후 실행하세요.", "확인");
                return;
            }

            HexFragmentSpawner[] spawners =
                Object.FindObjectsByType<HexFragmentSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (spawners.Length == 0)
            {
                EditorUtility.DisplayDialog("FX 레이어 정리", "씬에 HexFragmentSpawner가 없습니다.", "확인");
                return;
            }

            Canvas canvas = FindTrackerCanvas(spawners[0]);
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("FX 레이어 정리", "Canvas를 찾지 못했습니다.", "확인");
                return;
            }

            // 스프라이트가 실제로 들어 있는 프리팹을 우선한다. 비어 있으면 흰 사각형으로 나와 연출이 안 보인다.
            HexFragment chosen = null;
            int poolSize = 24;
            foreach (HexFragmentSpawner spawner in spawners)
            {
                HexFragment candidate = ReadPrefab(spawner);
                if (candidate == null) continue;

                if (chosen == null || (!HasSprite(chosen) && HasSprite(candidate)))
                {
                    chosen = candidate;
                    poolSize = Mathf.Max(poolSize, ReadPoolSize(spawner));
                }
            }

            if (chosen == null)
                Debug.LogWarning("[QuestTrackerBuilder] 스포너에 파편 프리팹이 하나도 연결돼 있지 않습니다.");
            else if (!HasSprite(chosen))
                Debug.LogWarning($"[QuestTrackerBuilder] 파편 프리팹 '{chosen.name}'에 스프라이트가 없습니다. 흰 사각형으로 보입니다.");

            // 남길 레이어를 만들고, 기존 것은 컴포넌트만 떼어낸다
            // (HUD 같은 공용 오브젝트에 붙어 있을 수 있어 오브젝트째 지우면 안 된다).
            RectTransform layer = EnsureFxLayer(canvas);
            int removed = 0;
            foreach (HexFragmentSpawner spawner in spawners)
            {
                if (spawner.transform == layer) continue;

                DestroyPooledChildren(spawner.transform);
                Undo.DestroyObjectImmediate(spawner);
                removed++;
            }

            HexFragmentSpawner kept = layer.GetComponent<HexFragmentSpawner>();
            if (kept == null) kept = Undo.AddComponent<HexFragmentSpawner>(layer.gameObject);

            new Wire(kept).Ref("prefab", chosen).Int("initialPoolSize", poolSize).Apply();

            EditorSceneManager.MarkSceneDirty(layer.gameObject.scene);
            Selection.activeGameObject = layer.gameObject;
            Debug.Log($"[QuestTrackerBuilder] 스포너 {removed}개 제거, '{layer.name}'만 남겼습니다. " +
                      $"파편 프리팹={(chosen != null ? chosen.name : "없음")}, 풀 크기={poolSize}");
        }

        // 파편이 잘리거나 좌표가 어긋나지 않는 자리 = Canvas 직속 마지막 자식.
        // Layout Group 안이면 앵커·크기가 덮어써지고, 마스크 안이면 벗어난 파편이 잘린다.
        private static RectTransform EnsureFxLayer(Canvas canvas)
        {
            RectTransform canvasRect = (RectTransform)canvas.transform;

            for (int i = 0; i < canvasRect.childCount; i++)
            {
                Transform child = canvasRect.GetChild(i);
                if (child.name != "QuestFxLayer") continue;

                RectTransform found = (RectTransform)child;
                Undo.RecordObject(found, "FX 레이어 정리");
                Stretch(found, 0f);
                found.SetAsLastSibling();
                return found;
            }

            RectTransform layer = NewRect("QuestFxLayer", canvasRect);
            Stretch(layer, 0f);
            layer.SetAsLastSibling();
            Undo.RegisterCreatedObjectUndo(layer.gameObject, "FX 레이어 정리");
            return layer;
        }

        // 플레이 중 만들어졌다가 남은 풀 인스턴스를 치운다(에디터 모드에선 보통 없다).
        private static void DestroyPooledChildren(Transform spawner)
        {
            for (int i = spawner.childCount - 1; i >= 0; i--)
            {
                Transform child = spawner.GetChild(i);
                if (child.GetComponent<HexFragment>() != null)
                    Undo.DestroyObjectImmediate(child.gameObject);
            }
        }

        private static Canvas FindTrackerCanvas(HexFragmentSpawner any)
        {
            QuestTrackerHud hud = Object.FindAnyObjectByType<QuestTrackerHud>();
            if (hud != null)
            {
                Canvas fromHud = hud.GetComponentInParent<Canvas>();
                if (fromHud != null) return fromHud.rootCanvas;
            }

            Canvas fromSpawner = any != null ? any.GetComponentInParent<Canvas>() : null;
            return fromSpawner != null ? fromSpawner.rootCanvas : Object.FindAnyObjectByType<Canvas>();
        }

        private static HexFragment ReadPrefab(HexFragmentSpawner spawner)
        {
            SerializedProperty property = new SerializedObject(spawner).FindProperty("prefab");
            return property != null ? property.objectReferenceValue as HexFragment : null;
        }

        private static int ReadPoolSize(HexFragmentSpawner spawner)
        {
            SerializedProperty property = new SerializedObject(spawner).FindProperty("initialPoolSize");
            return property != null ? property.intValue : 0;
        }

        private static bool HasSprite(HexFragment fragment)
        {
            if (fragment == null) return false;

            Image image = fragment.GetComponent<Image>();
            return image != null && image.sprite != null;
        }

        // ---------- 창 ----------

        private static void BuildWindow(RectTransform parent, GameObject cardPrefab, QuestDetailPopup popup)
        {
            RectTransform window = NewRect("QuestList", parent);
            // 우상단 고정, 아래로 자란다. 항목이 늘어도 미니맵 아래 시작점이 흔들리지 않는다.
            window.anchorMin = new Vector2(1f, 1f);
            window.anchorMax = new Vector2(1f, 1f);
            window.pivot = new Vector2(1f, 1f);
            window.anchoredPosition = new Vector2(-10f, -10f);
            window.sizeDelta = new Vector2(WindowWidth, 0f);

            VerticalLayoutGroup windowLayout = window.gameObject.AddComponent<VerticalLayoutGroup>();
            SetupVertical(windowLayout, spacing: 2f);

            ContentSizeFitter windowFitter = window.gameObject.AddComponent<ContentSizeFitter>();
            windowFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            windowFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // --- 헤더 ---
            RectTransform header = NewRect("Header", window);
            LayoutElement headerLayout = header.gameObject.AddComponent<LayoutElement>();
            headerLayout.minHeight = 36f;
            headerLayout.preferredHeight = 36f;

            HorizontalLayoutGroup headerRow = header.gameObject.AddComponent<HorizontalLayoutGroup>();
            SetupHorizontal(headerRow, spacing: 6f);
            // 헤더 글자가 카드와 같은 세로선에서 시작하도록 왼쪽 여유를 똑같이 준다.
            headerRow.padding = new RectOffset((int)PopMargin, 8, 0, 0);

            TMP_Text headerLabel = NewText("Label", header, "퀘스트", 18f, TextAlignmentOptions.Left);
            LayoutElement headerLabelLayout = headerLabel.gameObject.AddComponent<LayoutElement>();
            headerLabelLayout.flexibleWidth = 1f;

            RectTransform foldButtonRect = NewRect("FoldButton", header);
            Image foldImage = foldButtonRect.gameObject.AddComponent<Image>();
            foldImage.color = new Color(1f, 1f, 1f, 0.08f);
            Button foldButton = foldButtonRect.gameObject.AddComponent<Button>();
            foldButton.targetGraphic = foldImage;
            LayoutElement foldLayout = foldButtonRect.gameObject.AddComponent<LayoutElement>();
            foldLayout.preferredWidth = 28f;
            foldLayout.preferredHeight = 28f;

            RectTransform arrow = NewRect("Arrow", foldButtonRect);
            Stretch(arrow, 4f);
            Image arrowImage = arrow.gameObject.AddComponent<Image>();
            arrowImage.raycastTarget = false;

            // --- 스크롤 영역 ---
            RectTransform scrollView = NewRect("ScrollView", window);
            LayoutElement scrollLayout = scrollView.gameObject.AddComponent<LayoutElement>();
            // ExpandableScrollList가 매 Refresh마다 덮어쓰므로 여기 값은 초기치일 뿐이다.
            scrollLayout.preferredHeight = ScrollMaxHeight;

            ScrollRect scrollRect = scrollView.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.inertia = false;
            scrollRect.scrollSensitivity = 20f;

            RectTransform viewport = NewRect("Viewport", scrollView);
            Stretch(viewport, 0f);
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = NewRect("Content", viewport);
            // 위에서 아래로 자라야 스크롤 시작 위치가 맨 위가 된다.
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 0f);

            VerticalLayoutGroup contentLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            SetupVertical(contentLayout, spacing: 6f);
            // 카드를 오른쪽으로 밀어 왼쪽에 튀어나올 여유를 남긴다.
            contentLayout.padding = new RectOffset((int)PopMargin, 0, 0, 0);

            ContentSizeFitter contentFitter = content.gameObject.AddComponent<ContentSizeFitter>();
            contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = viewport;
            scrollRect.content = content;

            // --- 컴포넌트 연결 ---
            ExpandableScrollList list = window.gameObject.AddComponent<ExpandableScrollList>();
            new Wire(list)
                .Ref("scrollRect", scrollRect)
                .Ref("scrollAreaLayout", scrollLayout)
                .Ref("content", content)
                .Float("maxHeight", ScrollMaxHeight)
                .Ref("foldButton", foldButton)
                .Ref("foldArrow", arrow)
                .Apply();

            QuestTrackerHud hud = window.gameObject.AddComponent<QuestTrackerHud>();
            new Wire(hud)
                .Ref("window", list)
                .Ref("content", content)
                .Ref("cardPrefab", cardPrefab != null ? cardPrefab.GetComponent<QuestTrackerEntry>() : null)
                .Ref("detailPopup", popup)
                .Apply();

            Undo.RegisterCreatedObjectUndo(window.gameObject, "퀘스트 트래커 구조 생성");
            Selection.activeGameObject = window.gameObject;
        }

        // ---------- 카드 프리팹 ----------

        private static GameObject BuildCardPrefab()
        {
            // 슬롯: 레이아웃이 배치하는 빈 껍데기. 높이만 차지하고 그림은 없다.
            // 여기 폭은 초기값일 뿐이고, 런타임에는 Content의 Layout Group이 정한다.
            RectTransform slot = NewRect("QuestCard", null);
            slot.sizeDelta = new Vector2(CardWidth, 60f);
            LayoutElement slotLayout = slot.gameObject.AddComponent<LayoutElement>();
            slotLayout.preferredHeight = 60f;

            // 비주얼: 슬롯 안에서 레이아웃 통제를 받지 않는 자유 오브젝트.
            RectTransform visual = NewRect("Visual", slot);
            visual.anchorMin = new Vector2(0f, 1f);
            visual.anchorMax = new Vector2(1f, 1f);
            visual.pivot = new Vector2(0.5f, 1f);
            visual.anchoredPosition = Vector2.zero;
            visual.sizeDelta = new Vector2(0f, 60f);

            VerticalLayoutGroup visualLayout = visual.gameObject.AddComponent<VerticalLayoutGroup>();
            SetupVertical(visualLayout, spacing: 0f);

            ContentSizeFitter visualFitter = visual.gameObject.AddComponent<ContentSizeFitter>();
            visualFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            visualFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            CanvasGroup visualGroup = visual.gameObject.AddComponent<CanvasGroup>();
            RectMask2D visualMask = visual.gameObject.AddComponent<RectMask2D>();

            // 배경: 레이아웃에서 빼야 카드 높이가 이중 계산되지 않는다.
            RectTransform bg = NewRect("BG", visual);
            Stretch(bg, 0f);
            Image bgImage = bg.gameObject.AddComponent<Image>();
            bgImage.color = CardBg;
            bgImage.raycastTarget = false;
            bg.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            // --- 제목 영역: 누르면 고정 ---
            RectTransform titleArea = NewRect("TitleArea", visual);
            Button titleButton = titleArea.gameObject.AddComponent<Button>();

            LayoutElement titleLayout = titleArea.gameObject.AddComponent<LayoutElement>();
            titleLayout.minHeight = 30f;

            HorizontalLayoutGroup titleRow = titleArea.gameObject.AddComponent<HorizontalLayoutGroup>();
            SetupHorizontal(titleRow, spacing: 6f);
            titleRow.padding = new RectOffset(8, 8, 3, 3);

            // 배경은 이 영역에 직접 붙이지 않고 자식으로 내린다.
            // Image는 ILayoutElement라 스프라이트를 넣는 순간 '원본 픽셀 크기'를 preferred size로 보고하고,
            // 부모의 Child Control Height가 그 값을 그대로 높이로 써서 줄이 스프라이트만큼 부풀어 오른다.
            // 자식 + Ignore Layout으로 빼면 레이아웃 계산에서 제외돼 어떤 스프라이트를 넣어도 높이가 안 변한다.
            Image titleBackground = NewAreaBackground("TitleBg", titleArea, TitleBlue);
            titleButton.targetGraphic = titleBackground;

            Toggle completedToggle = InstantiateToggle(titleArea);

            TMP_Text titleText = NewText("QuestTitle", titleArea, "퀘스트 제목", 20f, TextAlignmentOptions.Left);
            titleText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            TMP_Text objectiveCount = NewText("ObjectiveCount", titleArea, "0/0", 16f, TextAlignmentOptions.Right);
            objectiveCount.gameObject.AddComponent<LayoutElement>().preferredWidth = 46f;

            TMP_Text extraCount = NewText("ExtraCount", titleArea, "+0", 16f, TextAlignmentOptions.Right);
            extraCount.gameObject.AddComponent<LayoutElement>().preferredWidth = 34f;
            // 완료 요약 줄에서만 쓰므로 평소에는 꺼 둔다(QuestTrackerEntry가 필요할 때 켠다).
            extraCount.gameObject.SetActive(false);

            // --- 내용 영역: 누르면 상세 팝업 ---
            RectTransform detailArea = NewRect("DetailArea", visual);
            Button detailButton = detailArea.gameObject.AddComponent<Button>();

            VerticalLayoutGroup detailLayout = detailArea.gameObject.AddComponent<VerticalLayoutGroup>();
            SetupVertical(detailLayout, spacing: 2f);
            detailLayout.padding = new RectOffset(10, 10, 6, 8);

            // 제목 영역과 같은 이유로 배경을 자식으로 뺀다(스프라이트 원본 크기가 높이를 밀어 올리는 것 방지).
            Image detailBackground = NewAreaBackground("DetailBg", detailArea, DetailBg);
            detailButton.targetGraphic = detailBackground;

            // 여기엔 Content Size Fitter를 붙이지 않는다. 부모(Visual)의 Layout Group이 Child Control Height로
            // 이미 이 영역의 높이를 정하므로, 붙이면 두 컴포넌트가 같은 높이를 서로 덮어쓰며 싸운다.

            // TMP 기본값이 이미 줄바꿈 Normal + Overflow라 카드가 내용만큼 늘어난다.
            // Auto Size를 켜거나 Overflow를 Truncate로 바꾸면 그 순간 안 늘어나니 건드리지 말 것.
            TMP_Text progressText = NewText("QuestDetails", detailArea, "퀘스트 내용", 16f, TextAlignmentOptions.TopLeft);
            // 두 줄 상한을 QuestTrackerEntry가 이 LayoutElement의 preferredHeight로 써 넣는다.
            LayoutElement progressLayout = progressText.gameObject.AddComponent<LayoutElement>();

            // --- 컴포넌트 연결 ---
            QuestCardDisintegrateFx fx = slot.gameObject.AddComponent<QuestCardDisintegrateFx>();
            new Wire(fx)
                .Ref("visual", visual)
                .Ref("visualMask", visualMask)
                .Color("fragmentColor", TitleGreen)
                .Apply();

            QuestTrackerEntry entry = slot.gameObject.AddComponent<QuestTrackerEntry>();
            new Wire(entry)
                .Ref("visual", visual)
                .Ref("visualGroup", visualGroup)
                .Ref("visualFitter", visualFitter)
                .Ref("titleButton", titleButton)
                .Ref("titleBackground", titleBackground)
                .Color("unpinnedColor", TitleBlue)
                .Color("pinnedColor", TitleOrange)
                .Color("completedColor", TitleGreen)
                .Ref("titleText", titleText)
                .Ref("objectiveCountText", objectiveCount)
                .Ref("completedToggle", completedToggle)
                .Ref("extraCountText", extraCount)
                .Ref("detailArea", detailArea.gameObject)
                .Ref("detailButton", detailButton)
                .Ref("progressText", progressText)
                .Ref("progressLayout", progressLayout)
                .Int("maxProgressLines", 2)
                .Ref("disintegrateFx", fx)
                .Apply();

            GameObject asset = SaveAsPrefab(slot.gameObject, CardPrefabPath);
            Object.DestroyImmediate(slot.gameObject);
            return asset;
        }

        // 이미 있는 Toggle 프리팹을 재사용한다. 없으면 최소 구성으로 만든다.
        private static Toggle InstantiateToggle(RectTransform parent)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(TogglePrefabPath);
            GameObject instance;

            if (source != null)
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            }
            else
            {
                instance = NewRect("Toggle", parent).gameObject;
                RectTransform background = NewRect("Background", (RectTransform)instance.transform);
                Stretch(background, 0f);
                background.gameObject.AddComponent<Image>();

                RectTransform checkmark = NewRect("Checkmark", background);
                Stretch(checkmark, 2f);
                checkmark.gameObject.AddComponent<Image>();

                Debug.LogWarning($"[QuestTrackerBuilder] {TogglePrefabPath}를 찾지 못해 빈 토글을 만들었습니다.");
            }

            Toggle toggle = instance.GetComponent<Toggle>();
            if (toggle == null) toggle = instance.AddComponent<Toggle>();

            // 표시 전용이라 코드가 interactable을 끈다. Transition이 남아 있으면 비활성 색으로 어두워진다.
            toggle.transition = Selectable.Transition.None;
            toggle.isOn = false;

            LayoutElement layout = instance.GetComponent<LayoutElement>();
            if (layout == null) layout = instance.AddComponent<LayoutElement>();
            layout.preferredWidth = 25f;
            layout.preferredHeight = 25f;

            return toggle;
        }

        // ---------- 파편 프리팹 / FX 오버레이 ----------

        private static GameObject BuildFragmentPrefab()
        {
            RectTransform fragment = NewRect("HexFragment", null);
            fragment.sizeDelta = new Vector2(24f, 24f);

            Image image = fragment.gameObject.AddComponent<Image>();
            image.color = TitleGreen;
            image.raycastTarget = false;

            fragment.gameObject.AddComponent<HexFragment>();

            GameObject asset = SaveAsPrefab(fragment.gameObject, FragmentPrefabPath);
            Object.DestroyImmediate(fragment.gameObject);
            return asset;
        }

        private static HexFragmentSpawner BuildFxLayer(Canvas canvas, GameObject fragmentPrefab)
        {
            // 스크롤 뷰의 RectMask2D 바깥이어야 파편이 잘리지 않는다. 캔버스 최상위 마지막 자식에 둔다.
            RectTransform layer = NewRect("QuestFxLayer", (RectTransform)canvas.transform);
            Stretch(layer, 0f);
            layer.SetAsLastSibling();

            HexFragmentSpawner spawner = layer.gameObject.AddComponent<HexFragmentSpawner>();
            new Wire(spawner)
                .Ref("prefab", fragmentPrefab != null ? fragmentPrefab.GetComponent<HexFragment>() : null)
                .Int("initialPoolSize", 24)
                .Apply();

            Undo.RegisterCreatedObjectUndo(layer.gameObject, "퀘스트 트래커 구조 생성");
            return spawner;
        }

        // ---------- 상세 팝업 ----------

        private static QuestDetailPopup BuildDetailPopup(Transform parent)
        {
            // UIManager 하위일 필요가 없다 — QuestTrackerHud가 RegisterPopup으로 등록시킨다.
            // (UIManager는 Bootstrap 씬에 있고 자기 자식에서만 BasePopup을 수집하므로, HUD 씬의 팝업은 스스로 등록해야 한다.)
            RectTransform popup = NewRect("QuestDetailPopup", (RectTransform)parent);
            popup.anchorMin = new Vector2(0.5f, 0.5f);
            popup.anchorMax = new Vector2(0.5f, 0.5f);
            popup.pivot = new Vector2(0.5f, 0.5f);
            popup.anchoredPosition = new Vector2(-260f, 120f);
            popup.sizeDelta = new Vector2(660f, 360f);

            Image popupBg = popup.gameObject.AddComponent<Image>();
            popupBg.color = new Color(0.06f, 0.45f, 0.75f, 0.92f);

            TMP_Text title = NewText("Title", popup, "퀘스트 제목", 26f, TextAlignmentOptions.TopLeft);
            Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -20f),
                new Vector2(-60f, 40f), new Vector2(0.5f, 1f));

            TMP_Text story = NewText("Story", popup, "퀘스트 상세 스토리", 20f, TextAlignmentOptions.TopLeft);
            Anchor(story.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -76f),
                new Vector2(-60f, 200f), new Vector2(0.5f, 1f));

            TMP_Text reward = NewText("Reward", popup, "퀘스트 보상", 20f, TextAlignmentOptions.BottomRight);
            Anchor(reward.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 20f),
                new Vector2(-60f, 60f), new Vector2(0.5f, 0f));

            // 닫기(X) 버튼. Esc·바깥 클릭을 몰라도 눈에 보이는 닫기 수단이 하나는 있어야 한다.
            RectTransform closeRect = NewRect("CloseButton", popup);
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-12f, -12f);
            closeRect.sizeDelta = new Vector2(32f, 32f);

            Image closeImage = closeRect.gameObject.AddComponent<Image>();
            closeImage.color = new Color(1f, 1f, 1f, 0.15f);
            Button closeButton = closeRect.gameObject.AddComponent<Button>();
            closeButton.targetGraphic = closeImage;

            TMP_Text closeLabel = NewText("Label", closeRect, "X", 20f, TextAlignmentOptions.Center);
            Stretch(closeLabel.rectTransform, 0f);

            // 연결선이 시작되는 지점(팝업 오른쪽 가장자리). 그림은 없고 위치만 쓴다.
            RectTransform connectorOrigin = NewRect("ConnectorOrigin", popup);
            connectorOrigin.anchorMin = new Vector2(1f, 0.5f);
            connectorOrigin.anchorMax = new Vector2(1f, 0.5f);
            connectorOrigin.pivot = new Vector2(0.5f, 0.5f);
            connectorOrigin.anchoredPosition = Vector2.zero;
            connectorOrigin.sizeDelta = new Vector2(2f, 2f);

            // 오른쪽으로 자라야 하므로 pivot을 왼쪽 끝에 둔다(코드가 길이·각도를 매 프레임 갱신).
            RectTransform connector = NewRect("Connector", popup);
            connector.anchorMin = new Vector2(0.5f, 0.5f);
            connector.anchorMax = new Vector2(0.5f, 0.5f);
            connector.pivot = new Vector2(0f, 0.5f);
            connector.sizeDelta = new Vector2(10f, 2f);
            Image connectorImage = connector.gameObject.AddComponent<Image>();
            connectorImage.color = new Color(0.35f, 0.8f, 1f, 0.9f);
            connectorImage.raycastTarget = false;

            QuestDetailPopup component = popup.gameObject.AddComponent<QuestDetailPopup>();
            new Wire(component)
                .Ref("titleText", title)
                .Ref("storyText", story)
                .Ref("rewardText", reward)
                .Ref("connector", connector)
                .Ref("connectorOrigin", connectorOrigin)
                .Ref("closeButton", closeButton)
                .Apply();

            // BasePopup.Show가 켜 주므로 꺼진 상태로 둔다.
            popup.gameObject.SetActive(false);

            Undo.RegisterCreatedObjectUndo(popup.gameObject, "퀘스트 트래커 구조 생성");
            return component;
        }

        // ---------- 공통 헬퍼 ----------

        private static RectTransform NewRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)go.transform;
            if (parent != null) rect.SetParent(parent, false);

            rect.localScale = Vector3.one;
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        /// <summary>
        /// 클릭 영역의 배경 이미지를 자식으로 만든다. 부모에 직접 붙이면 스프라이트 원본 크기가
        /// 그 영역의 preferred size로 보고돼 높이가 스프라이트에 끌려간다.
        /// Ignore Layout으로 레이아웃에서 빼고 stretch로 부모를 덮게 하면 크기는 부모를 따라가면서
        /// 계산에는 관여하지 않는다. raycastTarget은 켠 채로 둬야 부모의 Button이 클릭을 받는다.
        /// </summary>
        private static Image NewAreaBackground(string name, RectTransform parent, Color color)
        {
            RectTransform rect = NewRect(name, parent);
            Stretch(rect, 0f);
            rect.SetAsFirstSibling();

            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = true;
            rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            return image;
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static void Anchor(RectTransform rect, Vector2 min, Vector2 max,
            Vector2 position, Vector2 size, Vector2 pivot)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        // 세로 목록에서 두 축의 규칙이 반대다.
        //  - Height(쌓이는 축): Force Expand를 끈다. 켜면 남는 세로 공간이 항목마다 배분돼 간격이 벌어진다.
        //  - Width(가로지르는 축): Force Expand를 켠다. 끄면 자식 폭이 preferred width가 되는데,
        //    슬롯처럼 Graphic도 LayoutElement 폭도 없는 오브젝트는 그 값이 0이라 통째로 안 보인다.
        private static void SetupVertical(VerticalLayoutGroup group, float spacing)
        {
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            group.childAlignment = TextAnchor.UpperLeft;
            group.spacing = spacing;
            group.padding = new RectOffset(0, 0, 0, 0);
        }

        // 가로 줄에서는 Width가 항목이 나눠 갖는 축이므로 Force Expand를 끈다
        // (켜면 남는 가로 공간이 항목마다 배분돼 아이콘과 글자 사이가 벌어진다).
        // 폭을 늘려 채울 항목은 그 항목의 LayoutElement.flexibleWidth로 지정한다.
        private static void SetupHorizontal(HorizontalLayoutGroup group, float spacing)
        {
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            group.childAlignment = TextAnchor.MiddleLeft;
            group.spacing = spacing;
            group.padding = new RectOffset(0, 0, 0, 0);
        }

        // 줄바꿈(Normal)과 넘침(Overflow)은 TMP 기본값이 이미 원하는 값이라 건드리지 않는다.
        private static TMP_Text NewText(string name, Transform parent, string text,
            float fontSize, TextAlignmentOptions alignment)
        {
            RectTransform rect = NewRect(name, parent);
            TextMeshProUGUI tmp = rect.gameObject.AddComponent<TextMeshProUGUI>();

            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = alignment;
            tmp.enableAutoSizing = false;   // Auto Size는 높이에 맞춰 폰트를 줄여 ContentSizeFitter와 충돌한다
            tmp.raycastTarget = false;
            return tmp;
        }

        private static GameObject SaveAsPrefab(GameObject go, string path)
        {
            string directory = System.IO.Path.GetDirectoryName(path);
            if (!AssetDatabase.IsValidFolder(directory))
            {
                Debug.LogError($"[QuestTrackerBuilder] 폴더가 없습니다: {directory}");
                return null;
            }

            return PrefabUtility.SaveAsPrefabAsset(go, path);
        }

        /// <summary>
        /// private [SerializeField] 필드를 이름으로 채우는 작은 헬퍼. 인스펙터에서 일일이 끌어다 놓는 작업을 대신한다.
        /// 필드 이름이 바뀌면 조용히 비는 대신 경고를 남긴다.
        /// </summary>
        private class Wire
        {
            private readonly SerializedObject serialized;
            private readonly Object target;

            public Wire(Object target)
            {
                this.target = target;
                serialized = new SerializedObject(target);
            }

            public Wire Ref(string field, Object value) => Set(field, p => p.objectReferenceValue = value);
            public Wire Float(string field, float value) => Set(field, p => p.floatValue = value);
            public Wire Int(string field, int value) => Set(field, p => p.intValue = value);
            public Wire Color(string field, Color value) => Set(field, p => p.colorValue = value);

            public void Apply() => serialized.ApplyModifiedPropertiesWithoutUndo();

            private Wire Set(string field, System.Action<SerializedProperty> assign)
            {
                SerializedProperty property = serialized.FindProperty(field);
                if (property == null)
                {
                    Debug.LogWarning($"[QuestTrackerBuilder] {target.GetType().Name}에 '{field}' 필드가 없습니다.");
                    return this;
                }

                assign(property);
                return this;
            }
        }
    }
}
