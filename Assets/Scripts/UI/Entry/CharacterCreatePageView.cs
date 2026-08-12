using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 캐릭터 생성 페이지. 중앙에 3D 모델을 두고 회전해 볼 수 있게 하며,
    /// 하단 입력창 옆의 생성 버튼으로 확정한다.
    ///
    /// 이름 검증은 <b>즉시 판정되는 것만</b> 여기서 막는다(글자 수·금지문자).
    /// 조건에 맞지 않으면 생성 버튼이 잠기고 <see cref="nameHintText"/>가 이유를 말한다 —
    /// 버튼만 잠그고 이유를 안 보여주면 왜 안 눌리는지 모른 채 멈추게 된다.
    ///
    /// 이름 중복은 서버에 물어봐야 알 수 있어 버튼 비활성으로 막을 수 없다.
    /// 그건 생성을 시도한 뒤 <c>AlertPopup</c>으로 알린다.
    /// </summary>
    public class CharacterCreatePageView : MonoBehaviour
    {
        /// <summary>이름 규칙 최소 길이. Firebase 쪽 검증과 같은 값을 유지해야 한다.</summary>
        public const int MinNameLength = 2;

        /// <summary>이름 규칙 최대 길이.</summary>
        public const int MaxNameLength = 12;

        // Firebase 실시간 DB의 키로 쓸 수 없는 문자들. 서버 검증과 규칙이 갈리면
        // 통과시켜 놓고 생성에서 실패하는 상황이 되므로 값을 함께 맞춰야 한다.
        private const string ForbiddenChars = ".#$[]/";

        /// <summary>생성 버튼을 눌렀다. 인자는 다듬어진 캐릭터 이름.</summary>
        public event Action<string> OnCreateRequested;

        [Header("모델 프리뷰")]
        [SerializeField] private RawImage modelViewport;
        [SerializeField] private ModelViewportRotator rotator;
        [SerializeField] private Button rotateLeftButton;
        [SerializeField] private Button rotateRightButton;

        [Header("이름")]
        [SerializeField] private TMP_InputField nameField;
        [SerializeField] private Button createButton;
        [SerializeField] private TMP_Text nameHintText;

        [Header("하단")]
        [SerializeField] private Button prevButton;

        /// <summary>이전 단계 버튼(클래스 선택으로 복귀).</summary>
        public Button PrevButton => prevButton;

        /// <summary>모델 프리뷰가 그려지는 RawImage.</summary>
        public RawImage ModelViewport => modelViewport;

        /// <summary>현재 입력된 이름(앞뒤 공백 제거).</summary>
        public string CharacterName => nameField.text != null ? nameField.text.Trim() : string.Empty;

        private void OnEnable()
        {
            nameField.onValueChanged.AddListener(HandleNameChanged);
            createButton.onClick.AddListener(HandleCreateClicked);
            rotateLeftButton.onClick.AddListener(rotator.StepLeft);
            rotateRightButton.onClick.AddListener(rotator.StepRight);

            Validate();
        }

        private void OnDisable()
        {
            nameField.onValueChanged.RemoveListener(HandleNameChanged);
            createButton.onClick.RemoveListener(HandleCreateClicked);
            rotateLeftButton.onClick.RemoveListener(rotator.StepLeft);
            rotateRightButton.onClick.RemoveListener(rotator.StepRight);

            // 스테이지를 캐릭터 선택 페이지와 공유하므로, 돌려놓은 채 나가면
            // 선택 화면의 캐릭터가 뒤를 보고 서 있게 된다.
            rotator.ResetRotation();
        }

        /// <summary>입력을 비우고 처음 상태로 되돌린다(페이지 진입·생성 완료 후).</summary>
        public void ClearName()
        {
            nameField.text = string.Empty;
            Validate();
        }

        /// <summary>
        /// 힌트 줄에 문구를 직접 세운다. 서버 응답을 팝업 대신 인라인으로 보여주고 싶을 때 쓴다.
        /// 다음 입력이 들어오면 검증 결과로 덮어써진다.
        /// </summary>
        /// <param name="message">표시할 문구</param>
        public void SetHint(string message)
        {
            if (nameHintText != null) nameHintText.text = message;
        }

        private void HandleNameChanged(string _) => Validate();

        private void HandleCreateClicked()
        {
            if (!IsNameValid(CharacterName, out string _)) return;
            OnCreateRequested?.Invoke(CharacterName);
        }

        // 버튼 잠금과 힌트 문구는 항상 같은 판정에서 나온다(따로 계산하면 어긋난다).
        private void Validate()
        {
            string name = CharacterName;
            bool valid = IsNameValid(name, out string reason);

            createButton.interactable = valid;

            // 아직 아무것도 안 친 상태에서 "2자 이상 입력하세요"가 먼저 떠 있으면 잔소리처럼 보인다.
            // 버튼은 그 상태에서도 잠겨 있으므로 기능상 문제는 없다.
            nameHintText.text = name.Length == 0 ? string.Empty : reason;
        }

        private static bool IsNameValid(string name, out string reason)
        {
            if (name.Length < MinNameLength)
            {
                reason = $"이름은 {MinNameLength}자 이상이어야 합니다.";
                return false;
            }

            if (name.Length > MaxNameLength)
            {
                reason = $"이름은 {MaxNameLength}자까지 쓸 수 있습니다.";
                return false;
            }

            foreach (char c in name)
            {
                if (ForbiddenChars.IndexOf(c) >= 0)
                {
                    reason = "이름에 . # $ [ ] / 는 쓸 수 없습니다.";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }
    }
}
