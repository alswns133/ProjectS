using System;
using UnityEngine;
using ProjectS.Settings;

namespace ProjectS.Events
{
    /// <summary>
    /// 옵션 설정 변경을 시스템 간에 알리는 static 이벤트 허브.
    /// 볼륨처럼 "바뀐 순간 한 번 반영하면 되는" 값을 가진 시스템(SoundManager 등)이 구독한다.
    /// 매 프레임 읽는 값(마우스 감도 등)은 구독 대신 <see cref="GameSettings.Current"/>를 직접 읽어도 된다.
    /// </summary>
    public static class SettingsEvents
    {
        /// <summary>
        /// 설정이 확정되었거나(Apply/Reset) 미리보기 중(Preview/Revert)일 때 발행된다.
        /// 인자는 지금 반영해야 할 설정 — 미리보기 중엔 확정 전 사본이라 <see cref="GameSettings.Current"/>와 다를 수 있다.
        /// </summary>
        public static event Action<GameSettings> OnChanged;

        /// <summary>설정 변경을 알린다. <see cref="GameSettings"/>의 Apply/Preview/Revert가 호출한다.</summary>
        /// <param name="settings">반영할 설정</param>
        public static void FireChanged(GameSettings settings)
            => OnChanged?.Invoke(settings);

        /// <summary>
        /// 모든 구독을 초기화. 도메인 리로드를 꺼도 플레이 시작 시 깨끗한 상태를 보장한다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnChanged = null;
        }
    }
}
