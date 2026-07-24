using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// 에디터 전용 코드는 Unity 규칙상 "Editor" 폴더에 있어야 하지만, 네임스페이스 세그먼트를
// Editor로 두면 UnityEditor.Editor 타입이 가려지므로(단순명 가림 회피 원칙) EditorTools를 사용한다.
namespace ProjectS.EditorTools
{
    /// <summary>
    /// 현재 열려 있는 씬을 PortalCubemap_01과 같은 형태(등장방형 EXR → 큐브맵 임포트)로 캡처하는 에디터 툴.
    /// Synty 데모(Demo_Corporation_01)처럼 포탈 위치의 Reflection Probe에 Custom Cubemap으로 연결해
    /// 포탈 너머로 다른 씬(예: DungeonGather)이 보이는 연출에 쓴다.
    /// URP에서는 Camera.RenderToCubemap이 동작하지 않으므로 RenderPipeline.StandardRequest로 6면을 렌더링한다.
    /// </summary>
    public static class SceneCubemapCaptureTool
    {
        // 면당 1024면 등장방형 4096x2048 기준으로 손실이 거의 없고,
        // 임포터에서 2048로 줄여 원본(PortalCubemap_01)과 동일한 최종 크기를 맞춘다.
        private const int FaceSize = 1024;
        private const int EquirectWidth = 4096;
        private const int EquirectHeight = 2048;
        private const string OutputFolder = "Assets/Textures/Cubemaps";
        private const string FlatOutputFolder = "Assets/Textures/Screenshots";

        /// <summary>
        /// 하이어라키에서 선택한 오브젝트 위치(없으면 씬 뷰 피벗)를 기준점으로 현재 씬을 큐브맵으로 캡처한다.
        /// 선택 오브젝트의 Y 회전(파란 화살표가 향하는 쪽)이 큐브맵의 정면이 된다.
        /// </summary>
        [MenuItem("Tools/ProjectS/씬 큐브맵 캡처 (선택 오브젝트 기준)")]
        private static void CaptureFromSelection()
        {
            Vector3 capturePosition = GetCapturePosition(out float captureYaw, out string positionSource);
            Capture(capturePosition, captureYaw, positionSource);
        }

        /// <summary>
        /// 씬 뷰에서 지금 보고 있는 위치·방향 그대로를 큐브맵의 정면으로 캡처한다.
        /// 원하는 구도를 씬 뷰에서 잡은 뒤 바로 실행하는 용도이며, 선택 오브젝트는 무시한다.
        /// 수평 유지를 위해 위아래 기울임(피치)은 반영하지 않는다.
        /// </summary>
        [MenuItem("Tools/ProjectS/씬 큐브맵 캡처 (씬 뷰 시점 그대로)")]
        private static void CaptureFromSceneView()
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
            {
                Debug.LogError("열려 있는 씬 뷰가 없어 캡처할 수 없습니다. 씬 뷰를 한 번 클릭한 뒤 다시 실행하세요.");
                return;
            }

            Transform viewCamera = sceneView.camera.transform;
            Capture(viewCamera.position, viewCamera.eulerAngles.y, "씬 뷰 카메라");
        }

        /// <summary>
        /// 씬 뷰에서 지금 보고 있는 화면 그대로를 2D 텍스처 한 장으로 캡처한다.
        /// 큐브맵과 달리 바라보는 방향(내려다보는 각도 포함)만 담기므로,
        /// 포탈 안쪽에 Quad를 세워 붙이는 "고정된 그림" 연출에 쓴다.
        /// 결과는 Assets/Textures/Screenshots/{씬이름}Shot_01.exr로 저장되며, 같은 이름이 있으면 덮어쓴다.
        /// </summary>
        [MenuItem("Tools/ProjectS/씬 스크린샷 캡처 (씬 뷰 구도 그대로)")]
        private static void CaptureFlatFromSceneView()
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
            {
                Debug.LogError("열려 있는 씬 뷰가 없어 캡처할 수 없습니다. 씬 뷰를 한 번 클릭한 뒤 다시 실행하세요.");
                return;
            }

            Camera viewCamera = sceneView.camera;
            int width = 2048;
            int height = Mathf.Clamp(Mathf.RoundToInt(width / viewCamera.aspect), 64, 4096);

            GameObject cameraObject = new GameObject("SceneScreenshotCaptureCamera")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            Camera captureCamera = cameraObject.AddComponent<Camera>();
            captureCamera.enabled = false;
            captureCamera.transform.SetPositionAndRotation(viewCamera.transform.position, viewCamera.transform.rotation);
            captureCamera.clearFlags = CameraClearFlags.Skybox;
            captureCamera.nearClipPlane = 0.3f;
            captureCamera.farClipPlane = 1000f;
            captureCamera.allowHDR = true;
            // 씬 뷰와 같은 화각·비율이어야 "보이는 그대로" 캡처된다.
            captureCamera.fieldOfView = viewCamera.fieldOfView;
            captureCamera.aspect = viewCamera.aspect;

            RenderTexture targetRT = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf);

            try
            {
                RenderPipeline.StandardRequest request = new RenderPipeline.StandardRequest();
                if (!RenderPipeline.SupportsRenderRequest(captureCamera, request))
                {
                    Debug.LogError("현재 렌더 파이프라인이 StandardRequest 렌더링을 지원하지 않아 캡처할 수 없습니다.");
                    return;
                }

                request.destination = targetRT;
                RenderPipeline.SubmitRenderRequest(captureCamera, request);

                string sceneName = SceneManager.GetActiveScene().name;
                string assetPath = $"{FlatOutputFolder}/{sceneName}Shot_01.exr";
                Directory.CreateDirectory(FlatOutputFolder);
                WriteExrAsset(targetRT, assetPath);

                Texture captured = AssetDatabase.LoadAssetAtPath<Texture>(assetPath);
                EditorGUIUtility.PingObject(captured);
                Debug.Log($"씬 스크린샷 캡처 완료: {assetPath} ({width}x{height})", captured);
            }
            finally
            {
                RenderTexture.active = null;
                Object.DestroyImmediate(cameraObject);
                targetRT.Release();
                Object.DestroyImmediate(targetRT);
            }
        }

        // 결과는 Assets/Textures/Cubemaps/{씬이름}Cubemap_01.exr로 저장되며, 같은 이름이 있으면 덮어쓴다.
        private static void Capture(Vector3 capturePosition, float captureYaw, string positionSource)
        {
            // HideAndDontSave: 캡처용 임시 카메라가 씬에 저장되거나 하이어라키에 노출되지 않게 한다.
            GameObject cameraObject = new GameObject("SceneCubemapCaptureCamera")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            Camera captureCamera = cameraObject.AddComponent<Camera>();
            captureCamera.enabled = false;
            captureCamera.transform.SetPositionAndRotation(capturePosition, Quaternion.identity);
            captureCamera.clearFlags = CameraClearFlags.Skybox;
            captureCamera.nearClipPlane = 0.3f;
            captureCamera.farClipPlane = 1000f;
            captureCamera.allowHDR = true;
            // 큐브맵 한 면은 시야각 90도의 정사각형이어야 6면이 빈틈없이 이어진다.
            captureCamera.fieldOfView = 90f;
            captureCamera.aspect = 1f;

            RenderTexture cubeRT = new RenderTexture(FaceSize, FaceSize, 24, RenderTextureFormat.ARGBHalf)
            {
                dimension = TextureDimension.Cube
            };
            RenderTexture equirectRT = new RenderTexture(EquirectWidth, EquirectHeight, 0, RenderTextureFormat.ARGBHalf);
            RenderTexture rotatedRT = null;
            RenderTexture faceFlipSrc = null;
            RenderTexture faceFlipDst = null;

            try
            {
                RenderPipeline.StandardRequest request = new RenderPipeline.StandardRequest();
                if (!RenderPipeline.SupportsRenderRequest(captureCamera, request))
                {
                    Debug.LogError("현재 렌더 파이프라인이 StandardRequest 렌더링을 지원하지 않아 캡처할 수 없습니다.");
                    return;
                }

                // StandardRequest.face는 "어느 면에 그릴지"만 정하고 카메라를 회전시키지 않는다.
                // 회전 없이 제출하면 6면에 같은 화면이 복사되어 만화경처럼 보이므로,
                // 면마다 큐브맵 규약(D3D 순서: +X,-X,+Y,-Y,+Z,-Z)에 맞는 방향으로 직접 회전시킨다.
                Quaternion[] faceRotations =
                {
                    Quaternion.Euler(0f, 90f, 0f),   // +X: 오른쪽
                    Quaternion.Euler(0f, -90f, 0f),  // -X: 왼쪽
                    Quaternion.Euler(-90f, 0f, 0f),  // +Y: 위
                    Quaternion.Euler(90f, 0f, 0f),   // -Y: 아래
                    Quaternion.identity,             // +Z: 앞
                    Quaternion.Euler(0f, 180f, 0f),  // -Z: 뒤
                };

                for (int face = 0; face < 6; face++)
                {
                    captureCamera.transform.rotation = faceRotations[face];
                    request.destination = cubeRT;
                    request.face = (CubemapFace)face;
                    RenderPipeline.SubmitRenderRequest(captureCamera, request);
                }

                // DirectX 계열(UV 원점이 위)에서는 면 렌더 결과가 위아래 뒤집혀 기록된다.
                // 전체 이미지가 아니라 면 단위로 뒤집히는 것을 실제 캡처로 확인했기 때문에,
                // 각 면을 제자리에서 세로로 뒤집어 보정한다.
                if (SystemInfo.graphicsUVStartsAtTop)
                {
                    faceFlipSrc = new RenderTexture(FaceSize, FaceSize, 0, RenderTextureFormat.ARGBHalf);
                    faceFlipDst = new RenderTexture(FaceSize, FaceSize, 0, RenderTextureFormat.ARGBHalf);
                    faceFlipSrc.Create();
                    faceFlipDst.Create();

                    for (int face = 0; face < 6; face++)
                    {
                        Graphics.CopyTexture(cubeRT, face, 0, faceFlipSrc, 0, 0);
                        Graphics.Blit(faceFlipSrc, faceFlipDst, new Vector2(1f, -1f), new Vector2(0f, 1f));
                        Graphics.CopyTexture(faceFlipDst, 0, 0, cubeRT, face, 0);
                    }
                }

                // 큐브맵 RT를 그대로 저장할 수 없어 등장방형(2:1)으로 펼친다.
                // 임포터의 Auto Cubemap이 2:1 비율을 보고 다시 큐브맵으로 복원한다.
                cubeRT.ConvertToEquirect(equirectRT, Camera.MonoOrStereoscopicEye.Mono);

                // 큐브맵 면 렌더링은 카메라 회전을 무시하고 항상 월드 축 방향으로 찍는다.
                // 그래서 선택 오브젝트의 Y 회전은 등장방형 이미지를 가로로 순환 이동시켜 반영한다.
                // (등장방형의 가로축이 360도 경도이므로, 가로로 미는 것 = 수평으로 회전시키는 것과 같다)
                equirectRT.wrapMode = TextureWrapMode.Repeat;
                rotatedRT = new RenderTexture(EquirectWidth, EquirectHeight, 0, RenderTextureFormat.ARGBHalf);
                Graphics.Blit(equirectRT, rotatedRT, Vector2.one, new Vector2(captureYaw / 360f, 0f));

                string sceneName = SceneManager.GetActiveScene().name;
                string assetPath = $"{OutputFolder}/{sceneName}Cubemap_01.exr";
                Directory.CreateDirectory(OutputFolder);
                WriteExrAsset(rotatedRT, assetPath);
                ApplyPortalCubemapImportSettings(assetPath);

                Texture captured = AssetDatabase.LoadAssetAtPath<Texture>(assetPath);
                EditorGUIUtility.PingObject(captured);
                Debug.Log($"씬 큐브맵 캡처 완료: {assetPath} (기준점: {positionSource} {capturePosition}, 정면 Y회전 {captureYaw:F0}도)", captured);
            }
            finally
            {
                // 렌더 요청/등장방형 변환이 RT를 active로 남겨둘 수 있어, 해제 전에 반드시 풀어준다.
                // 풀지 않으면 Release 시 "Releasing render texture that is set to be RenderTexture.active!" 경고가 남는다.
                RenderTexture.active = null;
                Object.DestroyImmediate(cameraObject);
                cubeRT.Release();
                Object.DestroyImmediate(cubeRT);
                equirectRT.Release();
                Object.DestroyImmediate(equirectRT);
                if (rotatedRT != null)
                {
                    rotatedRT.Release();
                    Object.DestroyImmediate(rotatedRT);
                }
                if (faceFlipSrc != null)
                {
                    faceFlipSrc.Release();
                    Object.DestroyImmediate(faceFlipSrc);
                }
                if (faceFlipDst != null)
                {
                    faceFlipDst.Release();
                    Object.DestroyImmediate(faceFlipDst);
                }
            }
        }

        private static Vector3 GetCapturePosition(out float captureYaw, out string positionSource)
        {
            // 포탈에서 보일 시점을 팀원이 직접 고를 수 있도록 선택 오브젝트를 최우선으로 쓴다.
            // 회전은 Y축만 반영한다. X/Z 회전까지 반영하면 지평선이 기울어진 큐브맵이 나오기 때문.
            if (Selection.activeGameObject != null)
            {
                captureYaw = Selection.activeGameObject.transform.eulerAngles.y;
                positionSource = $"선택 오브젝트 '{Selection.activeGameObject.name}'";
                return Selection.activeGameObject.transform.position;
            }

            captureYaw = 0f;

            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null)
            {
                positionSource = "씬 뷰 피벗";
                return sceneView.pivot;
            }

            positionSource = "기본 위치";
            return Vector3.up * 1.5f;
        }

        // RT 픽셀을 읽어 float EXR로 저장하고 에셋으로 임포트한다.
        private static void WriteExrAsset(RenderTexture source, string assetPath)
        {
            Texture2D readback = new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false);
            try
            {
                RenderTexture previousActive = RenderTexture.active;
                RenderTexture.active = source;
                readback.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                readback.Apply();
                RenderTexture.active = previousActive;
                File.WriteAllBytes(assetPath, readback.EncodeToEXR(Texture2D.EXRFlags.CompressZIP));
                AssetDatabase.ImportAsset(assetPath);
            }
            finally
            {
                Object.DestroyImmediate(readback);
            }
        }

        // PortalCubemap_01.exr.meta와 동일한 핵심 설정: 큐브맵 형태, Auto 레이아웃 감지,
        // Specular 컨볼루션(Reflection Probe에서 거칠기별 밉맵 반사에 필요), 심리스 경계.
        private static void ApplyPortalCubemapImportSettings(string assetPath)
        {
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.textureShape = TextureImporterShape.TextureCube;
            settings.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
            settings.cubemapConvolution = TextureImporterCubemapConvolution.Specular;
            settings.seamlessCubemap = true;
            settings.filterMode = FilterMode.Trilinear;
            importer.SetTextureSettings(settings);
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }
    }
}
