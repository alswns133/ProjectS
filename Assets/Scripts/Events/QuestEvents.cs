using System;

public static class QuestEvents
{
    /// <summary>
    /// 퀘스트를 수락했을 때 발행 → 퀘스트 로그·추적 UI가 갱신
    /// </summary>
    public static event Action<QuestData> OnQuestAccepted;

    /// <summary>
    /// 퀘스트를 완료했을 때 발행 → 보상 지급·UI 갱신 등이 반응
    /// </summary>
    public static event Action<QuestData> OnQuestCompleted;

    /// <summary>
    /// 진행도가 바뀔 때마다 발행. 인자: (퀘스트, 현재값, 목표값) → 추적 UI가 "3/10" 식으로 표시
    /// </summary>
    public static event Action<QuestData, int, int> OnQuestProgressUpdated;

    public static void FireQuestAccepted(QuestData data) => OnQuestAccepted?.Invoke(data);

    public static void FireQuestCompleted(QuestData data) => OnQuestCompleted?.Invoke(data);

    public static void FireQuestProgressUpdated(QuestData data, int cur, int max) => OnQuestProgressUpdated?.Invoke(data, cur, max);
}
