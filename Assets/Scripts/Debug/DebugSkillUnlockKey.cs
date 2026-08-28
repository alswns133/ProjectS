#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.InputSystem;
using ProjectS.Skills;

namespace ProjectS.Debugging
{
    /// <summary>
    /// 에디터 전용: U 키(Unlock)로 현재 캐릭터의 액티브 스킬2·3·4(각성기 포함)를 즉시 해금한다.
    /// 정식 흐름(레벨 도달 → 메인 퀘스트 → 보상 해금)을 매번 밟지 않고 각성기 등을 바로 테스트하기 위한 임시 도구다.
    /// <para>
    /// 씬 배치 불필요 — 플레이 시작 시 자기 오브젝트를 만들어 붙는다(AutoCreate). 파일 전체가 #if UNITY_EDITOR라
    /// 빌드에는 포함되지 않는다. <see cref="SkillState.Unlock"/>이 해금·배너·빈 슬롯 자동 등록·저장까지 처리하므로,
    /// 누르면 곧바로 단축키로 발동해 볼 수 있다.
    /// </para>
    /// </summary>
    public class DebugSkillUnlockKey : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            GameObject go = new GameObject("[DebugSkillUnlockKey]");
            go.AddComponent<DebugSkillUnlockKey>();
            DontDestroyOnLoad(go);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.uKey.wasPressedThisFrame) UnlockAll();
        }

        // TargetId 2·3·4는 SkillState.Unlock이 현재 캐릭터 스킬(검사 102~104 / 거너 202~204)로 환산해 해금한다.
        private void UnlockAll()
        {
            for (int n = 2; n <= 4; n++) SkillState.Unlock(n);
            DevLog.Log("[Debug] 액티브 스킬 2·3·4 해금(각성기 포함)");
        }
    }
}
#endif
