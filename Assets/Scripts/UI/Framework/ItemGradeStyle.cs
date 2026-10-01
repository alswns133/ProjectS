using UnityEngine;
using ProjectS.Data;
using ProjectS.Managers;

namespace ProjectS.UI.Framework
{
    /// <summary>
    /// 등급 표기·색 조회의 공용 창구. 등급 표기와 색은 <see cref="ItemGradeData"/> 테이블(JSON)이 유일한 기준이다.
    /// 슬롯·카드·툴팁마다 테이블 조회와 폴백을 따로 쓰면, 등급을 쓰는 UI가 늘어날 때 폴백 규칙이 제각각이 되고
    /// 색 4개를 코드·인스펙터에 복제하는 곳이 생겨 톤을 바꿀 때 한 곳씩 빠뜨리게 된다. 등급으로 색이 갈리는
    /// UI(이름 텍스트, 이후 테두리 등)는 이 클래스를 거친다.
    /// </summary>
    public static class ItemGradeStyle
    {
        /// <summary>
        /// 등급 행을 조회한다. 로딩 전(IsReady 이전)이거나 행이 없으면 null을 돌려주고, 호출측이 폴백을 정한다.
        /// 등급 하나 때문에 창·툴팁 자체가 안 뜨는 것보다는 낫기 때문이다.
        /// </summary>
        /// <param name="grade">조회할 등급</param>
        /// <returns>등급 행. 없으면 null</returns>
        public static ItemGradeData Row(ItemGrade grade)
            => JsonManager.Instance != null ? JsonManager.Instance.Get<ItemGradeData>((int)grade) : null;

        /// <summary>
        /// 아이템의 등급 표시 색. 아이템이 없거나 등급 행을 못 찾으면 <paramref name="fallback"/>을 돌려준다.
        /// 폴백으로는 보통 프리팹에 지정된 원래 텍스트 색을 넘긴다 — 흰색으로 고정하면 디자이너가 정한 기본색이 사라진다.
        /// </summary>
        /// <param name="item">색을 정할 아이템(null 허용)</param>
        /// <param name="fallback">조회 실패 시 쓸 색</param>
        /// <returns>등급 색 또는 폴백 색</returns>
        public static Color ColorOf(ItemData item, Color fallback)
        {
            if (item == null) return fallback;

            ItemGradeData row = Row(item.Grade);
            return row != null ? row.DisplayColor : fallback;
        }
    }
}
