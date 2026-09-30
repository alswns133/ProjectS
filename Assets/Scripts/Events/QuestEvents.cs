using System;
using ProjectS.Data;

namespace ProjectS.Events
{
    /// <summary>퀘스트 수락·진행·완료·포기를 알리는 static 이벤트 허브. 보상 지급과 UI 갱신은 구독자가 맡는다.</summary>
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
        /// 퀘스트를 포기했을 때 발행 → 추적 UI가 카드를 제거. 완료(OnQuestCompleted)와 달리 보상 지급은 없다.
        /// </summary>
        public static event Action<QuestData> OnQuestAbandoned;

        /// <summary>
        /// 진행도가 바뀔 때마다 발행. 인자: (퀘스트, 현재값, 목표값) → 추적 UI가 "3/10" 식으로 표시
        /// </summary>
        public static event Action<QuestData, int, int> OnQuestProgressUpdated;

        /// <summary>
        /// 세이브에서 퀘스트를 복원한 뒤 발행(부트스트랩 로딩 후 1회). 복원은 수락 이벤트를 쏘지 않으므로,
        /// 로딩보다 먼저 켜진 트래커 등 UI가 복원 결과를 반영하려면 이 신호로 다시 그리게 한다.
        /// </summary>
        public static event Action OnQuestsRestored;

        /// <summary><see cref="OnQuestAccepted"/>를 발행한다.</summary>
        public static void FireQuestAccepted(QuestData data) => OnQuestAccepted?.Invoke(data);

        /// <summary><see cref="OnQuestCompleted"/>를 발행한다. 보상 지급은 이 이벤트의 구독자가 한다.</summary>
        public static void FireQuestCompleted(QuestData data) => OnQuestCompleted?.Invoke(data);

        /// <summary><see cref="OnQuestAbandoned"/>를 발행한다.</summary>
        public static void FireQuestAbandoned(QuestData data) => OnQuestAbandoned?.Invoke(data);

        /// <summary><see cref="OnQuestProgressUpdated"/>를 발행한다.</summary>
        /// <param name="data">진행된 퀘스트.</param>
        /// <param name="cur">현재 진행값.</param>
        /// <param name="max">목표값.</param>
        public static void FireQuestProgressUpdated(QuestData data, int cur, int max) => OnQuestProgressUpdated?.Invoke(data, cur, max);

        /// <summary><see cref="OnQuestsRestored"/>를 발행한다. 세이브에서 퀘스트 복원이 끝난 뒤 1회 호출한다.</summary>
        public static void FireQuestsRestored() => OnQuestsRestored?.Invoke();
    }
}
