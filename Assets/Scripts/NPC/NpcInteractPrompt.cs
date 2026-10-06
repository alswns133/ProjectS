using UnityEngine;
using TMPro;

namespace ProjectS.NPCs
{
    /// <summary>
    /// NPC 근처에서 "[F] 대화" 같은 상호작용 안내를 머리 위에 띄운다. 항상 카메라를 향한다(빌보드).
    ///
    /// 플레이어가 근접 범위(<see cref="NpcOutlineTrigger"/>)에 들어오면 켜고, 벗어나거나 상호작용(허브·대화)이
    /// 시작되면 끈다. 상호작용 중에도 남아 있으면 허브 UI와 겹치고 이미 누른 키를 또 안내하는 꼴이 된다.
    /// 상호작용이 끝났을 때 아직 범위 안이면 다시 켠다.
    ///
    /// 근접 상태는 <see cref="NpcInteractionController"/>가 아니라 같은 <see cref="NpcOutlineTrigger"/>를 직접 구독한다
    /// (컨트롤러에 안내 UI 용도의 상태 노출을 늘리지 않기 위함). 상호작용 중 여부는 컨트롤러의 static
    /// <see cref="NpcInteractionController.Active"/>로 판단한다.
    ///
    /// 배치: NPC 루트(또는 머리 위 자식)에 붙이고, 월드 스페이스 Canvas/TMP를 담은 자식을 visualRoot에 연결한다.
    /// 이 컴포넌트가 붙은 오브젝트 자체는 꺼지지 않으므로 이벤트 구독이 유지된다(꺼지는 건 visualRoot뿐).
    /// </summary>
    public class NpcInteractPrompt : MonoBehaviour
    {
        [Header("표시 값")]
        [Tooltip("키 이름. 표시는 [ ]로 감싼다.")]
        [SerializeField] private string keyLabel = "F";

        [Tooltip("행동 설명(예: 대화, 상점). 비우면 키만 표시한다.")]
        [SerializeField] private string actionLabel = "대화";

        [Header("연결")]
        [Tooltip("안내를 담은 자식 오브젝트. 이 오브젝트가 켜지고 꺼진다.")]
        [SerializeField] private GameObject visualRoot;

        [SerializeField] private TMP_Text promptText;

        [Tooltip("근접 감지기. 비우면 자식에서 자동 탐색.")]
        [SerializeField] private NpcOutlineTrigger proximity;

        [Tooltip("스스로 카메라를 향해 회전할지. NpcNameplate처럼 이미 빌보드되는 부모 아래에 두면 끈다(회전이 서로 다투지 않게).")]
        [SerializeField] private bool faceCamera = true;

        private Camera mainCamera;
        private bool playerNear;

        private void Awake()
        {
            if (proximity == null)
                proximity = GetComponentInChildren<NpcOutlineTrigger>(true);

            ApplyText();
            Refresh();
        }

        private void OnEnable()
        {
            if (proximity != null)
            {
                proximity.PlayerNearChanged += OnPlayerNearChanged;
                playerNear = proximity.IsPlayerInside;   // 늦게 켜진 경우 현재 상태 동기화
            }

            NpcInteractionController.ActiveChanged += OnActiveChanged;
            Refresh();
        }

        private void OnDisable()
        {
            if (proximity != null)
                proximity.PlayerNearChanged -= OnPlayerNearChanged;

            NpcInteractionController.ActiveChanged -= OnActiveChanged;
        }

        // 빌보드: 안내가 켜져 있을 때만 카메라를 향해 y축 회전한다.
        private void LateUpdate()
        {
            if (!faceCamera || visualRoot == null || !visualRoot.activeSelf) return;

            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) return;

            Vector3 direction = visualRoot.transform.position - mainCamera.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;

            visualRoot.transform.rotation = Quaternion.LookRotation(direction.normalized);
        }

        private void OnPlayerNearChanged(bool near)
        {
            playerNear = near;
            Refresh();
        }

        // 어떤 NPC든 상호작용이 시작/종료되면 갱신한다(동시에 하나만 활성이라 전역 판단으로 충분).
        private void OnActiveChanged(NpcInteractionController controller) => Refresh();

        private void Refresh()
        {
            if (visualRoot == null) return;

            bool show = playerNear && NpcInteractionController.Active == null;
            if (visualRoot.activeSelf != show) visualRoot.SetActive(show);
        }

        // 키/행동 텍스트를 채운다. 행동이 없으면 키만 표시한다.
        private void ApplyText()
        {
            if (promptText == null) return;

            promptText.text = string.IsNullOrEmpty(actionLabel)
                ? $"[{keyLabel}]"
                : $"[{keyLabel}] {actionLabel}";
        }

    #if UNITY_EDITOR
        // 인스펙터에서 값을 바꾸면 바로 반영해 씬 뷰에서 확인할 수 있게 한다.
        private void OnValidate() => ApplyText();
    #endif
    }
}
