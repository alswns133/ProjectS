using UnityEngine;

namespace ProjectS.UI.Framework
{
    /// <summary>
    /// 이동식 창의 위치를 창 id별로 PlayerPrefs에 저장/복원하는 스토어. 창 위치는 캐릭터별 게임플레이
    /// 세이브가 아니라 "기기 로컬 UI 환경설정"이라(모니터 해상도에 종속) 자동로그인과 같은 PlayerPrefs에 둔다.
    /// 사용자가 한 번 배치하면 다음에도 그 자리로 열려 같은 정리를 반복하지 않게 하는 편의 기능이다.
    /// 인벤토리 전용이 아니라 창 id만 다르게 주면 장비·스킬 등 다른 이동식 창도 그대로 위치를 기억한다.
    /// </summary>
    public static class WindowLayoutStore
    {
        /// <summary>창의 현재 위치를 저장한다. id나 대상이 없으면 무시한다.</summary>
        /// <param name="windowId">창 고유 키(예: "inventory")</param>
        /// <param name="target">저장할 창 RectTransform</param>
        public static void Save(string windowId, RectTransform target)
        {
            if (string.IsNullOrEmpty(windowId) || target == null) return;

            Vector2 pos = target.anchoredPosition;
            PlayerPrefs.SetFloat(KeyX(windowId), pos.x);
            PlayerPrefs.SetFloat(KeyY(windowId), pos.y);
            PlayerPrefs.Save();
        }

        /// <summary>저장된 위치를 창에 적용한다. 저장 이력이 없으면 아무것도 하지 않는다.</summary>
        /// <param name="windowId">창 고유 키</param>
        /// <param name="target">적용할 창 RectTransform</param>
        /// <returns>저장된 위치를 적용했으면 true(없으면 false — 호출측이 기본 위치를 유지)</returns>
        public static bool Load(string windowId, RectTransform target)
        {
            if (string.IsNullOrEmpty(windowId) || target == null) return false;
            if (!PlayerPrefs.HasKey(KeyX(windowId))) return false;

            target.anchoredPosition = new Vector2(
                PlayerPrefs.GetFloat(KeyX(windowId)),
                PlayerPrefs.GetFloat(KeyY(windowId)));
            return true;
        }

        private static string KeyX(string windowId) => $"win.{windowId}.x";
        private static string KeyY(string windowId) => $"win.{windowId}.y";
    }
}
