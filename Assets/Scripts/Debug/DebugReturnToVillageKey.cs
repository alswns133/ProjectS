#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.InputSystem;
using ProjectS.Managers;
using ProjectS.Scenes;
using ProjectS.UI;

namespace ProjectS.Debugging
{
    /// <summary>
    /// 에디터 전용: V 키(Village)로 던전 등 어디서든 마을(<see cref="VillageGather"/>) 씬으로 즉시 복귀한다.
    /// 던전에는 아직 마을로 돌아가는 게이트/UI가 없어서, 퀘스트 흐름(수락→처치→반납)을 반복 테스트할 때
    /// 매번 플레이를 멈췄다 다시 시작하는 불편을 덜기 위한 임시 도구다.
    /// <para>
    /// 씬에 배치할 필요가 없다 — 플레이 시작 시 자기 오브젝트를 만들어 붙는다(AutoCreate).
    /// 파일 전체가 #if UNITY_EDITOR라 빌드에는 클래스도 자동 생성도 포함되지 않는다.
    /// 정식 복귀 동선(마을 게이트 등)이 생기면 이 파일을 지우면 된다.
    /// </para>
    /// </summary>
    public class DebugReturnToVillageKey : MonoBehaviour
    {
        // 플레이 시작 시 자기 오브젝트를 만들어 붙는다. 씬마다 배치할 필요가 없다.
        // AfterSceneLoad: 첫 씬이 올라온 뒤라 DontDestroyOnLoad 이관이 안전하다.
        // 파일이 #if UNITY_EDITOR로 감싸여 있어 이 자동 생성도 에디터 플레이에서만 동작한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            GameObject go = new GameObject("[DebugReturnToVillageKey]");
            go.AddComponent<DebugReturnToVillageKey>();
            DontDestroyOnLoad(go);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            // 채팅 등 텍스트 입력 중에는 무시한다 — "victory" 같이 v를 치면 마을로 튕기기 때문(raw 키 읽기라 입력 억제 무관).
            if (UiTypingGuard.IsTypingInInputField()) return;

            if (keyboard.vKey.wasPressedThisFrame) ReturnToVillage();
        }

        // 실제 워프·활성화·마을 모드 전환은 VillageGather.Enter가 처리하므로 여기는 씬 전환만 요청한다.
        // 이미 마을이거나 로딩 중이면 RequestSceneChange 쪽에서 걸러진다.
        private void ReturnToVillage()
        {
            if (GameSceneManager.Instance == null) return;

            DevLog.Log("[Debug] 마을로 복귀");
            GameSceneManager.Instance.RequestSceneChange<VillageGather>();
        }
    }
}
#endif
