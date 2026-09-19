using System;
using System.Collections;
using UnityEngine;

namespace ProjectS.UI
{
    /// <summary>
    /// 캐릭터 선택 → 게임 씬 로딩 사이의 짧은 연출. 스테이지 카메라가 목표 각도로 돌아간 뒤
    /// 문 두 짝이 열리고, 끝나면 콜백으로 로딩을 넘긴다.
    /// 간단한 1회성 연출이라 Timeline/Animator 대신 코드 보간으로 처리한다(ModelFreeView에 부착).
    /// 작성: TH
    /// </summary>
    public class CharacterStartTransition : MonoBehaviour
    {
        [Serializable]
        private class DoorMotion
        {
            [Tooltip("움직일 문 오브젝트")]
            public Transform target;

            [Tooltip("현재 로컬 위치 기준 이동량(xyz, 부모 로컬 좌표계)")]
            public Vector3 offset = new Vector3(0f, 0f, 1f);

            [Tooltip("이동에 걸리는 시간(초). 작을수록 빠르다")]
            [Min(0.01f)] public float duration = 1f;

            [Tooltip("이동 진행 커브(가로 0~1 = 시간, 세로 0~1 = 이동 비율)")]
            public AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

            [Tooltip("문 연출 시작 시점 기준 추가 지연(초). 두 짝을 엇갈리게 열 때 사용")]
            [Min(0f)] public float delay;
        }

        [Header("카메라")]
        [SerializeField] private Transform stageCamera;

        [Tooltip("최종 로컬 회전 각도(오일러). 기본은 X축 -45도")]
        [SerializeField] private Vector3 cameraTargetEuler = new Vector3(-45f, 0f, 0f);

        [Tooltip("회전에 걸리는 시간(초). 작을수록 빠르다")]
        [SerializeField, Min(0.01f)] private float cameraDuration = 1f;

        [Tooltip("회전 진행 커브(가로 0~1 = 시간, 세로 0~1 = 회전 비율)")]
        [SerializeField] private AnimationCurve cameraCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("문")]
        [SerializeField] private DoorMotion leftDoor = new DoorMotion();
        [SerializeField] private DoorMotion rightDoor = new DoorMotion();

        [Tooltip("카메라 회전이 끝난 뒤 문이 움직이기 시작할 때까지의 대기(초)")]
        [SerializeField, Min(0f)] private float doorStartDelay;

        [Header("마무리")]
        [Tooltip("문이 다 열린 뒤 로딩으로 넘어가기 전 대기(초)")]
        [SerializeField, Min(0f)] private float holdBeforeLoad = 0.2f;

        /// <summary>연출 재생 중 여부. 재생 중 중복 시작·뒤로가기를 막는 데 쓴다.</summary>
        public bool IsPlaying { get; private set; }

        /// <summary>
        /// 연출을 재생하고 끝나면 <paramref name="onComplete"/>를 호출한다(여기서 씬 로딩을 넘긴다).
        /// 이미 재생 중이면 무시한다.
        /// </summary>
        public void Play(Action onComplete)
        {
            if (IsPlaying) return;
            StartCoroutine(PlayRoutine(onComplete));
        }

        private IEnumerator PlayRoutine(Action onComplete)
        {
            IsPlaying = true;

            if (stageCamera != null)
            {
                Quaternion from = stageCamera.localRotation;
                Quaternion to = Quaternion.Euler(cameraTargetEuler);
                yield return Tween(cameraDuration, cameraCurve,
                    t => stageCamera.localRotation = Quaternion.SlerpUnclamped(from, to, t));
            }

            if (doorStartDelay > 0f) yield return new WaitForSeconds(doorStartDelay);

            // 두 짝은 동시에 움직이고, 더 늦게 끝나는 쪽까지 기다린다.
            Coroutine left = StartCoroutine(MoveDoor(leftDoor));
            Coroutine right = StartCoroutine(MoveDoor(rightDoor));
            yield return left;
            yield return right;

            if (holdBeforeLoad > 0f) yield return new WaitForSeconds(holdBeforeLoad);

            IsPlaying = false;
            onComplete?.Invoke();
        }

        private IEnumerator MoveDoor(DoorMotion door)
        {
            if (door == null || door.target == null) yield break;
            if (door.delay > 0f) yield return new WaitForSeconds(door.delay);

            Vector3 from = door.target.localPosition;
            Vector3 to = from + door.offset;
            yield return Tween(door.duration, door.curve,
                t => door.target.localPosition = Vector3.LerpUnclamped(from, to, t));
        }

        // duration 동안 0→1 진행도를 커브에 통과시켜 apply에 넘긴다. 마지막 프레임은 커브 끝값으로 정확히 맞춘다.
        private static IEnumerator Tween(float duration, AnimationCurve curve, Action<float> apply)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float n = Mathf.Clamp01(elapsed / duration);
                apply(curve != null ? curve.Evaluate(n) : n);
                yield return null;
            }
            apply(curve != null ? curve.Evaluate(1f) : 1f);
        }
    }
}
