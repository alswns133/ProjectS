using UnityEngine;

public class HudPresenter : BasePresenter
{
    [SerializeField] private HUDPanel _view;

    private void Awake()
    {
        _view = GetComponent<HUDPanel>();
    }

    protected override void Subscribe()
    {
        PlayerEvents.OnHpChanged += OnHpChanged;
        PlayerEvents.OnSGChanged += OnMpChanged;
        PlayerEvents.OnLevelUp += OnLevelUp;
        PlayerEvents.OnGoldChanged += OnGoldChanged;
    }

    protected override void Unsubscribe()
    {
        PlayerEvents.OnHpChanged -= OnHpChanged;
        PlayerEvents.OnSGChanged -= OnMpChanged;
        PlayerEvents.OnLevelUp -= OnLevelUp;
        PlayerEvents.OnGoldChanged -= OnGoldChanged;
    }

    // 이벤트 받아서 가공 후 View한테 전달
    private void OnHpChanged(float cur, float max)
        => _view.SetHp(cur / max);         // 비율 계산은 P가!

    private void OnMpChanged(float cur, float max)
        => _view.SetMp(cur / max);

    private void OnLevelUp(int level)
        => _view.SetLevel(level);

    private void OnGoldChanged(int gold)
        => _view.SetGold(gold);
}
