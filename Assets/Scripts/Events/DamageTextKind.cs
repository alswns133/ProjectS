namespace ProjectS.Events
{
    /// <summary>
    /// 데미지 텍스트의 종류. 색을 결정하는 유일한 기준이며, 실제 색값은 구독자(DamageTextSpawner)가 갖는다.
    /// 전투 로직이 Color를 들고 다니지 않게 하기 위한 분리 — 로직은 "무슨 일이 있었는지"만 알리고,
    /// "그래서 무슨 색인지"는 연출 쪽에서 정한다.
    /// </summary>
    public enum DamageTextKind
    {
        /// <summary>플레이어가 적을 때렸고 치명타가 아님.</summary>
        Normal,

        /// <summary>플레이어가 적을 때렸고 치명타가 터짐.</summary>
        Critical,

        /// <summary>플레이어가 맞음. 때린 쪽의 치명타 여부와 무관하게 이 종류로 표시한다.</summary>
        PlayerDamaged,
    }
}
