using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class HUDPresenter : BasePresenter
{
    [SerializeField] private HUDPanel _view;

    // Test 용도
    [SerializeField] private InputAction test;
    private float currnetHp = 100;

    private void Test(InputAction.CallbackContext context)
    {

        if(context.phase == InputActionPhase.Started)
        {
            currnetHp -= 10;
            PlayerEvents.FireHpChanged(currnetHp, 100);
        }
    }
    private void Awake()
    {
        _view = GetComponent<HUDPanel>();

        if(test != null) 
        {
            test.started += Test;
            test.canceled += Test;
        }
    }

    protected override void Subscribe()
    {
        PlayerEvents.OnHpChanged += OnHpChanged;
        PlayerEvents.OnSGChanged += OnSgChanged;
        PlayerEvents.OnExpChanged += OnExpChanged;

        test?.Enable();
    }

    protected override void Unsubscribe()
    {
        PlayerEvents.OnHpChanged -= OnHpChanged;
        PlayerEvents.OnSGChanged -= OnSgChanged;
        PlayerEvents.OnExpChanged -= OnExpChanged;

        test?.Disable();
    }

    // 이벤트 받아서 가공 후 View한테 전달
    private void OnHpChanged(float cur, float max)
        => _view.SetHp(cur / max);         // 비율 계산은 P가!

    private void OnSgChanged(float cur, float max)
        => _view.SetSg(cur / max);

    private void OnExpChanged(int cur, int max)
        => _view.SetExp(cur/max);
}
