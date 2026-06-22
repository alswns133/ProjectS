using System;
using UnityEngine;

public class HUDPresenter1 : BasePresenter
{
    [SerializeField] private HUD view;

    private void Awake()
    {
        view = GetComponent<HUD>();
    }

    protected override void Subscribe()
    {
        PlayerEvents.OnHpChanged += OnHpChanged;
        PlayerEvents.OnSGChanged += OnSGChanged;
    }

    protected override void Unsubscribe()
    {
        PlayerEvents.OnHpChanged -= OnHpChanged;
        PlayerEvents.OnSGChanged -= OnSGChanged;
    }

    private void OnHpChanged(float cur, float max) => view.SetHP(cur / max);

    private void OnSGChanged(float cur, float max) => view.SetSG(cur / max);
}
