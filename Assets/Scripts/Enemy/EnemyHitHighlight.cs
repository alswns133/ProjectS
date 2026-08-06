using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 몬스터가 피격당한 순간 하이라이트(윤곽선/발광)를 잠깐 켰다가 끈다.
    /// EnemyStats가 데미지를 실제로 적용할 때 <see cref="Flash"/>를 호출한다.
    ///
    /// <b>왜 HighlightEffect를 직접 참조하지 않는가</b>: 윤곽선 에셋(HighlightPlus)은
    /// ExternalAssets 아래에 있고 .gitignore로 깃에서 제외된다. 여기서 직접 참조하면
    /// 에셋이 없는 환경(CI, 에셋 미보유 팀원)에서 컴파일이 깨진다(NpcOutlineTrigger와 같은 이유).
    /// 그래서 켜고 끄는 신호만 UnityEvent&lt;bool&gt;로 내보내고,
    /// 인스펙터에서 <b>부모의</b> HighlightEffect.SetHighlighted(bool)를 Dynamic bool로 연결한다.
    /// 나중에 다른 연출(셰이더 등)로 갈아탈 때도 코드를 안 고쳐도 된다.
    /// </summary>
    public class EnemyHitHighlight : MonoBehaviour
    {
        /// <summary>
        /// 인스펙터에 노출하기 위한 구체 이벤트 타입.
        /// UnityEvent&lt;T&gt;는 제네릭 그대로는 직렬화되지 않아 상속받은 클래스가 필요하다.
        /// </summary>
        [Serializable]
        public class BoolEvent : UnityEvent<bool> { }

        [Header("피격 하이라이트")]
        // 인스펙터에서 부모의 HighlightEffect.SetHighlighted(bool)를 Dynamic bool로 연결한다.
        // 피격 순간 true, highlightDuration 뒤 false가 그대로 전달된다.
        [SerializeField] private BoolEvent onHighlightChanged = new BoolEvent();

        // 하이라이트가 켜져 있는 시간. 피격 '번쩍임'이라 아주 짧게 둔다.
        [SerializeField, Min(0f)] private float highlightDuration = 0.12f;

        // 진행 중인 번쩍임 코루틴. 연타로 다시 맞으면 새로 시작해 지속 시간을 갱신한다.
        private Coroutine flashRoutine;

        /// <summary>
        /// 하이라이트를 켜고 <see cref="highlightDuration"/> 뒤에 끈다.
        /// 이미 켜져 있는 중에 다시 호출되면 타이머를 처음부터 다시 잰다(연타 대응).
        /// </summary>
        public void Flash()
        {
            // 비활성 상태에서는 코루틴을 돌릴 수 없다. 사망 직전 마지막 타격 등에서 안전하게 무시.
            if (!isActiveAndEnabled) return;

            if (flashRoutine != null) StopCoroutine(flashRoutine);
            flashRoutine = StartCoroutine(FlashRoutine());
        }

        private IEnumerator FlashRoutine()
        {
            onHighlightChanged?.Invoke(true);
            yield return new WaitForSeconds(highlightDuration);
            onHighlightChanged?.Invoke(false);
            flashRoutine = null;
        }

        // 켜진 채로 비활성화/파괴되면 다음에 다시 켤 때 남아 있을 수 있어 확실히 꺼 둔다.
        // (사망 시 DeadState가 오브젝트를 정리하거나, 켜진 상태로 풀에 반납되는 경우)
        private void OnDisable()
        {
            if (flashRoutine != null)
            {
                StopCoroutine(flashRoutine);
                flashRoutine = null;
            }

            onHighlightChanged?.Invoke(false);
        }
    }
}
