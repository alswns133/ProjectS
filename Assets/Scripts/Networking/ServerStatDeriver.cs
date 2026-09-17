using System.Collections.Generic;
using ProjectS.Data;
using ProjectS.Enhance;
using ProjectS.Items;
using ProjectS.Managers;
using ProjectS.Skills;
using UnityEngine;

namespace ProjectS.Networking
{
    /// <summary>
    /// 서버가 전투에 쓰는 <b>도출된 캐릭터 전투 스탯</b>의 스냅샷. 서버가 접속 커넥션의 세이브로 계산해 보관하고,
    /// (다음 단계에서) 소유 클라에 복제하며, 보스 데미지 계산의 공격자 값으로 쓴다. 전부 값 타입이라 Mirror
    /// SyncVar/메시지로 그대로 실어 보낼 수 있다.
    /// <para>필드 구성·의미는 <see cref="ProjectS.Players.PlayerStats"/>의 최종 프로퍼티(AttackPower 등)와 1:1이다.</para>
    /// </summary>
    public struct CombatStatBlock
    {
        public int MaxHp;
        public float AttackPower;
        public float Defense;
        public float CritChance;
        public float CritDamage;
        public float DefensePenetration;
        public float DamageIncrease;
        public float BossDamage;

        public override string ToString()
            => $"HP={MaxHp}, AD={AttackPower:F1}, DEF={Defense:F1}, CritC={CritChance:P0}, CritD={CritDamage:F2}";
    }

    /// <summary>
    /// 캐릭터 세이브(<see cref="CharacterSaveData"/>) 하나로 <see cref="CombatStatBlock"/>을 도출하는 <b>순수·서버용</b> 계산기.
    /// 클라의 <see cref="ProjectS.Players.PlayerStats"/> getter와 <b>완전히 같은 공식</b>으로 base·장비·패시브를 합성한다
    /// — 그래야 서버가 계산한 데미지가 클라가 예측한 것과 어긋나지 않는다.
    ///
    /// <para><b>재사용(중복 없음).</b> 장비 합산은 <see cref="EquipmentStatCalculator"/>+<see cref="EquipmentSaveLoader"/>,
    /// 패시브 합산은 <see cref="SkillState.ComputePassiveStats"/>로 클라와 같은 코드를 공유한다. 여기서 새로 하는 건
    /// base 테이블 조회와 세 층(base·장비·패시브)의 최종 합성뿐이다.</para>
    ///
    /// <para><b>전제.</b> 서버도 <see cref="JsonManager"/>(스탯·레벨·아이템·스킬 테이블)가 로딩돼 있어야 한다
    /// (전용 서버도 JSON 테이블은 로드한다). 테이블/행이 없으면 그 층은 0 폴백이라 무적/즉사가 되진 않지만 값이 낮아진다.</para>
    /// </summary>
    public static class ServerStatDeriver
    {
        /// <summary>세이브로 전투 스탯을 도출한다. save가 null이면 기본값(0) 블록을 돌려준다(호출측이 폴백 처리).</summary>
        public static CombatStatBlock Derive(CharacterSaveData save)
        {
            CombatStatBlock block = default;
            if (save == null) return block;

            JsonManager json = JsonManager.Instance;
            if (json == null || !json.IsReady)
            {
                Debug.LogWarning("[ServerStatDeriver] JsonManager 미준비 — 스탯 도출 불가(0 반환). 서버 테이블 로딩 확인 필요.");
                return block;
            }

            // ── base(레벨·캐릭터 테이블) ── PlayerStats.ApplyLevelRow/ApplyCharacterRow와 같은 조회 ──
            float baseHp = 0f, baseAd = 0f, baseDef = 0f, baseCritChance = 0f, baseCritDamage = 0f;

            PlayerLevelTable levelRow = json.Get<PlayerLevelTable>(Mathf.Max(1, save.level));
            if (levelRow != null)
            {
                baseHp = levelRow.BaseHP;
                baseAd = levelRow.BaseAD;
                baseDef = levelRow.BaseDefense;
            }
            else
            {
                Debug.LogWarning($"[ServerStatDeriver] PlayerLevelTable에 Level {save.level} 행 없음 — base 스탯 0 폴백.");
            }

            PlayerStatTable charRow = json.Get<PlayerStatTable>(save.characterType);
            if (charRow != null)
            {
                baseCritChance = charRow.CritChance;
                baseCritDamage = charRow.CritDamage;
            }
            else
            {
                Debug.LogWarning($"[ServerStatDeriver] PlayerStatTable에 CharacterId {save.characterType} 행 없음 — 치명타 0 폴백.");
            }

            // ── 장비·패시브(클라와 같은 공유 계산) ──
            List<EquipmentInstance> equipped = EquipmentSaveLoader.BuildEquipped(save);
            EquipmentStats eq = EquipmentStatCalculator.Compute(equipped);
            PassiveStats pas = SkillState.ComputePassiveStats(save.characterType, save.skillLevels);

            // ── 최종 합성(★ PlayerStats getter와 동일 공식 — 바꾸면 양쪽을 함께 바꾼다) ──
            block.AttackPower = (baseAd + eq.FlatAD) * (1f + eq.PercentAD + pas.AttackPercent);
            block.Defense = (baseDef + eq.FlatDef) * (1f + eq.PercentDef + pas.DefensePercent);
            block.MaxHp = Mathf.RoundToInt((baseHp + eq.FlatHp) * (1f + eq.PercentHp + pas.HpPercent));
            block.CritChance = baseCritChance + eq.CritChance + pas.CritChance;
            block.CritDamage = baseCritDamage + eq.CritDamage + pas.CritDamage;
            block.DefensePenetration = eq.DefensePen + pas.Penetration;
            block.DamageIncrease = eq.DamageIncrease;
            block.BossDamage = eq.BossDamage;

            return block;
        }
    }
}
