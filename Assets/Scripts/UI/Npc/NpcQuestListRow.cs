using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using ProjectS.Data;

namespace ProjectS.UI
{
    /// <summary>
    /// 퀘스트 리스트 한 줄. 마커(수락 가능=!, 완료 가능=?) + 제목 + 선택 하이라이트.
    /// 마커 색은 종류 표시 — 메인=노랑, 반복=하양. 마우스 올리면 선택 이동, 클릭하면 선택+실행.
    /// (진행중 퀘스트는 리스트에 오지 않으므로 여기서 다루지 않는다.)
    /// </summary>
    public class NpcQuestListRow : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
    {
        [Tooltip("마커 글리프(! / ?). 색으로 종류(메인/반복)도 나타낸다.")]
        [SerializeField] private TMP_Text markerText;
        [SerializeField] private TMP_Text titleText;
        [Tooltip("선택된 줄 표시(배경 등). 선택 시에만 켠다.")]
        [SerializeField] private GameObject highlight;

        [Header("종류 색")]
        [SerializeField] private Color mainColor = new Color(1f, 0.85f, 0.2f);   // 메인=노랑
        [SerializeField] private Color repeatColor = Color.white;                // 반복=하양

        private int index;
        private Action<int> onHover;
        private Action<int> onClick;

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
            if (markerText != null)
            {
                markerText.text = MarkerGlyph(entry.Status);
                markerText.color = entry.QuestType == QuestType.Main ? mainColor : repeatColor;
            }
            SetSelected(false);
        }

        /// <summary>이 줄이 현재 선택됐는지 표시한다.</summary>
        /// <param name="selected">선택되면 true</param>
        public void SetSelected(bool selected)
        {
            if (highlight != null) highlight.SetActive(selected);
        }

        // 수락 가능=!, 완료 가능=?. (진행중은 리스트에서 제외되므로 오지 않는다.)
        private static string MarkerGlyph(NpcQuestStatus status)
        {
            return status == NpcQuestStatus.Completable ? "?" : "!";
        }

        public void OnPointerEnter(PointerEventData eventData) => onHover?.Invoke(index);
        public void OnPointerClick(PointerEventData eventData) => onClick?.Invoke(index);
    }
}
