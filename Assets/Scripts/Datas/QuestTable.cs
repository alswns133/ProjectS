using System;
using System.Collections.Generic;

namespace ProjectS.Data
{
    /// <summary>
    /// 퀘스트 한 개의 '정의' 행(기획서 8.1). JsonManager가 QuestId를 키로 로드해 캐시한다.
    /// 진행 상태(카운트·완료)는 담지 않는다 — 수락하면 이 정의로부터 런타임 인스턴스(<see cref="QuestData"/>)를
    /// 만들고, 변하는 값은 그 인스턴스가 들고 있다("ID만 저장, 값은 테이블 조회" 원칙).
    /// </summary>
    [Serializable]
    public class QuestTable : IDataRow
    {
        /// <summary>퀘스트 고유 ID. 6자리 [종류][지역2][순번3](docs/ID_NUMBERING.md). Validate가 형식을 강제한다.</summary>
        public int QuestId;

        /// <summary>메인/반복. QuestId 앞자리(5=메인, 6=반복)와 일치해야 한다(Validate가 강제).</summary>
        public QuestType QuestType;

        /// <summary>
        /// 이 퀘스트를 주고 반납받는 NPC 이름. (기획서 8.1에는 아직 없는 확장 필드 —
        /// NPC↔퀘스트 연결/대화 흐름이 확정되기 전까지 QuestGiver 자동 조회용으로 임시 사용한다.)
        /// </summary>
        public string QuestNpc = string.Empty;

        /// <summary>이 퀘스트의 목표 판정 방식. 퀘스트당 하나다(4장).</summary>
        public ObjectiveType ObjectiveType;

        /// <summary>목표 대상 목록(대상 ID + 필요 수량). 다중 목표를 위해 배열이며 최소 1개다.</summary>
        public List<ObjectiveTarget> ObjectiveTargets = new();

        /// <summary>수락/완료에 필요한 레벨. 0이면 레벨 제한이 없다.</summary>
        public int RequiredLevel;

        /// <summary>선행 메인 퀘스트 ID(체인용). 0이면 선행이 없다(체인의 첫 퀘스트).</summary>
        public int PrerequisiteQuestId;

        /// <summary>완료 보상 목록.</summary>
        public List<QuestRewardData> Rewards = new();

        /// <summary>
        /// 수락 전 도입 대사(DialogueTable ID). 이 퀘스트가 '수락 가능'일 때만 재생되므로,
        /// 수락하고 나면 더는 나오지 않는다. 0이면 대사 없이 바로 수락.
        /// </summary>
        public int IntroDialogueId;

        /// <summary>반납(완료) 시 재생할 대사(DialogueTable ID). 0이면 대사 없이 바로 반납.</summary>
        public int TurnInDialogueId;

        /// <summary>표시용 제목. 콘텐츠 확정 후 채운다(현재는 자리만).</summary>
        public string Title = string.Empty;

        /// <summary>표시용 설명. 콘텐츠 확정 후 채운다.</summary>
        public string Description = string.Empty;

        int IDataRow.Index => QuestId;

        /// <summary>
        /// 넘버링 규칙과 목표 유효성을 강제한다. 오타난 ID·종류 불일치를 로딩 시점에 걸러
        /// 잘못된 데이터가 조용히 통과하는 것을 막는다("구조가 규칙을 만든다").
        /// 규칙: 6자리 [종류1][지역2][순번3], 종류 5=메인·6=반복, 그리고 그 종류가 QuestType과 일치.
        /// </summary>
        /// <param name="error">탈락 사유(통과 시 null)</param>
        /// <returns>사용 가능한 행이면 true</returns>
        public bool Validate(out string error)
        {
            // 6자리가 아니면 kind가 한 자리로 떨어지지 않아 5/6 검사에서 자연히 걸러진다.
            // 예) 61001(5자리)→kind 0, 6010010(7자리)→kind 60. 둘 다 탈락.
            int kind = QuestId / 100000;      // 앞 1자리(종류)
            int region = (QuestId / 1000) % 100;  // 2~3자리(지역)
            int seq = QuestId % 1000;         // 4~6자리(순번)

            QuestType idType;
            if (kind == 5) idType = QuestType.Main;
            else if (kind == 6) idType = QuestType.Repeat;
            else
            {
                error = $"QuestId {QuestId}: 6자리이고 앞자리(종류)가 5(메인)/6(반복)이어야 함";
                return false;
            }

            // ID가 말하는 종류와 필드가 어긋나면 둘 중 하나가 오타다. 어느 쪽도 신뢰할 수 없으니 탈락시킨다.
            if (idType != QuestType)
            {
                error = $"QuestId {QuestId}: ID 종류({idType})와 QuestType({QuestType})가 불일치";
                return false;
            }

            if (region < 1)
            {
                error = $"QuestId {QuestId}: 지역 자리(2~3번째)는 01 이상이어야 함";
                return false;
            }

            if (seq < 1)
            {
                error = $"QuestId {QuestId}: 순번 자리(4~6번째)는 001 이상이어야 함";
                return false;
            }

            if (ObjectiveTargets == null || ObjectiveTargets.Count == 0)
            {
                error = $"QuestId {QuestId}: 목표(ObjectiveTargets)가 비어있음";
                return false;
            }

            // 필요 수량이 0/음수면 진행 판정이 성립하지 않으므로 1로 보정한다.
            foreach (var target in ObjectiveTargets)
            {
                if (target.RequiredCount < 1)
                    target.RequiredCount = 1;
            }

            Rewards ??= new List<QuestRewardData>();

            error = null;
            return true;
        }
    }
}
