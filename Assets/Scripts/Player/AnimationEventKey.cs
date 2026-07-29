namespace ProjectS.Players
{
    /// <summary>
    /// 애니메이션 이벤트가 넘기는 슬롯 키(히트박스·투사체·스킬·이펙트)를 비교/조회하기 전에
    /// 정규화하는 유일한 통로. 클립 저작(팀원)과 인스펙터/코드가 키 표기에서 언더바 규칙이
    /// 갈렸다 — 예: 클립은 "Attack_1"/"Skill_1_1", 인스펙터·코드는 "Attack1"/"Skill1_1".
    /// 어느 표기로 적혀도 같은 키로 취급되도록, 비교 직전에 언더바를 제거한다.
    ///
    /// 왜 강제 통일이 아니라 '무시'인가: 언더바는 사람이 읽기 좋으라고 넣는 구분자일 뿐
    /// 의미가 없어, 한쪽 규칙으로 클립/인스펙터를 재저작하는 것보다 비교에서 무시하는 편이
    /// 재작업 없이 맞물린다. 정규화 후 서로 다른 키가 겹치면 슬롯 등록 시 중복 경고로 잡힌다.
    ///
    /// 주의: 언더바를 지우므로 "Skill3"과 "Skill3_Wave"처럼 토큰 자체가 다른 키는 여전히 다르다
    /// (이건 언더바 문제가 아니라 이름 불일치라 여기서 해결되지 않는다).
    /// </summary>
    public static class AnimationEventKey
    {
        /// <summary>언더바를 제거한 비교용 키. null/빈 문자열은 그대로 돌려준다.</summary>
        public static string Normalize(string key)
            => string.IsNullOrEmpty(key) ? key : key.Replace("_", string.Empty);
    }
}
