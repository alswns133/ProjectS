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

        // ── 인벤토리 (InventoryManager가 저장 WriteTo·복원 RestoreFrom) ─────────
        /// <summary>보유 장비 목록. 장비는 인스턴스마다 +N이 달라 개별로 저장한다.</summary>
        public List<EquipmentSave> equipment = new();

        /// <summary>보유 스택형 아이템(소비품·재료) 목록. 같은 아이템은 수량으로 묶어 저장한다.</summary>
        public List<ItemStackSave> stackItems = new();

        /// <summary>착용 중인 장비(부위별). 가방(equipment)과 분리 저장한다.</summary>
        public List<EquippedSave> equipped = new();

        /// <summary>HUD 포션 퀵슬롯에 등록된 소비품 itemId(인덱스=슬롯, 0=빈칸). 캐릭터 로드아웃이라 캐릭터별 저장.</summary>
        public int[] potionQuickSlots = new int[2];

        // ── 스킬창(배운 레벨·단축키 등록) — SkillState가 저장 WriteTo·복원 RestoreFrom ──
        /// <summary>
        /// 스킬창에서 올린 배운 레벨. 시작 레벨(액티브 1 / 패시브 0)을 넘긴 스킬만 담는다(기본값은 생략해 용량 절약).
        /// SP 사용량은 이 레벨 합에서 파생되므로 따로 저장하지 않는다.
        /// </summary>
        public List<SkillLevelSave> skillLevels = new();

        /// <summary>
        /// 단축키 로드아웃. 인덱스 i = 슬롯 i+1에 등록된 스킬ID(0=빈칸). 캐릭터별 배치라 캐릭터마다 저장한다
        /// (포션 퀵슬롯 potionQuickSlots와 같은 방식). 비어 있으면 복원 시 액티브 순서로 기본 배치된다.
        /// </summary>
        public int[] skillLoadout = new int[4];

        /// <summary>
        /// 해금한 액티브 스킬 ID 목록(스킬2·3·4 = 102·103·104 등). 스킬1은 생성 시부터 항상 열려 있어 담지 않고,
        /// 메인 퀘스트 보상(SkillUnlock)으로 열린 것만 저장한다. 패시브는 게이트가 없어 담지 않는다.
        /// </summary>
        public List<int> unlockedSkills = new();

        public CharacterSaveData() { }

        public CharacterSaveData(long uniqueId, int characterType, string name)
        {
            this.uniqueId = uniqueId;
            this.characterType = characterType;
            this.name = name;
        }
    }

    /// <summary>배운 스킬 레벨 하나의 세이브. 정의(이름·효과)는 SkillGrowthTable로 복원하므로 ID와 레벨만 담는다.</summary>
    [Serializable]
    public class SkillLevelSave
    {
        /// <summary>스킬 ID(SkillGrowthTable.SkillId).</summary>
        public int skillId;

        /// <summary>배운 레벨.</summary>
        public int level;
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

    /// <summary>
    /// 보유 장비 1개의 세이브. 아이템 정의(이름·주스탯·부위)는 tableId로 ItemData/EquipmentData에서
    /// 복원하므로 저장하지 않고, 인스턴스마다 다른 강화 단계만 담는다.
    /// </summary>
    [Serializable]
    public class EquipmentSave
    {
        /// <summary>아이템 테이블 ID(ItemData/EquipmentData 공통 Index).</summary>
        public int tableId;

        /// <summary>현재 강화 단계(+N).</summary>
        public int enhanceStep;

        /// <summary>인벤토리 격자 슬롯 위치(사용자 배치 유지). 구버전 세이브엔 없어 0 → 복원 시 앞에서부터 폴백.</summary>
        public int slot;

        /// <summary>드랍 시 롤된 +0강 기준 주 스탯값. 구버전 세이브엔 없어 0 → 복원 시 MainStatBase로 폴백.</summary>
        public int mainStat;

        /// <summary>드랍 시 롤된 옵션(인스턴스 고유).</summary>
        public List<ItemOptionSave> options = new();
    }

    /// <summary>착용 중인 장비 1개의 세이브. 가방 장비와 같은 데이터에 부위(equipSlot)만 더한다.</summary>
    [Serializable]
    public class EquippedSave
    {
        /// <summary>착용 부위(EquipSlot enum 값).</summary>
        public int equipSlot;

        public int tableId;
        public int enhanceStep;
        public int mainStat;
        public List<ItemOptionSave> options = new();
    }

    /// <summary>롤된 옵션 하나의 세이브. 표시용(퍼센트·라벨)은 복원 시 ItemOptionData에서 재조립하므로 값만 담는다.</summary>
    [Serializable]
    public class ItemOptionSave
    {
        /// <summary>옵션 종류(ItemOptionType enum 값).</summary>
        public int type;

        /// <summary>롤된 값.</summary>
        public float value;
    }

    /// <summary>
    /// 보유 스택형 아이템(소비품·재료) 1종의 세이브. 정의는 tableId로 복원하고 수량만 담는다.
    /// </summary>
    [Serializable]
    public class ItemStackSave
    {
        /// <summary>아이템 테이블 ID(ItemData.Index).</summary>
        public int tableId;

        /// <summary>보유 수량.</summary>
        public int count;

        /// <summary>인벤토리 격자 슬롯 위치(사용자 배치 유지). 구버전 세이브엔 없어 0 → 복원 시 앞에서부터 폴백.</summary>
        public int slot;
    }
}
