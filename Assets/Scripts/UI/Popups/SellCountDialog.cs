using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// "판매 개수를 정해주세요" 수량 팝업. 스택형 아이템(소모품·재료)을 판매 슬롯에 올릴 때만 뜬다
    /// (장비는 인스턴스 낱개라 바로 올라간다). 확인 시 정한 개수를 콜백으로 넘기고, 취소는 아무 일도 하지 않는다.
    /// 상점 창 안의 자식으로 두고 ShopSellView가 직접 참조한다(전역 싱글톤 ConfirmDialog와 달리 상점 전용).
    /// </summary>
    public class SellCountDialog : MonoBehaviour
    {
        [Tooltip("수량 입력칸(TMP_InputField). 화살표·슬라이더로 바뀐 값도 여기 표시되고, 숫자를 직접 쳐서 정할 수도 있다.")]
        [SerializeField] private TMP_InputField countInput;
        [SerializeField] private Button increaseButton;
        [SerializeField] private Button decreaseButton;
        [Tooltip("수량 슬라이더(0~최대). wholeNumbers 켜 둘 것.")]
        [SerializeField] private Slider slider;
        [SerializeField] private Button cancelButton;
        [SerializeField] private Button confirmButton;

        [Tooltip("켜면 팝업이 최대 수량(전부 팔기)으로 열린다. 끄면 1개로 열린다.")]
        [SerializeField] private bool startAtMax;

        private Action<int> onConfirm;
        private int maxCount;
        private int count;

        // Show 도중인지. 꺼진 채 시작한 팝업은 Show의 SetActive(true)에서 처음 Awake가 도는데,
        // 그때 Awake의 Close()가 방금 연 팝업을 다시 닫아 버리지 않게 막는다.
        private bool isShowing;

        /// <summary>팝업이 떠 있는지. 떠 있는 동안 다른 판매 입력을 막는 데 쓴다.</summary>
        public bool IsOpen => gameObject.activeSelf;

        private void Awake()
        {
            if (increaseButton != null) increaseButton.onClick.AddListener(() => SetCount(count + 1));
            if (decreaseButton != null) decreaseButton.onClick.AddListener(() => SetCount(count - 1));
            if (slider != null) slider.onValueChanged.AddListener(v => SetCount(Mathf.RoundToInt(v)));
            if (cancelButton != null) cancelButton.onClick.AddListener(Close);
            if (confirmButton != null) confirmButton.onClick.AddListener(OnConfirm);

            if (countInput != null)
            {
                // 숫자만 받는다. 인스펙터 설정에 맡기지 않고 코드로 거는 이유: 빠뜨리면 글자가 들어와 파싱이 늘 실패한다.
                countInput.contentType = TMP_InputField.ContentType.IntegerNumber;
                countInput.onValueChanged.AddListener(OnCountInputChanged);
                countInput.onEndEdit.AddListener(OnCountInputEndEdit);
            }

            // 켜진 채 배치돼 있으면 시작 시 숨긴다. Show가 처음 켜는 중(꺼진 채 배치)이면 닫지 않는다.
            if (!isShowing) Close();
        }

        /// <summary>수량 팝업을 연다.</summary>
        /// <param name="max">올릴 수 있는 최대 개수(스택 수량 − 이미 판매 슬롯에 올린 수).</param>
        /// <param name="confirmHandler">확인 시 정한 개수를 받는 콜백.</param>
        public void Show(int max, Action<int> confirmHandler)
        {
            if (max <= 0) return;   // 올릴 게 없으면 열지 않는다

            // 켜기(=첫 Awake)를 먼저 하고 값을 채운다. 순서가 반대면 첫 Awake의 리스너 등록 전에 값을 넣게 된다.
            isShowing = true;
            gameObject.SetActive(true);
            isShowing = false;
            transform.SetAsLastSibling();   // 같은 캔버스의 슬롯·카드보다 위에 그린다

            maxCount = max;
            onConfirm = confirmHandler;

            if (slider != null)
            {
                slider.wholeNumbers = true;
                slider.minValue = 0;
                slider.maxValue = max;
            }

            SetCount(startAtMax ? max : 1);

            // 뜨자마자 바로 숫자를 칠 수 있게 입력칸에 포커스를 준다.
            if (countInput != null) countInput.ActivateInputField();
        }

        /// <summary>팝업을 닫는다(취소와 같음). 상점 창이 닫히거나 모드가 바뀔 때도 부른다.</summary>
        public void Close()
        {
            onConfirm = null;
            gameObject.SetActive(false);
        }

        private void SetCount(int value)
        {
            count = Mathf.Clamp(value, 0, maxCount);

            // WithoutNotify: 코드가 넣은 값으로 onValueChanged가 다시 돌아 SetCount가 서로를 부르지 않게.
            if (countInput != null) countInput.SetTextWithoutNotify(count.ToString());
            if (slider != null) slider.SetValueWithoutNotify(count);

            RefreshControls();
        }

        // 타이핑 중 실시간 반영. 확인 버튼이 count를 바로 읽으므로 엔터/포커스 해제를 기다리지 않는다.
        private void OnCountInputChanged(string text)
        {
            // 빈칸(전부 지우고 다시 쓰는 중)은 그대로 둔다 — 여기서 0으로 채우면 "1을 지우고 5를 치면 05"처럼 꼬인다.
            // 숫자로 못 읽는 값(음수 부호만 친 상태 등)도 같은 이유로 확정 때까지 기다린다.
            if (!int.TryParse(text, out int value)) return;

            int clamped = Mathf.Clamp(value, 0, maxCount);
            count = clamped;
            if (slider != null) slider.SetValueWithoutNotify(count);

            // 최대 초과·음수 입력은 즉시 잘라 보여준다(몇 개까지 팔 수 있는지 바로 알 수 있게).
            // 범위 안이면 입력칸을 덮어쓰지 않는다 — 덮어쓰면 캐럿이 끝으로 튄다.
            if (clamped != value && countInput != null)
            {
                countInput.SetTextWithoutNotify(clamped.ToString());
                countInput.caretPosition = countInput.text.Length;
            }

            RefreshControls();
        }

        // 입력 확정(엔터·포커스 해제) 시 빈칸이나 잘못된 값을 현재 count로 되돌린다.
        private void OnCountInputEndEdit(string text)
        {
            SetCount(int.TryParse(text, out int value) ? value : count);
        }

        // 화살표·확인 버튼 상태를 현재 count/maxCount에 맞춘다(입력칸 텍스트는 건드리지 않음).
        private void RefreshControls()
        {
            // 끝에 닿으면 눌러도 변화가 없으므로 버튼을 꺼서 한계를 눈에 보이게 한다.
            if (increaseButton != null) increaseButton.interactable = count < maxCount;
            if (decreaseButton != null) decreaseButton.interactable = count > 0;
            if (confirmButton != null) confirmButton.interactable = count > 0;   // 0개 판매는 의미 없음
        }

        private void OnConfirm()
        {
            if (count <= 0) return;

            // Close가 onConfirm을 지우므로 먼저 지역변수로 받아 둔다. 닫은 뒤 부르는 이유는,
            // 콜백 안에서 꽉 참 토스트 등이 뜰 때 팝업이 이미 내려가 있어야 화면이 자연스럽기 때문이다.
            Action<int> handler = onConfirm;
            int confirmed = count;

            Close();
            handler?.Invoke(confirmed);
        }
    }
}
