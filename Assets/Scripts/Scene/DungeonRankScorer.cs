using UnityEngine;

namespace ProjectS.Scenes
{
    /// <summary>
    /// 던전 한 판의 집계값을 받아 점수와 랭크(C/B/A/S)를 내는 산정기. 상태를 갖지 않는 순수 계산이다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 Reporter 안에 두지 않는가</b>: 밸런싱할 때 던전을 실제로 깨지 않고 값만 넣어 결과를 봐야 한다.
    /// 집계(<see cref="DungeonResultReporter"/>)와 산정을 갈라 두면 산정식만 단독으로 돌릴 수 있다.
    /// Reporter는 "무엇을 모았는가"만 알고, "어떻게 점수가 되는가"는 전부 이 클래스가 안다.
    /// </para>
    /// <para>
    /// <b>세 축을 8:1:1로 합산한다</b>: 클리어 시간(<see cref="TimeScoreMax"/>점) · 콤보 유지율
    /// (<see cref="ComboScoreMax"/>점) · 생존(<see cref="SurvivalScoreMax"/>점). 조건문으로 등급을 직접
    /// 판정하지 않고 점수를 합산해 구간으로 가르는 이유는, 평가 축이 하나 늘 때마다 조건 분기가 곱으로
    /// 늘어나기 때문이다. 합산이면 축을 더하거나 배점을 바꿔도 구간 판정 코드는 그대로다.
    /// </para>
    /// <para>
    /// <b>콤보를 "최대 연쇄 길이"가 아니라 "유지 시간 비율"로 보는 이유 (2026-10-06 변경)</b>:
    /// 이전 식은 <c>maxCombo ÷ (총 유효타 × 비율)</c>이었다. 분모를 그 판의 총 유효타로 두어 공격력
    /// 의존은 지웠지만(적 N마리·마리당 h타면 분자 분모에서 h가 약분된다), <b>분자가 중립이 아니었다</b>.
    /// 한 연쇄의 길이는 적 배치·이동 거리·사거리에 좌우된다:
    /// <list type="bullet">
    /// <item>거너(Erwin)는 사거리 안이면 적중이 계속 이어져 유지 창이 끊기지 않아 <b>사실상 상시 만점</b>이었고,
    /// 검사(Haru)는 적에서 적으로 <b>접근하는 이동</b>이 유지 창을 넘겨 분자가 판 전체의 일부만 됐다.</item>
    /// <item>보스는 HP가 커서 저렙에선 보스전 한 구간이 그대로 최대 연쇄가 되는데, 고렙은 보스가 금방 죽어
    /// <b>분자만 급락하고 분모(여러 방에 흩어진 잡몹 타수)는 그대로</b> 남아 랭크가 역전됐다.</item>
    /// </list>
    /// 유지 시간 비율은 분자·분모가 모두 <b>시간</b>이고 둘 다 공격력·타수·사거리와 무관해서 이 두 기울기가
    /// 동시에 사라진다. 콤보가 끊기기까지의 유예(<c>PlayerHitCombo</c>의 comboResetDelay)가 그대로 "이동
    /// 허용 시간"으로 쓰여, 근접이 접근하는 동안에도 유지 시간이 쌓인다.
    /// </para>
    /// <para>
    /// 밸런싱 수치(배점·커트라인·<see cref="UptimeRatio"/>)는 전부 여기 상수로 모아 둔다. 여러 곳에 흩어지면
    /// 커트라인을 조정할 때 어디를 고쳐야 하는지 추적이 안 된다. 던전마다 달라지는 값(기준 시간·피격 허용치)만
    /// 테이블(<c>DungeonRewardTable</c>)이 갖는다.
    /// </para>
    /// <para>
    /// <b>※ 커트라인을 건드릴 때 알아야 할 것</b>: 시간 축이 100점 중 80점이라 <b>S는 시간이 거의 결정</b>한다
    /// (콤보·생존을 만점 받아도 시간에서 65/80 이상이 필요하다). 반대로 콤보·생존이 0점이어도 시간 만점이면
    /// 80점으로 A가 나온다. 이게 과하면 <see cref="RankSCut"/>을 내리거나 테이블의 TargetTime을
    /// "숙련자가 실제로 찍는 값"으로 낮추는 쪽을 먼저 보라 — 배점만 바꾸면 세 축의 8:1:1 의도가 깨진다.
    /// </para>
    /// </remarks>
    public static class DungeonRankScorer
    {
        /// <summary>클리어 시간 축의 만점.</summary>
        public const int TimeScoreMax = 80;

        /// <summary>콤보 유지율 축의 만점.</summary>
        public const int ComboScoreMax = 10;

        /// <summary>생존(무사망 + 피격 횟수) 축의 만점.</summary>
        public const int SurvivalScoreMax = 10;

        /// <summary>총점 만점. 달성현황 비율(0~1)을 낼 때 분모로도 쓴다.</summary>
        public const int ScoreMax = TimeScoreMax + ComboScoreMax + SurvivalScoreMax;

        /// <summary>
        /// 콤보 만점 기준 비율. "전투 시간의 이 비율만큼 콤보를 끊기지 않게 유지하면 콤보 만점"이라는 뜻이다.
        /// 0.6이면 전투 시간 200초 중 120초를 유지했을 때 만점.
        /// </summary>
        public const float UptimeRatio = 0.6f;

        /// <summary>테이블에 피격 허용치가 없을 때 쓰는 기본값(이 횟수를 맞으면 생존 축 0점).</summary>
        /// <remarks>
        /// 시간 축은 기준값이 비면 0점을 주는데(던전 길이마다 달라 기본값을 정할 수 없다) 생존 축은 폴백을
        /// 두는 이유: 피격 허용치는 "대체로 이 정도"가 성립하는 값이라, 행 하나를 빠뜨렸을 때 그 던전을 도는
        /// 모두를 0점으로 만드는 쪽이 더 나쁘다. 폴백이 쓰였다는 사실은 Reporter가 경고로 남긴다.
        /// </remarks>
        public const int DefaultHitLimit = 12;

        /// <summary>S 랭크 커트라인(총점).</summary>
        public const int RankSCut = 85;

        /// <summary>A 랭크 커트라인(총점).</summary>
        public const int RankACut = 70;

        /// <summary>B 랭크 커트라인(총점). 이 아래는 모두 C다(클리어했으면 최소 C).</summary>
        public const int RankBCut = 45;

        /// <summary>
        /// 집계값으로 점수와 랭크를 낸다. 클리어 판정을 내린 쪽이 결과창을 채우기 직전에 호출한다.
        /// </summary>
        /// <param name="input">이 판의 집계값과 던전별 기준값(기준 시간·피격 허용치)</param>
        /// <returns>축별 점수 · 총점 · 등급 문자 · 달성 비율</returns>
        public static DungeonRankResult Evaluate(in DungeonRankInput input)
        {
            int timeScore = ScoreTime(input.clearTime, input.targetTime, input.limitTime);
            int comboScore = ScoreCombo(input.comboUptime, input.combatTime);
            int survivalScore = ScoreSurvival(input.hitsTaken, input.deaths, input.hitLimit);

            // 축별로 먼저 반올림한 뒤 더한다. 합계를 반올림하면 화면에 나란히 띄운 축별 점수의 합이
            // 총점과 1점 어긋나 보이는 일이 생긴다.
            int total = timeScore + comboScore + survivalScore;

            return new DungeonRankResult
            {
                timeScore = timeScore,
                comboScore = comboScore,
                survivalScore = survivalScore,
                totalScore = total,
                grade = ToGrade(total),
                ratio = ScoreMax > 0 ? (float)total / ScoreMax : 0f,
            };
        }

        /// <summary>
        /// 클리어 시간 점수. <paramref name="targetTime"/> 이내면 만점, <paramref name="limitTime"/>을
        /// 넘기면 0점이고 그 사이는 선형이다.
        /// </summary>
        /// <remarks>
        /// 구간식("3분 이내 60점 / 5분 이내 35점")이 아니라 선형인 이유는 두 가지다. 1초 차이로 등급이
        /// 점프하는 느낌을 줄이고, 밸런싱할 때 기준값 두 개만 만지면 된다.
        /// 기준값이 없거나(둘 다 0) 역전된 행이면 0점을 돌려준다 — 호출 쪽이 경고를 남긴다.
        /// 배점이 80점이라 구간 폭이 좁으면 1초당 점수가 커진다(120초 구간이면 1초 ≈ 0.67점).
        /// </remarks>
        /// <param name="clearTime">클리어까지 걸린 시간(초)</param>
        /// <param name="targetTime">만점 기준 시간(초)</param>
        /// <param name="limitTime">0점 기준 시간(초)</param>
        /// <returns>0 ~ <see cref="TimeScoreMax"/></returns>
        public static int ScoreTime(float clearTime, float targetTime, float limitTime)
        {
            if (limitTime <= targetTime) return 0;   // 미설정·역전 행: 시간 축을 포기한다(점수 0)

            float ratio = (limitTime - clearTime) / (limitTime - targetTime);
            return Mathf.RoundToInt(TimeScoreMax * Mathf.Clamp01(ratio));
        }

        /// <summary>
        /// 콤보 유지율 점수. 전투 시간 중 콤보가 끊기지 않고 살아 있던 시간의 비율을 본다.
        /// </summary>
        /// <remarks>
        /// 분자·분모가 모두 시간이라 캐릭터·공격력·사거리와 무관하다(클래스 주석 참고).
        /// <paramref name="combatTime"/>은 <b>첫 유효타부터</b> 재야 한다 — 판 시작부터 재면 방 사이를
        /// 걸어 다닌 시간이 분모에 들어가 넓은 던전이 구조적으로 불리해진다.
        /// 한 번도 때리지 않고 클리어한 판(전투 시간 0)은 0점이다 — 0으로 나누는 것도 함께 막는다.
        /// </remarks>
        /// <param name="comboUptime">콤보가 살아 있던(히트 카운트 1 이상) 누적 시간(초)</param>
        /// <param name="combatTime">첫 유효타부터 클리어까지의 시간(초)</param>
        /// <returns>0 ~ <see cref="ComboScoreMax"/></returns>
        public static int ScoreCombo(float comboUptime, float combatTime)
        {
            float denominator = combatTime * UptimeRatio;
            if (denominator <= 0f) return 0;

            return Mathf.RoundToInt(ComboScoreMax * Mathf.Clamp01(comboUptime / denominator));
        }

        /// <summary>
        /// 생존 점수. 사망하면 0점이고, 무사망이면 피격 횟수가 적을수록 높다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>캐릭터·레벨에 완전히 중립인 축</b>이라 콤보 축에 남은 편차를 눌러 주는 역할을 한다.
        /// 피격은 받은 피해량이 아니라 <b>횟수</b>로 센다 — 피해량은 레벨·장비에 따라 달라지지만
        /// "몇 번 맞았나"는 그렇지 않다.
        /// </para>
        /// <para>
        /// <b>사망 1회에 축 전체를 0으로 두는 이유</b>: <c>ReviveBudget.MaxPerRun</c>이 1이라 결과창까지
        /// 오는 판의 사망은 0회 아니면 1회뿐이다(두 번 죽으면 마을로 쫓겨나 결과창이 열리지 않는다).
        /// 그래서 이 축은 사실상 "무사망인가"의 이진 판정이고, 부분 점수를 주려고 상수를 더 두는 대신
        /// 축 전체를 0으로 두는 읽기 쉬운 규칙으로 뒀다.
        /// </para>
        /// <para>
        /// <b>※ 사망해도 S는 나올 수 있다</b>: 이 축을 0점 맞아도 총점 상한이 90점이고
        /// <see cref="RankSCut"/>이 85라서, 시간이 거의 만점이면 사망한 판도 S가 된다.
        /// "사망하면 S 불가"로 만들려면 <see cref="RankSCut"/>을 91로 올려야 하는데, 그러면 무사망 판도
        /// 세 축이 전부 만점에 가까워야 S가 나와 사실상 S가 사라진다. 사망을 더 무겁게 벌하려면
        /// 커트라인이 아니라 "사망 시 총점에서 정액 감점"을 따로 두는 쪽이 맞다 — 기획 결정이 필요해
        /// 지금은 손대지 않았다.
        /// </para>
        /// </remarks>
        /// <param name="hitsTaken">실제로 적용된 피격 횟수(무적으로 씹은 공격·사망 타격 제외)</param>
        /// <param name="deaths">이 판의 사망 횟수. 1 이상이면 0점</param>
        /// <param name="hitLimit">0점이 되는 피격 횟수. 0 이하면 <see cref="DefaultHitLimit"/>을 쓴다</param>
        /// <returns>0 ~ <see cref="SurvivalScoreMax"/></returns>
        public static int ScoreSurvival(int hitsTaken, int deaths, int hitLimit)
        {
            if (deaths > 0) return 0;

            int limit = hitLimit > 0 ? hitLimit : DefaultHitLimit;
            float remain = 1f - (float)hitsTaken / limit;

            return Mathf.RoundToInt(SurvivalScoreMax * Mathf.Clamp01(remain));
        }

        /// <summary>총점을 등급 문자로 바꾼다. 결과창(<c>PerformanceGaugeView.SetRank</c>)이 이 문자를 그대로 표시한다.</summary>
        /// <param name="totalScore">축별 점수의 합</param>
        /// <returns>"S" · "A" · "B" · "C"</returns>
        public static string ToGrade(int totalScore)
        {
            if (totalScore >= RankSCut) return "S";
            if (totalScore >= RankACut) return "A";
            if (totalScore >= RankBCut) return "B";
            return "C";
        }
    }

    /// <summary>랭크 산정에 넣는 한 판의 집계값. 모으는 쪽(<see cref="DungeonResultReporter"/>)이 채운다.</summary>
    public struct DungeonRankInput
    {
        /// <summary>클리어까지 걸린 시간(초).</summary>
        public float clearTime;

        /// <summary>시간 만점 기준(초). 던전별 테이블 값.</summary>
        public float targetTime;

        /// <summary>시간 0점 기준(초). 던전별 테이블 값.</summary>
        public float limitTime;

        /// <summary>콤보가 살아 있던 누적 시간(초). 콤보 점수의 분자.</summary>
        public float comboUptime;

        /// <summary>첫 유효타부터 클리어까지의 시간(초). 콤보 점수의 분모라서 빠지면 콤보가 항상 0점이 된다.</summary>
        public float combatTime;

        /// <summary>실제로 적용된 피격 횟수.</summary>
        public int hitsTaken;

        /// <summary>이 판의 사망 횟수(부활 포함). 1 이상이면 생존 축이 0점이 된다.</summary>
        public int deaths;

        /// <summary>생존 축이 0점이 되는 피격 횟수. 던전별 테이블 값.</summary>
        public int hitLimit;
    }

    /// <summary>랭크 산정 결과. 결과창의 점수·등급·달성현황 바가 전부 여기서 나온다.</summary>
    public struct DungeonRankResult
    {
        /// <summary>클리어 시간 점수.</summary>
        public int timeScore;

        /// <summary>콤보 유지율 점수.</summary>
        public int comboScore;

        /// <summary>생존(무사망 + 피격 횟수) 점수.</summary>
        public int survivalScore;

        /// <summary>총점(축별 점수의 합).</summary>
        public int totalScore;

        /// <summary>등급 문자("S"/"A"/"B"/"C").</summary>
        public string grade;

        /// <summary>달성 비율(총점 ÷ 만점, 0~1). 달성현황 바가 읽는다.</summary>
        public float ratio;
    }
}
