using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 캐릭터 선택 화면의 슬롯 카드 1장. 6칸이 씬에 고정 배치되며 런타임에 생성/파괴하지 않는다.
    ///
    /// 카드 높이는 프리팹에 박힌 고정값이고(<c>LayoutElement.preferredHeight</c>), 선택 상태가 바뀌어도
    /// 높이는 변하지 않는다. 구분선 아래 <c>ActionArea</c>의 <b>내용만</b> 바뀐다 —
    /// 비선택이면 보조 정보, 선택이면 게임 시작·삭제 버튼. 카드가 세로로 늘어나는 방식(아코디언)을
    /// 쓰지 않는 이유는 <see cref="VerticalLayoutGroup"/>이 매 프레임 리빌드되고, 늘어난 만큼
    /// 아래 카드가 밀려 커서 밑의 카드가 바뀌며, 에디터에서 최종 배치를 볼 수 없기 때문이다.
    ///
    /// 이 컴포넌트는 표시와 클릭 통지만 담당한다. 세이브 데이터·서버 호출은 알지 못하며,
    /// 상위 컨트롤러가 <see cref="SetCharacter"/>/<see cref="SetEmpty"/>로 값을 넣고 이벤트를 받는다.
    /// (세이브 인스턴스를 뷰로 넘기지 않는 이유: 접속 시 세션에 담아야 하는 것은 서버가 준 인스턴스
    /// 그 자체라, 뷰가 복사본을 들고 있으면 그쪽을 쓰는 실수가 생긴다.)
    /// </summary>
    public class CharacterSlotView : MonoBehaviour
    {
        /// <summary>채워진 카드의 본체를 눌렀다(= 이 캐릭터를 선택). 인자는 슬롯 인덱스.</summary>
        public event Action<int> OnSelected;

        /// <summary>빈 카드를 눌렀다(= 신규 생성 진입). 인자는 슬롯 인덱스.</summary>
        public event Action<int> OnCreateRequested;

        /// <summary>선택된 카드의 게임 시작을 눌렀다. 인자는 슬롯 인덱스.</summary>
        public event Action<int> OnStartRequested;

        /// <summary>선택된 카드의 삭제(×)를 눌렀다. 인자는 슬롯 인덱스.</summary>
        public event Action<int> OnDeleteRequested;

        [Header("카드")]
        [SerializeField] private Button cardButton;
        [SerializeField] private Image selectedFrame;

        [Header("정보")]
        [SerializeField] private Image portrait;
        [SerializeField] private TMP_Text infoText;
        [SerializeField] private TMP_Text emptyLabel;

        [Header("액션 영역 (높이 고정, 자식만 교체)")]
        [SerializeField] private TMP_Text subInfoText;
        [SerializeField] private Button startButton;
        [SerializeField] private Button deleteButton;

        /// <summary>이 카드가 맡은 슬롯 번호(0부터). 목록 갱신 때 다시 지정된다.</summary>
        public int Index { get; private set; }

        /// <summary>빈 슬롯인지. true면 카드 본체 클릭이 선택이 아니라 신규 생성으로 간다.</summary>
        public bool IsEmpty { get; private set; } = true;

        /// <summary>현재 선택된 카드인지. 액션 영역의 내용이 이 값으로 갈린다.</summary>
        public bool IsSelected { get; private set; }

        private void OnEnable()
        {
            cardButton.onClick.AddListener(HandleCardClicked);
            startButton.onClick.AddListener(HandleStartClicked);
            deleteButton.onClick.AddListener(HandleDeleteClicked);
        }

        private void OnDisable()
        {
            cardButton.onClick.RemoveListener(HandleCardClicked);
            startButton.onClick.RemoveListener(HandleStartClicked);
            deleteButton.onClick.RemoveListener(HandleDeleteClicked);
        }

        /// <summary>
        /// 캐릭터가 들어 있는 카드로 채운다. 채우기만 하고 선택 상태는 건드리지 않으므로,
        /// 목록 갱신 후 <see cref="SetSelected"/>를 따로 불러 줘야 한다.
        /// </summary>
        /// <param name="index">슬롯 번호(0부터)</param>
        /// <param name="portraitSprite">초상화. null이면 초상화 칸을 숨긴다</param>
        /// <param name="characterName">표시할 캐릭터 이름</param>
        /// <param name="level">표시할 레벨</param>
        /// <param name="subInfo">비선택일 때 액션 영역에 뜨는 보조 문구(예: "전사 · 시작 마을")</param>
        public void SetCharacter(int index, Sprite portraitSprite, string characterName, int level, string subInfo)
        {
            Index = index;
            IsEmpty = false;

            portrait.sprite = portraitSprite;
            portrait.gameObject.SetActive(portraitSprite != null);
            portrait.enabled = portraitSprite != null;

            infoText.text = $"Lv.{level}  {characterName}";
            infoText.gameObject.SetActive(true);
            emptyLabel.gameObject.SetActive(false);

            subInfoText.text = subInfo;

            ApplySelectionVisual();
        }

        /// <summary>빈 카드("+ 신규 캐릭터")로 만든다. 선택 상태는 함께 해제된다.</summary>
        /// <param name="index">슬롯 번호(0부터)</param>
        public void SetEmpty(int index)
        {
            Index = index;
            IsEmpty = true;
            IsSelected = false;

            portrait.gameObject.SetActive(false);
            infoText.gameObject.SetActive(false);
            emptyLabel.gameObject.SetActive(true);

            ApplySelectionVisual();
        }

        /// <summary>
        /// 선택 상태를 바꾼다. 빈 카드는 선택될 수 없으므로 무시한다.
        /// 카드 높이는 바뀌지 않고 액션 영역의 내용만 교체된다.
        /// </summary>
        /// <param name="selected">선택 상태로 만들지</param>
        public void SetSelected(bool selected)
        {
            IsSelected = selected && !IsEmpty;
            ApplySelectionVisual();
        }

        // 액션 영역은 rect가 고정이고 자식 셋만 켜고 끈다. 여기서 SetActive 말고 크기를 건드리면
        // 상위 VerticalLayoutGroup이 리빌드되어 아래 카드들이 밀린다.
        private void ApplySelectionVisual()
        {
            bool showActions = IsSelected && !IsEmpty;

            selectedFrame.gameObject.SetActive(showActions);
            startButton.gameObject.SetActive(showActions);
            deleteButton.gameObject.SetActive(showActions);

            // 빈 카드는 보조 문구도 띄우지 않는다("+ 신규 캐릭터"가 카드 전체를 쓰므로).
            subInfoText.gameObject.SetActive(!showActions && !IsEmpty);
        }

        // 카드 본체: 채워진 카드면 선택, 빈 카드면 신규 생성으로 갈린다.
        // 시작/삭제 버튼 영역은 그 버튼들이 클릭을 먼저 소비하므로 여기로 오지 않는다.
        private void HandleCardClicked()
        {
            if (IsEmpty) OnCreateRequested?.Invoke(Index);
            else OnSelected?.Invoke(Index);
        }

        private void HandleStartClicked() => OnStartRequested?.Invoke(Index);

        private void HandleDeleteClicked() => OnDeleteRequested?.Invoke(Index);
    }
}
