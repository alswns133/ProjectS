using UnityEngine;

namespace ProjectS.Debugging
{
    /// <summary>
    /// 에디터 전용 로그 래퍼. 각 메서드에 [Conditional("UNITY_EDITOR")]가 붙어 있어
    /// 빌드에서는 호출 자체가 컴파일 단계에서 제거된다 — 인자 계산과 문자열 생성까지 사라지므로
    /// 릴리스 빌드에 로그 비용이 전혀 남지 않는다(#if로 호출부를 감싸는 것보다 깔끔하고 실수가 없다).
    ///
    /// 임시/개발용 출력은 UnityEngine.Debug.Log 대신 이 클래스를 쓴다.
    /// 반대로 "빌드에서도 남겨야 하는" 실패(로드 실패 등)나 QA용 경고는
    /// Debug.LogError / Debug.LogWarning을 직접 써서 빌드에 남긴다.
    ///
    /// 개발 빌드(디바이스 테스트)에서도 로그를 보려면 각 속성에
    /// [Conditional("DEVELOPMENT_BUILD")]를 한 줄 더 붙이면 된다(OR로 동작).
    /// </summary>
    public static class DevLog
    {
        /// <summary>에디터에서만 출력되는 일반 로그. 빌드에서는 호출이 제거된다.</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void Log(object message, Object context = null) => Debug.Log(message, context);

        /// <summary>에디터에서만 출력되는 경고. 빌드에서도 남겨야 하면 Debug.LogWarning을 직접 쓴다.</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void Warning(object message, Object context = null) => Debug.LogWarning(message, context);

        /// <summary>에디터에서만 출력되는 에러. 빌드에서도 남겨야 하는 실패는 Debug.LogError를 직접 쓴다.</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void Error(object message, Object context = null) => Debug.LogError(message, context);
    }
}
