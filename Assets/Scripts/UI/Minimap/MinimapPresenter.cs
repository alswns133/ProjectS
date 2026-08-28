using UnityEngine;
using ProjectS.Events;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 미니맵 Presenter. MinimapEvents(존재 변화)를 받아 MinimapView(마커)로 넘긴다.
    /// HUD의 HpChanged → HUDPresenter → HUDPanel 흐름과 같은 구조다.
    /// 위치 갱신은 여기를 거치지 않고 MinimapView가 매 프레임 직접 처리한다(존재/위치 책임 분리).
    /// </summary>
    public class MinimapPresenter : BasePresenter
    {
        [SerializeField] private MinimapView view;

        private void Awake()
        {
            if (view == null) view = GetComponentInChildren<MinimapView>(true);
        }

        protected override void Subscribe()
        {
            MinimapEvents.OnRegistered += view.AddMarker;
            MinimapEvents.OnUnregistered += view.RemoveMarker;
            MinimapEvents.OnStageChanged += view.SetBackground;

            // 늦게 켜졌을 때(적이 먼저 스폰됐거나 씬이 HUD보다 먼저 스테이지를 발행한 경우)
            // 이미 정해진 배경과 등록된 대상들을 한 번 훑어 따라잡는다.
            // 이게 없으면 이 Presenter가 켜지기 전에 발행된 이벤트를 놓쳐 배경·마커가 비어 보인다.
            if (MinimapEvents.HasStage)
                view.SetBackground(MinimapEvents.StageSprite, MinimapEvents.StageCenter, MinimapEvents.StageSize, MinimapEvents.StageName);

            var active = MinimapEvents.Active;
            for (int i = 0; i < active.Count; i++)
                view.AddMarker(active[i].Target, active[i].Type);
        }

        protected override void Unsubscribe()
        {
            MinimapEvents.OnRegistered -= view.AddMarker;
            MinimapEvents.OnUnregistered -= view.RemoveMarker;
            MinimapEvents.OnStageChanged -= view.SetBackground;

            // 다시 켜질 때 Subscribe의 복원과 중복되지 않도록 마커를 비운다.
            if (view != null) view.ClearAll();
        }
    }
}
