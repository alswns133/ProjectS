using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class HUDPresenter : BasePresenter
{
    [SerializeField] private HUDPanel _view;

    private void Awake()
    {
        _view = GetComponent<HUDPanel>();
    }

    protected override void Subscribe()
    {
        PlayerEvents.OnHpChanged += OnHpChanged;
        PlayerEvents.OnSGChanged += OnSgChanged;
        PlayerEvents.OnStaminaChanged += OnStaminaChanged;
        PlayerEvents.OnExpChanged += OnExpChanged;
    }

    protected override void Unsubscribe()
    {
        PlayerEvents.OnHpChanged -= OnHpChanged;
        PlayerEvents.OnSGChanged -= OnSgChanged;
        PlayerEvents.OnStaminaChanged -= OnStaminaChanged;
        PlayerEvents.OnExpChanged -= OnExpChanged;
    }

    // 이벤트 받아서 가공 후 View한테 전달
    private void OnHpChanged(float cur, float max)
        => _view.SetHp(cur / max);         // 비율 계산은 P가!

    private void OnSgChanged(float cur, float max)
        => _view.SetSg(cur / max);

    private void OnStaminaChanged(float cur, float max)
        => _view.SetStamina(cur / max);

    private void OnExpChanged(int cur, int max)
        => _view.SetExp((float)cur / max);
}
