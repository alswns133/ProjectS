using UnityEngine;

public abstract class BasePresenter : MonoBehaviour
{
    /// <summary>
    /// 패널이 열릴 때 이벤트 구독
    /// </summary>
    protected abstract void Subscribe();

    /// <summary>
    /// 패널이 닫힐 때 이벤트 해제
    /// </summary>
    protected abstract void Unsubscribe();

    private void OnEnable() => Subscribe();
    private void OnDisable() => Unsubscribe();
}
