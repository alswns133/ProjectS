using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 로그인 화면 옆에 잠시 떴다 사라지는 장식용 홀로그램 정보창 하나.
    /// TV 켜짐 → 0/1 데이터 타이핑 → 비트가 깜빡이며 잠시 머묾 → TV 꺼짐 순서로 한 번 재생하고 풀로 돌아간다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>프리팹 없이 코드로 조립한다.</b> 크기가 매번 무작위라 모든 조각을 늘어나는 앵커로 두면 되고,
    /// 장식이라 인스펙터에서 손볼 부분이 없다. 생성은 <see cref="HoloInfoSpawner"/>가 풀링해서 한다.
    /// </para>
    /// <para>
    /// <b>켜짐/꺼짐은 캐릭터 생성 소개 패널의 <c>TV_PowerOn.anim</c>과 같은 타이밍</b>을 코드로 재현한다
    /// (점 → 가로 한 줄 → 세로 펼침 + 흰 플래시). 클립을 쓰지 않는 이유: 창마다 크기가 달라도 배율 애니는 문제없지만,
    /// 꺼짐(역재생)과 급히 걷기(로그인 성공 시)를 속도만 바꿔 같은 코드로 돌리려면 코드 쪽이 단순하다.
    /// </para>
    /// </remarks>
    public class HoloInfoWindow : MonoBehaviour
    {
        // TV_PowerOn.anim 기준 값: 0초 (0.05, 0.02) → 0.05초 (1, 0.02) → 0.15초 (1, 1), 플래시 0 → 0.1초 0.6 → 0.18초 0.
        private const float DotScaleX = 0.05f;
        private const float LineScaleY = 0.02f;
        private const float OnWidenTime = 0.05f;
        private const float OnOpenTime = 0.15f;
        private const float FlashPeakTime = 0.1f;
        private const float FlashEndTime = 0.18f;
        private const float FlashPeakAlpha = 0.6f;

        // 꺼짐: 세로로 접혀 한 줄 → 가로로 줄어 점 → 사라짐.
        private const float OffCloseTime = 0.08f;
        private const float OffShrinkTime = 0.14f;

        private const float Padding = 10f;
        private const float HeaderHeight = 22f;
        private const float BorderThickness = 1.5f;
        private const float CornerLength = 14f;
        private const float CornerThickness = 3f;

        private static readonly Color PanelColor = new Color32(14, 34, 70, 92);
        private static readonly Color BorderColor = new Color32(100, 165, 245, 110);
        private static readonly Color CornerColor = new Color32(150, 205, 255, 220);
        private static readonly Color HeaderLineColor = new Color32(100, 165, 245, 90);
        private static readonly Color HeaderColor = new Color32(200, 228, 255, 235);
        private static readonly Color StatusColor = new Color32(120, 185, 255, 220);
        private static readonly Color FlashColor = new Color32(200, 230, 255, 255);

        private RectTransform rect;
        private CanvasGroup group;
        private Image flash;
        private TMP_Text title;
        private TMP_Text status;
        private TMP_Text body;

        private readonly List<int> scrambleIndices = new();
        private char[] bodyChars = Array.Empty<char>();
        private Coroutine routine;
        private Action<HoloInfoWindow> onFinished;
        private float fontSize;

        /// <summary>지금 재생 중(화면에 떠 있음)인가.</summary>
        public bool IsPlaying { get; private set; }

        /// <summary>풀 배치용 사각형(부모 로컬 좌표). 겹침 판정에 쓴다.</summary>
        public Rect Area { get; private set; }

        /// <summary>
        /// 정보창 하나를 코드로 조립한다. 처음엔 꺼진 상태다.
        /// </summary>
        /// <param name="parent">정보창이 놓일 레이어</param>
        /// <param name="font">본문·머리줄 폰트(숫자·영문이 있어야 함)</param>
        /// <param name="material">그래픽에 쓸 머티리얼(선택). 가산 합성 머티리얼을 주면 더 빛처럼 보인다.</param>
        public static HoloInfoWindow Create(RectTransform parent, TMP_FontAsset font, Material material)
        {
            var go = new GameObject("HoloInfoWindow", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;

            HoloInfoWindow window = go.AddComponent<HoloInfoWindow>();
            window.Build(font, material);
            go.SetActive(false);
            return window;
        }

        /// <summary>
        /// 주어진 자리·크기로 한 번 재생한다. 끝나면(또는 <see cref="Dismiss"/>로 걷히면) 스스로 꺼지고 콜백을 부른다.
        /// </summary>
        /// <param name="area">부모 로컬 좌표의 사각형(부모 피벗 기준)</param>
        /// <param name="bodyFontSize">본문 글자 크기</param>
        /// <param name="charsPerSecond">타이핑 속도</param>
        /// <param name="holdSeconds">다 쓴 뒤 머무는 시간</param>
        /// <param name="finished">끝났을 때 호출(풀 반환용)</param>
        public void Play(Rect area, float bodyFontSize, float charsPerSecond, float holdSeconds, Action<HoloInfoWindow> finished)
        {
            Area = area;
            fontSize = bodyFontSize;
            onFinished = finished;

            RectTransform parent = (RectTransform)rect.parent;
            rect.anchorMin = rect.anchorMax = parent.pivot;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = area.size;
            rect.anchoredPosition = area.center;

            FillContent(area.size);

            gameObject.SetActive(true);
            IsPlaying = true;
            routine = StartCoroutine(PlayRoutine(charsPerSecond, holdSeconds));
        }

        /// <summary>
        /// 재생을 끊고 빠르게 꺼지게 한다. 로그인 성공 시 떠 있는 모든 창을 걷을 때 쓴다.
        /// </summary>
        /// <param name="speed">꺼짐 속도 배율(1 = 평소, 클수록 빠름)</param>
        public void Dismiss(float speed)
        {
            if (!IsPlaying) return;

            if (routine != null) StopCoroutine(routine);

            // 글리치 도중 끊겼으면 자리·알파가 틀어진 채라 먼저 되돌린다.
            rect.anchoredPosition = Area.center;
            group.alpha = 1f;
            routine = StartCoroutine(PowerOffThenFinish(Mathf.Max(0.01f, speed)));
        }

        private IEnumerator PlayRoutine(float charsPerSecond, float holdSeconds)
        {
            body.maxVisibleCharacters = 0;
            yield return PowerOn();

            // 타이핑: 보이는 글자 수를 늘린다(서식 태그는 세지 않으므로 색이 깨지지 않는다).
            body.ForceMeshUpdate();
            int total = body.textInfo.characterCount;
            float typed = 0f;
            float nextScramble = 0f;
            while (typed < total)
            {
                typed += charsPerSecond * Time.unscaledDeltaTime;
                body.maxVisibleCharacters = Mathf.Min(total, Mathf.FloorToInt(typed));
                nextScramble = TickScramble(nextScramble);
                yield return null;
            }

            // 머묾: 비트가 계속 뒤집히고, 가끔 옆으로 튀는 글리치.
            float end = Time.unscaledTime + holdSeconds;
            float nextGlitch = Time.unscaledTime + UnityEngine.Random.Range(0.4f, 1.2f);
            while (Time.unscaledTime < end)
            {
                nextScramble = TickScramble(nextScramble);
                if (Time.unscaledTime >= nextGlitch)
                {
                    yield return Glitch();
                    nextGlitch = Time.unscaledTime + UnityEngine.Random.Range(0.6f, 1.6f);
                }
                yield return null;
            }

            yield return PowerOffThenFinish(1f);
        }

        // 점 → 가로 한 줄 → 세로 펼침, 동시에 흰 플래시가 번쩍였다 사그라든다.
        private IEnumerator PowerOn()
        {
            group.alpha = 1f;
            float t = 0f;
            while (t < FlashEndTime)
            {
                float sx = t < OnWidenTime ? Mathf.Lerp(DotScaleX, 1f, t / OnWidenTime) : 1f;
                float sy = t < OnWidenTime ? LineScaleY : Mathf.Lerp(LineScaleY, 1f, Mathf.Clamp01((t - OnWidenTime) / (OnOpenTime - OnWidenTime)));
                rect.localScale = new Vector3(sx, sy, 1f);

                float a = t < FlashPeakTime
                    ? Mathf.Lerp(0f, FlashPeakAlpha, t / FlashPeakTime)
                    : Mathf.Lerp(FlashPeakAlpha, 0f, (t - FlashPeakTime) / (FlashEndTime - FlashPeakTime));
                SetFlash(a);

                t += Time.unscaledDeltaTime;
                yield return null;
            }

            rect.localScale = Vector3.one;
            SetFlash(0f);
        }

        // 켜짐의 역순: 세로로 접혀 한 줄(플래시 번쩍) → 가로로 줄어 점 → 사라짐.
        private IEnumerator PowerOffThenFinish(float speed)
        {
            float closeTime = OffCloseTime / speed;
            float shrinkTime = OffShrinkTime / speed;
            Vector3 startScale = rect.localScale;

            float t = 0f;
            while (t < shrinkTime)
            {
                float sy = t < closeTime ? Mathf.Lerp(startScale.y, LineScaleY, t / closeTime) : LineScaleY;
                // 켜지는 도중에 걷혀도 튀지 않게 지금 가로 배율에서 줄어든다.
                float sx = t < closeTime ? startScale.x : Mathf.Lerp(startScale.x, DotScaleX, (t - closeTime) / (shrinkTime - closeTime));
                rect.localScale = new Vector3(sx, sy, 1f);

                SetFlash(t < closeTime ? FlashPeakAlpha * (t / closeTime) : FlashPeakAlpha * (1f - (t - closeTime) / (shrinkTime - closeTime)));
                group.alpha = t < closeTime ? 1f : 1f - (t - closeTime) / (shrinkTime - closeTime);

                t += Time.unscaledDeltaTime;
                yield return null;
            }

            Finish();
        }

        // 짧게 옆으로 튀며 옅어졌다 돌아온다 — 투사가 잠깐 불안정한 순간.
        private IEnumerator Glitch()
        {
            Vector2 home = rect.anchoredPosition;
            float shift = UnityEngine.Random.Range(3f, 7f) * (UnityEngine.Random.value < 0.5f ? -1f : 1f);

            rect.anchoredPosition = home + new Vector2(shift, 0f);
            group.alpha = 0.55f;
            yield return new WaitForSecondsRealtime(0.04f);

            rect.anchoredPosition = home - new Vector2(shift * 0.5f, 0f);
            group.alpha = 0.85f;
            yield return new WaitForSecondsRealtime(0.03f);

            rect.anchoredPosition = home;
            group.alpha = 1f;
        }

        // 일정 간격으로 이진수 몇 자리를 뒤집어 데이터가 살아 흐르는 것처럼 보이게 한다.
        private float TickScramble(float nextTime)
        {
            if (Time.unscaledTime < nextTime || scrambleIndices.Count == 0) return nextTime;

            int flips = Mathf.Max(1, scrambleIndices.Count / 14);
            for (int i = 0; i < flips; i++)
            {
                int index = scrambleIndices[UnityEngine.Random.Range(0, scrambleIndices.Count)];
                bodyChars[index] = bodyChars[index] == '0' ? '1' : '0';
            }

            int visible = body.maxVisibleCharacters;
            body.SetText(bodyChars);
            body.maxVisibleCharacters = visible;
            return Time.unscaledTime + 0.08f;
        }

        private void Finish()
        {
            routine = null;
            IsPlaying = false;
            rect.localScale = Vector3.one;
            SetFlash(0f);
            gameObject.SetActive(false);

            Action<HoloInfoWindow> callback = onFinished;
            onFinished = null;
            callback?.Invoke(this);
        }

        // 크기에 맞춰 머리줄·본문을 새로 만든다. 줄 수·글자 수는 창 크기와 글자 크기로 정한다.
        private void FillContent(Vector2 size)
        {
            HoloInfoText.BuildHeader(out string titleText, out string statusText);
            title.text = titleText;
            status.text = statusText;

            // 본문은 고정폭(mspace)으로 찍는다 — 이진수 열이 세로로 맞아야 데이터 표처럼 읽힌다.
            float charWidth = fontSize * 0.62f;
            float lineHeight = fontSize * 1.3f;
            int columns = Mathf.Max(8, Mathf.FloorToInt((size.x - Padding * 2f) / charWidth));
            int lines = Mathf.Max(2, Mathf.FloorToInt((size.y - HeaderHeight - Padding * 2f) / lineHeight));

            string text = HoloInfoText.BuildBody(columns, lines, scrambleIndices);

            // mspace 태그의 길이만큼 뒤집을 위치를 뒤로 민다(태그를 앞에 붙이므로).
            const string mono = "<mspace=.62em>";
            for (int i = 0; i < scrambleIndices.Count; i++) scrambleIndices[i] += mono.Length;

            bodyChars = (mono + text).ToCharArray();
            body.fontSize = fontSize;
            body.SetText(bodyChars);
        }

        private void SetFlash(float alpha)
        {
            Color c = FlashColor;
            c.a = alpha;
            flash.color = c;
        }

        // ── 조립 ─────────────────────────────────────────────

        private void Build(TMP_FontAsset font, Material material)
        {
            rect = (RectTransform)transform;
            group = GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            Image panel = gameObject.AddComponent<Image>();
            panel.color = PanelColor;
            panel.raycastTarget = false;
            if (material != null) panel.material = material;

            // 얇은 테두리 4변 + 대각 두 모서리의 굵은 꺾쇠(홀로그램 창의 전형적인 장식).
            Edge("BorderTop", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, BorderThickness), BorderColor, material);
            Edge("BorderBottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, BorderThickness), BorderColor, material);
            Edge("BorderLeft", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(BorderThickness, 0f), BorderColor, material);
            Edge("BorderRight", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(BorderThickness, 0f), BorderColor, material);
            Corner("CornerTopLeft", new Vector2(0f, 1f), material);
            Corner("CornerBottomRight", new Vector2(1f, 0f), material);

            Image headerLine = CreateImage("HeaderLine", HeaderLineColor, material);
            RectTransform hl = headerLine.rectTransform;
            hl.anchorMin = new Vector2(0f, 1f);
            hl.anchorMax = new Vector2(1f, 1f);
            hl.pivot = new Vector2(0.5f, 1f);
            hl.offsetMin = new Vector2(Padding, -HeaderHeight - 1f);
            hl.offsetMax = new Vector2(-Padding, -HeaderHeight);

            title = CreateText("Title", font, 13f, HeaderColor, TextAlignmentOptions.MidlineLeft);
            TopBand(title.rectTransform);
            status = CreateText("Status", font, 13f, StatusColor, TextAlignmentOptions.MidlineRight);
            TopBand(status.rectTransform);

            body = CreateText("Body", font, 14f, Color.white, TextAlignmentOptions.TopLeft);
            body.richText = true;
            body.textWrappingMode = TextWrappingModes.NoWrap;
            body.overflowMode = TextOverflowModes.Truncate;
            RectTransform br = body.rectTransform;
            br.anchorMin = Vector2.zero;
            br.anchorMax = Vector2.one;
            br.offsetMin = new Vector2(Padding, Padding);
            br.offsetMax = new Vector2(-Padding, -HeaderHeight - Padding * 0.6f);

            flash = CreateImage("Flash", FlashColor, material);
            Stretch(flash.rectTransform);
            SetFlash(0f);
        }

        private void TopBand(RectTransform r)
        {
            r.anchorMin = new Vector2(0f, 1f);
            r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.offsetMin = new Vector2(Padding, -HeaderHeight);
            r.offsetMax = new Vector2(-Padding, 0f);
        }

        private void Edge(string name, Vector2 min, Vector2 max, Vector2 size, Color color, Material material)
        {
            RectTransform r = CreateImage(name, color, material).rectTransform;
            r.anchorMin = min;
            r.anchorMax = max;
            r.pivot = new Vector2(Mathf.Approximately(min.x, max.x) ? min.x : 0.5f, Mathf.Approximately(min.y, max.y) ? min.y : 0.5f);
            r.sizeDelta = size;
            r.anchoredPosition = Vector2.zero;
        }

        // corner = 모서리 위치(0/1). 그 모서리에서 안쪽으로 가로·세로 두 막대를 그어 꺾쇠를 만든다.
        private void Corner(string name, Vector2 corner, Material material)
        {
            RectTransform root = CreateRect(name);
            root.anchorMin = root.anchorMax = corner;
            root.pivot = corner;
            root.sizeDelta = new Vector2(CornerLength, CornerLength);
            root.anchoredPosition = Vector2.zero;

            RectTransform h = CreateImage("H", CornerColor, material, root).rectTransform;
            h.anchorMin = new Vector2(0f, corner.y);
            h.anchorMax = new Vector2(1f, corner.y);
            h.pivot = new Vector2(0.5f, corner.y);
            h.sizeDelta = new Vector2(0f, CornerThickness);
            h.anchoredPosition = Vector2.zero;

            RectTransform v = CreateImage("V", CornerColor, material, root).rectTransform;
            v.anchorMin = new Vector2(corner.x, 0f);
            v.anchorMax = new Vector2(corner.x, 1f);
            v.pivot = new Vector2(corner.x, 0.5f);
            v.sizeDelta = new Vector2(CornerThickness, 0f);
            v.anchoredPosition = Vector2.zero;
        }

        private RectTransform CreateRect(string name, Transform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent != null ? parent : transform, false);
            go.layer = gameObject.layer;
            return (RectTransform)go.transform;
        }

        private Image CreateImage(string name, Color color, Material material, Transform parent = null)
        {
            Image img = CreateRect(name, parent).gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            if (material != null) img.material = material;
            return img;
        }

        private TMP_Text CreateText(string name, TMP_FontAsset font, float size, Color color, TextAlignmentOptions alignment)
        {
            TextMeshProUGUI text = CreateRect(name).gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return text;
        }

        private static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
        }
    }
}
