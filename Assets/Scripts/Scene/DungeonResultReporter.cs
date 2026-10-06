using System.Collections.Generic;
using ProjectS.Data;
using ProjectS.Enemies;
using ProjectS.Events;
using ProjectS.Managers;
using ProjectS.Players;
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
    /// 집계 진행: 클리어 시간 · 최대 콤보(표시용) · 콤보 유지 시간 · 피격 횟수 · 사망 횟수를 판 단위로
    /// 직접 모아 <see cref="DungeonRankScorer"/>에 넘긴다. 피격·사망은 static 이벤트가 아니라
    /// 조작 중인 아바타의 <c>PlayerStats</c> 인스턴스를 보고 센다 — 멀티에서 다른 플레이어의 피격까지
    /// 세어 내 랭크가 깎이는 것을 막기 위함이다.
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

        private int maxCombo;   // 이번 판 최대 콤보 (점수에는 안 쓰고 결과창 표시용으로만 남는다)

        // 콤보가 살아 있던 누적 시간. 콤보 점수의 분자다(DungeonRankScorer.ScoreCombo).
        private float comboUptime;

        // 첫 유효타 시각. 콤보 점수의 분모인 '전투 시간'을 여기서부터 잰다 — 판 시작부터 재면
        // 방 사이를 걸어 다닌 시간이 분모에 들어가 넓은 던전이 구조적으로 불리해진다.
        // -1이면 아직 한 번도 때리지 않은 상태(전투 시간 0 → 콤보 0점).
        private float firstHitTime;

        // 실제로 적용된 피격 횟수. 생존 점수의 분자다.
        private int hitsTaken;

        // 이번 판 사망 횟수. ReviveBudget.MaxPerRun이 1이라 결과창까지 오는 판은 0 또는 1이다.
        private int deaths;

        // 피격·사망을 세는 대상(조작 중인 아바타의 스탯). PlayerStats.Damaged가 static이 아니라
        // 인스턴스 이벤트라서 "지금 누구를 보고 있는지"를 들고 있어야 한다.
        private PlayerStats damageSource;

        // 직전 프레임의 사망 여부. IsDead가 false→true로 바뀐 순간만 사망 1회로 센다.
        private bool wasDead;

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

            // 재도전은 씬 리로드로 새 인스턴스가 되지만, 같은 인스턴스가 다시 켜지는 경로가 생겨도
            // 지난 판의 집계가 섞이지 않게 여기서 함께 비운다.
            maxCombo = 0;
            comboUptime = 0f;
            firstHitTime = -1f;
            hitsTaken = 0;
            deaths = 0;
            wasDead = false;

            BossEvents.OnBossDisappeared += OnBossDisappeared;
            PlayerEvents.OnHitComboChanged += OnHitCombo;

        }

        private void OnDisable()
        {
            BossEvents.OnBossDisappeared -= OnBossDisappeared;
            PlayerEvents.OnHitComboChanged -= OnHitCombo;

            // 피격 구독은 플레이어 인스턴스에 직접 걸려 있으므로 여기서 짝을 맞춰 떼어 낸다.
            // 빠지면 씬을 다시 들어왔을 때 지난 판의 Reporter가 계속 피격을 세어 같은 피격이 두 번 집계된다.
            if (damageSource != null) damageSource.Damaged -= OnPlayerDamaged;
            damageSource = null;
        }

        /// <remarks>
        /// 매 프레임 하는 일 두 가지다.
        /// <list type="number">
        /// <item><b>콤보 유지 시간 누적</b> — 히트 카운트가 1 이상인 프레임만 더한다. 끊김 유예
        /// (<c>PlayerHitCombo</c>의 comboResetDelay)는 그쪽이 관리하므로 여기서 그 값을 알 필요가 없다.
        /// 값이 한 곳에만 있어 유예 시간을 조정해도 집계가 따라온다.</item>
        /// <item><b>피격·사망 집계 대상 갱신</b> — <c>PlayerStats.Damaged</c>는 인스턴스 이벤트라
        /// 플레이어가 생긴 뒤에 붙어야 한다. 던전 씬이 켜지는 시점엔 아직 없을 수 있고(부트스트랩 워프 전),
        /// 멀티 레이드에선 네트워크 아바타가 늦게 스폰되거나 교체되므로 매 프레임 대상을 확인한다.</item>
        /// </list>
        /// 결과를 이미 보고했으면(<c>reported</c>) 아무것도 하지 않는다 — 결과창이 열린 뒤의 시간이
        /// 집계에 섞이지 않게 한다.
        /// </remarks>
        private void Update()
        {
            if (reported) return;

            Player player = LocalPlayer.Current;   // 멀티 레이드에선 마을 캐릭터가 아니라 조작 중인 아바타

            BindDamageSource(player);
            CountDeathTransition();

            if (player == null || player.HitCombo == null) return;

            if (player.HitCombo.HitCount > 0) comboUptime += Time.deltaTime;
        }

        // 피격을 세는 대상을 "지금 조작 중인 아바타"로 맞춘다. 대상이 바뀌면 이전 구독을 떼고 새로 붙인다.
        // static 이벤트(PlayerEvents)를 쓰지 않는 이유: 멀티에서 다른 플레이어의 피격·사망까지 받아
        // 내 랭크가 깎인다.
        private void BindDamageSource(Player player)
        {
            PlayerStats stats = player != null ? player.Stats : null;

            if (stats == damageSource) return;   // 둘 다 null인 경우도 여기서 걸러진다

            if (damageSource != null) damageSource.Damaged -= OnPlayerDamaged;

            damageSource = stats;
            if (damageSource != null) damageSource.Damaged += OnPlayerDamaged;

            // 새 아바타로 갈아탔으면 사망 판정 기준도 그 아바타 기준으로 다시 잡는다.
            // 안 비우면 교체 직후 한 프레임이 false→true로 보여 사망이 한 번 더 세어질 수 있다.
            wasDead = damageSource != null && damageSource.IsDead;
        }

        // 사망 횟수는 IsDead의 false→true 전이로 센다. PlayerStats가 사망을 발행하는 경로는
        // static(PlayerEvents.OnPlayerDied)뿐이라, 인스턴스 기준으로 세려면 상태 변화를 직접 봐야 한다.
        private void CountDeathTransition()
        {
            bool isDead = damageSource != null && damageSource.IsDead;

            if (isDead && !wasDead) deaths++;
            wasDead = isDead;
        }

        // 실제로 적용된 피격 1회. PlayerStats.Damaged는 무적으로 씹힌 공격과 사망 타격을 제외하고 발행되므로
        // (사망은 OnPlayerDied가 담당) 이 카운트와 deaths가 겹치지 않는다.
        private void OnPlayerDamaged()
        {
            if (reported) return;

            hitsTaken++;
        }

        private void OnBossDisappeared(Boss boss)
        {
            if (reported) return;

            // 최종 보스를 지정했다면 그 보스일 때만 클리어로 친다(잡몹 웨이브 중 다른 보스 퇴장 무시).
            if (clearBoss != null && boss != clearBoss) return;

            // ★ '죽어서' 사라진 보스만 클리어다. 퇴장 이벤트는 처치 말고도 나간다 — 멀티 클라에서 레이드 실패 후
            //   마을로 돌아가면 인스턴스의 보스가 내 화면에서 제거되며(BossNetSync.OnStopClient) 퇴장이 발행되는데,
            //   이걸 클리어로 받아 실패한 판에 결과창이 뜨고 보상까지 지급됐다(2026-09-28).
            //   클라의 HP는 서버 값으로 동기화되므로(SetNetworkHp) IsDead로 싱글·호스트·클라를 같은 기준으로 가른다.
            if (boss != null && boss.Stats != null && !boss.Stats.IsDead) return;

            reported = true;

            // EndBoss가 죽어 사라지는 이 시점에 히트 콤보를 0으로 비워 HUD 콤보 표시를 끈다.
            // 결과창(Open)이 열리면서 HUD가 비활성화되므로, 그 전에(HUD가 아직 살아 있을 때) 리셋해야
            // SetHitCombo(0)이 반영돼 표시가 꺼진다. 리셋이 쏘는 OnHitComboChanged(0)은 maxCombo에 영향 없다
            // (Max 누적이라 최고값 유지) — 그래서 BuildResult의 콤보 집계도 그대로다.
            Player player = LocalPlayer.Current;   // 멀티 레이드에선 숨겨진 마을 캐릭터가 아니라 조작 중인 아바타
            if (player != null) player.HitCombo.ResetHitCombo();

            // 보상 행을 한 번만 조회해, 랜덤을 뽑고(1회), 실제로 지급한 뒤, 같은 결과로 화면을 그린다.
            DungeonRewardTable reward = ResolveReward();
            bool hasRandom = TryRollRandom(reward, out DungeonRewardDisplayItem rolled);

            GrantRewards(reward, hasRandom, rolled);

            DungeonResultData data = BuildResult(reward, hasRandom, rolled);
            DungeonResultPanel.Open(data);
        }

        // 히트 콤보 이벤트로 두 값을 모은다 — 결과창에 띄울 최대 콤보와, 전투 시간의 시작점(첫 유효타 시각).
        // 콤보 유지 시간 자체는 이벤트가 아니라 Update에서 센다(이벤트는 적중 순간에만 오므로 '유지'를 알 수 없다).
        // 리셋이 쏘는 0은 둘 다에 영향이 없다 — maxCombo는 Max 누적이고, 첫 타 시각은 한 번만 찍는다.
        private void OnHitCombo(int hitCount)
        {
            if (hitCount <= 0) return;

            if (firstHitTime < 0f) firstHitTime = Time.time;

            maxCombo = Mathf.Max(maxCombo, hitCount);
        }

        /// <summary>
        /// 이번 판의 결과 스냅샷을 만든다. 채워진 값과 아직 placeholder인 값이 섞여 있다.
        /// </summary>
        /// <remarks>
        /// 값 소스:
        /// <list type="bullet">
        /// <item>난이도 → <see cref="DungeonContext"/> (완료)</item>
        /// <item>클리어 시간 → 판 시작~클리어 타이머 (완료)</item>
        /// <item>최대 콤보 → OnHitCombo 누적 (완료 — 표시 전용. 점수에는 쓰이지 않는다)</item>
        /// <item>던전 이름 → 입장 시 <see cref="GameSession.SelectedDungeonName"/>에 실림 (완료)</item>
        /// <item>단계(stage) → 난이도와 별개 슬롯, 기획상 의미 미정 (보류)</item>
        /// <item>점수·등급·달성률 → <see cref="DungeonRankScorer"/> (완료)</item>
        /// <item>클리어 점수(clearScore) → 세 축 모델에 해당 항목이 없어 미사용 (아래 주석 참고)</item>
        /// <item>보상 exp·gold·아이템 → DungeonRewardTable에서 조회·지급 (완료)</item>
        /// </list>
        /// </remarks>
        private DungeonResultData BuildResult(DungeonRewardTable reward, bool hasRandom, DungeonRewardDisplayItem rolled)
        {
            float clearTime = Time.time - runStartTime;   // 판 시작(OnEnable)~클리어 경과 시간
            DungeonRankResult rank = EvaluateRank(reward, clearTime);

            return new DungeonResultData
            {
                // ── 채워진 값 ─────────────────────────────
                difficulty = DungeonContext.Difficulty,   // ID 뒷자리라 DungeonContext에서 바로 나온다
                clearTime = clearTime,
                maxCombo = this.maxCombo,                 // OnHitCombo가 누적한 이번 판 최고 콤보
                dungeonName = GameSession.SelectedDungeonName,   // 입장 시 세션에 실린 표시 이름(없으면 패널이 "-")

                // ── 랭크 산정(DungeonRankScorer) ──────────
                playScore = rank.totalScore,
                achieveRatio = rank.ratio,
                grade = rank.grade,

                // TODO(UI): 세 축(시간·콤보·생존) 모델에는 "클리어 점수"에 해당하는 항목이 없다. 패널의 스탯 행 0이
                //   이 값을 "클리어 점수"로 표시하므로 지금은 0이 뜬다. 그 행을 "시간 점수"(rank.timeScore)나
                //   "생존 점수"(rank.survivalScore)로 바꿀지, 행 자체를 다른 항목으로 교체할지 결정이 필요하다
                //   (라벨은 DungeonResultPanel.BindScore). 생존 점수는 등급에는 반영되지만 아직 화면에 안 뜬다.
                clearScore = 0,

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
        /// 이번 판의 집계값과 던전별 기준값을 <see cref="DungeonRankScorer"/>에 넘겨 점수·등급을 받는다.
        /// </summary>
        /// <remarks>
        /// 기준값(기준 시간·피격 허용치)은 보상 행과 같은 테이블·같은 키(던전 ID)에서 온다. 행이 없거나
        /// 기준 시간이 비어 있으면 시간 축이 0점이 되어 랭크가 실제 실력보다 낮게 나오므로, 조용히 넘기지 않고
        /// 경고를 남긴다. 피격 허용치가 비면 산정기가 기본값으로 폴백하므로 점수는 나오지만, 난이도별 튜닝이
        /// 빠진 상태라는 뜻이라 함께 경고한다.
        /// 콤보 축은 던전별 기준값이 필요 없다 — 그 판의 전투 시간이 분모라서 테이블 없이도 정상 동작한다.
        /// </remarks>
        /// <param name="reward">이 던전의 테이블 행(null 허용 — 기준값 없음으로 처리)</param>
        /// <param name="clearTime">클리어까지 걸린 시간(초)</param>
        /// <returns>축별 점수·총점·등급·달성 비율</returns>
        private DungeonRankResult EvaluateRank(DungeonRewardTable reward, float clearTime)
        {
            float targetTime = reward != null ? reward.TargetTime : 0f;
            float limitTime = reward != null ? reward.LimitTime : 0f;
            int hitLimit = reward != null ? reward.HitLimit : 0;

            if (limitTime <= targetTime)
            {
                Debug.LogWarning(
                    $"[DungeonResultReporter] 던전 {DungeonContext.CurrentDungeonId}의 랭크 기준 시간이 유효하지 않아" +
                    $" 시간 점수를 0으로 둔다(TargetTime {targetTime:0}초 · LimitTime {limitTime:0}초)." +
                    " DungeonRewardTable 행에 두 값을 채워야 한다.", this);
            }

            if (hitLimit <= 0)
            {
                Debug.LogWarning(
                    $"[DungeonResultReporter] 던전 {DungeonContext.CurrentDungeonId}에 피격 허용치(HitLimit)가 없어" +
                    $" 기본값 {DungeonRankScorer.DefaultHitLimit}회로 생존 점수를 낸다." +
                    " 난이도별로 다른 값이라 DungeonRewardTable 행에 채워야 한다.", this);
            }

            // 전투 시간은 '첫 유효타 ~ 클리어'다. 한 번도 때리지 않았으면(firstHitTime < 0) 0으로 넘겨
            // 콤보 축을 0점으로 만든다 — 산정기가 0 나눗셈을 막는다.
            float combatTime = firstHitTime >= 0f ? Time.time - firstHitTime : 0f;

            return DungeonRankScorer.Evaluate(new DungeonRankInput
            {
                clearTime = clearTime,
                targetTime = targetTime,
                limitTime = limitTime,
                comboUptime = comboUptime,
                combatTime = combatTime,
                hitsTaken = hitsTaken,
                deaths = deaths,
                hitLimit = hitLimit,
            });
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
