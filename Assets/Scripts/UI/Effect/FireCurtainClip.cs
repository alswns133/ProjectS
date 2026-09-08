using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace ProjectS.UI
{
    /// <summary>
    /// 불 가림막을 Timeline 클립 하나로 재생한다. <b>클립 길이가 곧 연출 길이</b>이고,
    /// 클립 안에서 덮임·유지·걷힘의 비율을 나눈다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 Signal 두 발이 아니라 클립인가.</b> Signal은 한 순간에 한 번 튀는 통보라, 받은 쪽 코루틴이
    /// 제 시간으로 흘러간다. 그러면 타임라인을 스크럽하거나 되감아도 화면은 따라오지 않아서
    /// <b>연출을 눈으로 맞출 수가 없다</b> — 등장 컷신은 프레임 단위로 카메라·보스 모션과 맞춰야 하는데
    /// 미리보기가 안 되면 매번 플레이해서 감으로 맞춰야 한다. 클립은 타임라인의 시간을 그대로 받으므로
    /// 에디터에서 헤드를 끌면 불이 같이 덮이고 걷힌다.
    /// </para>
    /// <para>
    /// <b>비율로 나누는 이유.</b> 초 단위로 세 구간을 따로 두면 클립을 늘렸을 때 뒤가 잘리거나 남는다.
    /// 비율이면 클립 양 끝을 끌어 전체 길이만 조절해도 세 구간이 같은 인상으로 늘어난다.
    /// 유지 구간은 남는 몫이라 따로 지정하지 않는다(= 1 - 덮임 - 걷힘).
    /// </para>
    /// <para>
    /// <b>바인딩</b>: <see cref="FireCurtainTrack"/>에 <see cref="FireCurtainFx"/>를 꽂는다.
    /// 폭발 중심은 <see cref="explosionCenter"/>에 씬의 Transform을 물린다(보통 폭발 파티클 오브젝트).
    /// 비워 두면 화면 중앙에서 자란다.
    /// </para>
    /// </remarks>
    public class FireCurtainClip : PlayableAsset, ITimelineClipAsset
    {
        [Tooltip("불이 자라날 지점. 보통 폭발 파티클 오브젝트를 물린다. 비우면 화면 중앙.")]
        public ExposedReference<Transform> explosionCenter;

        [Tooltip("클립 길이 중 덮임에 쓸 비율. 짧아야 '터졌다'로 읽힌다.")]
        [Range(0.02f, 0.9f)] public float coverRatio = 0.12f;

        [Tooltip("클립 길이 중 걷힘에 쓸 비율. 남는 몫이 유지 구간이 된다.")]
        [Range(0.02f, 0.9f)] public float burnRatio = 0.36f;

        [Tooltip("덮이는 진행 곡선. 앞이 가파를수록 터져 나온 것처럼 읽힌다.")]
        public AnimationCurve coverCurve = new(
            new Keyframe(0f, 0f, 3.4f, 3.4f), new Keyframe(1f, 1f, 0.2f, 0.2f));

        [Tooltip("타들어가는 진행 곡선. 반드시 0에서 시작해 1로 끝나야 한다.")]
        public AnimationCurve burnCurve = new(
            new Keyframe(0f, 0f, 0.8f, 0.8f), new Keyframe(1f, 1f, 2.2f, 2.2f));

        [Tooltip("진행값을 1보다 얼마나 더 밀지. 경계가 노이즈로 일렁이므로 정확히 1에서 멈추면 " +
                 "화면 모서리에 불이 남거나 덜 걷힌 자국이 생긴다.")]
        [Range(0f, 0.6f)] public float overshoot = 0.3f;

        /// <summary>블렌딩·확장을 쓰지 않는다. 두 클립이 겹쳐 같은 머티리얼을 서로 덮어쓰면 값이 튄다.</summary>
        public ClipCaps clipCaps => ClipCaps.None;

        /// <inheritdoc/>
        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            ScriptPlayable<FireCurtainBehaviour> playable = ScriptPlayable<FireCurtainBehaviour>.Create(graph);

            FireCurtainBehaviour behaviour = playable.GetBehaviour();
            behaviour.Center = explosionCenter.Resolve(graph.GetResolver());
            behaviour.CoverRatio = coverRatio;
            behaviour.BurnRatio = burnRatio;
            behaviour.CoverCurve = coverCurve;
            behaviour.BurnCurve = burnCurve;
            behaviour.Overshoot = overshoot;

            return playable;
        }
    }

    /// <summary>
    /// <see cref="FireCurtainClip"/>의 실제 구동부. 클립 안의 진행도를 덮임·걷힘 두 값으로 바꿔
    /// <see cref="FireCurtainFx.SetProgress"/>에 넣는다.
    /// </summary>
    /// <remarks>
    /// 이 클래스는 시간을 스스로 세지 않는다. Timeline이 주는 <c>playable.GetTime()</c>만 읽으므로
    /// 스크럽·되감기·배속이 전부 그대로 반영된다 — 그게 클립으로 만든 이유다.
    /// </remarks>
    public class FireCurtainBehaviour : PlayableBehaviour
    {
        /// <summary>불이 자라날 지점. null이면 화면 중앙에서 자란다.</summary>
        public Transform Center;

        /// <summary>클립 길이 중 덮임에 쓸 비율.</summary>
        public float CoverRatio = 0.12f;

        /// <summary>클립 길이 중 걷힘에 쓸 비율.</summary>
        public float BurnRatio = 0.36f;

        /// <summary>덮이는 진행 곡선.</summary>
        public AnimationCurve CoverCurve;

        /// <summary>타들어가는 진행 곡선.</summary>
        public AnimationCurve BurnCurve;

        /// <summary>진행값을 1보다 얼마나 더 미는지.</summary>
        public float Overshoot = 0.3f;

        // 중심을 매 프레임 다시 뜨지 않는다. 가려진 동안 카메라가 보스를 잡으러 움직이는데,
        // 월드 좌표를 계속 따라가면 불의 중심이 화면에서 미끄러진다.
        private bool centerApplied;

        // 클립이 끝나거나 잘렸을 때 화면을 되돌리려면 대상이 필요한데,
        // OnBehaviourPause에는 playerData가 오지 않는다. ProcessFrame에서 붙잡아 둔다.
        private FireCurtainFx bound;

        /// <inheritdoc/>
        public override void OnBehaviourPlay(Playable playable, FrameData info)
        {
            centerApplied = false;
        }

        /// <inheritdoc/>
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (playerData is not FireCurtainFx curtain) return;
            bound = curtain;

            if (!centerApplied)
            {
                if (Center != null) curtain.SetWorldCenter(Center.position);
                centerApplied = true;
            }

            double duration = playable.GetDuration();
            if (duration <= 0.0) return;

            float t = Mathf.Clamp01((float)(playable.GetTime() / duration));

            // 세 구간의 경계. 유지 구간은 남는 몫이라 따로 지정하지 않는다.
            float coverEnd = Mathf.Clamp(CoverRatio, 0.001f, 0.98f);
            float burnStart = Mathf.Clamp(1f - BurnRatio, coverEnd, 0.999f);

            float goal = 1f + Overshoot;

            float cover = Evaluate(CoverCurve, Mathf.Clamp01(t / coverEnd)) * goal;

            float burn = t <= burnStart
                ? 0f
                : Evaluate(BurnCurve, Mathf.Clamp01((t - burnStart) / Mathf.Max(0.001f, 1f - burnStart))) * goal;

            curtain.SetProgress(cover, burn);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// 클립 범위를 벗어나면 화면을 되돌린다. <b>없으면 연출이 중간에 잘렸을 때 화면이 불에 덮인 채 남는다</b>
        /// — 디렉터가 Stop되거나 씬이 바뀌는 경우가 그렇다. 클립을 정상적으로 끝까지 재생했을 때는
        /// 이미 걷힘이 끝나 아무것도 안 보이므로 이 호출이 눈에 띄지 않는다.
        /// 스크럽으로 클립 밖에 나갔다 들어오는 경우도 같은 처리가 맞다(다시 들어오면 ProcessFrame이 곧바로 다시 칠한다).
        /// </remarks>
        public override void OnBehaviourPause(Playable playable, FrameData info)
        {
            if (bound == null) return;

            bound.Clear();
            bound = null;
        }

        /// <summary>곡선이 비어 있어도 선형으로 동작하게 한다. 인스펙터에서 곡선을 지우면 null이 온다.</summary>
        private static float Evaluate(AnimationCurve curve, float t)
        {
            if (curve == null || curve.length == 0) return t;
            return Mathf.Clamp01(curve.Evaluate(t));
        }
    }
}
