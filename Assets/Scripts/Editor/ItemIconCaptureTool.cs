// Editor 폴더 안에 있어 플레이어 빌드에는 포함되지 않는다(MinimapSnapshotTool과 같은 규칙).
// 네임스페이스 세그먼트를 Editor로 두면 UnityEditor.Editor 타입이 가려지므로 EditorTools를 쓴다.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 무기·장비 같은 3D 프리팹을 투명 배경의 아이템 아이콘 PNG로 일괄 렌더하는 도구.
    /// 메뉴: ProjectS > Icons > 아이템 아이콘 캡처.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>기존 아이콘 규격에 맞춘다.</b> ItemIcons 폴더의 아이콘은 1024px·투명 배경·Sprite이고, 파일명이 곧
    /// ItemData.IconAddress다. 결과물을 같은 폴더·같은 설정으로 저장하므로, 캡처 후 "아이템 아이콘 등록"만 돌리면 이어진다.
    /// 후광은 넣지 않는다 — 슬롯 UI가 안쪽 그림자·광택·테두리로 깊이감을 주고, 후광은 라운드 마스크에 잘려 얼룩이 된다.
    /// </para>
    /// <para>
    /// <b>현재 씬을 건드리지 않는다.</b> 에디터 전용 프리뷰 씬에 프리팹·카메라·조명을 만들어 찍고 닫는다.
    /// 씬의 태양광·후처리가 섞이지 않아 어느 씬을 열어 두든 같은 결과가 나온다.
    /// </para>
    /// <para>
    /// <b>자동 정렬</b>은 모델의 가장 긴 축을 화면 가로, 가장 얇은 축을 카메라 방향으로 돌린다 — 총·검이 옆모습으로
    /// 눕는다. 거기에 대각선 각도를 더해 RPG 아이콘의 사선 구도를 만든다. 방어구처럼 정면이 중요한 모델은 끄고 쓴다.
    /// </para>
    /// <para>
    /// 가장자리를 매끈하게 하려고 2배 해상도로 렌더한 뒤 줄인다. 투명 배경 위에 그린 가장자리는 색이 알파만큼
    /// 어두워지므로(premultiplied), 저장 전에 알파로 나눠 되돌린다 — 안 하면 UI에서 테두리에 검은 띠가 생긴다.
    /// </para>
    /// </remarks>
    public class ItemIconCaptureTool : EditorWindow
    {
        [Serializable]
        private class Entry
        {
            public GameObject prefab;
            public string fileName;
        }

        private static readonly int[] SizeOptions = { 256, 512, 1024, 2048 };
        private static readonly string[] SizeLabels = { "256", "512", "1024", "2048" };

        [SerializeField] private List<Entry> entries = new List<Entry>();

        [SerializeField] private string outputFolder = "Assets/Textures/UI/ItemIcons";
        [SerializeField] private int size = 1024;
        [SerializeField] private bool overwrite;

        [SerializeField] private bool autoOrient = true;
        [SerializeField] private float diagonalAngle = 45f;
        [SerializeField] private bool flipEnds;
        [SerializeField] private bool flipSide;
        [SerializeField] private Vector3 extraRotation = Vector3.zero;
        [SerializeField, Range(0f, 0.3f)] private float padding = 0.06f;

        [SerializeField] private float keyIntensity = 1.3f;
        [SerializeField] private float fillIntensity = 0.6f;
        [SerializeField] private float rimIntensity = 0.8f;

        [SerializeField] private bool flipVertical;

        private Texture2D preview;
        private int previewIndex;
        private Vector2 scroll;

        [MenuItem("ProjectS/Icons/아이템 아이콘 캡처")]
        private static void Open()
        {
            GetWindow<ItemIconCaptureTool>("아이템 아이콘 캡처");
        }

        private void OnEnable()
        {
            // StandardRequest 결과가 그래픽 API에 따라 상하가 뒤집혀 나온다(D3D 계열). MinimapSnapshotTool과 같은 기준.
            flipVertical = SystemInfo.graphicsUVStartsAtTop;
        }

        private void OnDisable()
        {
            ClearPreview();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            DrawEntries();
            EditorGUILayout.Space();
            DrawSettings();
            EditorGUILayout.Space();
            DrawActions();
            DrawPreview();

            EditorGUILayout.EndScrollView();
        }

        private void DrawEntries()
        {
            EditorGUILayout.LabelField("대상 프리팹", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("파일 이름은 ItemData.IconAddress와 같아야 등록 도구가 연결한다(확장자 제외).", MessageType.None);

            int removeAt = -1;
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                EditorGUILayout.BeginHorizontal();

                GameObject picked = (GameObject)EditorGUILayout.ObjectField(entry.prefab, typeof(GameObject), false);
                if (picked != entry.prefab)
                {
                    // 이름을 손대지 않았으면 새 프리팹 이름을 따라간다.
                    if (entry.prefab == null || entry.fileName == entry.prefab.name)
                        entry.fileName = picked != null ? picked.name : string.Empty;
                    entry.prefab = picked;
                }

                entry.fileName = EditorGUILayout.TextField(entry.fileName);

                if (GUILayout.Button("미리보기", GUILayout.Width(64))) RenderPreview(i);
                if (GUILayout.Button("X", GUILayout.Width(22))) removeAt = i;

                EditorGUILayout.EndHorizontal();
            }

            if (removeAt >= 0) entries.RemoveAt(removeAt);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Project 창 선택 항목 추가")) AddSelectedPrefabs();
            if (GUILayout.Button("빈 칸 추가")) entries.Add(new Entry());
            if (GUILayout.Button("모두 비우기", GUILayout.Width(90))) entries.Clear();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSettings()
        {
            EditorGUILayout.LabelField("출력", EditorStyles.boldLabel);
            outputFolder = EditorGUILayout.TextField("저장 폴더", outputFolder);
            size = EditorGUILayout.IntPopup("크기(px)", size, SizeLabels, SizeOptions);
            overwrite = EditorGUILayout.Toggle("같은 이름 덮어쓰기", overwrite);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("구도", EditorStyles.boldLabel);
            autoOrient = EditorGUILayout.Toggle(new GUIContent("자동 정렬(옆모습)",
                "가장 긴 축을 가로로, 가장 얇은 축을 카메라 쪽으로 돌린다. 총·검용. 끄면 모델 정면이 카메라를 본다."), autoOrient);
            diagonalAngle = EditorGUILayout.Slider(new GUIContent("대각선 각도",
                "화면 안에서 반시계로 기울이는 각도. 45면 왼쪽 아래→오른쪽 위 사선. 방어구는 0."), diagonalAngle, -90f, 90f);
            flipEnds = EditorGUILayout.Toggle(new GUIContent("양 끝 뒤집기", "총구/칼끝 방향과 위아래를 함께 뒤집는다(화면 안 180° 회전)."), flipEnds);
            flipSide = EditorGUILayout.Toggle(new GUIContent("반대쪽 면 보기", "모델의 반대 옆면을 보여준다(좌우 반전)."), flipSide);
            extraRotation = EditorGUILayout.Vector3Field(new GUIContent("추가 회전", "위 결과에 마지막으로 더하는 회전(도). 살짝 비틀어 입체감을 줄 때."), extraRotation);
            padding = EditorGUILayout.Slider(new GUIContent("여백 비율", "한 변당 여백. 슬롯 마스크에 잘리지 않게 약간 둔다."), padding, 0f, 0.3f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("조명", EditorStyles.boldLabel);
            keyIntensity = EditorGUILayout.Slider("주광(왼쪽 위)", keyIntensity, 0f, 3f);
            fillIntensity = EditorGUILayout.Slider("보조광(오른쪽)", fillIntensity, 0f, 3f);
            rimIntensity = EditorGUILayout.Slider("윤곽광(뒤)", rimIntensity, 0f, 3f);

            EditorGUILayout.Space();
            flipVertical = EditorGUILayout.Toggle(new GUIContent("상하 뒤집기", "결과가 위아래 뒤집혀 나오면 바꾼다. 그래픽 API에 따라 다르다."), flipVertical);
        }

        private void DrawActions()
        {
            using (new EditorGUI.DisabledScope(entries.Count == 0))
            {
                if (GUILayout.Button($"전체 캡처 ({entries.Count}개)", GUILayout.Height(28))) CaptureAll();
            }
        }

        private void DrawPreview()
        {
            if (preview == null) return;

            EditorGUILayout.Space();
            string label = previewIndex < entries.Count && entries[previewIndex].prefab != null
                ? entries[previewIndex].prefab.name
                : "미리보기";
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);

            float side = Mathf.Min(position.width - 20f, 360f);
            Rect rect = GUILayoutUtility.GetRect(side, side, GUILayout.ExpandWidth(false));
            EditorGUI.DrawTextureTransparent(rect, preview, ScaleMode.ScaleToFit);
        }

        private void AddSelectedPrefabs()
        {
            foreach (GameObject go in Selection.GetFiltered<GameObject>(SelectionMode.Assets))
            {
                if (!PrefabUtility.IsPartOfPrefabAsset(go)) continue;
                if (entries.Exists(e => e.prefab == go)) continue;

                entries.Add(new Entry { prefab = go, fileName = go.name });
            }
        }

        private void RenderPreview(int index)
        {
            Entry entry = entries[index];
            if (entry.prefab == null) return;

            ClearPreview();
            // 미리보기는 창에 띄우는 용도라 512로 충분하다(1024 이상은 창에서 어차피 줄어든다).
            preview = Capture(entry.prefab, Mathf.Min(size, 512));
            previewIndex = index;
        }

        private void ClearPreview()
        {
            if (preview != null) DestroyImmediate(preview);
            preview = null;
        }

        private void CaptureAll()
        {
            if (!outputFolder.StartsWith("Assets", StringComparison.Ordinal))
            {
                EditorUtility.DisplayDialog("아이템 아이콘 캡처", "저장 폴더는 Assets 아래여야 한다.", "확인");
                return;
            }

            Directory.CreateDirectory(outputFolder);

            int saved = 0;
            List<string> skipped = new List<string>();
            List<string> savedPaths = new List<string>();

            try
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    Entry entry = entries[i];
                    if (entry.prefab == null || string.IsNullOrWhiteSpace(entry.fileName))
                    {
                        skipped.Add($"{i}번: 프리팹 또는 파일 이름이 비어 있음");
                        continue;
                    }

                    string path = $"{outputFolder.TrimEnd('/')}/{entry.fileName.Trim()}.png";
                    if (File.Exists(path) && !overwrite)
                    {
                        skipped.Add($"{entry.fileName}: 이미 있음(덮어쓰기 꺼짐)");
                        continue;
                    }

                    if (EditorUtility.DisplayCancelableProgressBar("아이템 아이콘 캡처", entry.fileName, (float)i / entries.Count)) break;

                    Texture2D texture = Capture(entry.prefab, size);
                    if (texture == null)
                    {
                        skipped.Add($"{entry.fileName}: 렌더 실패(콘솔 확인)");
                        continue;
                    }

                    File.WriteAllBytes(path, texture.EncodeToPNG());
                    DestroyImmediate(texture);

                    savedPaths.Add(path);
                    saved++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            // 임포트 설정은 파일을 다 쓴 뒤 한 번에 한다. 하나씩 하면 매번 에셋 새로고침이 돈다.
            AssetDatabase.Refresh();
            foreach (string path in savedPaths) ApplySpriteImportSettings(path);

            string report = $"[아이템 아이콘 캡처] 저장 {saved}개 → {outputFolder}";
            if (skipped.Count > 0) report += "\n건너뜀:\n - " + string.Join("\n - ", skipped);
            Debug.Log(report);

            if (savedPaths.Count > 0)
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(savedPaths[0]));
        }

        // 기존 ItemIcons와 같은 설정. 다르게 두면 같은 슬롯에서 아이콘마다 선명도·압축이 달라 보인다.
        private static void ApplySpriteImportSettings(string assetPath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }

        // ---- 렌더 ----

        private Texture2D Capture(GameObject prefab, int outputSize)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            RenderTexture big = null;
            RenderTexture small = null;

            try
            {
                GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                List<Renderer> renderers = CollectRenderers(model);
                if (renderers.Count == 0)
                {
                    Debug.LogWarning($"[아이템 아이콘 캡처] {prefab.name}: 그릴 Renderer가 없다.", prefab);
                    return null;
                }

                model.transform.rotation = ComputeRotation(renderers);

                if (!TryGetViewBounds(renderers, out Vector2 min, out Vector2 max, out float minZ, out float maxZ))
                {
                    Debug.LogWarning($"[아이템 아이콘 캡처] {prefab.name}: 메시 정점이 없다.", prefab);
                    return null;
                }

                Camera camera = CreateCamera(scene, min, max, minZ, maxZ);
                CreateLights(scene);

                // 2배로 렌더 후 절반으로 줄여 가장자리 계단을 없앤다(카메라 MSAA 대신 — 알파 보존이 확실하다).
                big = new RenderTexture(outputSize * 2, outputSize * 2, 24, RenderTextureFormat.ARGB32);
                if (!Render(camera, big)) return null;

                small = new RenderTexture(outputSize, outputSize, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear };
                big.filterMode = FilterMode.Bilinear;
                Graphics.Blit(big, small);

                return Readback(small, flipVertical);
            }
            finally
            {
                RenderTexture.active = null;
                ReleaseTexture(big);
                ReleaseTexture(small);

                // 프리뷰 씬을 닫으면 그 안의 모델·카메라·조명이 함께 파괴된다.
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        // LODGroup이 있으면 LOD0만 남긴다. 모든 LOD가 한꺼번에 그려지면 겹쳐 보이고 범위 계산도 틀어진다.
        // 파티클·트레일·라인은 아이콘 대상이 아니다.
        private static List<Renderer> CollectRenderers(GameObject model)
        {
            HashSet<Renderer> hidden = new HashSet<Renderer>();
            foreach (LODGroup group in model.GetComponentsInChildren<LODGroup>(true))
            {
                LOD[] lods = group.GetLODs();
                for (int i = 1; i < lods.Length; i++)
                {
                    foreach (Renderer r in lods[i].renderers)
                    {
                        if (r != null) hidden.Add(r);
                    }
                }
            }

            List<Renderer> result = new List<Renderer>();
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(false))
            {
                bool isEffect = r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer;
                if (hidden.Contains(r) || isEffect)
                {
                    r.enabled = false;
                    continue;
                }

                if (r.enabled) result.Add(r);
            }
            return result;
        }

        // 카메라는 +Z를 바라본다(화면 오른쪽 = +X, 위 = +Y). 그 기준으로 모델을 돌린다.
        private Quaternion ComputeRotation(List<Renderer> renderers)
        {
            Quaternion baseRotation;

            if (autoOrient)
            {
                // 모델 원래 자세(회전 0)에서 축별 길이를 재, 긴 축 → 가로(X), 중간 축 → 세로(Y), 얇은 축 → 카메라 방향(Z).
                Vector3 extent = MeasureExtent(renderers);
                Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
                float[] lengths = { extent.x, extent.y, extent.z };
                Array.Sort(lengths, axes);   // 짧은 순

                // LookRotation(f, u)은 Z→f, Y→u로 보내는 회전이라, 그 역은 f→Z, u→Y로 보낸다.
                baseRotation = Quaternion.Inverse(Quaternion.LookRotation(axes[0], axes[1]));
            }
            else
            {
                // 모델 정면(+Z)이 카메라를 보게 한다.
                baseRotation = Quaternion.Euler(0f, 180f, 0f);
            }

            Quaternion flips = Quaternion.identity;
            if (flipEnds) flips = Quaternion.AngleAxis(180f, Vector3.forward) * flips;
            if (flipSide) flips = Quaternion.AngleAxis(180f, Vector3.up) * flips;

            return Quaternion.Euler(extraRotation)
                 * Quaternion.AngleAxis(diagonalAngle, Vector3.forward)
                 * flips
                 * baseRotation;
        }

        private static Vector3 MeasureExtent(List<Renderer> renderers)
        {
            Vector3 min = Vector3.positiveInfinity;
            Vector3 max = Vector3.negativeInfinity;
            ForEachWorldVertex(renderers, v =>
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            });
            return max - min;
        }

        // 화면(카메라 X·Y) 기준의 딱 맞는 범위. Renderer.bounds는 회전하면 부풀어(대각선 모델일수록) 여백이 들쭉날쭉해진다.
        private static bool TryGetViewBounds(List<Renderer> renderers, out Vector2 min, out Vector2 max, out float minZ, out float maxZ)
        {
            Vector3 lo = Vector3.positiveInfinity;
            Vector3 hi = Vector3.negativeInfinity;
            bool any = false;

            ForEachWorldVertex(renderers, v =>
            {
                lo = Vector3.Min(lo, v);
                hi = Vector3.Max(hi, v);
                any = true;
            });

            min = new Vector2(lo.x, lo.y);
            max = new Vector2(hi.x, hi.y);
            minZ = lo.z;
            maxZ = hi.z;
            return any;
        }

        // 에디터에서는 Read/Write가 꺼진 메시도 정점을 읽을 수 있다. 스킨드 메시는 뼈 자세 대신 bounds 모서리로 대신한다
        // (아이콘용 프리팹은 대부분 정적 메시라 이 정도면 충분하다).
        private static void ForEachWorldVertex(List<Renderer> renderers, Action<Vector3> visit)
        {
            foreach (Renderer r in renderers)
            {
                Mesh mesh = null;
                if (r is MeshRenderer && r.TryGetComponent(out MeshFilter filter)) mesh = filter.sharedMesh;

                if (mesh != null)
                {
                    Matrix4x4 toWorld = r.transform.localToWorldMatrix;
                    foreach (Vector3 v in mesh.vertices) visit(toWorld.MultiplyPoint3x4(v));
                    continue;
                }

                Bounds b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    visit(new Vector3(
                        (i & 1) == 0 ? b.min.x : b.max.x,
                        (i & 2) == 0 ? b.min.y : b.max.y,
                        (i & 4) == 0 ? b.min.z : b.max.z));
                }
            }
        }

        // 직교 카메라 — 원근 왜곡이 없어야 아이콘끼리 크기감이 일정하다.
        private Camera CreateCamera(Scene scene, Vector2 min, Vector2 max, float minZ, float maxZ)
        {
            GameObject go = new GameObject("IconCaptureCamera");
            SceneManager.MoveGameObjectToScene(go, scene);

            Camera camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.scene = scene;   // 이 씬의 오브젝트·조명만 그린다

            Vector2 center = (min + max) * 0.5f;
            Vector2 extent = (max - min) * 0.5f;
            float half = Mathf.Max(extent.x, extent.y) / Mathf.Max(0.01f, 1f - padding * 2f);

            float depth = maxZ - minZ;
            go.transform.SetPositionAndRotation(new Vector3(center.x, center.y, minZ - 1f), Quaternion.identity);

            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(half, 0.001f);
            camera.aspect = 1f;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = depth + 2f;

            // 투명 배경: 알파 0으로 지운다. HDR을 켜면 URP 중간 버퍼가 알파 없는 포맷이 돼 배경이 검게 나온다.
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.allowHDR = false;
            camera.allowMSAA = false;
            return camera;
        }

        // 3점 조명. 방향은 카메라(+Z를 봄) 기준으로 고정하고 세기만 조절한다.
        private void CreateLights(Scene scene)
        {
            CreateLight(scene, "Key", new Vector3(0.5f, -0.6f, 1f), keyIntensity, new Color(1f, 0.97f, 0.92f));
            CreateLight(scene, "Fill", new Vector3(-0.8f, -0.2f, 1f), fillIntensity, new Color(0.85f, 0.9f, 1f));
            CreateLight(scene, "Rim", new Vector3(-0.3f, -0.4f, -1f), rimIntensity, Color.white);
        }

        private static void CreateLight(Scene scene, string name, Vector3 direction, float intensity, Color color)
        {
            if (intensity <= 0f) return;

            GameObject go = new GameObject($"IconCapture{name}Light");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.rotation = Quaternion.LookRotation(direction);

            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = color;
            light.shadows = LightShadows.None;   // 작은 모델의 자기 그림자는 아이콘에서 얼룩으로 보인다
        }

        // URP는 camera.Render()가 제대로 안 되므로 StandardRequest로 렌더한다(MinimapSnapshotTool과 같은 이유).
        private static bool Render(Camera camera, RenderTexture target)
        {
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                camera.targetTexture = target;
                camera.Render();
                camera.targetTexture = null;
                return true;
            }

            RenderPipeline.StandardRequest request = new RenderPipeline.StandardRequest();
            if (!RenderPipeline.SupportsRenderRequest(camera, request))
            {
                Debug.LogError("[아이템 아이콘 캡처] 현재 렌더 파이프라인이 StandardRequest 렌더링을 지원하지 않아 캡처할 수 없다.");
                return false;
            }

            request.destination = target;
            RenderPipeline.SubmitRenderRequest(camera, request);
            return true;
        }

        // 픽셀을 읽고, 필요하면 상하를 뒤집고, 가장자리 색을 알파로 나눠 되돌린다(premultiplied → straight).
        private static Texture2D Readback(RenderTexture source, bool flip)
        {
            int w = source.width;
            int h = source.height;

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = source;
            Texture2D texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = previous;

            Color32[] pixels = texture.GetPixels32();

            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                if (p.a == 0)
                {
                    pixels[i] = new Color32(0, 0, 0, 0);
                    continue;
                }
                if (p.a == 255) continue;

                float inv = 255f / p.a;
                pixels[i] = new Color32(
                    (byte)Mathf.Min(255f, p.r * inv),
                    (byte)Mathf.Min(255f, p.g * inv),
                    (byte)Mathf.Min(255f, p.b * inv),
                    p.a);
            }

            if (flip)
            {
                Color32[] row = new Color32[w];
                for (int y = 0; y < h / 2; y++)
                {
                    int top = y * w;
                    int bottom = (h - 1 - y) * w;
                    Array.Copy(pixels, top, row, 0, w);
                    Array.Copy(pixels, bottom, pixels, top, w);
                    Array.Copy(row, 0, pixels, bottom, w);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private static void ReleaseTexture(RenderTexture texture)
        {
            if (texture == null) return;

            texture.Release();
            DestroyImmediate(texture);
        }
    }
}
