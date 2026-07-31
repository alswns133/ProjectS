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
        public readonly int QuestId;
        public readonly string Title;
        public readonly QuestType QuestType;     // 아이콘 색: 메인=노랑, 반복=하양
        public readonly NpcQuestStatus Status;

        public NpcQuestEntry(int questId, string title, QuestType questType, NpcQuestStatus status)
        {
            QuestId = questId;
            Title = title;
            QuestType = questType;
            Status = status;
        }
    }
}
