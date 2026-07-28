namespace ProjectS.Data
{
    /// <summary>
    /// 퀘스트 대분류. 성장 곡선에서의 역할이 다르다(메인=전진, 반복=보충).
    /// QuestId 앞자리(5=메인, 6=반복)와 반드시 일치해야 하며, 어긋나면 로딩 시 걸러진다
    /// (<see cref="QuestTable.Validate"/>). "구조가 규칙을 만든다" — ID와 종류가 서로를 검증한다.
    /// </summary>
    public enum QuestType
    {
        Main,     // 메인: 선형 체인 + 레벨 게이트. 보상이 성장 분기점(스킬 해금 등)
        Repeat    // 반복: 무한 반복, 동시 최대 3슬롯. 재화·경험치 보충
    }

    /// <summary>
    /// 퀘스트의 목표 판정 방식. 한 퀘스트는 하나의 타입을 가지며,
    /// 이 값이 진행도 추적에서 어떤 게임 이벤트를 구독할지를 결정한다(기획서 4장).
    /// </summary>
    public enum ObjectiveType
    {
        Kill,      // 특정 몬스터 N마리 처치
        Collect,   // 특정 아이템 N개 획득
        Reach,     // 특정 레벨 도달 / 특정 지역 도착
        Clear      // 특정 던전/레이드 클리어
    }

    /// <summary>
    /// 퀘스트 보상의 종류. 실제 지급은 QuestManager가 하지 않고,
    /// QuestEvents.OnQuestCompleted 구독자가 이 값을 보고 처리한다(기획서 7장).
    /// </summary>
    public enum QuestRewardType
    {
        Gold,         // 골드(재화)
        Item,         // 무기/아이템 — TargetId로 아이템 테이블 조회, Amount만큼 지급
        Exp,          // 경험치(레벨업 동반)
        SkillUnlock   // 스킬/각성기 해금 — TargetId(스킬 ID)를 캐릭터 세이브 LearnedSkillIds에 추가
    }

    /// <summary>
    /// 퀘스트 진행 상태(기획서 3장). 매직 넘버 대신 열거형으로 다뤄 전이를 구조적으로 안전하게 한다.
    /// "완료가능"과 "완료"를 나눈 이유: 목표를 채운 시점과 보상을 받는 시점을 구분해야
    /// "NPC에게 돌아가 보고" 흐름과 보상 지급 타이밍을 제어할 수 있다.
    /// </summary>
    public enum QuestState
    {
        NotAccepted,   // 해금됐으나 아직 수락 안 함(런타임 인스턴스가 없는 상태)
        InProgress,    // 수락 후 목표 달성 중
        Completable,   // 목표는 다 채웠고 보상 수령만 남음
        Completed      // 보상 수령 완료
    }
}
