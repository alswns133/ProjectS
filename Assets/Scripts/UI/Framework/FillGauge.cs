using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[System.Serializable]   // 인스펙터에 노출 → Image/Text를 드래그로 연결
public class FillGauge
{

    [SerializeField] private Image image;
    [SerializeField] private float lerpSpeed = 1f;   // 게이지마다 속도 따로 튜닝 가능

    private MonoBehaviour runner;   // 코루틴을 대신 돌려줄 주인

    private Coroutine routine;

    private float target = 1f;       // 이 게이지만의 목표
    private float current = 1f;      // 이 게이지만의 현재값

    // 코루틴은 MonoBehaviour 위에서만 돌 수 있어서, 주인을 받아둠
    public void Init(MonoBehaviour runner)
    {
        this.runner = runner;

        Apply(current);   // ★ 시작할 때 초기값을 화면에 한 번 그려줌
    }

    public void SetRatio(float ratio)
    {
        target = Mathf.Clamp01(ratio);

        if (routine != null)
            runner.StopCoroutine(routine);

        routine = runner.StartCoroutine(LerpRoutine());
    }

    private IEnumerator LerpRoutine()
    {
        while (!Mathf.Approximately(current, target))
        {
            current = Mathf.MoveTowards(current, target, lerpSpeed * Time.deltaTime);
            Apply(current);
            yield return null;
        }

        // ★ 루프가 끝나면 target에 "정확히" 스냅 + 마지막 한 번 반영
        current = target;
        Apply(current);

        routine = null;
    }

    // 값을 실제 화면(셰이더 + 텍스트)에 꽂는 일만 담당
    private void Apply(float ratio)
    {
        image.fillAmount = ratio;
    }
}
