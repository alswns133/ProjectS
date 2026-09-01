using System.Collections.Generic;
using ProjectS.Data;
using ProjectS.Enemies;
using ProjectS.Events;
using ProjectS.Managers;
using ProjectS.UI;
using UnityEngine;

namespace ProjectS.Scenes
{
    /// <summary>
    /// 던전/레이드 씬에서 <b>보스 퇴장 → 결과 화면</b>을 잇는 감시자. 씬에 하나 배치한다.
    /// 보스가 사망 연출을 끝내고 소멸하는 순간(<see cref="BossEvents.OnBossDisappeared"/>) 결과를 집계해
    /// <see cref="DungeonResultPanel.Open"/>으로 결과 화면을 연다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 Boss가 직접 Open을 부르지 않는가</b>: 결과 데이터(클리어 시간·점수·콤보·보상)는 판/씬 단위 집계라
    /// 보스가 들고 있지 않다. 그래서 보스는 "사라졌다"는 사실만 발행하고(<see cref="Boss.OnDespawn"/>),
    /// 집계와 화면 전환은 씬 쪽인 이 컴포넌트가 맡는다 — 관심사 분리이자, 결과창을 여는 진입점을 한 곳으로 모은다.
    /// </para>
    /// <para>
    /// B안(사망 연출 종료 후 표시)이라 발행 시점이 곧 소멸 시점이다. HP 바도 같은 이벤트로 내려가므로,
    /// 보스가 사라질 때까지 바가 남았다가 결과창과 함께 사라진다.
    /// </para>
    /// <para>
    /// 집계 진행: 클리어 시간·최대 콤보는 이 컴포넌트가 판 단위로 직접 모은다.
    /// 점수·등급·달성률·보상은 아직 소스(산정 규칙·드랍 테이블)가 없어 placeholder다.
    /// </para>
    /// </remarks>
    public class DungeonResultReporter : MonoBehaviour
    {
        // 레이드 등 보스가 여럿인 씬을 대비해, "이 보스가 죽으면 클리어"인 최종 보스만 반응하도록 거를 수 있다.
        // 비워 두면(스폰 쪽에서 지정하지 않으면) 처음 사라진 보스를 클리어로 본다(단일 보스 던전 기준).
        private Boss clearBoss;

        // 결과창을 두 번 열지 않도록 하는 가드(퇴장 이벤트가 여러 번 오거나 재입장 시 대비).
        // reporter는 씬 오브젝트라 재도전 시 씬 리로드로 새 인스턴스가 되어 자연히 false로 초기화된다.
        private bool reported;

        private float runStartTime; //판 시작 시각

        private int maxCombo;   // 이번 판 최대 콤보

        /// <summary>
        /// 이 판의 "클리어로 칠 최종 보스"를 등록한다. 스폰 권위(<see cref="EnemyRoom.SetEndBoss"/> 경유)가
        /// 최종 보스 인스턴스를 실제로 만든 직후 호출한다 — 인스펙터로 미리 물릴 수 없는 런타임 스폰이라 이 통로로 받는다.
        /// </summary>
        /// <remarks>
        /// 지정하면 그 보스가 사라질 때만 결과창을 연다(다른 보스/웨이브 퇴장은 무시). 지정하지 않으면
        /// 처음 퇴장한 보스를 클리어로 본다. <b>최종 보스 스폰 포인트는 count=1 규약</b>이다 —
        /// 한 포인트에서 보스를 여럿 뽑으면 이 값이 마지막 한 마리로 덮여, 나머지 보스가 죽어도 결과창이 안 뜬다.
        /// 다중 최종 보스가 필요해지면 여기서 "남은 보스 수 카운트다운" 모델로 바꿔야 한다.
        /// </remarks>
        /// <param name="boss">클리어 판정 기준이 될 최종 보스 인스턴스.</param>
        public void SetEndBossSpawn(Boss boss)
        {
            clearBoss = boss;
        }

        private void OnEnable()
        {
            runStartTime = Time.time;
            BossEvents.OnBossDisappeared += OnBossDisappeared;
            PlayerEvents.OnHitComboChanged += OnHitCombo;

        }

        private void OnDisable()
        {
            BossEvents.OnBossDisappeared -= OnBossDisappeared;
            PlayerEvents.OnHitComboChanged -= OnHitCombo;
        }

        private void OnBossDisappeared(Boss boss)
        {
            if (reported) return;

            // 최종 보스를 지정했다면 그 보스일 때만 클리어로 친다(잡몹 웨이브 중 다른 보스 퇴장 무시).
            if (clearBoss != null && boss != clearBoss) return;

            reported = true;

            // EndBoss가 죽어 사라지는 이 시점에 히트 콤보를 0으로 비워 HUD 콤보 표시를 끈다.
            // 결과창(Open)이 열리면서 HUD가 비활성화되므로, 그 전에(HUD가 아직 살아 있을 때) 리셋해야
            // SetHitCombo(0)이 반영돼 표시가 꺼진다. 리셋이 쏘는 OnHitComboChanged(0)은 maxCombo에 영향 없다
            // (Max 누적이라 최고값 유지) — 그래서 BuildResult의 콤보 집계도 그대로다.
            if (PlayerManager.Instance != null && PlayerManager.Instance.Player != null)
                PlayerManager.Instance.Player.HitCombo.ResetHitCombo();

            // 보상 행을 한 번만 조회해, 랜덤을 뽑고(1회), 실제로 지급한 뒤, 같은 결과로 화면을 그린다.
            DungeonRewardTable reward = ResolveReward();
            bool hasRandom = TryRollRandom(reward, out DungeonRewardDisplayItem rolled);

            GrantRewards(reward, hasRandom, rolled);

            DungeonResultData data = BuildResult(reward, hasRandom, rolled);
            DungeonResultPanel.Open(data);
        }

        private void OnHitCombo(int hitCount)
            => maxCombo = Mathf.Max(maxCombo, hitCount);

        /// <summary>
        /// 이번 판의 결과 스냅샷을 만든다. 채워진 값과 아직 placeholder인 값이 섞여 있다.
        /// </summary>
        /// <remarks>
        /// 값 소스:
        /// <list type="bullet">
        /// <item>난이도 → <see cref="DungeonContext"/> (완료)</item>
        /// <item>클리어 시간 → 판 시작~클리어 타이머 (완료)</item>
        /// <item>최대 콤보 → OnHitCombo 누적 (완료)</item>
        /// <item>던전 이름 → 입장 시 <see cref="GameSession.SelectedDungeonName"/>에 실림 (완료)</item>
        /// <item>단계(stage) → 난이도와 별개 슬롯, 기획상 의미 미정 (보류)</item>
        /// <item>점수·등급·달성률·퍼포먼스 비율 → 산정 규칙 확정 후 (기획 미결, 문서 6장)</item>
        /// <item>보상 exp·gold·아이템 → DungeonRewardTable에서 조회·지급 (완료)</item>
        /// </list>
        /// </remarks>
        private DungeonResultData BuildResult(DungeonRewardTable reward, bool hasRandom, DungeonRewardDisplayItem rolled)
        {
            return new DungeonResultData
            {
                // ── 채워진 값 ─────────────────────────────
                difficulty = DungeonContext.Difficulty,   // ID 뒷자리라 DungeonContext에서 바로 나온다
                clearTime = Time.time - runStartTime,     // 판 시작(OnEnable)~클리어 경과 시간
                maxCombo = this.maxCombo,                 // OnHitCombo가 누적한 이번 판 최고 콤보
                dungeonName = GameSession.SelectedDungeonName,   // 입장 시 세션에 실린 표시 이름(없으면 패널이 "-")

                // ── 아직 placeholder(소스 미정/기획 미결) ──
                playScore = 0,                // TODO: 점수 산정(기획 미결)
                clearScore = 0,               // TODO
                achieveRatio = 0f,            // TODO: 달성현황 비율
                grade = string.Empty,         // TODO: 등급 산정(기획 미결)

                // 보상 — 던전 보상 테이블(DungeonRewardTable)에서 이 던전의 경험치·골드·아이템을 읽는다.
                // 행이 없으면(테이블 미등록·미정의 던전) 0·빈 배열로 둔다.
                exp = reward != null ? reward.Exp : 0,
                gold = reward != null ? reward.Gold : 0,
                rewards = BuildRewardItems(reward),   // 기본+확정 보상(확정 슬롯)
                hasRandomReward = hasRandom,          // 랜덤 슬롯: 뽑혔으면 공개, 아니면 패널이 '?'
                randomReward = rolled,
            };
        }

        /// <summary>
        /// 이 던전의 보상 행을 안전하게 찾는다. 없거나(테이블 미등록·미정의 던전) 로딩 전이면 null을 돌려준다.
        /// </summary>
        /// <remarks>
        /// Dictionary 인덱서 <c>[key]</c>는 키가 없으면 예외를 던지므로 <c>TryGetValue</c>로 조회한다.
        /// 키는 <see cref="DungeonContext.CurrentDungeonId"/>를 쓴다 — 세션이 없는 직접 씬 테스트에서도
        /// 던전 진입 시 세팅되며, 난이도(difficulty) 소스와 같은 값이라 일관된다.
        /// </remarks>
        private static DungeonRewardTable ResolveReward()
        {
            if (JsonManager.Instance == null || !JsonManager.Instance.IsReady) return null;

            JsonManager.Instance.DungeonRewardDict.TryGetValue(DungeonContext.CurrentDungeonId, out DungeonRewardTable row);
            return row;
        }

        /// <summary>
        /// 보상 테이블의 기본 보상 + 확정 보상을 결과 화면 슬롯용 배열로 펼친다(랜덤 보상은 포함하지 않는다 —
        /// 패널이 '?'로 따로 그린다). 행이 없으면 빈 배열.
        /// </summary>
        private static DungeonRewardDisplayItem[] BuildRewardItems(DungeonRewardTable reward)
        {
            if (reward == null) return System.Array.Empty<DungeonRewardDisplayItem>();

            int baseCount = reward.BaseRewards?.Count ?? 0;
            int fixedCount = reward.FixedRewards?.Count ?? 0;
            var items = new DungeonRewardDisplayItem[baseCount + fixedCount];

            int i = 0;
            if (reward.BaseRewards != null)
                foreach (RewardItemEntry e in reward.BaseRewards)
                    if (e != null) items[i++] = new DungeonRewardDisplayItem { itemId = e.ItemId, count = e.Count };

            if (reward.FixedRewards != null)
                foreach (RewardItemEntry e in reward.FixedRewards)
                    if (e != null) items[i++] = new DungeonRewardDisplayItem { itemId = e.ItemId, count = e.Count };

            // null 엔트리를 건너뛰어 길이가 남으면 잘라낸다.
            if (i != items.Length) System.Array.Resize(ref items, i);
            return items;
        }

        /// <summary>
        /// 랜덤 보상 풀에서 Weight 비율로 하나를 뽑는다. 풀이 비었거나 모든 Weight가 0이면 뽑지 않는다.
        /// </summary>
        /// <param name="reward">이 던전의 보상 행(null 허용).</param>
        /// <param name="rolled">뽑힌 아이템(성공 시). 실패 시 기본값.</param>
        /// <returns>하나 뽑았으면 true.</returns>
        private static bool TryRollRandom(DungeonRewardTable reward, out DungeonRewardDisplayItem rolled)
        {
            rolled = default;
            if (reward?.RandomRewards == null || reward.RandomRewards.Count == 0) return false;

            int totalWeight = 0;
            foreach (RandomRewardEntry e in reward.RandomRewards)
                if (e != null && e.Weight > 0) totalWeight += e.Weight;

            if (totalWeight <= 0) return false;   // 뽑을 항목이 없음(전부 Weight 0)

            // [0, totalWeight) 구간에서 하나를 골라, 누적 Weight로 해당 항목을 찾는다.
            int pick = Random.Range(0, totalWeight);
            foreach (RandomRewardEntry e in reward.RandomRewards)
            {
                if (e == null || e.Weight <= 0) continue;

                pick -= e.Weight;
                if (pick < 0)
                {
                    rolled = new DungeonRewardDisplayItem { itemId = e.ItemId, count = e.Count };
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 클리어 보상을 <b>실제로 지급</b>한다 — 경험치·골드·확정 아이템(기본+확정)·뽑힌 랜덤 아이템.
        /// 지급 API는 퀘스트 보상(<see cref="QuestRewardGranter"/>)과 같은 것을 재사용한다.
        /// </summary>
        /// <remarks>
        /// OnBossDisappeared는 <c>reported</c> 가드로 판당 한 번만 실행되므로 보상도 한 번만 지급된다.
        /// 매니저가 없으면(직접 씬 테스트) 조용히 건너뛴다.
        /// </remarks>
        private static void GrantRewards(DungeonRewardTable reward, bool hasRandom, DungeonRewardDisplayItem rolled)
        {
            if (reward == null) return;

            // 경험치(임계치 넘으면 자동 레벨업 + HUD 갱신)
            if (reward.Exp > 0)
                PlayerManager.Instance?.Player?.Stats?.AddExp(reward.Exp);

            // 골드
            if (reward.Gold > 0 && InventoryManager.Instance != null)
                InventoryManager.Instance.AddGold(reward.Gold);

            // 확정 아이템(기본 + 확정)
            GrantItems(reward.BaseRewards);
            GrantItems(reward.FixedRewards);

            // 뽑힌 랜덤 아이템
            if (hasRandom && InventoryManager.Instance != null)
                InventoryManager.Instance.AddItem(rolled.itemId, rolled.count);
        }

        // 확정 아이템 목록을 인벤토리에 넣는다. ItemId 0(미지정)은 건너뛴다.
        private static void GrantItems(List<RewardItemEntry> list)
        {
            if (list == null || InventoryManager.Instance == null) return;

            foreach (RewardItemEntry e in list)
                if (e != null && e.ItemId != 0) InventoryManager.Instance.AddItem(e.ItemId, e.Count);
        }
    }
}
