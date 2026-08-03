using System;
using System.Collections.Generic;

namespace ProjectS.Data
{
    /// <summary>
    /// 생성된 캐릭터 1개(슬롯)의 세이브. 캐릭터 슬롯 모델(생성 → 목록 선택 → 접속)이라
    /// Firebase의 Users/{uid}/Characters/{uniqueId} 노드마다 이 데이터가 하나씩 저장된다.
    /// 캐릭터 선택창은 <see cref="ProjectS.Managers.FirebaseManager.LoadAllCharacters"/>로 이 목록을 받아 나열한다.
    /// HP·스태미나·스킬게이지는 씬 진입 시 풀충전(PlayerStats.RefillOnSceneEnter) 설계라 저장하지 않는다.
    /// </summary>
    [Serializable]
    public class CharacterSaveData
    {
        /// <summary>캐릭터 슬롯 고유 id. 생성 시 발급하며(예: DateTime.UtcNow.Ticks) Characters 노드의 키가 된다.</summary>
        public long uniqueId;

        /// <summary>
        /// 캐릭터 타입(검사/거너 등). PlayerStatTable 행 ID이며 PlayerStats.CharacterId로 들어간다.
        /// 이 값으로 스킬 접두사·치명타 등 캐릭터 고정 스탯을 테이블에서 로딩한다(그 값들은 저장 대상 아님).
        /// </summary>
        public int characterType;

        /// <summary>생성 시 정한 캐릭터 이름(목록·HUD 표시용). 전 서버 유니크(CharacterNames 인덱스로 강제).</summary>
        public string name;

        /// <summary>현재 레벨. HP·AD·방어도는 이 값으로 PlayerLevelTable에서 파생된다.</summary>
        public int level = 1;

        /// <summary>현재 레벨에서 쌓은 경험치(0 ~ RequiredExp).</summary>
        public int currentExp;

        // ── 캐릭터별 인벤토리(재화도 캐릭터마다 따로) ──────────────────────────
        public int gold;
        public int lowMaterial;
        public int highMaterial;

        // ── 퀘스트 진행 (QuestManager가 저장 WriteTo·복원 RestoreFrom) ─────────
        /// <summary>반납 완료한(비반복) 퀘스트 ID. 선행 체인·재수락 차단 판정용.</summary>
        public List<int> completedQuestIds = new();

        /// <summary>진행 중 퀘스트의 목표별 카운트 + 핀 상태.</summary>
        public List<QuestSave> activeQuests = new();

        // TODO: 장비 세이브. InventoryManager의 EquipmentInstance 구조 확정 후
        //       { 아이템 tableId, enhanceLevel, rarity } 목록으로 추가(규칙에도 필드 추가 필요).
        // public List<EquipmentSaveData> equipment = new();

        public CharacterSaveData() { }

        public CharacterSaveData(long uniqueId, int characterType, string name)
        {
            this.uniqueId = uniqueId;
            this.characterType = characterType;
            this.name = name;
        }
    }

    /// <summary>
    /// 진행 중 퀘스트 하나의 세이브. 목표별 진행 카운트와 핀 상태만 담는다
    /// (완료 여부·정의는 QuestManager가 QuestTable로 복원하므로 저장하지 않는다).
    /// </summary>
    [Serializable]
    public class QuestSave
    {
        public int questId;
        public List<int> objectiveCounts = new();
        public bool pinned;
    }
}
