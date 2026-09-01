using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 초대 목록의 플레이어 카드 1장. 표시와 클릭 통지만 담당한다(docs/PARTY_WINDOW_UI.md §4~5).
    /// 세 구역으로 나뉜다 — ① 접속 구체 / ② 레벨·직업·닉네임 / ③ 초대 가능 상태.
    ///
    /// <para>
    /// <b>초대 불가 카드도 버튼을 살려 둔다.</b> EpisodeEntryView처럼 <c>interactable = false</c>로 막으면
    /// 클릭 자체가 안 들어와 "비허용 카드를 누르면 카드가 진동한다"는 피드백을 줄 수 없다. 그래서 입력은
    /// 그대로 받고 <see cref="OnClicked"/>를 항상 발행하며, 초대 가능 여부 판정은 팝업이 한다.
    /// </para>
    /// <para>
    /// <b>① 을 초록/빨강으로 칠하지 않는다.</b> 적록색맹이 구분하지 못하는 조합인 데다 구체가 작다.
    /// 채운 원과 빈 원 <b>두 오브젝트를 갈아 켜서</b> 형태로도 구분되게 한다(색은 프리팹에서 정한 것을 그대로 쓴다).
    /// </para>
    /// <para>
    /// <b>진동은 레이아웃이 관리하지 않는 오브젝트를 흔들어야 한다.</b> 목록이 VerticalLayoutGroup이면
    /// 카드 루트의 위치는 레이아웃 소유라, 흔드는 도중 리빌드가 한 번 돌면 카드가 제자리로 튕겨
    /// 연출이 씹힌다(매 프레임 도는 게 아니라 될 때도 있고 안 될 때도 있는 종류의 버그다).
    /// 그럴 때는 카드 안에 래퍼를 하나 두고 <c>shakeTarget</c>에 지정한다.
    /// </para>
    /// <para>
    /// <b>② 의 세 값은 자리가 고정이다.</b> 직업명 길이에 따라 닉네임 시작 위치가 카드마다 밀리면
    /// 목록을 훑기 어려워진다. 직업을 텍스트가 아니라 아이콘으로 두는 이유도 폭을 고정하기 위해서다.
    /// </para>
    /// </summary>
    public class PartyPlayerCard : MonoBehaviour
    {
        /// <summary>카드를 눌렀다. 인자는 목록 인덱스. <b>초대 불가 카드도 발행한다</b>(위 주석 참고).</summary>
        public event Action<int> OnClicked;

        [Header("카드")]
        [SerializeField] private Button cardButton;
        [Tooltip("카드 바탕. 기본/선택/불가 색을 여기에 칠한다.")]
        [SerializeField] private Image background;
        [Tooltip("선택 테두리. 색만으로 구분되지 않도록 선택 상태에 형태를 하나 더 준다.")]
        [SerializeField] private Image selectedFrame;

        [Header("① 접속")]
        [Tooltip("접속 중 — 꽉 찬 원. 아래 Offline과 둘 중 하나만 켠다.")]
        [SerializeField] private GameObject onlineMark;
        [Tooltip("비접속 — 테두리만 있는 빈 원. 색이 아니라 형태로도 구분되게 하려는 것이다.")]
        [SerializeField] private GameObject offlineMark;

        [Header("② 플레이어")]
        [SerializeField] private TMP_Text levelText;
        [Tooltip("직업 아이콘 오브젝트. 인덱스는 CharacterSaveData.characterType과 같고, 해당하는 하나만 켠다.")]
        [SerializeField] private GameObject[] classIcons;
        [SerializeField] private TMP_Text nameText;

        [Header("③ 상태")]
        [SerializeField] private TMP_Text stateText;

        [Header("색")]
        [SerializeField] private Color normalColor = new Color32(0x2F, 0x5F, 0xD0, 0xFF);
        [SerializeField] private Color selectedColor = new Color32(0xF0, 0xB4, 0x29, 0xFF);
        [Tooltip("초대 불가. 사유가 무엇이든 이 한 가지 색이며, 사유는 ①③에서 읽는다.")]
        [SerializeField] private Color blockedColor = new Color32(0x8A, 0x90, 0x99, 0xFF);

        [Header("거절 진동")]
        [Tooltip("흔들 대상. 목록이 VerticalLayoutGroup이면 카드 루트 대신 안쪽 래퍼를 지정한다(아래 주석 참고). "
               + "비우면 카드 루트를 흔든다.")]
        [SerializeField] private RectTransform shakeTarget;
        [Tooltip("좌우로 흔들 거리(px).")]
        [SerializeField] private float shakeDistance = 8f;
        [Tooltip("진동 전체 길이(초).")]
        [SerializeField] private float shakeDuration = 0.28f;

        /// <summary>이 카드가 맡은 목록 인덱스(0부터).</summary>
        public int Index { get; private set; }

        /// <summary>지금 그리고 있는 플레이어. 목록을 다시 그리기 전까지 유효하다.</summary>
        public PartyMemberInfo Member { get; private set; }

        /// <summary>현재 선택된 카드인지.</summary>
        public bool IsSelected { get; private set; }

        // 진동 중 원래 자리로 되돌리기 위한 기준점. 진동이 겹쳐 좌표가 밀리는 것을 막는다.
        private RectTransform rect;
        private Coroutine shakeRoutine;
        private Vector2 shakeOrigin;

        private void Awake()
        {
            rect = shakeTarget != null ? shakeTarget : transform as RectTransform;
        }

        private void OnEnable()
        {
            if (cardButton != null) cardButton.onClick.AddListener(HandleClicked);
        }

        private void OnDisable()
        {
            if (cardButton != null) cardButton.onClick.RemoveListener(HandleClicked);

            // 꺼진 채로 코루틴이 끊기면 카드가 흔들린 위치에 그대로 굳는다. 풀에서 다시 꺼내 쓰므로 반드시 되돌린다.
            StopShake();
        }

        /// <summary>
        /// 카드 내용을 채운다. 선택 상태는 건드리지 않으므로 목록을 다시 그린 뒤
        /// <see cref="SetSelected"/>를 따로 불러 줘야 한다.
        /// </summary>
        /// <param name="index">목록 인덱스(0부터)</param>
        /// <param name="member">표시할 플레이어</param>
        public void SetMember(int index, PartyMemberInfo member)
        {
            Index = index;
            Member = member;
            if (member == null) return;

            if (levelText != null) levelText.text = $"Lv.{member.Level}";
            if (nameText != null) nameText.text = member.Nickname;

            // 직업 아이콘: 해당하는 하나만 켠다. 범위를 벗어나면 전부 꺼져 아이콘 자리가 빈다
            // (엉뚱한 직업을 보여주는 것보다 낫다).
            if (classIcons != null)
            {
                for (int i = 0; i < classIcons.Length; i++)
                {
                    if (classIcons[i] != null) classIcons[i].SetActive(i == member.CharacterType);
                }
            }

            // ① 접속 구체: 색이 아니라 형태(채운 원/빈 원)가 다른 두 오브젝트를 갈아 켠다.
            // 색 하나에만 기대면 적록색맹이 구분하지 못한다.
            if (onlineMark != null) onlineMark.SetActive(member.IsOnline);
            if (offlineMark != null) offlineMark.SetActive(!member.IsOnline);

            if (stateText != null) stateText.text = DescribeState(member);

            ApplyColor();
        }

        /// <summary>선택 테두리와 카드 색을 갱신한다.</summary>
        /// <param name="selected">이 카드가 선택된 상태인지</param>
        public void SetSelected(bool selected)
        {
            IsSelected = selected;
            if (selectedFrame != null) selectedFrame.enabled = selected;
            ApplyColor();
        }

        /// <summary>
        /// 초대할 수 없는 카드를 눌렀을 때 좌우로 짧게 흔든다. 버튼이 아니라 <b>카드</b>를 흔드는 이유는,
        /// 눌린 자리에서 바로 "이 사람은 안 된다"를 보여 주기 위해서다(버튼을 흔들면 누구 때문인지 알 수 없다).
        /// </summary>
        public void PlayRejectShake()
        {
            if (rect == null || !isActiveAndEnabled) return;

            StopShake();
            shakeOrigin = rect.anchoredPosition;
            shakeRoutine = StartCoroutine(ShakeRoutine());
        }

        private void ApplyColor()
        {
            if (background == null) return;

            if (Member != null && !Member.CanInvite) background.color = blockedColor;
            else background.color = IsSelected ? selectedColor : normalColor;
        }

        // ③ 문구. 접속 여부와 초대 상태를 여기서 합친다 — PartyInviteState에 Offline을 두지 않는 이유다.
        private static string DescribeState(PartyMemberInfo member)
        {
            if (!member.IsOnline) return "비접속";

            switch (member.InviteState)
            {
                case PartyInviteState.InParty:      return "파티중";
                case PartyInviteState.NotAccepting: return "초대 거부";
                default:                            return "초대 가능";
            }
        }

        private void HandleClicked()
        {
            OnClicked?.Invoke(Index);
        }

        private IEnumerator ShakeRoutine()
        {
            float elapsed = 0f;

            while (elapsed < shakeDuration)
            {
                elapsed += Time.unscaledDeltaTime;

                // 뒤로 갈수록 잦아들게 감쇠를 곱한다. 끝에서 뚝 끊기면 카드가 튕긴 것처럼 보인다.
                float damping = 1f - Mathf.Clamp01(elapsed / shakeDuration);
                float offset = Mathf.Sin(elapsed * 46f) * shakeDistance * damping;
                rect.anchoredPosition = shakeOrigin + new Vector2(offset, 0f);
                yield return null;
            }

            rect.anchoredPosition = shakeOrigin;
            shakeRoutine = null;
        }

        // 진행 중이면 멈추고 반드시 원래 자리로 돌려놓는다. 자리를 안 되돌리면 카드가 흔들린 위치에 굳는다.
        private void StopShake()
        {
            if (shakeRoutine == null) return;

            StopCoroutine(shakeRoutine);
            shakeRoutine = null;
            if (rect != null) rect.anchoredPosition = shakeOrigin;
        }
    }
}
