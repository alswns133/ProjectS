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
    /// <b>두 축으로만 가른다</b>: 클리어 시간(<see cref="TimeScoreMax"/>점)과 콤보 유지율
    /// (<see cref="ComboScoreMax"/>점). 조건문으로 등급을 직접 판정하지 않고 점수를 합산해 구간으로 가르는 이유는,
    /// 평가 축이 하나 늘 때마다 조건 분기가 곱으로 늘어나기 때문이다. 합산이면 축을 더하거나 배점을 바꿔도
    /// 구간 판정 코드는 그대로다.
    /// </para>
    /// <para>
    /// <b>콤보를 절대 수가 아니라 비율로 보는 이유</b>: 한 판에서 쌓을 수 있는 콤보의 천장은
    /// "적 총 체력 ÷ 타당 데미지"라서, 공격력이 오르면 천장이 같이 내려간다. 고정 기준값과 비교하면
    /// <b>레벨을 올린 뒤 같은 던전을 돌았을 때 랭크가 오히려 낮게</b> 나온다. 분모를 그 판의 총 유효타 수로
    /// 두면 분자와 분모가 공격력에 같이 반응해 레벨 영향이 사라진다.
    /// </para>
    /// <para>
    /// 밸런싱 수치(배점·커트라인·<see cref="ComboRatio"/>)는 전부 여기 상수로 모아 둔다. 여러 곳에 흩어지면
    /// 커트라인을 조정할 때 어디를 고쳐야 하는지 추적이 안 된다. 던전마다 달라지는 값(기준 시간)만 테이블이 갖는다.
    /// </para>
    /// </remarks>
    public static class DungeonRankScorer
    {
        /// <summary>클리어 시간 축의 만점.</summary>
        public const int TimeScoreMax = 60;

        /// <summary>콤보 유지율 축의 만점.</summary>
        public const int ComboScoreMax = 40;

        /// <summary>총점 만점. 달성현황 비율(0~1)을 낼 때 분모로도 쓴다.</summary>
        public const int ScoreMax = TimeScoreMax + ComboScoreMax;

        /// <summary>
        /// 콤보 만점 기준 비율. "판 전체 유효타의 이 비율을 한 연쇄에 담으면 콤보 만점"이라는 뜻이다.
        /// 0.6이면 총 50타 중 30타가 한 번에 이어지면 만점.
        /// </summary>
        public const float ComboRatio = 0.6f;

        /// <summary>S 랭크 커트라인(총점).</summary>
        public const int RankSCut = 85;

        /// <summary>A 랭크 커트라인(총점).</summary>
        public const int RankACut = 70;

        /// <summary>B 랭크 커트라인(총점). 이 아래는 모두 C다(클리어했으면 최소 C).</summary>
        public const int RankBCut = 45;

        /// <summary>
        /// 집계값으로 점수와 랭크를 낸다. 클리어 판정을 내린 쪽이 결과창을 채우기 직전에 호출한다.
        /// </summary>
        /// <param name="input">이 판의 집계값과 던전별 기준 시간</param>
        /// <returns>축별 점수 · 총점 · 등급 문자 · 달성 비율</returns>
        public static DungeonRankResult Evaluate(in DungeonRankInput input)
        {
            int timeScore = ScoreTime(input.clearTime, input.targetTime, input.limitTime);
            int comboScore = ScoreCombo(input.maxCombo, input.totalHits);

            // 축별로 먼저 반올림한 뒤 더한다. 합계를 반올림하면 화면에 나란히 띄운 축별 점수의 합이
            // 총점과 1점 어긋나 보이는 일이 생긴다.
            int total = timeScore + comboScore;

            return new DungeonRankResult
            {
                timeScore = timeScore,
                comboScore = comboScore,
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
        /// 콤보 유지율 점수. 판 전체 유효타 중 가장 긴 연쇄가 차지한 비율을 본다.
        /// </summary>
        /// <remarks>
        /// 분모가 그 판의 총 유효타 수라서 공격력과 무관하다(클래스 주석 참고).
        /// 한 번도 때리지 않고 클리어한 판(총 유효타 0)은 0점이다 — 0으로 나누는 것도 함께 막는다.
        /// </remarks>
        /// <param name="maxCombo">이 판의 최대 연속 유효타 수</param>
        /// <param name="totalHits">이 판의 총 유효타 수</param>
        /// <returns>0 ~ <see cref="ComboScoreMax"/></returns>
        public static int ScoreCombo(int maxCombo, int totalHits)
        {
            float denominator = totalHits * ComboRatio;
            if (denominator <= 0f) return 0;

            return Mathf.RoundToInt(ComboScoreMax * Mathf.Clamp01(maxCombo / denominator));
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

        /// <summary>이 판의 최대 연속 유효타 수.</summary>
        public int maxCombo;

        /// <summary>이 판의 총 유효타 수. 콤보 점수의 분모라서 빠지면 콤보가 항상 0점이 된다.</summary>
        public int totalHits;
    }

    /// <summary>랭크 산정 결과. 결과창의 점수·등급·달성현황 바가 전부 여기서 나온다.</summary>
    public struct DungeonRankResult
    {
        /// <summary>클리어 시간 점수.</summary>
        public int timeScore;

        /// <summary>콤보 유지율 점수.</summary>
        public int comboScore;

        /// <summary>총점(축별 점수의 합).</summary>
        public int totalScore;

        /// <summary>등급 문자("S"/"A"/"B"/"C").</summary>
        public string grade;

        /// <summary>달성 비율(총점 ÷ 만점, 0~1). 달성현황 바가 읽는다.</summary>
        public float ratio;
    }
}
