using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace ProjectS.UI
{
    /// <summary>
    /// 보스 등장 UI 연출(<see cref="BossIntroFx"/>)을 꽂아 <see cref="BossIntroClip"/>을 올리는 Timeline 트랙.
    /// 레이드 보스 등장 컷신(<c>RaidIntro.playable</c>)에서 불 가림막 트랙과 나란히 쓴다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>보스와 연동하지 않는다.</b> 등장 연출 UI는 씬에 미리 있는 오브젝트라 인스펙터에서 한 번 꽂아 두면
    /// 바인딩이 비지 않는다. 보스 이름도 클립에 적으므로, 연출이 보스 스폰·등장 신호를 기다리지 않고
    /// 타임라인 시각에 맞춰 돈다(<see cref="FireCurtainTrack"/>과 같은 방침).
    /// </para>
    /// <para>
    /// 한 트랙에 클립은 하나만 둔다. 두 클립이 겹치면 같은 UI를 서로 덮어써 진행이 튄다
    /// (<see cref="BossIntroClip.clipCaps"/>가 블렌딩을 막아 두는 이유).
    /// </para>
    /// </remarks>
    [TrackColor(0.78f, 0.14f, 0.18f)]
    [TrackBindingType(typeof(BossIntroFx))]
    [TrackClipType(typeof(BossIntroClip))]
    public class BossIntroTrack : TrackAsset
    {
        /// <inheritdoc/>
        /// <remarks>
        /// 에디터에서 헤드를 끌어 미리보기할 때 연출이 바꾸는 속성(활성 상태·위치·크기·알파)을 등록해,
        /// 미리보기가 끝나면 원래 값으로 되돌아가게 한다. <b>빠지면 스크럽만 해도 씬이 더러워진다</b>
        /// — 경고 띠가 밀려난 위치나 꺼진 오브젝트가 그대로 저장된다.
        /// </remarks>
        public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
        {
            if (director != null && director.GetGenericBinding(this) is BossIntroFx fx)
                fx.CollectDrivenProperties(driver);

            base.GatherProperties(director, driver);
        }
    }
}
