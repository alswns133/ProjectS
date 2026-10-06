using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI.Framework
{
    /// <summary>
    /// 스킬 슬롯 하나의 쿨타임 표시. 시작 신호(StartCooldown) 한 번만 받으면
    /// 이후 카운트다운은 자체 코루틴으로 진행한다 → 매 프레임 게임 로직을 폴링하지 않는다.
    /// FillGauge와 같은 직렬화 클래스 방식: 인스펙터에서 Image/Text를 드래그로 연결한다.
    /// </summary>
    [System.Serializable]
    public class SkillCooldownSlot
    {
        // 쿨타임 오버레이. Image Type을 Filled(Radial 360)로 두면 시계 방향으로 걷히는 연출이 된다.
        [SerializeField] private Image overlay;

        // 남은 초 텍스트. 안 쓰는 슬롯 디자인이면 비워둬도 된다(null 허용).
        [SerializeField] private TMP_Text remainText;

        private MonoBehaviour runner;   // 코루틴을 대신 돌려줄 주인(FillGauge와 동일 패턴)
        private Coroutine routine;

        // 마지막으로 시작한 쿨타임의 전체 길이. Sync로 도중부터 다시 그릴 때 게이지 비율의 분모로 쓴다.
        private float totalDuration;

        /// <summary>초기화. 쿨타임 없음 상태로 표시를 정리한다.</summary>
        public void Init(MonoBehaviour runner)
        {
            this.runner = runner;
            SetIdle();
        }

        /// <summary>
        /// 쿨타임 카운트다운을 시작한다. 진행 중에 다시 호출되면 새 시간으로 덮어쓴다.
        /// </summary>
        /// <param name="duration">쿨타임 길이(초)</param>
        public void StartCooldown(float duration)
        {
            if (overlay == null || runner == null) return;
            if (duration <= 0f) return;

            totalDuration = duration;
            Run(duration);
        }

        /// <summary>
        /// 남은 시간 기준으로 표시를 다시 맞춘다. 0 이하면 쿨타임 없음으로 정리한다.
        /// HUD가 꺼졌다 켜질 때(재도전 로딩 등) 부른다 — 꺼지는 순간 카운트다운 코루틴이 같이 죽어
        /// 게이지가 그 자리에 굳기 때문이다.
        /// </summary>
        /// <param name="remaining">남은 쿨타임(초). 판정 원천(PlayerCombat)의 값을 넘긴다.</param>
        public void Sync(float remaining)
        {
            if (remaining <= 0f)
            {
                Clear();
                return;
            }

            if (overlay == null || runner == null) return;

            // 전체 길이를 모르면(이 슬롯으로 시작한 적 없음) 남은 시간을 분모로 써서 가득 찬 게이지부터 그린다.
            if (totalDuration < remaining) totalDuration = remaining;
            Run(remaining);
        }

        private void Run(float remaining)
        {
            // 꺼지면서 이미 죽은 코루틴이어도 StopCoroutine은 무해하다.
            if (routine != null)
                runner.StopCoroutine(routine);

            routine = runner.StartCoroutine(CooldownRoutine(remaining));
        }

        /// <summary>
        /// 진행 중인 카운트다운을 멈추고 쿨타임 없음 상태로 되돌린다(마을 진입 시 쿨타임 초기화).
        /// </summary>
        public void Clear()
        {
            if (routine != null && runner != null)
                runner.StopCoroutine(routine);

            routine = null;
            SetIdle();
        }

        private IEnumerator CooldownRoutine(float remaining)
        {
            overlay.enabled = true;
            if (remainText != null) remainText.enabled = true;

            while (remaining > 0f)
            {
                overlay.fillAmount = remaining / totalDuration;

                // 1초 이상은 정수(3, 2, 1), 1초 미만은 소수 한 자리(0.9…)로 긴박감을 준다.
                if (remainText != null)
                    remainText.text = remaining >= 1f
                        ? Mathf.CeilToInt(remaining).ToString()
                        : remaining.ToString("0.0");

                yield return null;
                remaining -= Time.deltaTime;
            }

            SetIdle();
            routine = null;
        }

        // 쿨타임 없음 상태: 오버레이와 텍스트를 모두 숨긴다.
        private void SetIdle()
        {
            if (overlay != null)
            {
                overlay.fillAmount = 0f;
                overlay.enabled = false;
            }

            if (remainText != null)
            {
                remainText.text = string.Empty;
                remainText.enabled = false;
            }
        }
    }
}
