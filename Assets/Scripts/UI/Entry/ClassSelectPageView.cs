using System;
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
        [SerializeField] private Image warriorImage;
        [SerializeField] private Image gunnerImage;

        [Tooltip("고르지 않은 쪽에 곱해지는 색. 검은 오버레이를 덮으면 대각선으로 잘린 알파 밖까지 사각형으로 어두워진다.")]
        [SerializeField] private Color dimColor = new Color(0.4f, 0.4f, 0.4f, 1f);

        [Header("소개 패널")]
        [Tooltip("전사를 골랐을 때 패널이 갈 자리(= 거너 쪽). 레이아웃 컴포넌트를 붙이면 안 된다.")]
        [SerializeField] private RectTransform introSlotRight;

        [Tooltip("거너를 골랐을 때 패널이 갈 자리(= 전사 쪽).")]
        [SerializeField] private RectTransform introSlotLeft;

        [SerializeField] private RectTransform introPanel;
        [SerializeField] private RawImage videoArea;
        [SerializeField] private Image fallbackImage;
        [SerializeField] private TMP_Text infoText;

        [Header("하단")]
        [SerializeField] private Button prevButton;
        [SerializeField] private Button selectButton;

        /// <summary>이전 단계 버튼.</summary>
        public Button PrevButton => prevButton;

        /// <summary>선택 확정 버튼. 아무것도 안 고른 상태에서는 꺼져 있다.</summary>
        public Button SelectButton => selectButton;

        /// <summary>현재 고른 클래스 인덱스. 고르지 않았으면 -1.</summary>
        public int SelectedIndex { get; private set; } = -1;

        private void Awake() => ClearSelection();

        private void OnEnable()
        {
            warriorButton.onClick.AddListener(HandleWarriorClicked);
            gunnerButton.onClick.AddListener(HandleGunnerClicked);
        }

        private void OnDisable()
        {
            warriorButton.onClick.RemoveListener(HandleWarriorClicked);
            gunnerButton.onClick.RemoveListener(HandleGunnerClicked);
        }

        /// <summary>
        /// 클래스를 선택하고 반대편에 소개 패널을 연다.
        /// 이미 같은 클래스가 선택돼 있으면 아무것도 하지 않는다 — 연타할 때마다
        /// 0.15초짜리 등장 연출이 다시 나가면 깜빡거려 보이기 때문이다.
        /// </summary>
        /// <param name="index">클래스 인덱스(<see cref="ClassWarrior"/> / <see cref="ClassGunner"/>)</param>
        /// <param name="info">소개 패널에 띄울 텍스트(이름·이명·나이·무기·시작 위치)</param>
        /// <param name="video">소개 영상 텍스처. null이면 대체 이미지를 보여준다</param>
        public void ShowIntro(int index, string info, Texture video = null)
        {
            if (SelectedIndex == index) return;

            SelectedIndex = index;

            bool warriorSelected = index == ClassWarrior;
            warriorImage.color = warriorSelected ? Color.white : dimColor;
            gunnerImage.color = warriorSelected ? dimColor : Color.white;

            infoText.text = info;

            bool hasVideo = video != null;
            videoArea.texture = video;
            videoArea.enabled = hasVideo;
            fallbackImage.gameObject.SetActive(!hasVideo);

            // 껐다 켜면서 반대편 슬롯으로 옮긴다. 활성화 시 Animator가 기본 상태부터 다시 돌아
            // 등장 연출이 재생된다(이동 연출 없이 그 자리에서 켜지는 모양).
            introPanel.gameObject.SetActive(false);
            introPanel.SetParent(warriorSelected ? introSlotRight : introSlotLeft, false);

            if (warriorSelected)
                introSlotRight.gameObject.SetActive(true);
            else
                introSlotLeft.gameObject.SetActive(true);

            introPanel.gameObject.SetActive(true);

            selectButton.interactable = true;
        }

        /// <summary>선택을 지운다(진입 직후·이전 단계로 돌아갈 때). 패널이 닫히고 확정 버튼이 잠긴다.</summary>
        public void ClearSelection()
        {
            SelectedIndex = -1;

            warriorImage.color = Color.white;
            gunnerImage.color = Color.white;

            introPanel.gameObject.SetActive(false);
            selectButton.interactable = false;
        }

        private void HandleWarriorClicked() => OnClassClicked?.Invoke(ClassWarrior);

        private void HandleGunnerClicked() => OnClassClicked?.Invoke(ClassGunner);
    }
}
