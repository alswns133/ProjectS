using UnityEngine;
using UnityEngine.UI;
using ProjectS.Core;
using ProjectS.Managers;
using ProjectS.Scenes;
using ProjectS.Data;
using ProjectS.Events;
using ProjectS.Debugging;
using System;

namespace ProjectS.Tutorials
{
    /// <summary>
    /// 튜토리얼 종료 처리. 지정한 '마지막 튜토리얼 퀘스트'(finalQuestId)가 반납 완료되면
    /// QuestEvents.OnQuestCompleted를 받아 캐릭터 상태를 확정 저장하고 마을 씬으로 돌려보낸다.
    /// QuestRewardGranter와 같은 완료 구독 패턴이며, 씬(튜토리얼)의 관리자 오브젝트에 붙인다.
    /// 건너뛰기 버튼은 SkipAndReturn을 onClick에 연결한다.
    /// </summary>
    public class TutorialCompleter : MonoBehaviour
    {
        [Tooltip("이 퀘스트가 완료되면 튜토리얼이 끝난 것으로 본다(마지막 튜토리얼 퀘스트 ID).")]
        [SerializeField] private int finalQuestId;

        [Tooltip("건너뛰기 버튼")]
        [SerializeField] Button skipButton;

        private void Awake()
        {
            // 설정 실수 방어: ID가 비어 있으면(0) 어떤 퀘스트도 매칭되지 않아 튜토리얼이 영영 끝나지 않는다.
            if (finalQuestId <= 0)
                DevLog.Warning($"[TutorialCompleter] finalQuestId가 설정되지 않았습니다({name}). 튜토리얼이 완료되지 않습니다.");
        }

        private void Start()
        {
            if(skipButton != null)
            {
                skipButton.gameObject.SetActive(true);  // 건너뛰기 버튼은 튜토리얼 시작 시점에 켠다. 인스펙터에서 꺼두면 안 됨.
                skipButton.onClick.AddListener(SkipAndReturn);  // 버튼 클릭 시 튜토리얼 건너뛰기
            }
        }

        private void OnEnable()
        {
            QuestEvents.OnQuestCompleted += OnQuestCompleted;
        }

        private void OnDisable()
        {
            QuestEvents.OnQuestCompleted -= OnQuestCompleted;
        }

        // 완료·건너뛰기·완료이벤트가 겹쳐 종료 처리가 두 번 도는 것을 막는다.
        // 가드가 없으면 마을로의 씬 전환(RequestSceneChange)이 중복 요청될 수 있다.
        private bool finished;

        private void OnQuestCompleted(QuestData quest)
        {
            if (quest == null || quest.QuestId != finalQuestId) return;

            Finish(TutorialState.Completed);
        }

        // 튜토리얼을 건너뜀으로 확정하고 마을로 보낸다.
        private void SkipAndReturn() => Finish(TutorialState.Skipped);

        private void Finish(TutorialState result)
        {
            if (finished) return;   // 재진입 차단(버튼 연타·완료+건너뛰기 동시 등)
            finished = true;

            var ch = GameSession.SelectedCharacter;
            if (ch != null && ch.tutorialState != result)
            {
                ch.tutorialState = result;                        // ① 메모리 상태 확정
                if (FirebaseManager.Instance != null)
                    _ = FirebaseManager.Instance.SaveCharacter(ch); // ② 저장(매니저 유지되어 씬 넘어도 완료)
            }

            GameSceneManager.Instance.RequestSceneChange<VillageGather>();  // ③ 마을로
        }
    }
}
