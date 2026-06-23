using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[System.Serializable]   // 인스펙터에 노출 → Image/Text를 드래그로 연결
public class FillGauge
{
    private static readonly int FillAmountID = Shader.PropertyToID("_FillAmount");

    [SerializeField] private Image _image;
    [SerializeField] private TMP_Text _text;
    [SerializeField] private float _lerpSpeed = 1f;   // 게이지마다 속도 따로 튜닝 가능

    private MonoBehaviour _runner;   // 코루틴을 대신 돌려줄 주인
    private Material _material;
    private Coroutine _routine;

    private float _target = 1f;       // 이 게이지만의 목표
    private float _current = 1f;      // 이 게이지만의 현재값

    // 코루틴은 MonoBehaviour 위에서만 돌 수 있어서, 주인을 받아둠
    public void Init(MonoBehaviour runner)
    {
        _runner = runner;
        _material = _image.material;

        Apply(_current);   // ★ 시작할 때 초기값을 화면에 한 번 그려줌
    }

    public void SetRatio(float ratio)
    {
        _target = Mathf.Clamp01(ratio);

        if (_routine != null)
            _runner.StopCoroutine(_routine);

        _routine = _runner.StartCoroutine(LerpRoutine());
    }

    private IEnumerator LerpRoutine()
    {
        while (!Mathf.Approximately(_current, _target))
        {
            _current = Mathf.MoveTowards(_current, _target, _lerpSpeed * Time.deltaTime);
            Apply(_current);
            yield return null;
        }

        // ★ 루프가 끝나면 target에 "정확히" 스냅 + 마지막 한 번 반영
        _current = _target;
        Apply(_current);

        _routine = null;
    }

    // 값을 실제 화면(셰이더 + 텍스트)에 꽂는 일만 담당
    private void Apply(float ratio)
    {
        _material.SetFloat(FillAmountID, ratio);
        _text.text = $"{Mathf.RoundToInt(ratio * 100)}%";
    }
}