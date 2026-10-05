using ProjectS.UI.Framework;
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 클래스 선택 페이지. 대각선으로 갈린 전신 일러스트 두 장을 보여주고,
    /// 고른 쪽의 <b>반대편</b> 자리에 소개 패널을 연다.
    ///
    /// 진입 직후에는 아무것도 선택되지 않은 상태다(일러스트 두 장만 밝게, 패널 없음).
    /// 선택하면 안 고른 쪽이 어두워지고 그 자리에 패널이 TV 켜지듯 나타난다.
    ///
    /// 패널은 좌우로 <b>움직이지 않는다</b> — 반대편으로 넘어가는 연출은 눈에 거슬려서,
    /// 껐다가 반대쪽 슬롯에서 다시 켜는 방식으로 정했다. 켜지는 연출은 패널에 붙은
    /// Animator 클립이 담당하며, 오브젝트가 비활성→활성될 때 Animator가 기본 상태부터
    /// 다시 재생하므로 재생 코드는 필요 없다.
    /// </summary>
    public class ClassSelectPageView : MonoBehaviour
    {
        /// <summary>전사 클래스 인덱스.</summary>
        public const int ClassWarrior = 1;

        /// <summary>거너 클래스 인덱스.</summary>
        public const int ClassGunner = 2;

        /// <summary>일러스트를 눌러 클래스를 골랐다. 인자는 클래스 인덱스.</summary>
        public event Action<int> OnClassClicked;

        [Header("일러스트")]
        [SerializeField] private Button warriorButton;
        [SerializeField] private Button gunnerButton;

        [Tooltip("전사 쪽 자식 오브젝트 전체의 밝기를 조절하는 CanvasGroup.")]
        [SerializeField] private CanvasGroup warriorCanvasGroup;

        [Tooltip("거너 쪽 자식 오브젝트 전체의 밝기를 조절하는 CanvasGroup.")]
        [SerializeField] private CanvasGroup gunnerCanvasGroup;

        [Tooltip("고르지 않은 쪽 CanvasGroup에 적용되는 알파값. 배경 색은 이제 각 오브젝트가 알아서 갖고 있어 " +
            "여기서는 색이 아니라 밝기(알파)로만 어둡게 한다.")]
        [SerializeField, Range(0f, 1f)] private float dimAlpha = 0.4f;

        [Header("선택 일러스트")]
        [Tooltip("전사 카드를 골랐을 때 나타나는 대형 일러스트. 평소엔 꺼져 있다.")]
        [SerializeField] private RectTransform warriorSelectIllust;

        [Tooltip("거너 카드를 골랐을 때 나타나는 대형 일러스트. 평소엔 꺼져 있다.")]
        [SerializeField] private RectTransform gunnerSelectIllust;

        [Tooltip("전사 카드의 기본(평소) 일러스트. warriorSelectIllust가 나타나는 동안 꺼졌다가, " +
            "선택이 풀리면 다시 켜진다.")]
        [SerializeField] private GameObject warriorBaseIllust;

        [Tooltip("거너 카드의 기본(평소) 일러스트. gunnerSelectIllust가 나타나는 동안 꺼졌다가, " +
            "선택이 풀리면 다시 켜진다.")]
        [SerializeField] private GameObject gunnerBaseIllust;

        [Tooltip("전사 일러스트가 등장할 때 x축으로 밀려 들어오는 거리(anchoredPosition 기준, px). " +
            "0이면 제자리에서 바로 나타난다. 부호로 밀려오는 방향을 정한다.")]
        [SerializeField] private float warriorSelectIllustMoveX = 80f;

        [Tooltip("거너 일러스트가 등장할 때 x축으로 밀려 들어오는 거리. 보통 전사와 반대 부호를 준다.")]
        [SerializeField] private float gunnerSelectIllustMoveX = -80f;

        [Tooltip("밀려 들어오는 데 걸리는 시간(초).")]
        [SerializeField, Min(0.01f)] private float selectIllustMoveDuration = 0.25f;

        [Tooltip("이동 진행 커브(가로 0~1 = 시간, 세로 0~1 = 이동 비율).")]
        [SerializeField] private AnimationCurve selectIllustMoveCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("전사 카드 확장")]
        [Tooltip("전사 카드 루트(IllustWarrior). 전사를 고르면 가로가 늘어난다. 비우면 확장하지 않는다. " +
            "레이아웃 그룹 자식이면 sizeDelta가 덮어써지므로 쓸 수 없다.")]
        [SerializeField] private RectTransform warriorCard;

        [Tooltip("전사를 골랐을 때 카드 가로를 늘리는 양(px). 선택을 풀거나 거너를 고르면 원래 폭으로 돌아온다.")]
        [SerializeField, Min(0f)] private float warriorSelectExtraWidth = 220f;

        [Tooltip("늘어나는 방향. -1 = 왼쪽으로(오른쪽 가장자리 고정), 0 = 피벗 기준(피벗이 0.5면 양쪽으로 똑같이), " +
            "1 = 오른쪽으로(왼쪽 가장자리 고정).")]
        [SerializeField, Range(-1, 1)] private int warriorWidenDirection;

        [Header("거너 카드 확장")]
        [Tooltip("거너 카드 루트(IllustGunner). 거너를 고르면 가로가 늘어난다. 비우면 확장하지 않는다.")]
        [SerializeField] private RectTransform gunnerCard;

        [Tooltip("거너를 골랐을 때 카드 가로를 늘리는 양(px).")]
        [SerializeField, Min(0f)] private float gunnerSelectExtraWidth = 220f;

        [Tooltip("늘어나는 방향. -1 = 왼쪽으로(오른쪽 가장자리 고정), 0 = 피벗 기준, 1 = 오른쪽으로(왼쪽 가장자리 고정).")]
        [SerializeField, Range(-1, 1)] private int gunnerWidenDirection;

        [Header("배경 연출 (선택)")]
        [Tooltip("클래스를 고르는 순간 링을 한 번 터뜨리는 배경 이미지(PulseRingsBurst). 비우면 터지지 않는다.")]
        [SerializeField] private PulseRingsBurst ringBurst;

        [Header("소개 패널")]
        [Tooltip("전사를 골랐을 때 패널이 갈 자리(= 거너 쪽). 레이아웃 컴포넌트를 붙이면 안 된다.")]
        [SerializeField] private RectTransform introSlotRight;

        [Tooltip("거너를 골랐을 때 패널이 갈 자리(= 전사 쪽).")]
        [SerializeField] private RectTransform introSlotLeft;

        [SerializeField] private RectTransform introPanel;
        [Tooltip("클래스 소개 영상(RawImage + VideoPlayer + AddressableVideoView). 비우면 대체 이미지만 띄운다.")]
        [SerializeField] private AddressableVideoView introVideo;
        [Tooltip("영상 주소가 없거나 로드에 실패했을 때(외부 에셋 미동기화 PC 포함) 대신 띄우는 이미지.")]
        [SerializeField] private Image fallbackImage;
        [SerializeField] private TMP_Text infoText;

        [Header("배경")]
        [Tooltip("카드 영역 밖(배경)을 덮는 버튼. 카드를 눌렀을 때는 카드 쪽 Button이 먼저 받으므로, " +
            "여기로 클릭이 넘어온다는 건 카드 밖을 눌렀다는 뜻이다 - 그때 선택을 해제한다.")]
        [SerializeField] private Button backgroundButton;

        [Header("하단")]
        [SerializeField] private Button prevButton;
        [SerializeField] private Button selectButton;

        /// <summary>이전 단계 버튼.</summary>
        public Button PrevButton => prevButton;

        /// <summary>선택 확정 버튼. 아무것도 안 고른 상태에서는 꺼져 있다.</summary>
        public Button SelectButton => selectButton;

        /// <summary>현재 고른 클래스 인덱스. 고르지 않았으면 -1.</summary>
        public int SelectedIndex { get; private set; } = -1;

        // 인스펙터에 배치된 anchoredPosition을 "제자리"로 삼고, 등장할 때만 x축으로 밀어냈다 되돌린다.
        private Vector2 warriorSelectRestPosition;
        private Vector2 gunnerSelectRestPosition;
        private Coroutine selectIllustRoutine;

        // 전사 카드의 평소 크기·위치. 확장은 항상 이 값 기준으로 계산해, 연타·선택 전환에도 값이 누적되지 않게 한다.
        private Vector2 warriorCardRestSize;
        private Vector2 warriorCardRestPosition;
        private Coroutine warriorCardRoutine;
        private Vector2 gunnerCardRestSize;
        private Vector2 gunnerCardRestPosition;
        private Coroutine gunnerCardRoutine;

        private void Awake()
        {
            if (warriorSelectIllust != null) warriorSelectRestPosition = warriorSelectIllust.anchoredPosition;
            if (gunnerSelectIllust != null) gunnerSelectRestPosition = gunnerSelectIllust.anchoredPosition;

            if (warriorCard != null)
            {
                warriorCardRestSize = warriorCard.sizeDelta;
                warriorCardRestPosition = warriorCard.anchoredPosition;
            }

            if (gunnerCard != null)
            {
                gunnerCardRestSize = gunnerCard.sizeDelta;
                gunnerCardRestPosition = gunnerCard.anchoredPosition;
            }

            ClearSelection();
        }

        private void OnEnable()
        {
            warriorButton.onClick.AddListener(HandleWarriorClicked);
            gunnerButton.onClick.AddListener(HandleGunnerClicked);
            if (backgroundButton != null) backgroundButton.onClick.AddListener(ClearSelection);
        }

        private void OnDisable()
        {
            warriorButton.onClick.RemoveListener(HandleWarriorClicked);
            gunnerButton.onClick.RemoveListener(HandleGunnerClicked);
            if (backgroundButton != null) backgroundButton.onClick.RemoveListener(ClearSelection);
        }

        /// <summary>
        /// 클래스를 선택하고 반대편에 소개 패널을 연다.
        /// 이미 같은 클래스가 선택돼 있으면 아무것도 하지 않는다 — 연타할 때마다
        /// 0.15초짜리 등장 연출이 다시 나가면 깜빡거려 보이기 때문이다.
        /// </summary>
        /// <param name="index">클래스 인덱스(<see cref="ClassWarrior"/> / <see cref="ClassGunner"/>)</param>
        /// <param name="info">소개 패널에 띄울 텍스트(이름·이명·나이·무기·시작 위치)</param>
        /// <param name="videoAddress">소개 영상(VideoClip) 어드레서블 주소. 비었거나 로드 실패면 대체 이미지를 보여준다</param>
        public void ShowIntro(int index, string info, string videoAddress = null)
        {
            if (SelectedIndex == index) return;

            SelectedIndex = index;

            bool warriorSelected = index == ClassWarrior;
            warriorCanvasGroup.alpha = warriorSelected ? 1f : dimAlpha;
            gunnerCanvasGroup.alpha = warriorSelected ? dimAlpha : 1f;

            infoText.text = info;

            // 영상을 띄울 수 있을 때는 대체 이미지를 숨겨 두고, 불가능하다고 판정되면 그때 켠다.
            bool tryVideo = introVideo != null && !string.IsNullOrEmpty(videoAddress);
            fallbackImage.gameObject.SetActive(!tryVideo);

            // 껐다 켜면서 반대편 슬롯으로 옮긴다. 활성화 시 Animator가 기본 상태부터 다시 돌아
            // 등장 연출이 재생된다(이동 연출 없이 그 자리에서 켜지는 모양).
            introPanel.gameObject.SetActive(false);
            introPanel.SetParent(warriorSelected ? introSlotRight : introSlotLeft, false);

            if (warriorSelected)
                introSlotRight.gameObject.SetActive(true);
            else
                introSlotLeft.gameObject.SetActive(true);

            introPanel.gameObject.SetActive(true);

            // 영상은 패널을 다시 켠 "뒤에" 건다 — 위의 껐다 켜기에서 영상 위젯의 OnDisable이 재생을 끊고,
            // 꺼진 VideoPlayer는 Prepare도 못 하기 때문이다. 이전 클래스 영상은 그 OnDisable에서 이미 내려갔다.
            if (tryVideo)
                introVideo.Play(videoAddress, () => fallbackImage.gameObject.SetActive(true));

            if (warriorSelected)
            {
                PlaySelectIllust(warriorSelectIllust, warriorSelectRestPosition, warriorSelectIllustMoveX, warriorBaseIllust);
                HideSelectIllust(gunnerSelectIllust, gunnerBaseIllust);
            }
            else
            {
                PlaySelectIllust(gunnerSelectIllust, gunnerSelectRestPosition, gunnerSelectIllustMoveX, gunnerBaseIllust);
                HideSelectIllust(warriorSelectIllust, warriorBaseIllust);
            }

            // 같은 클래스를 다시 눌러도 위의 early return으로 여기까지 오지 않으므로, 연타해도 링이 다시 터지지 않는다.
            if (ringBurst != null)
            {
                RectTransform pickedCard = warriorSelected ? warriorCard : gunnerCard;
                if (pickedCard == null) pickedCard = (warriorSelected ? warriorButton : gunnerButton).transform as RectTransform;
                ringBurst.Burst(pickedCard);
            }

            AnimateCard(warriorCard, ref warriorCardRoutine, warriorCardRestSize, warriorCardRestPosition,
                warriorSelected ? warriorSelectExtraWidth : 0f, warriorWidenDirection);
            AnimateCard(gunnerCard, ref gunnerCardRoutine, gunnerCardRestSize, gunnerCardRestPosition,
                warriorSelected ? 0f : gunnerSelectExtraWidth, gunnerWidenDirection);

            selectButton.interactable = true;
        }

        /// <summary>선택을 지운다(진입 직후·이전 단계로 돌아갈 때). 패널이 닫히고 확정 버튼이 잠긴다.</summary>
        public void ClearSelection()
        {
            SelectedIndex = -1;

            warriorCanvasGroup.alpha = 1f;
            gunnerCanvasGroup.alpha = 1f;

            // 패널을 끄면 영상 위젯의 OnDisable이 재생을 멈추고 클립을 메모리에서 내린다.
            introPanel.gameObject.SetActive(false);
            selectButton.interactable = false;

            if (selectIllustRoutine != null)
            {
                StopCoroutine(selectIllustRoutine);
                selectIllustRoutine = null;
            }

            HideSelectIllust(warriorSelectIllust, warriorBaseIllust);
            HideSelectIllust(gunnerSelectIllust, gunnerBaseIllust);

            // 선택이 풀리면 연출 없이 바로 평소 폭으로 되돌린다(페이지를 떠나거나 다시 열 때 늘어난 채 남지 않게).
            ResetCard(warriorCard, ref warriorCardRoutine, warriorCardRestSize, warriorCardRestPosition, warriorWidenDirection);
            ResetCard(gunnerCard, ref gunnerCardRoutine, gunnerCardRestSize, gunnerCardRestPosition, gunnerWidenDirection);
        }

        private void ResetCard(RectTransform card, ref Coroutine routine, Vector2 restSize, Vector2 restPosition, int direction)
        {
            if (card == null) return;

            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }

            ApplyCardWidth(card, restSize, restPosition, 0f, direction);
        }

        // 고른 쪽 카드는 늘리고, 안 고른 쪽(extra = 0)은 평소 폭으로 되돌린다. 선택 일러스트와 같은 시간·커브를 쓴다.
        // 종료한 코루틴 참조는 따로 비우지 않는다 — 이터레이터는 ref를 받을 수 없고, 끝난 코루틴에 StopCoroutine을 불러도 무해하다.
        private void AnimateCard(RectTransform card, ref Coroutine routine, Vector2 restSize, Vector2 restPosition,
            float targetExtra, int direction)
        {
            if (card == null) return;

            if (routine != null) StopCoroutine(routine);

            routine = StartCoroutine(WidenCard(card, restSize, restPosition, targetExtra, direction));
        }

        private IEnumerator WidenCard(RectTransform card, Vector2 restSize, Vector2 restPosition, float targetExtra, int direction)
        {
            float from = card.sizeDelta.x - restSize.x;
            float elapsed = 0f;

            while (elapsed < selectIllustMoveDuration)
            {
                elapsed += Time.deltaTime;
                float n = Mathf.Clamp01(elapsed / selectIllustMoveDuration);
                float t = selectIllustMoveCurve != null ? selectIllustMoveCurve.Evaluate(n) : n;
                ApplyCardWidth(card, restSize, restPosition, Mathf.LerpUnclamped(from, targetExtra, t), direction);
                yield return null;
            }

            ApplyCardWidth(card, restSize, restPosition, targetExtra, direction);
        }

        // 평소 폭에 extra만큼 더한다. 피벗이 어디든 늘어나는 방향이 인스펙터 설정대로 나오도록 위치를 함께 보정한다:
        // 가장자리는 피벗 비율(px)만큼 왼쪽/나머지(1-px)만큼 오른쪽으로 벌어지므로, 한쪽을 고정하려면 그 몫을 위치로 되돌린다.
        private static void ApplyCardWidth(RectTransform card, Vector2 restSize, Vector2 restPosition, float extra, int direction)
        {
            float pivotX = card.pivot.x;
            float shift = direction > 0 ? pivotX * extra
                : direction < 0 ? -(1f - pivotX) * extra
                : 0f;

            card.sizeDelta = new Vector2(restSize.x + extra, restSize.y);
            card.anchoredPosition = restPosition + new Vector2(shift, 0f);
        }

        // 골라진 쪽 일러스트를 켜고, x축으로 밀린 자리에서 제자리(rest)까지 밀려 들어오게 한다.
        // 대형 일러스트가 뜨는 동안에는 기본 일러스트가 겹쳐 보이지 않도록 함께 끈다.
        private void PlaySelectIllust(RectTransform illust, Vector2 restPosition, float moveX, GameObject baseIllust)
        {
            if (baseIllust != null) baseIllust.SetActive(false);

            if (illust == null) return;

            if (selectIllustRoutine != null) StopCoroutine(selectIllustRoutine);

            illust.gameObject.SetActive(true);
            illust.anchoredPosition = restPosition + new Vector2(moveX, 0f);
            selectIllustRoutine = StartCoroutine(MoveSelectIllust(illust, restPosition));
        }

        private IEnumerator MoveSelectIllust(RectTransform illust, Vector2 restPosition)
        {
            Vector2 from = illust.anchoredPosition;
            float elapsed = 0f;

            while (elapsed < selectIllustMoveDuration)
            {
                elapsed += Time.deltaTime;
                float n = Mathf.Clamp01(elapsed / selectIllustMoveDuration);
                float t = selectIllustMoveCurve != null ? selectIllustMoveCurve.Evaluate(n) : n;
                illust.anchoredPosition = Vector2.LerpUnclamped(from, restPosition, t);
                yield return null;
            }

            illust.anchoredPosition = restPosition;
            selectIllustRoutine = null;
        }

        // 반대편(안 고른 쪽) 일러스트는 연출 없이 바로 끄고, 그 자리에 기본 일러스트를 다시 켠다.
        private static void HideSelectIllust(RectTransform illust, GameObject baseIllust)
        {
            if (illust != null && illust.gameObject.activeSelf) illust.gameObject.SetActive(false);
            if (baseIllust != null) baseIllust.SetActive(true);
        }

        private void HandleWarriorClicked() => OnClassClicked?.Invoke(ClassWarrior);

        private void HandleGunnerClicked() => OnClassClicked?.Invoke(ClassGunner);
    }
}
