using ProjectS.Data;

namespace ProjectS.Enhance
{
    /// <summary>
    /// View에 뿌릴 강화 정보 스냅샷. 판정을 담지 않는 읽기 전용 값이라 여러 번 만들어도 안전하다.
    /// EnhanceService가 대상 인스턴스의 현재 상태로 조립해 EnhancePopup.SetTarget에 넘긴다.
    /// (2026-07-23 TH)
    /// </summary>
    public readonly struct EnhanceInfo
    {
        /// <summary>현재 강화 단계.</summary>
        public readonly int CurrentStep;

        /// <summary>최대 단계 도달 여부. true면 확률·비용·다음 스탯은 의미가 없다.</summary>
        public readonly bool IsMax;

        /// <summary>다음 단계 성공 확률(0~1).</summary>
        public readonly float SuccessRate;

        /// <summary>다음 시도의 골드 비용.</summary>
        public readonly int ZenyCost;

        /// <summary>다음 시도의 하급 재료 소모량.</summary>
        public readonly int LowMaterial;

        /// <summary>다음 시도의 상급 재료 소모량.</summary>
        public readonly int HighMaterial;

        /// <summary>현재 주 스탯 값(+0 기준 + 누적 강화 보너스).</summary>
        public readonly int CurrentMainStat;

        /// <summary>성공 시 주 스탯 값(프리뷰용).</summary>
        public readonly int NextMainStat;

        /// <summary>주 스탯 종류(라벨 표시용).</summary>
        public readonly MainStatType MainStatType;

        public EnhanceInfo(int currentStep, bool isMax, float successRate, int zenyCost,
            int lowMaterial, int highMaterial, int currentMainStat, int nextMainStat, MainStatType mainStatType)
        {
            CurrentStep = currentStep;
            IsMax = isMax;
            SuccessRate = successRate;
            ZenyCost = zenyCost;
            LowMaterial = lowMaterial;
            HighMaterial = highMaterial;
            CurrentMainStat = currentMainStat;
            NextMainStat = nextMainStat;
            MainStatType = mainStatType;
        }
    }
}
