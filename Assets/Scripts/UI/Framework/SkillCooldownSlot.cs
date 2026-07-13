using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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

        if (routine != null)
            runner.StopCoroutine(routine);

        routine = runner.StartCoroutine(CooldownRoutine(duration));
    }

    private IEnumerator CooldownRoutine(float duration)
    {
        float remaining = duration;
        overlay.enabled = true;
        if (remainText != null) remainText.enabled = true;

        while (remaining > 0f)
        {
            overlay.fillAmount = remaining / duration;

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
