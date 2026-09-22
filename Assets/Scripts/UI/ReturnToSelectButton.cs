using UnityEngine;
using UnityEngine.UI;
using ProjectS.Managers;

namespace ProjectS.UI
{
    /// <summary>
    /// 인게임 메뉴의 "캐릭터 선택으로" 버튼. 확인을 한 번 받고 <see cref="SessionReboot"/>에 넘긴다.
    ///
    /// 되돌릴 수 없는(플레이 중이던 세션을 끝내는) 행동이라 <see cref="ConfirmDialog"/>를 반드시 끼운다.
    /// 실제 정리 순서(저장 → 네트워크 → 파괴 → 씬 로드)는 이 컴포넌트가 아니라 SessionReboot이 가진다 —
    /// 호출하는 곳이 늘어도 순서가 갈라지지 않게 하기 위해서다.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class ReturnToSelectButton : MonoBehaviour
    {
        [Tooltip("돌아갈 캐릭터 선택 씬 이름. Build Settings에 등록돼 있어야 한다.")]
        [SerializeField] private string characterSelectScene = "CharacterSelect";

        [Tooltip("확인 대화상자 문구. 저장된다는 사실을 함께 알려 준다.")]
        [SerializeField, TextArea] private string confirmMessage = "캐릭터 선택 화면으로 돌아갈까요?\n진행 상황은 저장됩니다.";

        [Tooltip("복귀 중 화면을 덮을 베일 프리팹(EntryVeil). 비우면 SessionReboot이 단색 베일을 만든다.")]
        [SerializeField] private GameObject veilPrefab;

        private Button button;

        private void Awake()
        {
            button = GetComponent<Button>();

            // 캐릭터 선택 씬의 베일과 같은 프리팹을 쓰면, 인게임 → 선택 전환도 로그인 경로와 똑같이 보인다.
            // SessionReboot은 static이라 인스펙터가 없어, 프리팹을 들고 있는 이쪽에서 넘겨 준다.
            if (veilPrefab != null) SessionReboot.VeilPrefab = veilPrefab;
        }

        private void OnEnable() => button.onClick.AddListener(OnClick);
        private void OnDisable() => button.onClick.RemoveListener(OnClick);

        private void OnClick()
        {
            if (SessionReboot.IsRebooting) return;   // 이미 나가는 중이면 무시(중복 클릭)

            // 대화상자가 없는 씬(단독 테스트)에서는 확인 없이 진행한다 — 버튼이 먹통으로 보이는 편이 더 나쁘다.
            if (ConfirmDialog.Instance == null)
            {
                Leave();
                return;
            }

            ConfirmDialog.Instance.Show(confirmMessage, Leave);
        }

        private void Leave() => SessionReboot.ToCharacterSelect(characterSelectScene, ShowAborted);

        // 저장 실패 등으로 나가지 못했을 때. 아무 일도 일어나지 않은 것처럼 보이면 사용자가 계속 누르므로
        // 반드시 사유를 띄운다(확인만 있는 알림으로 재사용).
        private void ShowAborted(string reason)
        {
            Debug.LogWarning($"[ReturnToSelectButton] 복귀 중단: {reason}");

            if (ConfirmDialog.Instance != null) ConfirmDialog.Instance.Show(reason, null);
        }
    }
}
