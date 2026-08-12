// Editor 폴더 안에 있어 플레이어 빌드에는 포함되지 않는다(MinimapBoundsTool과 같은 규칙).
// 네임스페이스 세그먼트를 Editor로 두면 UnityEditor.Editor 타입이 가려지므로 EditorTools를 쓴다.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;
using ProjectS.UI;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 씬을 직교(Orthographic) 탑다운 카메라로 찍어 미니맵 배경 스냅샷 PNG를 만드는 도구.
    /// MinimapBoundsTool이 "범위(worldCenter/worldSize)를 재는" 짝이라면, 이 도구는 "그 범위를 정확히 덮는
    /// 그림을 뽑는" 짝이다. 스냅샷이 담는 월드 사각형과 MinimapData의 worldCenter/worldSize가 같아야
    /// 마커가 그림 위 제자리에 찍히므로(MinimapView.UpdateBackground 참조), 한 번에 그림과 값을 함께 적용한다.
    /// <para>
    /// 종횡비를 맵과 같게(가로:세로 = size.x:size.z) 렌더하는 것이 핵심이다. bg.sizeDelta가 worldSize에
    /// 직접 비례하므로(MinimapView), 이미지 종횡비가 worldSize와 어긋나면 배경이 늘어난다.
    /// </para>
    /// <para>
    /// URP에서는 camera.Render()가 제대로 동작하지 않아 RenderPipeline.StandardRequest로 렌더한다
    /// (SceneCubemapCaptureTool과 같은 이유).
    /// </para>
    /// 메뉴: ProjectS > Minimap > 미니맵 스냅샷 캡처.
    /// </summary>
    public class MinimapSnapshotTool : EditorWindow
    {
        // 측정·렌더에 포함할 레이어. 이펙트·UI처럼 미니맵에 넣으면 안 되는 것을 빼는 데 쓴다(캡처에도 그대로 적용).
        private LayerMask includeLayers = ~0;

        // 파티클은 순간적으로 멀리 튀어 범위를 부풀리므로 기본 제외한다(측정 기준. 렌더 컬링은 레이어로만 한다).
        private bool skipParticles = true;

        // Terrain은 Renderer가 없어 범위 측정에 따로 더한다.
        private bool includeTerrains = true;

        // 긴 변 기준 해상도(px). 짧은 변은 종횡비에 맞춰 자동 계산한다.
        private int resolution = 1024;

        // 클리어 색. 알파를 0으로 두면 맵 바깥이 투명하게 저장되어 미니맵에 얹기 좋다.
        private Color background = new Color(0.08f, 0.09f, 0.12f, 1f);

        // 플랫폼(UV 원점)에 따라 결과가 상하로 뒤집혀 저장될 수 있어, 눈으로 보고 끌 수 있게 토글로 둔다.
        private bool flipVertical;

        // 캡처 스타일. 사실적=씬 재질·조명 그대로, 도식=흰 실루엣 + 글로우(스캐너/설계도 느낌).
        private enum CaptureStyle { Realistic, Schematic }
        private CaptureStyle style = CaptureStyle.Schematic;

        // 도식 스타일 파라미터. 최종 형체 색조와, 면 채움 불투명도·글로우 세기·글로우 번짐 반경.
        private Color tint = Color.white;
        private float fillOpacity = 0.25f;
        private float glowStrength = 1.4f;
        private int glowSpread = 4;

        // 측정 결과(XZ 범위 + 카메라 배치용 Y 범위). size를 수동으로 고쳐도 Y는 이 값을 쓴다.
        private Vector2 center;
        private Vector2 size = new Vector2(100f, 100f);
        private float boundsMinY;
        private float boundsMaxY = 50f;

        // 저장 경로와, 저장한 스냅샷을 바로 적용할 MinimapData(선택).
        private string outputPath = "Assets/UI/Minimap/MinimapSnapshot.png";
        private MinimapData target;

        [MenuItem("ProjectS/Minimap/미니맵 스냅샷 캡처")]
        private static void Open() => GetWindow<MinimapSnapshotTool>("Minimap Snapshot");

        private void OnEnable()
        {
            // DirectX 계열(UV 원점 위)에서는 StandardRequest 결과가 상하 반전되어 읽히는 경우가 많아 기본값을 맞춰 둔다.
            flipVertical = SystemInfo.graphicsUVStartsAtTop;
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "직교 탑다운 카메라로 씬을 찍어 미니맵 배경 PNG를 만듭니다.\n" +
                "'범위 측정'으로 맵을 감싸는 값을 잡은 뒤 '캡처'를 누르세요. " +
                "이미지 종횡비를 맵(size.x:size.z)과 같게 찍어 배경이 늘어나지 않습니다.",
                MessageType.Info);

            // LayerMask는 IMGUI에서 바로 그릴 수 없어 내부 유틸로 실제 레이어 목록과 변환한다.
            int mask = InternalEditorUtility.LayerMaskToConcatenatedLayersMask(includeLayers);
            mask = EditorGUILayout.MaskField("포함 레이어", mask, InternalEditorUtility.layers);
            includeLayers = InternalEditorUtility.ConcatenatedLayersMaskToLayerMask(mask);

            skipParticles = EditorGUILayout.Toggle("파티클 제외(측정)", skipParticles);
            includeTerrains = EditorGUILayout.Toggle("Terrain 포함", includeTerrains);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("씬 전체 범위 측정")) Measure(false);
                if (GUILayout.Button("선택 오브젝트 범위 측정")) Measure(true);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("범위 (XZ)", EditorStyles.boldLabel);
            center = EditorGUILayout.Vector2Field("World Center (X,Z)", center);
            size = EditorGUILayout.Vector2Field("World Size (X,Z)", size);

            using (new EditorGUILayout.HorizontalScope())
            {
                boundsMinY = EditorGUILayout.FloatField("바닥 Y", boundsMinY);
                boundsMaxY = EditorGUILayout.FloatField("천장 Y", boundsMaxY);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("캡처 설정", EditorStyles.boldLabel);
            resolution = Mathf.Clamp(EditorGUILayout.IntField("해상도(긴 변, px)", resolution), 64, 8192);
            style = (CaptureStyle)EditorGUILayout.EnumPopup("스타일", style);

            if (style == CaptureStyle.Realistic)
            {
                background = EditorGUILayout.ColorField("배경색(알파 0 = 투명)", background);
            }
            else
            {
                // 도식 모드는 배경이 항상 검정(투명 알파의 기준)이라 배경색 대신 스타일 값만 노출한다.
                tint = EditorGUILayout.ColorField("색조", tint);
                fillOpacity = EditorGUILayout.Slider("면 채움 불투명도", fillOpacity, 0f, 1f);
                glowStrength = EditorGUILayout.Slider("글로우 세기", glowStrength, 0f, 3f);
                glowSpread = EditorGUILayout.IntSlider("글로우 반경", glowSpread, 1, 8);
            }

            flipVertical = EditorGUILayout.Toggle("상하 뒤집기", flipVertical);

            using (new EditorGUILayout.HorizontalScope())
            {
                outputPath = EditorGUILayout.TextField("저장 경로", outputPath);
                if (GUILayout.Button("...", GUILayout.Width(30))) BrowseOutputPath();
            }

            target = (MinimapData)EditorGUILayout.ObjectField(
                "적용 대상 MinimapData", target, typeof(MinimapData), false);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(size.x <= 0f || size.y <= 0f))
            {
                if (GUILayout.Button("캡처 → PNG 저장" + (target != null ? " → MinimapData 적용" : ""), GUILayout.Height(30)))
                    Capture();
            }

            if (size.x <= 0f || size.y <= 0f)
                EditorGUILayout.HelpBox("World Size가 0 이하입니다. 먼저 범위를 측정하거나 값을 입력하세요.", MessageType.Warning);
        }

        // 씬(또는 선택) 안의 Renderer/Terrain을 훑어 합친 3D 범위를 구한다. MinimapBoundsTool과 같은 규칙이되,
        // 카메라 Y 배치를 위해 여기서는 높이(Y)까지 보존한다.
        private void Measure(bool selectionOnly)
        {
            bool has = false;
            Bounds bounds = new Bounds();

            Renderer[] renderers = selectionOnly
                ? GetComponentsInSelection<Renderer>()
                : FindObjectsByType<Renderer>(FindObjectsSortMode.None);

            foreach (Renderer r in renderers)
            {
                if (r == null || !r.enabled) continue;
                if (((1 << r.gameObject.layer) & includeLayers.value) == 0) continue;
                if (skipParticles && r is ParticleSystemRenderer) continue;

                Encapsulate(ref bounds, ref has, r.bounds);
            }

            if (includeTerrains)
            {
                Terrain[] terrains = selectionOnly
                    ? GetComponentsInSelection<Terrain>()
                    : FindObjectsByType<Terrain>(FindObjectsSortMode.None);

                foreach (Terrain t in terrains)
                {
                    if (t == null || t.terrainData == null) continue;

                    // Terrain의 transform.position은 코너(최소점)라 중심은 +size/2 지점이다.
                    Vector3 tSize = t.terrainData.size;
                    Encapsulate(ref bounds, ref has, new Bounds(t.transform.position + tSize * 0.5f, tSize));
                }
            }

            if (!has)
            {
                EditorUtility.DisplayDialog("Minimap Snapshot",
                    "측정할 Renderer/Terrain을 찾지 못했습니다. 포함 레이어나 선택을 확인하세요.", "확인");
                return;
            }

            center = new Vector2(bounds.center.x, bounds.center.z);
            size = new Vector2(bounds.size.x, bounds.size.z);
            boundsMinY = bounds.min.y;
            boundsMaxY = bounds.max.y;

            Debug.Log($"[MinimapSnapshot] Center=({center.x:0.##}, {center.y:0.##})  " +
                      $"Size=({size.x:0.##}, {size.y:0.##})  Y=[{boundsMinY:0.##}..{boundsMaxY:0.##}]");
        }

        private void Capture()
        {
            // 종횡비를 맵과 같게: 긴 변에 resolution을 주고 짧은 변은 비율로 맞춘다. 늘어남을 막는 핵심.
            float pixelsPerMeter = resolution / Mathf.Max(size.x, size.y);
            int texW = Mathf.Clamp(Mathf.RoundToInt(size.x * pixelsPerMeter), 8, 8192);
            int texH = Mathf.Clamp(Mathf.RoundToInt(size.y * pixelsPerMeter), 8, 8192);

            Texture2D readback = null;
            try
            {
                readback = style == CaptureStyle.Schematic
                    ? CaptureSchematic(texW, texH)
                    : CaptureRealistic(texW, texH);

                if (readback == null) return;

                string dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(outputPath, readback.EncodeToPNG());
                AssetDatabase.ImportAsset(outputPath);
                ApplySpriteImportSettings(outputPath);

                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(outputPath);
                if (target != null) ApplyToMinimapData(target, sprite, center, size);

                EditorGUIUtility.PingObject(sprite != null ? (Object)sprite : AssetDatabase.LoadAssetAtPath<Object>(outputPath));
                Debug.Log($"[MinimapSnapshot] 저장 완료: {outputPath} ({texW}x{texH}), 스타일={style}, " +
                          $"Center=({center.x:0.##},{center.y:0.##}) Size=({size.x:0.##},{size.y:0.##})",
                    AssetDatabase.LoadAssetAtPath<Object>(outputPath));
            }
            finally
            {
                if (readback != null) Object.DestroyImmediate(readback);
            }
        }

        // 사실적 스타일: 씬 재질·조명 그대로 한 번 렌더해 읽어온다.
        private Texture2D CaptureRealistic(int texW, int texH)
        {
            Camera cam = CreateTopDownCamera(texW, texH, background, out GameObject go);
            RenderTexture rt = new RenderTexture(texW, texH, 24, RenderTextureFormat.ARGB32);
            try
            {
                if (!TrySubmit(cam, rt)) return null;
                return Readback(rt, flipVertical);
            }
            finally
            {
                RenderTexture.active = null;
                Object.DestroyImmediate(go);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        // 도식 스타일: 모든 형체를 흰 실루엣으로 렌더(검정 배경) → 글로우를 만들어 합성한다.
        private Texture2D CaptureSchematic(int texW, int texH)
        {
            Dictionary<Renderer, Material[]> overrides = OverrideMaterialsWithFlatWhite(out Material flatWhite);
            Camera cam = CreateTopDownCamera(texW, texH, Color.black, out GameObject go);
            RenderTexture rt = new RenderTexture(texW, texH, 24, RenderTextureFormat.ARGB32);
            Texture2D baseTex = null;
            Texture2D glowTex = null;
            try
            {
                if (!TrySubmit(cam, rt)) return null;

                baseTex = Readback(rt, flipVertical);                        // 흰 실루엣(형체 = 밝음)
                glowTex = BuildGlow(rt, texW, texH, glowSpread, flipVertical); // 흐리게 번진 글로우
                return Composite(baseTex, glowTex);
            }
            finally
            {
                RenderTexture.active = null;
                Object.DestroyImmediate(go);
                rt.Release();
                Object.DestroyImmediate(rt);
                if (baseTex != null) Object.DestroyImmediate(baseTex);
                if (glowTex != null) Object.DestroyImmediate(glowTex);

                // 씬 오브젝트 재질은 반드시 원상복구한다(렌더 중 예외가 나도 finally에서).
                RestoreMaterials(overrides);
                if (flatWhite != null) Object.DestroyImmediate(flatWhite);
            }
        }

        // 맵 정중앙 위에서 똑바로 내려다보는 직교 카메라를 만든다. Euler(90,0,0)이면 화면 위=+Z, 오른쪽=+X가 되어
        // MinimapView의 마커 좌표(offset.x→x, offset.z→y)와 방향이 일치한다.
        private Camera CreateTopDownCamera(int texW, int texH, Color bg, out GameObject go)
        {
            float margin = Mathf.Max(1f, (boundsMaxY - boundsMinY) * 0.05f);
            Vector3 camPos = new Vector3(center.x, boundsMaxY + margin, center.y);

            go = new GameObject("MinimapSnapshotCamera") { hideFlags = HideFlags.HideAndDontSave };
            Camera cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.transform.SetPositionAndRotation(camPos, Quaternion.Euler(90f, 0f, 0f));
            cam.orthographic = true;
            cam.orthographicSize = size.y * 0.5f;   // 세로 절반(월드 Z) = 직교 크기
            cam.aspect = (float)texW / texH;         // 가로/세로 = size.x/size.z
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = bg;
            cam.cullingMask = includeLayers;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = (boundsMaxY - boundsMinY) + margin * 2f;
            return cam;
        }

        // URP는 camera.Render()가 제대로 안 되므로 StandardRequest로 렌더한다. 지원 안 하면 false.
        private static bool TrySubmit(Camera cam, RenderTexture rt)
        {
            RenderPipeline.StandardRequest request = new RenderPipeline.StandardRequest();
            if (!RenderPipeline.SupportsRenderRequest(cam, request))
            {
                Debug.LogError("현재 렌더 파이프라인이 StandardRequest 렌더링을 지원하지 않아 캡처할 수 없습니다.");
                return false;
            }

            request.destination = rt;
            RenderPipeline.SubmitRenderRequest(cam, request);
            return true;
        }

        // RenderTexture 픽셀을 Texture2D로 읽어온다. flip이면 세로만 뒤집는다(좌우는 유지해야 +X가 오른쪽).
        private static Texture2D Readback(RenderTexture rt, bool flip)
        {
            int w = rt.width;
            int h = rt.height;
            RenderTexture source = rt;
            RenderTexture flipRT = null;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);

            try
            {
                if (flip)
                {
                    flipRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(rt, flipRT, new Vector2(1f, -1f), new Vector2(0f, 1f));
                    source = flipRT;
                }

                RenderTexture previousActive = RenderTexture.active;
                RenderTexture.active = source;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = previousActive;
            }
            finally
            {
                if (flipRT != null)
                {
                    // Blit이 active를 flipRT로 남겨 previousActive 복원이 다시 flipRT를 가리킨다.
                    // 해제 전에 풀지 않으면 "active인 RT 릴리즈" 경고가 난다.
                    RenderTexture.active = null;
                    flipRT.Release();
                    Object.DestroyImmediate(flipRT);
                }
            }

            return tex;
        }

        // 셰이더 파일 없이 만드는 글로우: 절반씩 줄여 흐리게 만든 뒤 다시 전체 크기로 확대(바이리니어 보간)한다.
        // 다운/업샘플의 바이리니어 필터링이 곧 부드러운 블러라, 커스텀 블러 셰이더가 필요 없다.
        private static Texture2D BuildGlow(RenderTexture src, int w, int h, int spread, bool flip)
        {
            spread = Mathf.Clamp(spread, 1, 8);

            RenderTexture cur = src;
            List<RenderTexture> temps = new List<RenderTexture>();
            int cw = w;
            int ch = h;

            for (int i = 0; i < spread; i++)
            {
                cw = Mathf.Max(2, cw / 2);
                ch = Mathf.Max(2, ch / 2);
                RenderTexture small = new RenderTexture(cw, ch, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear };
                Graphics.Blit(cur, small);
                temps.Add(small);
                cur = small;
            }

            RenderTexture full = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear };
            Graphics.Blit(cur, full);
            Texture2D glow = Readback(full, flip);

            // Graphics.Blit/Readback이 active를 RT로 남겨둬, 해제 전에 풀지 않으면 "active인 RT 릴리즈" 경고가 난다.
            RenderTexture.active = null;
            full.Release();
            Object.DestroyImmediate(full);
            foreach (RenderTexture t in temps)
            {
                t.Release();
                Object.DestroyImmediate(t);
            }

            return glow;
        }

        // 실루엣(baseTex)과 글로우(glowTex)를 합쳐 최종 이미지를 만든다.
        // 알파 = 면 채움(실루엣×fillOpacity) + 글로우×glowStrength. 검정 배경은 알파 0이 되어 투명하게 저장된다.
        private Texture2D Composite(Texture2D baseTex, Texture2D glowTex)
        {
            int w = baseTex.width;
            int h = baseTex.height;
            Color[] b = baseTex.GetPixels();
            Color[] g = glowTex.GetPixels();
            Color[] outp = new Color[b.Length];

            for (int i = 0; i < b.Length; i++)
            {
                float silhouette = b[i].r;                    // 흰 실루엣이라 밝기 = r
                float glow = g[i].r * glowStrength;
                float a = Mathf.Clamp01(silhouette * fillOpacity + glow);
                outp[i] = new Color(tint.r, tint.g, tint.b, a);
            }

            Texture2D result = new Texture2D(w, h, TextureFormat.RGBA32, false);
            result.SetPixels(outp);
            result.Apply();
            return result;
        }

        // 도식 렌더용: 대상 Renderer들의 재질을 단색 흰 Unlit으로 잠깐 교체하고, 원본 배열을 돌려준다.
        // 조명·텍스처를 무시한 균일한 흰 실루엣을 얻기 위함. 파티클은 형체가 아니라 제외한다.
        private Dictionary<Renderer, Material[]> OverrideMaterialsWithFlatWhite(out Material flatWhite)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");

            flatWhite = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            if (flatWhite.HasProperty("_BaseColor")) flatWhite.SetColor("_BaseColor", Color.white);
            if (flatWhite.HasProperty("_Color")) flatWhite.SetColor("_Color", Color.white);

            Dictionary<Renderer, Material[]> originals = new Dictionary<Renderer, Material[]>();

            foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r == null) continue;
                if (((1 << r.gameObject.layer) & includeLayers.value) == 0) continue;
                if (r is ParticleSystemRenderer) continue;

                originals[r] = r.sharedMaterials;

                Material[] arr = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < arr.Length; i++) arr[i] = flatWhite;
                r.sharedMaterials = arr;
            }

            return originals;
        }

        private static void RestoreMaterials(Dictionary<Renderer, Material[]> originals)
        {
            if (originals == null) return;

            foreach (KeyValuePair<Renderer, Material[]> kv in originals)
                if (kv.Key != null) kv.Key.sharedMaterials = kv.Value;
        }

        // 저장한 PNG를 UI에서 쓸 Sprite로 임포트한다. 알파를 그대로 살리고, 스크롤 시 가장자리 번짐을 막게 Clamp.
        private static void ApplySpriteImportSettings(string assetPath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 8192; // 위에서 이미 종횡비대로 만들었으므로 축소되지 않게 넉넉히.
            importer.SaveAndReimport();
        }

        // 스냅샷과 범위를 한 세트로 적용한다. 그림과 값은 반드시 짝이어야 하므로(MinimapData 주석) 함께 쓴다.
        // private [SerializeField] 필드라 SerializedObject로 접근한다
        // (필드명 snapshot/worldCenter/worldSize는 MinimapData와의 계약).
        private static void ApplyToMinimapData(MinimapData data, Sprite sprite, Vector2 worldCenter, Vector2 worldSize)
        {
            var so = new SerializedObject(data);
            if (sprite != null) so.FindProperty("snapshot").objectReferenceValue = sprite;
            so.FindProperty("worldCenter").vector2Value = worldCenter;
            so.FindProperty("worldSize").vector2Value = worldSize;
            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(data);
            Debug.Log($"[MinimapSnapshot] '{data.name}'에 스냅샷·범위를 적용했습니다.", data);
        }

        private void BrowseOutputPath()
        {
            string abs = EditorUtility.SaveFilePanelInProject(
                "미니맵 스냅샷 저장", "MinimapSnapshot", "png", "PNG로 저장할 위치를 고르세요.",
                Path.GetDirectoryName(outputPath));

            if (!string.IsNullOrEmpty(abs)) outputPath = abs;
        }

        private static void Encapsulate(ref Bounds bounds, ref bool has, Bounds add)
        {
            if (!has)
            {
                bounds = add;
                has = true;
            }
            else
            {
                bounds.Encapsulate(add);
            }
        }

        private static T[] GetComponentsInSelection<T>() where T : Component
        {
            var list = new System.Collections.Generic.List<T>();

            foreach (GameObject go in Selection.gameObjects)
                list.AddRange(go.GetComponentsInChildren<T>(true));

            return list.ToArray();
        }
    }
}
