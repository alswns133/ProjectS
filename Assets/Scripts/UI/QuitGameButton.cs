using UnityEngine;
using UnityEngine.UI;
using ProjectS.Managers;

namespace ProjectS.UI
{
    /// <summary>
    /// 옵션 창의 "게임 종료" 버튼. 확인을 받고 → 저장 완료를 기다린 뒤 → 앱을 종료한다.
    ///
    /// <see cref="Application.Quit"/>만 부르면 안 되는 이유: 종료 시점의 저장(AutoSaveTicker.OnApplicationQuit)은
    /// best-effort라 업로드가 끝나기 전에 프로세스가 내려갈 수 있다. 사용자가 직접 누른 종료는 기다릴 여유가 있으므로
    /// <see cref="PlayerSaveService.SaveNow"/>를 끝까지 기다린다. 흐름은 <see cref="ReturnToSelectButton"/>과 맞췄다.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class QuitGameButton : MonoBehaviour
    {
        [Tooltip("종료 확인 문구.")]
        [SerializeField, TextArea] private string confirmMessage = "게임을 종료할까요?\n진행 상황은 저장됩니다.";

        [Tooltip("저장에 실패했을 때 묻는 문구. 확인하면 저장 없이 종료한다.")]
        [SerializeField, TextArea] private string saveFailedMessage = "저장에 실패했습니다.\n저장하지 않고 종료할까요?";

        private Button button;
        private bool isQuitting;

        private void Awake() => button = GetComponent<Button>();

        private void OnEnable() => button.onClick.AddListener(OnClick);
        private void OnDisable() => button.onClick.RemoveListener(OnClick);

        private void OnClick()
        {
            // 저장 대기 중 중복 클릭, 캐릭터 선택 복귀와 겹치는 것을 막는다.
            if (isQuitting || SessionReboot.IsRebooting) return;

            // 대화상자가 없는 씬(단독 테스트)에서는 확인 없이 진행한다 — 버튼이 먹통으로 보이는 편이 더 나쁘다.
            if (ConfirmDialog.Instance == null)
            {
                SaveAndQuit();
                return;
            }

            ConfirmDialog.Instance.Show(confirmMessage, SaveAndQuit);
        }

        private async void SaveAndQuit()
        {
            isQuitting = true;
            button.interactable = false;

            try
            {
                bool saved = await PlayerSaveService.SaveNow();
                bool hadCharacter = GameSession.SelectedCharacter != null;

                // 캐릭터가 없으면(캐릭터 선택 화면 등) 저장할 게 없어 SaveNow가 false를 돌려준다 — 실패가 아니다.
                if (!saved && hadCharacter)
                {
                    // 조용히 종료하면 진행이 사라진 걸 사용자가 모른다. 한 번 더 묻는다.
                    if (ConfirmDialog.Instance != null)
                    {
                        ConfirmDialog.Instance.Show(saveFailedMessage, Quit);
                        return;
                    }

                    Debug.LogWarning("[QuitGameButton] 저장 실패 — 확인 대화상자가 없어 저장 없이 종료합니다.");
                }

                Quit();
            }
            finally
            {
                // 저장 실패로 되물은 뒤 취소했을 때 다시 누를 수 있게 풀어 준다(종료됐다면 의미 없음).
                isQuitting = false;
                if (button != null) button.interactable = true;
            }
        }

        // 에디터에서는 Application.Quit이 무시되므로 플레이 모드를 끈다.
        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
