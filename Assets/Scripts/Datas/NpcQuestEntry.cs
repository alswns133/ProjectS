namespace ProjectS.Data
{
    /// <summary>
    /// NPC 퀘스트 목록에서 각 항목의 상태. 제목 옆에 이 상태를 표시한다.
    /// </summary>
    public enum NpcQuestStatus
    {
        Acceptable,   // 안 받음(수락 가능) — 표시 빈칸, 선택 시 도입 대화 → 수락
        InProgress,   // 받아서 진행 중 — 표시 "진행중"
        Completable   // 목표 완료, 반납 대기 — 표시 "완료 가능", 선택 시 보상 목록 → 반납
    }

    /// <summary>
    /// NPC 퀘스트 목록 한 줄에 필요한 표시용 데이터(제목·종류·상태). QuestManager가 만들어 준다.
    /// </summary>
    public readonly struct NpcQuestEntry
    {
        /// <summary>퀘스트 ID.</summary>
        public readonly int QuestId;

        /// <summary>목록에 표시할 퀘스트 제목.</summary>
        public readonly string Title;

        /// <summary>퀘스트 종류. 아이콘 색을 가른다(메인=노랑, 반복=하양).</summary>
        public readonly QuestType QuestType;
        /// <summary>이 NPC 기준 퀘스트 상태(수락 가능/진행 중/완료 가능 등).</summary>
        public readonly NpcQuestStatus Status;

        /// <summary>목록 항목을 만든다.</summary>
        public NpcQuestEntry(int questId, string title, QuestType questType, NpcQuestStatus status)
        {
            QuestId = questId;
            Title = title;
            QuestType = questType;
            Status = status;
        }
    }
}
