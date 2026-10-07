using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 소프트웨어 마우스 커서. 커서 바깥 글로우와, 커서가 지나간 자리에 회로(전선) 무늬를 그렸다가 천천히 지우는 흔적을 담당한다.
    /// (2026-10-07 TH 추가)
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 하드웨어 커서(<see cref="Cursor.SetCursor(Texture2D, Vector2, CursorMode)"/>)가 아닌가.</b>
    /// 하드웨어 커서는 OS가 텍스처를 그대로 그려 셰이더를 걸 수 없다. 그래서 하드웨어 커서는 투명 텍스처로 바꿔 두고,
    /// 최상단 캔버스의 UI로 커서를 직접 그린다. 대가로 커서가 OS 커서보다 1프레임 늦게 따라온다.
    /// </para>
    /// <para>
    /// <b>표시 여부는 <see cref="Cursor.visible"/>·<see cref="Cursor.lockState"/>를 그대로 따른다.</b>
    /// 하드웨어 커서를 숨길 때 <c>Cursor.visible = false</c>를 쓰지 않고 투명 텍스처로 바꾸는 이유가 이것이다 —
    /// 카메라·대화·팝업 등 여러 곳이 <c>Cursor.visible</c>로 마우스 모드를 켜고 끄는데, 우리가 그 값을 건드리면 그 판단이 전부 꼬인다.
    /// </para>
    /// <para>
    /// <b>흔적.</b> 화면 해상도를 <see cref="trailDownsample"/>로 나눈 RHalf 버퍼 두 장을 핑퐁하며, 매 프레임
    /// 이전 값을 선형으로 깎고 직전 위치 → 현재 위치 선분을 찍는다(<c>Hidden/ProjectS/Cursor Trail Stamp</c>).
    /// 회로 무늬 자체는 버퍼에 그리지 않고 <c>ProjectS/UI Cursor Circuit Trail</c>이 버퍼를 마스크로 읽어 계산한다.
    /// 흔적이 다 사라진 뒤에는 Blit과 전체 화면 RawImage를 함께 꺼서 대기 중 비용을 0으로 만든다.
    /// </para>
    /// 시간은 일시정지(timeScale 0)에도 커서가 살아 있어야 하므로 전부 unscaled 시계를 쓴다.
    /// </remarks>
    [RequireComponent(typeof(Canvas))]
    [DisallowMultipleComponent]
    public class CircuitCursorFx : MonoBehaviour
    {
        /// <summary>
        /// 현재 살아 있는 커서. 씬마다 배치돼 있어도 먼저 생긴 하나만 남긴다(커서가 두 개 그려지는 것을 막기 위함).
        /// </summary>
        public static CircuitCursorFx Instance { get; private set; }

        [Header("구성")]
        [Tooltip("커서 이미지(RawImage + ProjectS/UI Cursor Glow 머티리얼). 아틀라스 UV 문제로 Image가 아니라 RawImage를 쓴다.")]
        [SerializeField] private RawImage cursorImage;

        [Tooltip("화면 전체를 덮는 흔적 이미지(RawImage + ProjectS/UI Cursor Circuit Trail 머티리얼). 텍스처는 스크립트가 넣는다.")]
        [SerializeField] private RawImage trailImage;

        [Tooltip("Hidden/ProjectS/Cursor Trail Stamp. 숨김 셰이더는 어디서도 참조하지 않으면 빌드에서 빠지므로 여기서 직접 참조한다.")]
        [SerializeField] private Shader stampShader;

        [Tooltip("씬을 넘어 유지한다. 이 오브젝트가 루트에 있어야 한다.")]
        [SerializeField] private bool persistAcrossScenes = true;

        [Header("커서")]
        [Tooltip("커서 그림이 차지하는 화면 크기(px). 글로우 여백은 머티리얼의 _Pad만큼 자동으로 더해진다.")]
        [SerializeField] private Vector2 cursorSize = new Vector2(40f, 40f);

        [Tooltip("텍스처 안의 클릭 지점(0~1, 왼쪽 아래 기준). 화살 끝에 맞춘다.")]
        [SerializeField] private Vector2 hotspot = new Vector2(0.03f, 0.984f);

        [Header("흔적")]
        [Tooltip("흔적 버퍼 해상도 = 화면 / 이 값. 흔적은 부드러운 마스크라 낮춰도 티가 거의 안 난다(무늬는 셰이더가 원해상도로 계산).")]
        [SerializeField, Range(1, 8)] private int trailDownsample = 4;

        [Tooltip("커서가 지나가며 무늬를 드러내는 반경(px).")]
        [SerializeField] private float trailRadius = 34f;

        [Tooltip("흔적 가장자리 부드러움(px).")]
        [SerializeField] private float trailSoftness = 18f;

        [Tooltip("찍힌 흔적이 완전히 사라지기까지 걸리는 시간(초).")]
        [SerializeField] private float fadeDuration = 2.5f;

        [Tooltip("이보다 적게 움직이면 찍지 않는다. 멈춘 커서 아래 무늬도 함께 식게 하기 위함.")]
        [SerializeField] private float minMoveDistance = 1f;

        [Tooltip("한 프레임에 이보다 멀리 이동하면 선을 잇지 않는다(커서 잠금 해제 시 화면 중앙으로 튀는 경우 등).")]
        [SerializeField] private float teleportDistance = 600f;

        [Tooltip("좌클릭 순간 커서 주위에 찍는 원 반경(px). 0이면 끈다.")]
        [SerializeField] private float clickBurstRadius = 70f;

        private static readonly int NowId = Shader.PropertyToID("_Now");
        private static readonly int ClickTimeId = Shader.PropertyToID("_ClickTime");
        private static readonly int PadId = Shader.PropertyToID("_Pad");
        private static readonly int RectSizeId = Shader.PropertyToID("_RectSize");
        private static readonly int SegId = Shader.PropertyToID("_Seg");
        private static readonly int ParamsId = Shader.PropertyToID("_Params");
        private static readonly int ScreenPxId = Shader.PropertyToID("_ScreenPx");

        private Canvas canvas;
        private Material cursorMaterial;
        private Material trailMaterial;
        private Material stampMaterial;
        private Texture2D blankCursor;

        private RenderTexture trailRead;
        private RenderTexture trailWrite;

        private Vector2 prevPos;
        private bool hasPrev;
        private float lastStampTime = float.NegativeInfinity;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (persistAcrossScenes)
            {
                if (transform.parent == null) DontDestroyOnLoad(gameObject);
                else Debug.LogWarning("[CircuitCursorFx] 루트 오브젝트가 아니라 씬 유지를 건너뜁니다.", this);
            }

            // 모든 UI 위에 그려야 커서가 팝업·로딩 화면 뒤로 숨지 않는다.
            canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;

            cursorMaterial = InstantiateMaterial(cursorImage);
            trailMaterial = InstantiateMaterial(trailImage);

            if (stampShader != null) stampMaterial = new Material(stampShader) { name = "CursorTrailStamp (Runtime)" };
            else Debug.LogWarning("[CircuitCursorFx] stampShader가 비어 있어 흔적을 그리지 않습니다.", this);

            if (cursorImage != null)
            {
                cursorImage.raycastTarget = false;
                // 크기를 sizeDelta로 직접 정하므로 앵커는 한 점이어야 한다.
                RectTransform rt = cursorImage.rectTransform;
                rt.anchorMin = rt.anchorMax = Vector2.zero;
            }

            if (trailImage != null)
            {
                trailImage.raycastTarget = false;
                // 흔적 버퍼는 화면 전체 좌표라 이미지도 화면 전체를 덮어야 어긋나지 않는다.
                RectTransform rt = trailImage.rectTransform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                trailImage.enabled = false;

                // 흔적이 커서를 덮지 않게(같은 부모일 때 흔적이 커서보다 뒤 순서면 앞으로 당긴다).
                if (cursorImage != null && trailImage.transform.parent == cursorImage.transform.parent
                    && trailImage.transform.GetSiblingIndex() > cursorImage.transform.GetSiblingIndex())
                    trailImage.transform.SetSiblingIndex(cursorImage.transform.GetSiblingIndex());
            }

            // 하드웨어 커서 대신 끼워 둘 투명 텍스처. 일부 플랫폼이 32×32 미만 커서를 거부해 크기를 맞춘다.
            blankCursor = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "BlankCursor" };
            blankCursor.SetPixels32(new Color32[32 * 32]);
            blankCursor.Apply();
        }

        private void OnEnable()
        {
            if (Instance != this) return;
            Cursor.SetCursor(blankCursor, Vector2.zero, CursorMode.Auto);
            hasPrev = false;
        }

        private void OnDisable()
        {
            if (Instance != this) return;
            // 꺼지면 OS 기본 커서로 돌려준다(안 하면 커서가 아예 안 보이게 된다).
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            ReleaseTrailBuffers();
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;

            if (cursorMaterial != null) Destroy(cursorMaterial);
            if (trailMaterial != null) Destroy(trailMaterial);
            if (stampMaterial != null) Destroy(stampMaterial);
            if (blankCursor != null) Destroy(blankCursor);
        }

        // 다른 스크립트가 Update에서 Cursor.visible/lockState를 바꾼 결과를 같은 프레임에 반영하려고 LateUpdate에서 돈다.
        private void LateUpdate()
        {
            float now = Time.unscaledTime;
            Mouse mouse = Mouse.current;

            bool show = mouse != null
                        && Cursor.visible
                        && Cursor.lockState != CursorLockMode.Locked
                        && Application.isFocused;

            Vector2 pos = show ? mouse.position.ReadValue() : Vector2.zero;
            bool clicked = show && mouse.leftButton.wasPressedThisFrame;

            if (cursorImage != null)
            {
                cursorImage.enabled = show;
                if (show) PlaceCursor(pos);
            }

            if (cursorMaterial != null)
            {
                cursorMaterial.SetFloat(NowId, now);
                if (clicked) cursorMaterial.SetFloat(ClickTimeId, now);
            }

            UpdateTrail(show, pos, clicked, now);
        }

        /// <summary>
        /// 커서 이미지를 마우스 위치에 놓는다. 글로우 여백(_Pad)만큼 쿼드를 키우고, 피벗을 클릭 지점에 맞춘다.
        /// </summary>
        private void PlaceCursor(Vector2 screenPos)
        {
            float pad = cursorMaterial != null && cursorMaterial.HasProperty(PadId) ? cursorMaterial.GetFloat(PadId) : 0f;
            float inner = Mathf.Max(1f - 2f * pad, 0.01f);
            float scale = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;

            RectTransform rt = cursorImage.rectTransform;
            rt.sizeDelta = cursorSize / inner / scale;
            rt.pivot = new Vector2(pad + hotspot.x * inner, pad + hotspot.y * inner);
            // Screen Space Overlay에서는 월드 좌표가 곧 화면 px다.
            rt.position = screenPos;
        }

        private void UpdateTrail(bool show, Vector2 pos, bool clicked, float now)
        {
            if (trailImage == null || stampMaterial == null) return;

            Vector2 from = pos;
            float radius = 0f;

            if (!show)
            {
                // 다시 보일 때 숨기 전 위치에서 선을 잇지 않게 끊는다.
                hasPrev = false;
            }
            else if (!hasPrev || (pos - prevPos).sqrMagnitude > teleportDistance * teleportDistance)
            {
                prevPos = pos;
                hasPrev = true;
            }
            else if ((pos - prevPos).sqrMagnitude >= minMoveDistance * minMoveDistance)
            {
                from = prevPos;
                radius = trailRadius;
                prevPos = pos;
            }

            if (clicked && clickBurstRadius > 0f) radius = Mathf.Max(radius, clickBurstRadius);

            bool stamping = radius > 0f;
            if (stamping) lastStampTime = now;

            // 마지막으로 찍은 뒤 다 사라질 시간이 지났으면 Blit도, 전체 화면 그리기도 하지 않는다.
            bool alive = now - lastStampTime <= fadeDuration + 0.1f;
            trailImage.enabled = alive;
            if (!alive)
            {
                ReleaseTrailBuffers();
                return;
            }

            EnsureTrailBuffers();

            stampMaterial.SetVector(SegId, new Vector4(from.x, from.y, pos.x, pos.y));
            stampMaterial.SetVector(ParamsId, new Vector4(
                stamping ? radius : -1f,
                trailSoftness,
                fadeDuration > 0f ? Time.unscaledDeltaTime / fadeDuration : 1f,
                0f));
            stampMaterial.SetVector(ScreenPxId, new Vector4(Screen.width, Screen.height, 0f, 0f));

            Graphics.Blit(trailRead, trailWrite, stampMaterial);
            (trailRead, trailWrite) = (trailWrite, trailRead);

            trailImage.texture = trailRead;
            if (trailMaterial != null)
            {
                trailMaterial.SetFloat(NowId, now);
                trailMaterial.SetVector(RectSizeId, trailImage.rectTransform.rect.size);
            }
        }

        /// <summary>
        /// 화면 크기에 맞는 흔적 버퍼 두 장을 준비한다. 해상도가 바뀌면 새로 만든다(이전 흔적은 버린다).
        /// </summary>
        private void EnsureTrailBuffers()
        {
            int w = Mathf.Max(1, Screen.width / trailDownsample);
            int h = Mathf.Max(1, Screen.height / trailDownsample);
            if (trailRead != null && trailRead.width == w && trailRead.height == h) return;

            ReleaseTrailBuffers();

            RenderTextureFormat format =
                SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RHalf) ? RenderTextureFormat.RHalf :
                SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) ? RenderTextureFormat.ARGBHalf :
                RenderTextureFormat.Default;

            trailRead = CreateBuffer(w, h, format, "CursorTrailA");
            trailWrite = CreateBuffer(w, h, format, "CursorTrailB");
        }

        private static RenderTexture CreateBuffer(int w, int h, RenderTextureFormat format, string bufferName)
        {
            var rt = new RenderTexture(w, h, 0, format)
            {
                name = bufferName,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            rt.Create();

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = prev;
            return rt;
        }

        private void ReleaseTrailBuffers()
        {
            if (trailImage != null && trailImage.texture == trailRead) trailImage.texture = null;

            if (trailRead != null) { trailRead.Release(); Destroy(trailRead); trailRead = null; }
            if (trailWrite != null) { trailWrite.Release(); Destroy(trailWrite); trailWrite = null; }
        }

        // 공유 머티리얼 에셋을 매 프레임 고치면 에디터에서 에셋 파일이 계속 더러워진다. 인스턴스를 만들어 이 이미지만 쓴다.
        private static Material InstantiateMaterial(Graphic graphic)
        {
            if (graphic == null || graphic.material == null || graphic.material == graphic.defaultMaterial) return null;

            var instance = new Material(graphic.material) { name = graphic.material.name + " (Instance)" };
            graphic.material = instance;
            return instance;
        }
    }
}
