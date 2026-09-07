using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 파티 슬롯을 눌렀을 때 커서 위치에 뜨는 재사용 단일 컨텍스트 메뉴.
    /// 지금은 [파티 나가기] / [내보내기]뿐이지만, 항목을 <b>부르는 쪽이 넘겨</b> 만들기 때문에
    /// 나중에 귓속말·친구 추가·파티장 위임 같은 것이 붙어도 프리팹을 건드릴 필요가 없다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><see cref="ItemContextMenu"/>와 달리 버튼을 프리팹에 박아 두지 않는다.</b> 아이템 메뉴는
    /// 항목이 장착·등록·사용·파괴로 고정이라 그 방식이 맞지만, 파티 쪽은 무엇이 붙을지 아직 열려 있어
    /// 항목이 늘 때마다 프리팹을 고쳐야 하는 구조를 피했다. 대신 <see cref="entryPrefab"/> 하나를
    /// 복제해 채우고 남는 버튼은 꺼 둔다.
    /// </para>
    /// <para>
    /// <b>되돌릴 수 없는 항목은 <see cref="Entry.ConfirmMessage"/>를 채운다.</b> 그러면 실행 전에
    /// <see cref="ConfirmDialog"/>가 한 번 끼어든다. 파티에서 내보내는 건 다시 초대해야 되돌아오므로
    /// 확인을 받는다.
    /// </para>
    /// <para>
    /// 배치: 전체화면 루트(이 컴포넌트)에 자식 둘 — ① 메뉴 박스(<see cref="menuRect"/>, 커서 위치로 이동.
    /// VerticalLayoutGroup + ContentSizeFitter라 항목 수에 맞춰 높이가 늘고 준다)
    /// ② 전체화면 블로커(바깥 클릭 시 닫힘). 루트는 오버레이 Canvas 직속에 두고 Awake에서 자기를 숨긴다.
    /// </para>
    /// </remarks>
    public class PartyContextMenu : MonoBehaviour
    {
        /// <summary>메뉴에 올릴 항목 하나.</summary>
        public readonly struct Entry
        {
            /// <summary>버튼에 찍을 문구.</summary>
            public readonly string Label;

            /// <summary>고르면 실행할 동작.</summary>
            public readonly Action Action;

            /// <summary>비우면 즉시 실행. 채우면 그 문구로 <see cref="ConfirmDialog"/>를 한 번 거친다.</summary>
            public readonly string ConfirmMessage;

            /// <summary>남에게 영향을 주는 항목인지. 켜면 라벨이 경고색으로 찍힌다.</summary>
            /// <remarks>
            /// <see cref="ConfirmMessage"/>와는 다른 축이다. '파티 나가기'도 확인은 받지만 내 결정이라
            /// 경고색까지 줄 일은 아니고, '내보내기'는 상대를 쫓아내는 것이라 눈에 띄어야 한다.
            /// 확인 유무로 색을 유도하면 둘을 구분할 수 없다.
            /// </remarks>
            public readonly bool Destructive;

            /// <summary>메뉴 항목을 만든다.</summary>
            /// <param name="label">버튼 문구</param>
            /// <param name="action">고르면 실행할 동작</param>
            /// <param name="confirmMessage">되돌릴 수 없는 동작이면 확인 문구를 넣는다</param>
            /// <param name="destructive">상대에게 영향을 주는 항목이면 true(경고색)</param>
            public Entry(string label, Action action, string confirmMessage = null, bool destructive = false)
            {
                Label = label;
                Action = action;
                ConfirmMessage = confirmMessage;
                Destructive = destructive;
            }
        }

        /// <summary>전역 접근점. 슬롯 쪽이 타입 참조 없이 연다.</summary>
        public static PartyContextMenu Instance { get; private set; }

        [Tooltip("커서 위치로 이동하는 메뉴 박스(전체화면 루트가 아니라 안쪽 박스)")]
        [SerializeField] private RectTransform menuRect;

        [Tooltip("메뉴 위에 찍을 대상 이름. 비워도 된다.")]
        [SerializeField] private TMP_Text titleText;

        [Tooltip("항목 버튼이 쌓이는 곳. VerticalLayoutGroup + ContentSizeFitter를 붙인다.")]
        [SerializeField] private RectTransform entryRoot;

        [Tooltip("항목 버튼 한 개짜리 프리팹. 자식에 TMP_Text가 하나 있어야 한다.")]
        [SerializeField] private Button entryPrefab;

        [Tooltip("메뉴 뒤 전체화면 블로커. 바깥을 클릭하면 닫힌다(선택)")]
        [SerializeField] private Button backgroundBlocker;

        [Header("항목 색")]
        [SerializeField] private Color normalColor = new Color32(0xDC, 0xE3, 0xEE, 0xFF);
        [Tooltip("Destructive 항목(내보내기 등)에 쓸 경고색.")]
        [SerializeField] private Color destructiveColor = new Color32(0xE0, 0x5A, 0x5A, 0xFF);

        // 만들어 둔 버튼들. 항목이 줄면 남는 것은 꺼 두고 재사용한다.
        private readonly List<Button> entries = new();
        private readonly List<TMP_Text> entryLabels = new();

        private RectTransform parentRect;
        private Canvas canvas;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (menuRect == null) menuRect = (RectTransform)transform;
            if (entryRoot == null) entryRoot = menuRect;

            parentRect = menuRect.parent as RectTransform;
            canvas = GetComponentInParent<Canvas>();
            menuRect.anchorMin = menuRect.anchorMax = new Vector2(0.5f, 0.5f);

            if (backgroundBlocker != null) backgroundBlocker.onClick.AddListener(Hide);

            gameObject.SetActive(false);
        }

        /// <summary>
        /// 항목을 채워 커서 위치에 연다. 항목이 하나도 없으면 열지 않는다 —
        /// 빈 메뉴가 뜨면 유저는 눌러 놓고 아무것도 못 하는 상태가 된다.
        /// </summary>
        /// <param name="title">메뉴 위에 찍을 대상 이름</param>
        /// <param name="screenPos">커서 스크린 좌표</param>
        /// <param name="items">올릴 항목들</param>
        public void Show(string title, Vector2 screenPos, IReadOnlyList<Entry> items)
        {
            if (items == null || items.Count == 0) return;

            if (titleText != null) titleText.text = title;

            EnsureEntries(items.Count);

            for (int i = 0; i < entries.Count; i++)
            {
                bool used = i < items.Count;
                entries[i].gameObject.SetActive(used);
                if (!used) continue;

                Entry item = items[i];
                if (entryLabels[i] != null)
                {
                    entryLabels[i].text = item.Label;

                    // 색은 보조 신호다. 무엇을 하는지는 문구가 이미 말하고 있고, 색만으로 구분하게 두면
                    // 색을 못 가리는 사람에게는 아무 차이가 없다.
                    entryLabels[i].color = item.Destructive ? destructiveColor : normalColor;
                }

                // 이전에 걸어 둔 리스너를 지우지 않으면 메뉴를 열 때마다 같은 버튼에 동작이 쌓인다.
                entries[i].onClick.RemoveAllListeners();
                entries[i].onClick.AddListener(() => Choose(item));
            }

            gameObject.SetActive(true);
            Position(screenPos);
        }

        /// <summary>메뉴를 닫는다.</summary>
        public void Hide()
        {
            if (this != null) gameObject.SetActive(false);
        }

        // 확인이 필요한 항목은 메뉴를 먼저 닫고 확인창을 띄운다. 확인창이 비동기라 동작을 지역 변수로
        // 잡아 두지 않으면, 콜백이 도는 시점엔 이미 다른 항목으로 덮여 있을 수 있다.
        private void Choose(Entry item)
        {
            Action action = item.Action;
            string confirm = item.ConfirmMessage;

            Hide();
            if (action == null) return;

            if (string.IsNullOrEmpty(confirm))
            {
                action.Invoke();
                return;
            }

            // 확인창이 씬에 없으면 되돌릴 수 없는 동작을 조용히 실행하지 않는다 — 아이템 파괴와 같은 원칙.
            if (ConfirmDialog.Instance == null)
            {
                Debug.LogWarning("[PartyContextMenu] ConfirmDialog가 씬에 없어 동작을 취소했다.");
                return;
            }

            ConfirmDialog.Instance.Show(confirm, action);
        }

        private void EnsureEntries(int count)
        {
            if (entryPrefab == null || entryRoot == null) return;

            while (entries.Count < count)
            {
                Button button = Instantiate(entryPrefab, entryRoot);
                entries.Add(button);
                entryLabels.Add(button.GetComponentInChildren<TMP_Text>(true));
            }
        }

        // 커서가 화면 어느 사분면에 있는지에 따라 피벗을 바꿔, 메뉴가 화면 밖으로 나가지 않게 한다.
        private void Position(Vector2 screenPos)
        {
            bool left = screenPos.x < Screen.width * 0.5f;
            bool top = screenPos.y > Screen.height * 0.5f;
            menuRect.pivot = new Vector2(left ? 0f : 1f, top ? 1f : 0f);

            if (parentRect != null)
            {
                Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    ? canvas.worldCamera
                    : null;

                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPos, cam, out Vector2 local))
                    menuRect.anchoredPosition = local;
            }

            menuRect.SetAsLastSibling();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;
    }
}
