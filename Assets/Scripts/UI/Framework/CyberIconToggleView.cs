using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI.Framework
{
    /// <summary>
    /// 사운드 뮤트 등 On/Off 상태를 아이콘 스프라이트/컬러 스왑으로 표현하는 토글 뷰.
    /// 상태가 바뀌는 순간 아이콘에 짧은 스케일 펀치 연출을 준다.
    /// </summary>
    /// <remarks>
    /// - 순수 View: 실제 뮤트 처리는 OnToggled를 구독하는 쪽(Presenter/설정 팝업)에서 수행
    ///   ex) view.OnToggled += on => SoundManager.Instance.SetSfxMute(!on);
    /// - 설정 로드 등 이벤트 발화 없이 상태만 맞출 때는 SetWithoutNotify 사용
    /// - 2026-07-20 xogk2222 작성
    /// </remarks>
    public class CyberIconToggleView : MonoBehaviour
    {
        [SerializeField] private Toggle toggle;
        [SerializeField] private Image icon;
        [SerializeField] private Sprite soundOnSprite;   // 음표
        [SerializeField] private Sprite soundOffSprite;  // 뮤트 음표
        [SerializeField] private Color onColor = Color.white;
        [SerializeField] private Color offColor = new Color(1f, 0.24f, 0.36f); // #FF3D5C
        [SerializeField] private float punchScale = 1.15f;
        [SerializeField] private float punchDuration = 0.12f;

        /// <summary>
        /// 사용자 조작으로 토글 상태가 바뀔 때 발행. SetWithoutNotify로 바꿀 때는 발행되지 않는다.
        /// </summary>
        public event Action<bool> OnToggled;

        private Coroutine anim;

        private void Awake()
        {
            Apply(toggle.isOn, instant: true);
        }

        private void OnEnable()
        {
            toggle.onValueChanged.AddListener(HandleChanged);
        }

        private void OnDisable()
        {
            toggle.onValueChanged.RemoveListener(HandleChanged);

            // 펀치 연출 도중 비활성화되면 코루틴이 끊겨 스케일이 커진 채 남으므로 원복
            // (2026-07-20 xogk2222 수정)
            if (anim != null)
            {
                StopCoroutine(anim);
                anim = null;
            }
            icon.rectTransform.localScale = Vector3.one;
        }

        /// <summary>
        /// OnToggled 발화 없이 토글 상태와 아이콘을 맞춘다. 설정 로드 시 사용.
        /// </summary>
        /// <param name="on">토글 상태 (true = 사운드 켜짐)</param>
        public void SetWithoutNotify(bool on)
        {
            toggle.SetIsOnWithoutNotify(on);
            Apply(on, instant: true);
        }

        private void HandleChanged(bool on)
        {
            Apply(on, instant: false);
            OnToggled?.Invoke(on);
        }

        private void Apply(bool on, bool instant)
        {
            icon.sprite = on ? soundOnSprite : soundOffSprite;
            icon.color = on ? onColor : offColor;

            if (instant || !gameObject.activeInHierarchy) return;
            if (anim != null) StopCoroutine(anim);
            anim = StartCoroutine(Punch());
        }

        // 스왑 순간 살짝 튀는 스케일 펀치
        private IEnumerator Punch()
        {
            var rt = icon.rectTransform;
            float t = 0f;
            while (t < punchDuration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Sin(t / punchDuration * Mathf.PI); // 0→1→0
                rt.localScale = Vector3.one * Mathf.Lerp(1f, punchScale, k);
                yield return null;
            }
            rt.localScale = Vector3.one;
        }
    }
}
