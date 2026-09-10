using UnityEngine;
using UnityEngine.Timeline;

namespace ProjectS.UI
{
    /// <summary>
    /// 불 가림막(<see cref="FireCurtainFx"/>)을 꽂아 <see cref="FireCurtainClip"/>을 올리는 Timeline 트랙.
    /// 레이드 보스 등장 연출(<c>RaidIntro.playable</c>)에서 쓴다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 트랙에 이름을 붙여 두면 <c>BossIntroDirector</c>가 하듯 런타임 재바인딩도 가능하지만,
    /// 가림막은 <b>보스가 아니라 씬에 미리 있는 UI</b>라 인스펙터에서 한 번 꽂아 두면 그만이다
    /// (보스처럼 런타임 스폰되지 않으므로 바인딩이 비지 않는다).
    /// </para>
    /// <para>
    /// 한 트랙에 클립은 하나만 둔다. 두 클립이 겹치면 같은 머티리얼 값을 서로 덮어써 진행이 튄다
    /// (<see cref="FireCurtainClip.clipCaps"/>가 블렌딩을 막아 두는 이유).
    /// </para>
    /// </remarks>
    [TrackColor(0.85f, 0.36f, 0.12f)]
    [TrackBindingType(typeof(FireCurtainFx))]
    [TrackClipType(typeof(FireCurtainClip))]
    public class FireCurtainTrack : TrackAsset
    {
    }
}
