using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace ProjectS.UI
{
    /// <summary>
    /// 보스 등장 UI 연출을 Timeline 클립 하나로 재생한다. <b>클립 시작이 곧 연출 시작</b>이고,
    /// 클립 안의 시각을 그대로 <see cref="BossIntroFx.Sample"/>에 넘긴다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 Signal이 아니라 클립인가.</b> Signal로 <c>Play</c>를 부르면 받은 쪽 코루틴이 제 시간으로 흘러가,
    /// 타임라인을 스크럽·되감아도 화면이 따라오지 않는다. 클립은 타임라인 시간을 받으므로 헤드를 끌면
    /// 경고·커튼·BOSS 슬램이 같이 움직여 카메라 컷에 눈으로 맞출 수 있다(<see cref="FireCurtainClip"/>과 같은 이유).
    /// </para>
    /// <para>
    /// <b>비율이 아니라 절대 시각을 쓴다.</b> 불 가림막과 달리 이 연출은 깜박임 간격·슬램 속도처럼
    /// 초 단위로 맞춘 박자가 많아, 클립 길이에 맞춰 늘이면 인상이 무너진다. 그래서 단계별 시간은
    /// <see cref="BossIntroFx"/> 인스펙터에서 조절하고, 클립은 "언제 시작하나"만 정한다.
    /// 클립 길이는 <see cref="BossIntroFx.Duration"/> 이상으로 둔다 — 짧으면 끝이 잘린다(플레이 중 한 번 경고).
    /// </para>
    /// <para>
    /// <b>바인딩</b>: <see cref="BossIntroTrack"/>에 씬의 <see cref="BossIntroFx"/>를 꽂는다.
    /// </para>
    /// </remarks>
    public class BossIntroClip : PlayableAsset, ITimelineClipAsset
    {
        // 새로 만든 클립의 기본 길이(초). 기본 인스펙터 값 기준 연출 전체(약 3.7초)를 덮도록 잡는다.
        private const double DefaultDuration = 4.0;

        [Tooltip("BOSS 아래에 표시할 보스 이름. 비우면 이름 줄을 숨긴다. " +
                 "보스 오브젝트에서 읽지 않고 여기 적는다 — 연출이 보스 스폰과 무관하게 돌게 하기 위함.")]
        public string bossName;

        /// <summary>블렌딩·확장을 쓰지 않는다. 두 클립이 겹쳐 같은 UI를 서로 덮어쓰면 값이 튄다.</summary>
        public ClipCaps clipCaps => ClipCaps.None;

        /// <inheritdoc/>
        public override double duration => DefaultDuration;

        /// <inheritdoc/>
        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            ScriptPlayable<BossIntroBehaviour> playable = ScriptPlayable<BossIntroBehaviour>.Create(graph);
            playable.GetBehaviour().BossName = bossName;
            return playable;
        }
    }

    /// <summary>
    /// <see cref="BossIntroClip"/>의 실제 구동부. 클립 안의 시각을 <see cref="BossIntroFx.Sample"/>에 넣는다.
    /// </summary>
    /// <remarks>
    /// 이 클래스는 시간을 스스로 세지 않는다. Timeline이 주는 <c>playable.GetTime()</c>만 읽으므로
    /// 스크럽·되감기·배속이 전부 그대로 반영된다.
    /// </remarks>
    public class BossIntroBehaviour : PlayableBehaviour
    {
        /// <summary>BOSS 아래에 표시할 이름. 비우면 이름 줄을 숨긴다.</summary>
        public string BossName;

        // 클립이 끝나거나 잘렸을 때 화면을 되돌리려면 대상이 필요한데,
        // OnBehaviourPause에는 playerData가 오지 않는다. ProcessFrame에서 붙잡아 둔다.
        private BossIntroFx bound;

        private bool lengthWarned;

        /// <inheritdoc/>
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (playerData is not BossIntroFx fx) return;

            if (bound != fx)
            {
                if (bound != null) bound.EndSampling();
                bound = fx;
                fx.SetBossName(BossName);
                WarnIfClipTooShort(playable, fx);
            }

            fx.Sample((float)playable.GetTime());
        }

        /// <inheritdoc/>
        /// <remarks>
        /// 클립 범위를 벗어나면 화면을 되돌린다. <b>없으면 연출이 중간에 잘렸을 때 경고 띠·BOSS가 화면에 남는다</b>
        /// — 디렉터가 Stop되거나 씬이 바뀌는 경우가 그렇다. 스크럽으로 클립 밖에 나갔다 들어오는 경우도
        /// 같은 처리가 맞다(다시 들어오면 ProcessFrame이 곧바로 다시 그린다).
        /// </remarks>
        public override void OnBehaviourPause(Playable playable, FrameData info)
        {
            Release();
        }

        /// <inheritdoc/>
        /// <remarks>
        /// 그래프가 통째로 사라질 때(Timeline 창의 미리보기를 끄거나 디렉터를 바꿀 때)도 되돌린다.
        /// 이 경로에서 Pause가 불린다는 보장이 없다.
        /// </remarks>
        public override void OnPlayableDestroy(Playable playable)
        {
            Release();
        }

        private void Release()
        {
            if (bound == null) return;

            bound.EndSampling();
            bound = null;
        }

        // 클립이 연출보다 짧으면 BOSS가 타들어가기 전에 잘린다. 화면만 봐서는 "재 연출이 안 나온다"로 보여
        // 원인을 찾기 어려우니 플레이 중 한 번 짚어 준다(에디터 스크럽 중에는 매번 뜨면 소음이라 생략).
        private void WarnIfClipTooShort(Playable playable, BossIntroFx fx)
        {
            if (lengthWarned || !Application.isPlaying) return;

            float needed = fx.Duration;
            if (playable.GetDuration() + 0.001 >= needed) return;

            lengthWarned = true;
            Debug.LogWarning($"[BossIntroClip] 클립 길이({playable.GetDuration():0.00}초)가 연출 전체({needed:0.00}초)보다 짧아 " +
                             "끝부분(재 연출)이 잘립니다. 클립을 늘리거나 BossIntroFx의 단계 시간을 줄이세요.", fx);
        }
    }
}
