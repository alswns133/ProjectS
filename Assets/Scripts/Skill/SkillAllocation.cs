using System.Collections.Generic;

namespace ProjectS.Skills
{
    /// <summary>
    /// 스킬창의 "일괄 커밋(미리보기 후 확인)" 편집 상태. ▲/▼로 미리 레벨을 배치하고,
    /// [확인]에서 한 번에 소스로 커밋한다. [취소]/[RESET]은 배치를 되돌린다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 두 벌의 레벨을 들고 있다: <c>committed</c>(현재 배워 둔 값)와 <c>pending</c>(편집 중인 값).
    /// 화면에는 pending을 보여주고, 확인 전까지 소스·세이브는 건드리지 않는다 —
    /// 연출/조작 도중 창이 닫혀도 데이터가 어긋나지 않게 하기 위함이다(강화창 설계와 같은 원칙).
    /// </para>
    /// <para>
    /// ▼(내리기)는 이번 세션에 올린 만큼만 되돌린다 = <c>committed</c>가 바닥이다.
    /// 이미 확정한 레벨을 SP 환불받는 "리스펙"은 별도 결정 사항이라 여기서 다루지 않는다.
    /// </para>
    /// </remarks>
    public class SkillAllocation
    {
        private readonly ISkillWindowSource source;

        // 슬롯 정적 정보(레벨 상·하한). 표시·검증 양쪽에서 조회한다.
        private readonly Dictionary<int, SkillSlotInfo> infos = new();

        // 배운 값(바닥)과 편집 중 값. 확인 시 committed ← pending.
        private readonly Dictionary<int, int> committed = new();
        private readonly Dictionary<int, int> pending = new();

        /// <summary>슬롯 표시 순서를 보존한다(딕셔너리 순회 순서에 UI가 흔들리지 않게).</summary>
        private readonly List<int> order = new();

        /// <summary>
        /// 소스의 현재 상태에서 편집 세션을 시작한다. committed = pending = 소스의 현재 레벨.
        /// </summary>
        /// <param name="source">데이터 공급자</param>
        public SkillAllocation(ISkillWindowSource source)
        {
            this.source = source;

            AddGroup(source.GetActiveSlots());
            AddGroup(source.GetPassiveSlots());
        }

        private void AddGroup(IReadOnlyList<SkillSlotInfo> slots)
        {
            if (slots == null) return;

            foreach (SkillSlotInfo info in slots)
            {
                infos[info.SkillId] = info;
                committed[info.SkillId] = info.CurrentLevel;
                pending[info.SkillId] = info.CurrentLevel;
                order.Add(info.SkillId);
            }
        }

        /// <summary>이 캐릭터의 총 SP 예산.</summary>
        public int TotalSp => source.TotalSp;

        /// <summary>
        /// 지금까지(배운 것 + 이번에 배치한 것) 투자된 SP 합. 표시는 "UsedSp / TotalSp".
        /// 각 슬롯이 하한(MinLevel)을 넘은 레벨만큼 비용을 누적한다.
        /// </summary>
        public int UsedSp
        {
            get
            {
                int used = 0;
                foreach (int id in order)
                {
                    SkillSlotInfo info = infos[id];
                    for (int lv = info.MinLevel; lv < pending[id]; lv++)
                        used += source.SpCost(id, lv);
                }
                return used;
            }
        }

        /// <summary>남은 SP(= TotalSp - UsedSp). 0 미만으로 내려가지 않는다.</summary>
        public int RemainingSp => TotalSp - UsedSp;

        /// <summary>현재(편집 중) 레벨.</summary>
        public int PendingLevel(int skillId) => pending.TryGetValue(skillId, out int lv) ? lv : 0;

        /// <summary>슬롯 정적 정보를 돌려준다(프리뷰 표시용).</summary>
        public bool TryGetInfo(int skillId, out SkillSlotInfo info) => infos.TryGetValue(skillId, out info);

        /// <summary>이 슬롯을 ▲로 더 올릴 수 있는가(상한 미만 + 남은 SP 충분).</summary>
        public bool CanIncrease(int skillId)
        {
            if (!infos.TryGetValue(skillId, out SkillSlotInfo info)) return false;
            if (pending[skillId] >= info.MaxLevel) return false;
            return RemainingSp >= source.SpCost(skillId, pending[skillId]);
        }

        /// <summary>이 슬롯을 ▼로 내릴 수 있는가(이번 세션에 올린 만큼만 = committed 초과분).</summary>
        public bool CanDecrease(int skillId)
            => committed.TryGetValue(skillId, out int baseLv) && pending[skillId] > baseLv;

        /// <summary>레벨을 1 올린다. 올릴 수 없으면(상한/SP부족) false.</summary>
        public bool Increase(int skillId)
        {
            if (!CanIncrease(skillId)) return false;
            pending[skillId]++;
            return true;
        }

        /// <summary>레벨을 1 내린다(committed까지). 내릴 수 없으면 false.</summary>
        public bool Decrease(int skillId)
        {
            if (!CanDecrease(skillId)) return false;
            pending[skillId]--;
            return true;
        }

        /// <summary>이번 세션 배치를 전부 되돌린다(pending ← committed). RESET·취소가 쓴다.</summary>
        public void Reset()
        {
            foreach (int id in order)
                pending[id] = committed[id];
        }

        /// <summary>확인 전 편집 내용이 남아 있는가(pending ≠ committed인 슬롯이 하나라도 있는가).</summary>
        public bool HasChanges()
        {
            foreach (int id in order)
                if (pending[id] != committed[id]) return true;
            return false;
        }

        /// <summary>바뀐 슬롯만 커밋 변경 목록으로 만든다(소스 <c>Apply</c>에 넘길 값).</summary>
        public IReadOnlyList<SkillLevelChange> BuildChanges()
        {
            var changes = new List<SkillLevelChange>();
            foreach (int id in order)
                if (pending[id] != committed[id])
                    changes.Add(new SkillLevelChange(id, pending[id]));
            return changes;
        }

        /// <summary>커밋을 로컬 상태에 반영한다(committed ← pending). 소스 Apply 성공 뒤 호출한다.</summary>
        public void Commit()
        {
            foreach (int id in order)
                committed[id] = pending[id];
        }
    }
}
