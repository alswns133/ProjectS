using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using TMPro;
using ProjectS.Data;

namespace ProjectS.UI
{
    /// <summary>
    /// 퀘스트 리스트 한 줄. 마커 아이콘(자식 오브젝트 토글: 수락 가능=!, 완료 가능=?) + 제목 + 선택 표시.
    /// 마커 색은 종류 표시 — 메인=노랑, 반복=하양. 단 완료 가능(반납 대기) 줄은 제목·마커 모두 완료 색으로 칠한다
    /// (<see cref="SetCompleted"/>). 마우스 올리면 선택 이동, 클릭하면 선택+실행.
    ///
    /// 선택 표시는 두 가지다.
    ///  (1) 앞의 화살표(&gt;) 이미지(<see cref="selectionArrow"/>)를 선택 시에만 켠다.
    ///  (2) 버튼 컬러 트랜지션(Selected) — 뷰가 이 줄로 EventSystem 선택을 옮기면 Unity가 색을 칠한다.
    ///      쓰려면 <see cref="selectable"/>(버튼)의 Transition=Color Tint로 두고 색들을 설정한다.
    /// (진행중 퀘스트는 리스트에 오지 않으므로 여기서 다루지 않는다.)
    /// </summary>
    public class NpcQuestListRow : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
    {
        // 마커는 스프라이트 교체가 아니라 자식 오브젝트 두 개를 켜고 끈다.
        // !와 ? 원본 크기가 달라 한 Image에 번갈아 끼우면 RectTransform 크기가 한쪽에만 맞아 다른 쪽이 찌그러진다.
        [Tooltip("수락 가능(!) 마커. 수락 가능 상태에서만 켠다. 색으로 종류(메인/반복)를 나타낸다.")]
        [FormerlySerializedAs("markerIcon")]
        [SerializeField] private Image acceptableMarker;
        [Tooltip("완료 가능(?) 마커. 완료 가능 상태에서만 켠다.")]
        [SerializeField] private Image completableMarker;
        [SerializeField] private TMP_Text titleText;

        [Tooltip("선택된 줄 앞에 뜨는 화살표(>) 이미지. 선택 시에만 켠다.")]
        [FormerlySerializedAs("highlight")]
        [SerializeField] private GameObject selectionArrow;

        [Tooltip("이 줄의 버튼(컬러 트랜지션으로도 선택을 표시). 비우면 같은 오브젝트에서 자동으로 찾는다.")]
        [SerializeField] private Selectable selectable;

        [Header("종류 색")]
        [SerializeField] private Color mainColor = new Color(1f, 0.85f, 0.2f);   // 메인=노랑
        [SerializeField] private Color repeatColor = Color.white;                // 반복=하양

        [Header("완료 색")]
        [Tooltip("목표를 채워 반납 대기(완료 가능)인 줄의 제목 색.")]
        [SerializeField] private Color completedTitleColor = new Color(0.55f, 0.9f, 0.45f);
        [Tooltip("목표를 채워 반납 대기(완료 가능)인 줄의 마커 색. 완료 상태에선 종류 색 대신 이 색을 쓴다.")]
        [SerializeField] private Color completedIconColor = new Color(0.55f, 0.9f, 0.45f);

        private int index;

        // 완료 해제 시 되돌릴 원래 색. 제목은 프리팹에 칠해 둔 색, 마커는 Bind에서 정한 종류 색이다.
        private Color defaultTitleColor;
        private bool hasDefaultTitleColor;
        private Color typeColor = Color.white;
        private Action<int> onHover;
        private Action<int> onClick;

        /// <summary>이 줄의 Selectable(버튼). 선택 컬러 트랜지션용 EventSystem 선택 대상(없을 수 있음).</summary>
        public Selectable Selectable => selectable;

        private void Awake()
        {
            if (selectable == null) selectable = GetComponent<Selectable>();

            // 선택은 뷰가 SetSelectedGameObject로만 옮긴다. Unity 자체 내비게이션을 꺼서,
            // 키보드 입력에 선택이 한 칸 더 밀려 색이 엉뚱한 행에 뜨는 것을 막는다.
            if (selectable != null)
            {
                Navigation nav = selectable.navigation;
                nav.mode = Navigation.Mode.None;
                selectable.navigation = nav;
            }
        }

        /// <summary>이 줄에 퀘스트 항목을 채운다.</summary>
        /// <param name="index">리스트 내 위치(선택 이동/실행에 쓰인다)</param>
        /// <param name="entry">표시할 항목(제목·종류·상태)</param>
        /// <param name="onHover">마우스 진입 시 이 인덱스로 선택 이동</param>
        /// <param name="onClick">클릭 시 이 인덱스 선택+실행</param>
        public void Bind(int index, in NpcQuestEntry entry, Action<int> onHover, Action<int> onClick)
        {
            this.index = index;
            this.onHover = onHover;
            this.onClick = onClick;

            typeColor = entry.QuestType == QuestType.Main ? mainColor : repeatColor;

            if (titleText != null) titleText.text = entry.Title;
            // 수락 가능=!, 완료 가능=?. (진행중은 리스트에서 제외되므로 오지 않는다.)
            bool completable = entry.Status == NpcQuestStatus.Completable;
            if (acceptableMarker != null) acceptableMarker.gameObject.SetActive(!completable);
            if (completableMarker != null) completableMarker.gameObject.SetActive(completable);

            SetCompleted(entry.Status == NpcQuestStatus.Completable);
            SetSelected(false);
        }

        /// <summary>
        /// 퀘스트 완료(목표 달성, 반납 대기) 표시로 제목·마커 색을 바꾸거나 원래대로 되돌린다.
        /// <see cref="Bind"/>가 상태에 맞춰 부르므로 보통은 직접 부를 필요가 없다.
        /// 줄을 다시 만들지 않고 그 자리에서 상태만 바뀔 때(진행 이벤트 수신 등) 쓴다.
        /// </summary>
        /// <param name="completed">완료 가능 상태면 true, 아니면 false(제목=프리팹 원래 색, 마커=종류 색)</param>
        public void SetCompleted(bool completed)
        {
            CacheDefaultTitleColor();

            if (titleText != null) titleText.color = completed ? completedTitleColor : defaultTitleColor;
            // 켜진 쪽만 칠하면 상태가 바뀌어 반대쪽이 켜질 때 옛 색이 남으므로 둘 다 칠한다.
            Color iconColor = completed ? completedIconColor : typeColor;
            if (acceptableMarker != null) acceptableMarker.color = iconColor;
            if (completableMarker != null) completableMarker.color = iconColor;
        }

        // 행이 비활성 프리팹에서 생성되면 Bind가 Awake보다 먼저 올 수 있어, 첫 사용 시점에 한 번만 잡는다.
        // 이미 완료색으로 칠한 뒤에 잡으면 원래 색을 잃으므로 반드시 색을 바꾸기 전에 호출한다.
        private void CacheDefaultTitleColor()
        {
            if (hasDefaultTitleColor || titleText == null) return;
            defaultTitleColor = titleText.color;
            hasDefaultTitleColor = true;
        }

        /// <summary>이 줄이 현재 선택됐는지 표시한다(화살표 토글). 버튼 색은 뷰가 EventSystem 선택으로 처리.</summary>
        /// <param name="selected">선택되면 true</param>
        public void SetSelected(bool selected)
        {
            if (selectionArrow != null) selectionArrow.SetActive(selected);
        }

        public void OnPointerEnter(PointerEventData eventData) => onHover?.Invoke(index);
        public void OnPointerClick(PointerEventData eventData) => onClick?.Invoke(index);
    }
}
