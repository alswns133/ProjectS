namespace ProjectS.Enhance
{
    /// <summary>
    /// 강화 1회 판정 결과. 성공 여부와 단계 변화만 담는 읽기 전용 값이다.
    /// 연출(PlayResult)과 표시 갱신, 이벤트 발행이 이 값을 읽어 분기한다.
    /// (2026-07-23 TH)
    /// </summary>
    public readonly struct EnhanceResult
    {
        /// <summary>강화 성공 여부.</summary>
        public readonly bool Success;

        /// <summary>시도 전 강화 단계.</summary>
        public readonly int StepBefore;

        /// <summary>시도 후 강화 단계. 실패해도 하락은 없다(기획).</summary>
        public readonly int StepAfter;

        public EnhanceResult(bool success, int stepBefore, int stepAfter)
        {
            Success = success;
            StepBefore = stepBefore;
            StepAfter = stepAfter;
        }
    }
}
