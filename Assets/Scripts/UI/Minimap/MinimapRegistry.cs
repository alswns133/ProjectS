using System.Collections.Generic;
using ProjectS.Events;

namespace ProjectS.UI
{
    /// <summary>
    /// 스테이지(씬)별 MinimapData를 "씬 이름 → 데이터"로 보관하는 조회 등록소.
    /// Bootstrap이 시작 시 채우고(RegisterStage), 씬 전환 시 이름으로 찾아 적용한다(ApplyStage).
    /// <para>
    /// MinimapEvents(이벤트 허브)와 분리한 이유:
    /// - 이 등록소는 "발행된 이벤트의 캐시"가 아니라 Bootstrap이 넣는 원본 config 조회 테이블이라 성격이 다르다.
    /// - MinimapData(UI)를 직접 들어야 편한데, 저수준 이벤트 허브(Events)가 상위 UI 타입을 참조하면 의존 방향이 거꾸로다.
    ///   이 등록소는 UI 소속이라 MinimapData를 그대로 들 수 있고, 발행은 MinimapEvents의 원시값 메서드를 재사용한다.
    /// </para>
    /// </summary>
    public static class MinimapRegistry
    {
        // 씬 이름(GameSceneManager의 typeof(T).Name과 동일) → 그 스테이지의 미니맵 데이터.
        private static readonly Dictionary<string, MinimapData> stages = new Dictionary<string, MinimapData>();

        /// <summary>
        /// 스테이지 하나의 미니맵 데이터를 등록한다. Bootstrap이 인스펙터 목록을 시작 시 여기에 넣는다.
        /// 같은 씬 이름으로 다시 넣으면 덮어쓴다.
        /// </summary>
        /// <param name="sceneName">GameSceneManager가 쓰는 씬 이름(씬 클래스 이름)과 정확히 같아야 한다.</param>
        /// <param name="data">그 스테이지의 스냅샷·월드 범위를 담은 데이터.</param>
        public static void RegisterStage(string sceneName, MinimapData data)
        {
            if (string.IsNullOrEmpty(sceneName) || data == null) return;

            stages[sceneName] = data;
        }

        /// <summary>
        /// 지정 씬의 미니맵 배경을 적용한다. GameSceneManager가 씬 전환 시 next 이름으로 호출한다.
        /// 등록된 데이터가 없으면(아직 스냅샷 미제작) 아무것도 하지 않아 미니맵은 기존 배경을 유지한다.
        /// 실제 배경 교체는 MinimapEvents의 발행 경로를 그대로 탄다(중복 로직 없음).
        /// </summary>
        public static void ApplyStage(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return;
            if (!stages.TryGetValue(sceneName, out MinimapData data) || data == null) return;

            MinimapEvents.FireStageChanged(data.Snapshot, data.WorldCenter, data.WorldSize, data.DisplayName);
        }

        /// <summary>등록을 모두 비운다. 씬을 넘어 유지되는 static이므로 필요 시 초기화 경로에서 부른다.</summary>
        public static void Clear() => stages.Clear();
    }
}
