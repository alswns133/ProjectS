using System;
using TMPro;
using UnityEngine;

namespace ProjectS.UI
{
    /// <summary>
    /// 하나의 월드 공간 데미지 텍스트.
    /// 생성/삭제를 반복하지 않고 Spawner의 풀에서 꺼내 재사용된다.
    /// </summary>
    public class DamageText : MonoBehaviour
    {
        [SerializeField] private float floatSpeed = 1.5f;
        [SerializeField] private float lifetime = 0.8f;

        private TMP_Text label;
        private Transform cam;

        // 수명이 끝났을 때 자신을 풀로 돌려보내기 위해 Spawner가 넘겨주는 콜백.
        private Action<DamageText> returnToPool;
        private float elapsed;
        private Color baseColor;

        private void Awake()
        {
            label = GetComponentInChildren<TMP_Text>();
        }

        /// <summary>
        /// 텍스트를 띄운다. 표시 문자열과 색은 스포너가 종류(일반/치명타/플레이어 피격)에 따라 정해서 넘긴다.
        /// </summary>
        /// <param name="text">표시할 문자열. 치명타면 "CRITICAL"처럼 라벨이 함께 들어있다.</param>
        /// <param name="position">띄울 월드 좌표</param>
        /// <param name="color">글자 색. 페이드 아웃의 기준색으로도 쓰인다.</param>
        /// <param name="onFinished">수명 종료 시 풀 반환 콜백</param>
        public void Show(string text, Vector3 position, Color color, Action<DamageText> onFinished)
        {
            // 풀에서 재사용되므로 이전 사용 흔적을 모두 초기화한다.
            returnToPool = onFinished;
            transform.position = position;
            label.text = text;

            // 프리팹 색이 아니라 이번 호출의 색을 기준으로 페이드한다.
            // 풀에서 재사용될 때 이전 타격의 색이 남지 않게 매번 덮어쓴다.
            baseColor = color;
            label.color = baseColor;
            elapsed = 0f;

            if (cam == null && Camera.main != null)
                cam = Camera.main.transform;

            gameObject.SetActive(true);
        }

        private void Update()
        {
            elapsed += Time.deltaTime;

            // 화면 위로 떠오르는 연출. 실제 UI 좌표가 아니라 월드 좌표를 직접 움직인다.
            transform.position += Vector3.up * (floatSpeed * Time.deltaTime);

            Color c = baseColor;
            c.a = baseColor.a * (1f - elapsed / lifetime);
            label.color = c;

            if (cam != null)
                // 카메라 회전을 복사해서 어느 방향에서 봐도 텍스트가 읽히게 한다.
                transform.rotation = cam.rotation;

            if (elapsed < lifetime) return;

            gameObject.SetActive(false);
            returnToPool?.Invoke(this);

            // 풀에 들어간 뒤 중복 반환되는 상황을 막기 위해 콜백 참조를 비운다.
            returnToPool = null;
        }
    }
}
