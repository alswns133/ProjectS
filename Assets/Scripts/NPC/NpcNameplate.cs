using UnityEngine;
using TMPro;

namespace ProjectS.NPCs
{
    /// <summary>
    /// NPC 머리 위 이름표. 고유 이름과 역할(&lt;촌장&gt;, &lt;상점&gt; 등)을 표시하고 항상 카메라를 향한다(빌보드).
    /// 퀘스트 마커(<see cref="QuestMarker"/>)와 별개라, 퀘스트가 없는 기능 NPC(상점/강화/창고)에도 붙일 수 있다.
    ///
    /// 이름·역할 텍스트를 둘로 나눠 두므로 위/아래 배치·크기·색은 프리팹에서 자유롭게 정한다.
    /// 역할의 &lt; &gt;는 코드가 자동으로 감싼다(role에는 "촌장"처럼 알맹이만 넣는다).
    /// 배치: NPC 머리 위 월드 텍스트에 붙이고 nameText/roleText를 연결한다.
    /// </summary>
    public class NpcNameplate : MonoBehaviour
    {
        [Header("표시 값")]
        [Tooltip("고유 이름(예: 칼슨). 비워두면 같은 오브젝트의 QuestGiver.NpcName을 쓴다.")]
        [SerializeField] private string displayName = "";

        [Tooltip("역할/직함(예: 촌장, 상점, 강화). 표시는 <>로 감싼다. 비우면 역할 줄은 숨긴다.")]
        [SerializeField] private string role = "";

        [Header("텍스트")]
        [SerializeField] private TMP_Text nameText;   // 고유 이름
        [SerializeField] private TMP_Text roleText;   // <역할>

        // Camera.main은 매번 조회하면 비싸므로 캐싱한다.
        private Camera mainCamera;

        /// <summary>표시 중인 고유 이름.</summary>
        public string DisplayName => displayName;

        /// <summary>표시 중인 역할/직함.</summary>
        public string Role => role;

        private void Start()
        {
            // 이름을 비워두면 퀘스트 NPC는 QuestGiver 이름을 재사용한다(중복 입력 최소화).
            if (string.IsNullOrEmpty(displayName))
            {
                QuestGiver giver = GetComponent<QuestGiver>();
                if (giver != null) displayName = giver.NpcName;
            }

            Apply();
        }

        // 빌보드: 항상 카메라를 향하도록 y축만 회전시킨다.
        private void LateUpdate()
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) return;

            Vector3 direction = transform.position - mainCamera.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;

            transform.rotation = Quaternion.LookRotation(direction.normalized);
        }

        // 이름·역할 텍스트를 채운다. 역할이 없으면 역할 줄은 숨긴다.
        private void Apply()
        {
            if (nameText != null) nameText.text = displayName;

            if (roleText != null)
            {
                bool hasRole = !string.IsNullOrEmpty(role);
                roleText.text = hasRole ? $"<{role}>" : string.Empty;
                roleText.gameObject.SetActive(hasRole);
            }
        }

    #if UNITY_EDITOR
        // 인스펙터에서 값을 바꾸면 바로 반영해 씬 뷰에서 확인할 수 있게 한다.
        private void OnValidate() => Apply();
    #endif
    }
}
