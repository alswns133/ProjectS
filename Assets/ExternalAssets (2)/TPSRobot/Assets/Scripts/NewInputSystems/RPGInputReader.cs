using UnityEngine;
using UnityEngine.InputSystem;

// 첫번째 InputAction 사용하는 방식
// 1) InputActionAsset을 만든다.
// 2) InputActionAsset에 키를 정의한다.
// 3) InputActionAsset을 클릭한 후에 인스펙터뷰에서 클래스를 생성한다. ( 이 클래스 내부에는 인터페이스가 하나 존재한다. )
// 4) 사용자 정의 클래스를 만들고, InputActionAsset의 인터페이스를 상속받는다.
// 5) 인터페이스에 대한 메서드를 정의합니다. ( 정의해줍니다. )
// 6) InputActionAsset에 메서드가 정의된 클래스를 연결해줍니다.
public class RPGInputReader : MonoBehaviour, RPGInput.IRPGActions
{
    RPGInput rpgInput;

    public void OnAxis(InputAction.CallbackContext context)
    {
        print(context.phase);
        print("Axis : " + context.ReadValue<Vector2>() );
    }

    public void OnBAxis(InputAction.CallbackContext context)
    {
        print("OnBAxis : " + context.ReadValue<float>());
    }

    public void OnCAxis(InputAction.CallbackContext context)
    {
        print("OnCAxis : " + context.ReadValue<float>());
    }

    public void OnGKey(InputAction.CallbackContext context)
    {
        print("OnGKey : " + context.ReadValue<float>());
    }

    public void OnHKey(InputAction.CallbackContext context)
    {
        print("OnHKey : " + context.ReadValue<float>());
    }

    public void OnJKey(InputAction.CallbackContext context)
    {
       
    }

    public void OnVAxis(InputAction.CallbackContext context)
    {
        
    }

    void Awake()
    {
        rpgInput = new RPGInput();
        // SetCallbacks 이라는 메서드가 호출되지 않으면 키가 동작하지 않습니다.
        rpgInput.RPG.SetCallbacks(this);
    }
    void OnEnable()
    {
        rpgInput.Enable();
    }
    void OnDisable()
    {
        rpgInput.Disable();
    }
}
