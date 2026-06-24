using UnityEngine;

public class InGame : BaseScene
{
    public override void Enter()
    {
        UIManager.Instance.ShowPanel<HUDPanel>();

        PlayerEvents.FireHpChanged(100, 100);
        PlayerEvents.FireSgChanged(50, 50);
        PlayerEvents.FireExpChanged(0, 100);
        PlayerEvents.FireGoldChanged(50000);
        PlayerEvents.FireLevelUp(5);
    }

    public override void Exit()
    {

    }

    public override void Initialize()
    {

    }

    public override void Progress(float progress)
    {
        
    }
}
