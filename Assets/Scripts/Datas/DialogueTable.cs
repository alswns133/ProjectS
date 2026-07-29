using System;

namespace ProjectS.Data
{
    /// <summary>
    /// 대화 한 벌(여러 줄)의 정의. DialogueId로 조회한다. 인사말·퀘스트 도입·반납 등 '상황'마다 한 행을 두고,
    /// NPC/퀘스트가 상황에 맞는 DialogueId를 참조한다("상황별 대사"를 분기 로직이 아니라 id 참조로 표현).
    /// </summary>
    [Serializable]
    public class DialogueTable : IDataRow
    {
        public int DialogueId;

        /// <summary>
        /// 이 대화가 특정 NPC의 기본(잡담) 대사면 그 NPC 이름. QuestGiver가 idle 대사를 이름으로 자동 조회할 때 쓴다
        /// (questIds를 비우면 QuestNpc로 자동 조회하는 것과 같은 방식). 퀘스트 도입/반납 대사는 비워 둔다.
        /// </summary>
        public string Npc = string.Empty;

        /// <summary>순서대로 재생할 대사들. 화자가 섞일 수 있다(NPC ↔ 플레이어 ↔ 제3자).</summary>
        public DialogueLine[] Lines;

        int IDataRow.Index => DialogueId;

        /// <summary>
        /// 대사가 하나도 없으면 재생할 게 없으므로 행을 제외한다.
        /// </summary>
        /// <param name="error">탈락 사유(통과 시 null)</param>
        /// <returns>사용 가능한 행이면 true</returns>
        public bool Validate(out string error)
        {
            if (DialogueId <= 0)
            {
                error = $"DialogueId {DialogueId}: 0 이하 (제외됨)";
                return false;
            }

            if (Lines == null || Lines.Length == 0)
            {
                error = $"DialogueId {DialogueId}: 대사(Lines)가 비어있음 (제외됨)";
                return false;
            }

            error = null;
            return true;
        }
    }
}
