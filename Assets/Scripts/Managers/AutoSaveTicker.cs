using UnityEngine;

namespace ProjectS.Managers
{
    /// <summary>
    /// <see cref="PlayerSaveService"/>의 ②오토세이브·③앱경계 flush를 실제로 굴리는 러너.
    /// 세이브는 놓치면 안 되는 시스템이라, 씬 배치를 잊어도 항상 돌도록 첫 씬 로드 후 자동 생성(DDOL)한다.
    /// </summary>
    public class AutoSaveTicker : MonoBehaviour
    {
        private float timer;

        // 첫 씬 로드 후 1회 자동 생성. 씬에 수동 배치할 필요가 없다(누락 방지).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            GameObject go = new GameObject("[AutoSaveTicker]");
            go.AddComponent<AutoSaveTicker>();
            DontDestroyOnLoad(go);
        }

        // dirty가 쌓여 있을 때만 잰다. 일시정지(timeScale 0)에도 저장은 흘러야 하므로 unscaled 사용.
        private void Update()
        {
            if (!PlayerSaveService.IsDirty)
            {
                timer = 0f;
                return;
            }

            timer += Time.unscaledDeltaTime;
            if (timer >= PlayerSaveService.AutoSaveInterval)
            {
                timer = 0f;
                PlayerSaveService.SaveNow();
            }
        }

        // 앱 경계(③): 포커스 아웃·백그라운드·종료 시 즉시 flush. best-effort(종료 중엔 업로드가 못 끝날 수 있음).
        private void OnApplicationPause(bool paused) { if (paused) Flush(); }
        private void OnApplicationFocus(bool focused) { if (!focused) Flush(); }
        private void OnApplicationQuit() => Flush();

        private void Flush()
        {
            if (!PlayerSaveService.IsDirty) return;
            timer = 0f;
            PlayerSaveService.SaveNow();
        }
    }
}
