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
    /// 퀘스트 리스트 한 줄. 마커 아이콘(스프라이트: 수락 가능=!, 완료 가능=?) + 제목 + 선택 표시.
    /// 마커 색은 종류 표시 — 메인=노랑, 반복=하양. 마우스 올리면 선택 이동, 클릭하면 선택+실행.
    ///
    /// 선택 표시는 두 가지다.
    ///  (1) 앞의 화살표(&gt;) 이미지(<see cref="selectionArrow"/>)를 선택 시에만 켠다.
    ///  (2) 버튼 컬러 트랜지션(Selected) — 뷰가 이 줄로 EventSystem 선택을 옮기면 Unity가 색을 칠한다.
    ///      쓰려면 <see cref="selectable"/>(버튼)의 Transition=Color Tint로 두고 색들을 설정한다.
    /// (진행중 퀘스트는 리스트에 오지 않으므로 여기서 다루지 않는다.)
    /// </summary>
    public class NpcQuestListRow : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
    {
        [Tooltip("마커 아이콘. 스프라이트로 상태(!·?)를, 색으로 종류(메인/반복)를 나타낸다.")]
        [SerializeField] private Image markerIcon;
        [Tooltip("수락 가능(!) 스프라이트.")]
        [SerializeField] private Sprite acceptableSprite;
        [Tooltip("완료 가능(?) 스프라이트.")]
        [SerializeField] private Sprite completableSprite;
        [SerializeField] private TMP_Text titleText;

        [Tooltip("선택된 줄 앞에 뜨는 화살표(>) 이미지. 선택 시에만 켠다.")]
        [FormerlySerializedAs("highlight")]
        [SerializeField] private GameObject selectionArrow;

        [Tooltip("이 줄의 버튼(컬러 트랜지션으로도 선택을 표시). 비우면 같은 오브젝트에서 자동으로 찾는다.")]
        [SerializeField] private Selectable selectable;

        [Header("종류 색")]
        [SerializeField] private Color mainColor = new Color(1f, 0.85f, 0.2f);   // 메인=노랑
        [SerializeField] private Color repeatColor = Color.white;                // 반복=하양

        private int index;
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

            if (titleText != null) titleText.text = entry.Title;
            if (markerIcon != null)
            {
                markerIcon.sprite = MarkerSprite(entry.Status);
                markerIcon.color = entry.QuestType == QuestType.Main ? mainColor : repeatColor;
            }
            SetSelected(false);
        }

        /// <summary>이 줄이 현재 선택됐는지 표시한다(화살표 토글). 버튼 색은 뷰가 EventSystem 선택으로 처리.</summary>
        /// <param name="selected">선택되면 true</param>
        public void SetSelected(bool selected)
        {
            if (selectionArrow != null) selectionArrow.SetActive(selected);
        }

        // 수락 가능=!, 완료 가능=?. (진행중은 리스트에서 제외되므로 오지 않는다.)
        private Sprite MarkerSprite(NpcQuestStatus status)
            => status == NpcQuestStatus.Completable ? completableSprite : acceptableSprite;

        public void OnPointerEnter(PointerEventData eventData) => onHover?.Invoke(index);
        public void OnPointerClick(PointerEventData eventData) => onClick?.Invoke(index);
    }
}
