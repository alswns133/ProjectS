namespace ProjectS.Core
{
    /// <summary>
    /// 강화가 소모하는 재화·재료의 소유자 계약. EnhanceService가 인벤토리 구현에 직접 의존하지 않도록
    /// 사이를 끊는다. 인벤토리/지갑 시스템이 아직 확정 전이라, 나중에 실제 구현으로 갈아끼울 수 있게 둔다.
    /// 아이템 기능 연동 계획(seam 4곳 전체)은 EquipmentInstance 클래스 주석 참조. (2026-07-23 TH)
    /// </summary>
    public interface IEnhanceResources
    {
        /// <summary>현재 보유 골드.</summary>
        int Gold { get; }

        /// <summary>현재 보유 하급 재료 개수.</summary>
        int LowMaterial { get; }

        /// <summary>현재 보유 상급 재료 개수.</summary>
        int HighMaterial { get; }

        /// <summary>
        /// 지정 비용을 감당할 수 있는지 확인한다. Spend 전에 반드시 이걸로 검사한다.
        /// </summary>
        /// <param name="zeny">필요 골드</param>
        /// <param name="lowMaterial">필요 하급 재료</param>
        /// <param name="highMaterial">필요 상급 재료</param>
        /// <returns>모두 감당 가능하면 true</returns>
        bool CanAfford(int zeny, int lowMaterial, int highMaterial);

        /// <summary>
        /// 비용을 차감한다. 호출 전 <see cref="CanAfford"/>로 확인하는 것을 전제로 한다.
        /// </summary>
        void Spend(int zeny, int lowMaterial, int highMaterial);
    }
}
