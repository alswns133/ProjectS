using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ProjectS.Players
{
    /// <summary>
    /// 공중 대시 같은 고속 이동 중 화면 가장자리를 어둡게·왜곡시켜 가속감을 주는 포스트프로세싱 연출.
    /// Vignette(가장자리 암전) + Lens Distortion(안쪽으로 빨려드는 왜곡) + Motion Blur(카메라 이동 블러)
    /// 세 가지 URP 기본 이펙트를 조합한다. 진짜 방사형(radial) 블러는 커스텀 셰이더가 필요하지만,
    /// 이 조합만으로도 레이싱 게임 가속 연출과 체감상 매우 비슷하다.
    ///
    /// ★ 씬에 있는 Volume에 얹지 않고 자체적으로 Volume을 만들어 쓴다.
    ///   프로젝트를 확인해 보니 Vignette/Motion Blur가 설정된 DefaultVolumeProfile이 있지만
    ///   실제로 어떤 씬의 Volume에도 연결되어 있지 않았다(전역 Volume 자체가 없음).
    ///   씬 설정에 기대면 "이 던전엔 Volume이 없어서 이펙트가 안 보임" 사고가 나므로,
    ///   플레이어가 어디에 있든 동작하도록 스스로 Volume을 들고 다닌다.
    ///   isGlobal=true라 이 오브젝트의 위치는 효과 범위와 무관하다.
    ///
    /// ★ 애니메이션 이벤트가 아니라 PlayerMovement.IsJumpDashing을 매 프레임 읽어 판정한다.
    ///   대시 지속시간은 PlayerMovement가 코드로 소유하고(jumpDashDuration), 클립 이벤트로
    ///   시작/끝을 알려주지 않는다. State 클래스에 직접 훅을 넣지 않는 것은
    ///   PlayerVfxEffects가 IsActionInterrupted를 폴링하는 것과 같은 이유(State는 수정 금지 영역).
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerSpeedEffect : MonoBehaviour
    {
        [Header("가속감 강도 (0~1, 최대일 때 값)")]
        [SerializeField, Range(0f, 1f)] private float vignetteIntensity = 0.35f;
        [SerializeField, Range(-1f, 0f)] private float lensDistortionIntensity = -0.35f;
        [SerializeField, Range(0f, 1f)] private float motionBlurIntensity = 0.4f;

        [Header("타이밍")]
        // 0에서 최대 강도까지 올라가는 시간. 너무 느리면 대시 시작과 이펙트가 따로 놀아 보인다.
        [SerializeField, Min(0.01f)] private float punchInTime = 0.08f;

        // 대시가 끝난 뒤 최대 강도에서 0으로 풀리는 시간. 뚝 끊기면 부자연스러워 살짝 남겨 둔다.
        [SerializeField, Min(0.01f)] private float releaseTime = 0.25f;

        // 다른 Volume(추후 씬에 앰비언트 연출이 추가될 경우)보다 항상 위에서 적용되도록 높게 잡는다.
        [SerializeField] private int volumePriority = 100;

        [Header("스피드 라인 파티클 (선택)")]
        // Main Camera(또는 CameraPivot) 자식으로 만든 화면 고정 파티클. Stretched Billboard로
        // 중심에서 바깥으로 뻗는 줄무늬를 표현한다. 비워 두면 파티클 없이 포스트프로세싱만 동작한다.
        [SerializeField] private ParticleSystem speedLinesParticle;

        private PlayerMovement movement;

        // 대시 시작/종료 '순간'에만 Play/Stop을 호출하기 위한 엣지 감지. currentT처럼 매 프레임
        // 갱신하지 않는 이유: ParticleSystem.Play()를 매 프레임 부르면 파티클이 계속 리셋된다.
        private bool wasDashing;

        private Vignette vignette;
        private LensDistortion lensDistortion;
        private MotionBlur motionBlur;

        // 목표(0=평소, 1=최대 가속감)로 다가가는 현재 진행도. 대시 중이면 1을 향해,
        // 아니면 0을 향해 매 프레임 이동한다 — 연속 재대시에도 끊김 없이 자연스럽게 이어진다.
        private float currentT;

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();

            if (movement == null)
                Debug.LogWarning($"{name}: PlayerMovement가 없어 가속 이펙트가 비활성됩니다. PlayerMovement와 같은 오브젝트에 붙여야 합니다.", this);

            // 런타임에 프로필을 직접 구성한다. 에셋 파일로 만들지 않는 이유는 씬마다 다시
            // 연결해야 하는 수고를 없애고, 이 컴포넌트 하나만 붙이면 어디서든 동작하게 하기 위함.
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();

            vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0f);
            vignette.smoothness.Override(0.3f);

            lensDistortion = profile.Add<LensDistortion>(true);
            lensDistortion.intensity.Override(0f);

            motionBlur = profile.Add<MotionBlur>(true);
            motionBlur.mode.Override(MotionBlurMode.CameraOnly);
            motionBlur.intensity.Override(0f);

            // Volume은 별도 자식 오브젝트(레이어 0=Default)에 붙인다. 이 오브젝트(플레이어 루트)에
            // 바로 붙이면, 플레이어가 씬 카메라의 Volume Layer Mask에 포함되지 않는 레이어
            // (예: "Player" 레이어)에 있을 때 카메라가 이 Volume을 통째로 무시해 버린다
            // (증상: 에러 없이 조용히 아무 효과도 안 보임). Default 레이어로 분리해 두면
            // 씬마다 Volume Layer Mask 설정이 달라도, Haru가 어떤 레이어에 있어도 항상 잡힌다.
            var volumeObject = new GameObject("SpeedEffectVolume");
            volumeObject.layer = 0;
            volumeObject.transform.SetParent(transform, false);

            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = volumePriority;
            volume.weight = 1f;
            volume.profile = profile;
        }

        private void Update()
        {
            bool dashing = movement != null && movement.IsJumpDashing;

            // MoveTowards + 역시간 방식: 대시 중엔 punchInTime 안에 1로, 아니면 releaseTime 안에 0으로.
            // 코루틴 대신 이 방식을 쓰는 이유는 연속 재대시 중 상태가 겹쳐도(대시→즉시 재대시)
            // 별도 취소 로직 없이 항상 '지금 상태가 원하는 값'으로 자연스럽게 수렴하기 때문이다.
            float speed = dashing ? 1f / punchInTime : 1f / releaseTime;
            currentT = Mathf.MoveTowards(currentT, dashing ? 1f : 0f, speed * Time.deltaTime);

            vignette.intensity.value = vignetteIntensity * currentT;
            lensDistortion.intensity.value = lensDistortionIntensity * currentT;
            motionBlur.intensity.value = motionBlurIntensity * currentT;

            // 파티클은 강도(currentT)가 아니라 대시 여부의 엣지로만 켜고 끈다.
            // Stop은 기본 옵션(StopEmittingAndClear 아님)이라 이미 나간 줄무늬는 수명대로 자연히 사라진다
            // — 대시가 끝나자마자 화면에서 뚝 끊기면 오히려 부자연스럽다.
            if (speedLinesParticle != null && dashing != wasDashing)
            {
                if (dashing) speedLinesParticle.Play();
                else speedLinesParticle.Stop();
            }

            wasDashing = dashing;
        }
    }
}
