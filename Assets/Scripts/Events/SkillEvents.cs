using System;
using UnityEngine;

namespace ProjectS.Events
{
    /// <summary>
    /// 스킬 로드아웃(단축키 1~4 등록) 변경 알림 허브. HUD 스킬 슬롯이 구독해 아이콘을 갱신한다.
    /// InventoryEvents.OnQuickSlotChanged(포션 퀵슬롯)와 같은 결의 이벤트다.
    /// </summary>
    public static class SkillEvents
    {
        /// <summary>단축키 슬롯(1~4)의 등록 스킬이 바뀌었을 때. 인자: (슬롯번호 1~4, 스킬ID / 해제 시 0).</summary>
        public static event Action<int, int> OnLoadoutChanged;

        /// <summary>새 스킬이 해금됐을 때(메인 퀘스트 보상 등). 인자: 해금된 스킬ID. 해금 배너·스킬창이 구독한다.</summary>
        public static event Action<int> OnSkillUnlocked;

        /// <summary>로드아웃 변경을 발행한다(외부 직접 Invoke 금지 — 이 메서드로만).</summary>
        /// <param name="slotNumber">단축키 슬롯 번호(1~4)</param>
        /// <param name="skillId">등록된 스킬 ID(해제면 0)</param>
        public static void FireLoadoutChanged(int slotNumber, int skillId) => OnLoadoutChanged?.Invoke(slotNumber, skillId);

        /// <summary>스킬 해금을 발행한다. <see cref="ProjectS.Skills.SkillState.Unlock"/>만 부른다.</summary>
        /// <param name="skillId">해금된 스킬 ID</param>
        public static void FireSkillUnlocked(int skillId) => OnSkillUnlocked?.Invoke(skillId);

        // 플레이 모드 리로드 후에도 남을 수 있는 static 구독을 초기화한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnLoadoutChanged = null;
            OnSkillUnlocked = null;
        }
    }
}
