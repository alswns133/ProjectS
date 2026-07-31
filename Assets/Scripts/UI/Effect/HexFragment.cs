using System;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 카드 분해 연출에서 흩어지는 육각형 파편 하나. 스폰될 때 받은 속도로 날아가며 회전·감속·페이드하고,
    /// 수명이 끝나면 스포너로 스스로를 반환한다(<see cref="HexFragmentSpawner"/>).
    ///
    /// HUD 캔버스가 Screen Space Overlay라 파티클 시스템을 UI 위에 겹쳐 그릴 수 없다.
    /// 그래서 파편은 파티클이 아니라 UI Graphic이고, 이동·회전·페이드를 직접 계산한다.
    /// 일시정지(timeScale 0) 중에도 HUD 연출은 돌아야 하므로 unscaled 시간을 쓴다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(Image))]
    public class HexFragment : MonoBehaviour
    {
        [Tooltip("초당 감속 비율. 0이면 등속, 클수록 빨리 느려진다.")]
        [SerializeField] private float drag = 1.2f;

        [Tooltip("수명 대비 알파(0=스폰 직후, 1=소멸 직전).")]
        [SerializeField] private AnimationCurve alphaOverLife = AnimationCurve.Linear(0f, 1f, 1f, 0f);

        [Tooltip("수명 대비 크기 배율. 끝으로 갈수록 줄이면 빨려 들어가는 느낌이 난다.")]
        [SerializeField] private AnimationCurve scaleOverLife = AnimationCurve.Linear(0f, 1f, 1f, 0.4f);

        private RectTransform rect;
        private Graphic graphic;

        private Vector2 velocity;
        private float angularSpeed;
        private float lifetime;
        private float age;
        private Color baseColor;
        private Action<HexFragment> onFinished;

        private void Awake()
        {
            rect = (RectTransform)transform;
            graphic = GetComponent<Graphic>();
        }

        /// <summary>
        /// 파편을 스폰 상태로 초기화하고 날려 보낸다. 풀에서 꺼낸 직후 호출한다.
        /// </summary>
        /// <param name="localPosition">스포너 레이어 기준 시작 위치</param>
        /// <param name="size">한 변이 아닌 사각 경계 크기(px)</param>
        /// <param name="tint">파편 색(홀로그램 컨셉이라 보통 완료 색과 같은 녹색)</param>
        /// <param name="startVelocity">초기 속도(px/초). 왼쪽으로 흐르게 하려면 x가 음수</param>
        /// <param name="angular">초당 회전 각도</param>
        /// <param name="life">수명(초)</param>
        /// <param name="finished">수명이 끝났을 때 호출(스포너의 풀 반환)</param>
        public void Play(Vector2 localPosition, float size, Color tint, Vector2 startVelocity,
                         float angular, float life, Action<HexFragment> finished)
        {
            if (rect == null) rect = (RectTransform)transform;
            if (graphic == null) graphic = GetComponent<Graphic>();

            rect.anchoredPosition = localPosition;
            rect.sizeDelta = new Vector2(size, size);
            rect.localScale = Vector3.one;
            // 조각마다 각도가 달라야 부서진 파편처럼 보인다. 정렬된 육각형은 패턴으로 읽힌다.
            rect.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f));

            velocity = startVelocity;
            angularSpeed = angular;
            lifetime = Mathf.Max(0.01f, life);
            age = 0f;
            baseColor = tint;
            onFinished = finished;

            if (graphic != null) graphic.color = baseColor;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            age += dt;

            float t = age / lifetime;
            if (t >= 1f)
            {
                gameObject.SetActive(false);

                // 콜백을 비운 뒤 호출한다 — 풀에 돌아간 파편이 다시 반환을 시도하지 않게.
                Action<HexFragment> finished = onFinished;
                onFinished = null;
                finished?.Invoke(this);
                return;
            }

            rect.anchoredPosition += velocity * dt;
            velocity *= Mathf.Clamp01(1f - drag * dt);
            rect.localRotation *= Quaternion.Euler(0f, 0f, angularSpeed * dt);
            rect.localScale = Vector3.one * scaleOverLife.Evaluate(t);

            if (graphic != null)
            {
                Color c = baseColor;
                c.a = baseColor.a * alphaOverLife.Evaluate(t);
                graphic.color = c;
            }
        }
    }
}
