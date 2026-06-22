using System;

public static class QuestEvents
{
    // 퀘스트 수락
    public static event Action<QuestData> OnQuestAccepted;

    // 퀘스트 완료
    public static event Action<QuestData> OnQuestCompleted;

    // 퀘스트 진행도 갱신 (퀘스트, 현재값, 목표값)
    public static event Action<QuestData, int, int> OnQuestProgressUpdated;

    public static void FireQuestAccepted(QuestData data) => OnQuestAccepted?.Invoke(data);

    public static void FireQuestCompleted(QuestData data) => OnQuestCompleted?.Invoke(data);

    public static void FireQuestProgressUpdated(QuestData data, int cur, int max) => OnQuestProgressUpdated?.Invoke(data, cur, max);
}
