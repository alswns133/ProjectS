#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.InputSystem;
using ProjectS.Skills;
using ProjectS.Players;
using ProjectS.Managers;

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
    /// <para>
    /// ★ UI/로그인 흐름이 없는 테스트 씬 대응: 정식 흐름을 안 밟으면 <see cref="PlayerManager"/>의
    /// CurrentCharacterId가 0이라, 스킬 '번호'(2·3·4)를 넘기는 방식은 번호→ID 환산이 헛돌아
    /// (2는 100 미만이라 '항상 해금'으로 보고 조기 반환) 실제 캐릭터 스킬 102·103·104가 잠긴 채 남는다
    /// → 1스킬만 발동. 그래서 여기선 <b>씬에 배치된 플레이어의 CharacterId</b>로 완성 ID(charId*100+n)를
    /// 직접 만들어 해금한다. 완성 ID(>=100)는 <see cref="SkillState.Unlock"/>이 환산 없이 그대로 해금한다.
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

        // 씬에 배치된 플레이어의 CharacterId로 완성 ID(검사 102~104 / 거너 202~204)를 만들어 해금한다.
        // PlayerManager(로그인 흐름)에 기대지 않으므로 UI 없는 테스트 씬에서도 동작한다.
        private void UnlockAll()
        {
            int characterId = ResolveCharacterId();
            if (characterId <= 0)
            {
                DevLog.Log("[Debug] 플레이어/캐릭터를 못 찾아 스킬 해금 생략 (씬에 CharacterId가 있는 플레이어가 없음)");
                return;
            }

            // 로드아웃이 스스로 관리되는지(정상 흐름) 먼저 본다. 테스트 씬은 로그인 흐름이 없어 PlayerManager
            // CharacterId가 0 → EnsureLoadout이 기본 배치를 못 해 IsLoadoutReady=false다.
            bool loadoutManaged = SkillState.IsLoadoutReady;

            // 스킬2·3·4 해금. 완성 ID(>=100)로 넘겨 번호→ID 환산을 우회한다(스킬1은 항상 열려 있음).
            for (int n = 2; n <= 4; n++) SkillState.Unlock(characterId * 100 + n);

            // 테스트 씬 전용: 단축키 1~4를 스킬 1~4로 곧장 정리한다. 이 경우 Unlock의 빈 슬롯 자동 등록이
            // 초기화 안 된 로드아웃을 어긋나게 채워(키1이 스킬2로 나가는 등) 매핑이 밀리므로 1:1로 덮는다.
            // 정상 흐름(loadoutManaged=true)은 EnsureLoadout·자동 등록이 이미 올바르므로 손대지 않아
            // 플레이어가 배치한 로드아웃이 보존된다.
            if (!loadoutManaged)
            {
                for (int n = 1; n <= SkillState.SlotCount; n++)
                    SkillState.SetSlot(n, characterId * 100 + n);
            }

            DevLog.Log($"[Debug] 캐릭터 {characterId} 액티브 스킬 2·3·4 해금(각성기 포함)"
                + (loadoutManaged ? "" : " + 테스트용 단축키 1~4 매핑"));
        }

        // 조작 중인 플레이어 → 없으면 씬에서 직접 탐색(테스트 씬은 로그인 흐름을 안 거쳐 LocalPlayer/PlayerManager가
        // 비어 있을 수 있다). 에디터 전용 디버그 경로라 FindObjectOfType 비용은 무시한다.
        private static int ResolveCharacterId()
        {
            Player player = LocalPlayer.Current;
            if (player == null) player = FindObjectOfType<Player>();

            return player != null && player.Stats != null ? player.Stats.CharacterId : 0;
        }
    }
}
#endif
