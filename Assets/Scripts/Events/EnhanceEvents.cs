using System;
using UnityEngine;
using ProjectS.Enhance;

namespace ProjectS.Events
{
    /// <summary>
    /// 강화 결과를 시스템 간에 알리는 static 이벤트 허브. PlayerEvents/InventoryEvents와 같은 패턴이다.
    /// 강화 성공/실패에 반응해야 하는 곳(퀘스트·업적·사운드 등)이 여기에 구독한다.
    /// (2026-07-23 TH)
    /// </summary>
    public static class EnhanceEvents
    {
        /// <summary>
        /// 강화 시도가 완료됐을 때 발행(성공/실패 모두). 결과 값을 전달한다.
        /// ★ 아이템 기능 연동 시 "장비 스탯 집계기"가 이걸 구독해 장착 중이면 스탯을 재계산해야 한다
        ///   (현재는 구독자 없음). 연동 계획 전체는 EquipmentInstance 클래스 주석 참조.
        /// </summary>
        public static event Action<EnhanceResult> OnEnhanced;

        /// <summary>
        /// 강화 완료 이벤트 발행. 재료·골드 차감과 표시 갱신이 끝난 뒤 Presenter가 호출한다.
        /// </summary>
        /// <param name="result">강화 판정 결과</param>
        public static void FireEnhanced(EnhanceResult result)
            => OnEnhanced?.Invoke(result);

        /// <summary>
        /// 모든 구독을 초기화. 도메인 리로드를 꺼도 플레이 시작 시 깨끗한 상태를 보장한다.
        /// (static 이벤트가 이전 플레이 세션의 죽은 구독자를 들고 있는 것을 방지)
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnEnhanced = null;
        }
    }
}
