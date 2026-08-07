using System;
using Newtonsoft.Json;
using UnityEngine;

namespace ProjectS.Data
{
    /// <summary>
    /// 등급 공통 규칙 테이블. 한 행이 한 등급(Normal/Magic/Rare/Relic)에 1:1로 대응한다.
    /// 옵션 최대 개수나 표시 색처럼 "아이템 하나가 아니라 등급 전체에 걸리는 값"을 여기 모은다.
    /// 아이템 테이블(EquipmentData) 각 행에 복제하지 않는 이유는, 같은 등급이면 값이 같아서
    /// 수십 개 행에 같은 숫자를 뿌리면 기획자가 상한 하나 바꿀 때 전부 고쳐야 하기 때문이다.
    /// 여기서는 등급 4행만 만지면 된다. (TH)
    /// </summary>
    [Serializable]
    public class ItemGradeData : IDataRow
    {
        /// <summary>등급 값(ItemGrade의 정수). Normal 0 / Magic 1 / Rare 2 / Relic 3.</summary>
        public int Index;

        /// <summary>가독성·검증용. Index가 가리키는 등급과 같아야 한다(시트 정렬 실수 방지).</summary>
        public ItemGrade Grade;

        /// <summary>
        /// 이 등급이 가질 수 있는 옵션 최대 개수(상한). 기획서 확정값:
        /// Normal 0 / Magic 1 / Rare 2 / Relic 4.
        /// EquipmentData.OptionCount(아이템별 실제로 붙는 개수)가 넘을 수 없는 상한이다.
        /// 실제 개수는 등급에서 파생하지 않지만(유물 Lv30 무기가 0개인 예외가 있음),
        /// 상한만은 등급 공통이라 이 테이블이 유일한 기준이 된다.
        /// </summary>
        public int MaxOptionCount;

        /// <summary>
        /// 플레이어에게 보이는 등급 이름("노말"/"매직"/"레어"/"유물").
        /// enum 이름(Normal/Magic/...)을 그대로 쓰지 않는 이유는, 표기가 기획 결정이라
        /// enum 이름을 바꾸면 코드가 깨지기 때문이다. 표기만 이 컬럼에서 갈아끼운다.
        /// </summary>
        public string Label;

        /// <summary>
        /// 등급 표시 색. HTML 표기 "#RRGGBB" 또는 "#RRGGBBAA"를 받는다.
        /// 아이템 이름·등급 라벨·슬롯 테두리처럼 등급으로 색이 갈리는 UI가 전부 이 한 값을 본다.
        /// 문자열을 그대로 남겨 두는 이유: TMP 리치 텍스트 color 태그에 파싱 없이 바로 끼워 넣을 수 있어서다.
        /// (색을 각 UI 컴포넌트의 인스펙터에 4개씩 흩어 두면 톤을 조정할 때 한 곳을 빠뜨린다.)
        /// </summary>
        public string ColorHex;

        /// <summary>
        /// ColorHex를 파싱해 둔 값. 로딩 시 Validate에서 한 번만 변환한다
        /// (조회할 때마다 문자열을 파싱하면 슬롯을 수백 개 그릴 때 그대로 비용이 된다).
        /// JSON에 없는 파생 값이라 직렬화에서는 제외한다.
        /// </summary>
        [JsonIgnore]
        public Color DisplayColor { get; private set; }

        int IDataRow.Index => Index;

        /// <summary>
        /// Index와 Grade가 어긋나면(시트 정렬 실수) 조회 시 엉뚱한 등급을 가리키므로 행을 제외한다.
        /// 개수가 음수이거나 색 표기가 잘못된 경우는 보정만 하고 행은 살린다.
        /// </summary>
        /// <param name="error">탈락 사유(통과 시 null)</param>
        /// <returns>사용 가능한 행이면 true</returns>
        public bool Validate(out string error)
        {
            if ((int)Grade != Index)
            {
                error = $"Index {Index}: Grade({Grade})와 Index가 불일치 (제외됨)";
                return false;
            }

            if (MaxOptionCount < 0) MaxOptionCount = 0;

            // 표기가 비면 enum 이름으로 대체한다. UI에 빈 문자열이 나가면 "왜 안 보이지"로 헤매지만,
            // 영어 이름("Rare")이 뜨면 시트에서 빠진 칸을 바로 알아챌 수 있다.
            if (string.IsNullOrWhiteSpace(Label))
            {
                Debug.LogWarning($"[ItemGradeData] Index {Index}({Grade}): Label 비어 있음 → enum 이름으로 대체");
                Label = Grade.ToString();
            }

            // 색 오타로 행 전체를 버리지는 않는다. 버리면 같은 행의 MaxOptionCount까지 날아가
            // 그 등급의 옵션 상한이 조용히 0이 되기 때문이다. 흰색으로 대체하고 경고만 남긴다
            // (탈락이 아니라 error에 담을 수 없어 여기서 직접 로그한다).
            if (!ColorUtility.TryParseHtmlString(ColorHex, out Color parsed))
            {
                Debug.LogWarning($"[ItemGradeData] Index {Index}({Grade}): ColorHex '{ColorHex}' 파싱 실패 → 흰색 대체");
                parsed = Color.white;
            }
            DisplayColor = parsed;

            error = null;
            return true;
        }
    }
}
