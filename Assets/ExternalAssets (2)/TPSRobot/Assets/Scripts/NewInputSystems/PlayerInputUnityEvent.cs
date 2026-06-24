using UnityEngine;
using UnityEngine.InputSystem;


public class PlayerInputUnityEvent : MonoBehaviour
{
    // InputAsset 파일과 동일하게 메서드를 선언해야 합니다.
    public void OnAxis(InputAction.CallbackContext context)
    {
        print("OnAxis : " + context.ReadValue<Vector2>());
    }
    public void OnGKey(InputAction.CallbackContext context)
    {
        print("OnGKey : " + context.ReadValue<float>());
    }
    public void OnHKey(InputAction.CallbackContext context)
    {
        print("OnHKey : " + context.ReadValue<float>());
    }
}
