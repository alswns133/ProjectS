using UnityEngine;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.Events;
using ProjectS.Managers;

namespace ProjectS.NPCs
{
    /// <summary>
    /// NPC 머리 위 퀘스트 표시(! / ?). 같은 NPC의 <see cref="NpcInteractionController"/> 상태를 읽어 아이콘을
    /// 정하고, 표시할 게 없으면 숨긴다. 상태는 매 프레임 폴링하지 않고 퀘스트/레벨 이벤트에 반응해 갱신한다
    /// (수락하면 !가 사라지고, 목표를 다 채우면 ?가 뜨는 식). 아이콘은 항상 카메라를 향하도록 빌보드한다.
    ///
    /// 배치: NPC 머리 위에 월드 스페이스 아이콘(Image)을 두고 이 컴포넌트를 붙인다.
    /// controller를 비워두면 부모에서 자동으로 찾는다.
    /// </summary>
    public class QuestMarker : MonoBehaviour
    {
        [Tooltip("이 마커가 반영할 NPC. 비워두면 부모에서 자동으로 찾는다.")]
        [SerializeField] private NpcInteractionController controller;

        [Header("아이콘")]
        [SerializeField] private Image iconImage;
        [SerializeField] private Sprite exclamationIcon;   // ! 받을 퀘스트 있음
        [SerializeField] private Sprite questionIcon;      // ? 반납 가능

        // Camera.main은 매번 조회하면 비싸므로 캐싱한다.
        private Camera mainCamera;

        // 부모의 NpcInteractionController를 확보한다(인스펙터 미지정 시).
        private void Awake()
        {
            if (controller == null)
                controller = GetComponentInParent<NpcInteractionController>();
        }

        // 마커 상태를 바꿀 수 있는 이벤트에 구독한다(폴링 대신 이벤트 구동).
        private void OnEnable()
        {
            QuestEvents.OnQuestAccepted += OnQuestChanged;
            QuestEvents.OnQuestCompleted += OnQuestChanged;
            QuestEvents.OnQuestProgressUpdated += OnQuestProgress;
            PlayerEvents.OnLevelChanged += OnLevelChanged;

            Refresh();
        }

        private void OnDisable()
        {
            QuestEvents.OnQuestAccepted -= OnQuestChanged;
            QuestEvents.OnQuestCompleted -= OnQuestChanged;
            QuestEvents.OnQuestProgressUpdated -= OnQuestProgress;
            PlayerEvents.OnLevelChanged -= OnLevelChanged;
        }

        // 데이터 로딩 전에는 '받을 퀘스트' 조회가 비어 있으므로, 로딩이 끝난 뒤 초기 상태를 한 번 맞춘다.
        // async void는 Start 같은 진입점에서만 예외적으로 허용(JsonManager와 같은 방침).
        private async void Start()
        {
            JsonManager json = JsonManager.Instance;
            if (json != null && !json.IsReady) await json.ReadyTask;
            if (this == null) return;   // 대기 중 파괴됐을 수 있다.

            Refresh();
        }

        // 수락/반납 시 마커가 바뀔 수 있으니 다시 계산한다.
        private void OnQuestChanged(QuestData _) => Refresh();

        // 목표 카운트가 바뀌면 '반납 가능(?)'으로 바뀔 수 있으니 다시 계산한다.
        private void OnQuestProgress(QuestData quest, int cur, int max) => Refresh();

        // 레벨이 오르면 수락 가능 퀘스트가 생길 수 있으니 다시 계산한다.
        private void OnLevelChanged(int level) => Refresh();

        // 빌보드: 아이콘이 항상 카메라를 향하도록 y축만 회전시킨다.
        private void LateUpdate()
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) return;

            Vector3 direction = transform.position - mainCamera.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;

            transform.rotation = Quaternion.LookRotation(direction.normalized);
        }

        // NpcInteractionController 상태에 맞춰 아이콘을 바꾸거나 숨긴다.
        private void Refresh()
        {
            if (controller == null || iconImage == null) return;

            switch (controller.EvaluateMarkerState())
            {
                case QuestMarkerState.Completable:
                    iconImage.sprite = questionIcon;
                    iconImage.gameObject.SetActive(true);
                    break;

                case QuestMarkerState.Available:
                    iconImage.sprite = exclamationIcon;
                    iconImage.gameObject.SetActive(true);
                    break;

                default:
                    iconImage.gameObject.SetActive(false);
                    break;
            }
        }
    }
}
