using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputSendMessage : MonoBehaviour
{
    public void OnGKey()
    {
        print("G키 입력");
    }
    public void OnHKey()
    {
        print("H키 입력");
    }
    public void OnAxis(InputValue value)
    {
        print($"{value.Get<Vector2>()}");
    }
}
