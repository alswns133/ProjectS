using UnityEngine;
using UnityEngine.InputSystem;

public class MouseEventTest : MonoBehaviour
{
    // 직접 InputAction을 설정할 때는 Add binding메뉴를 선택한 후  Mouse/Position 메뉴를 선택하시면 됩니다.
    public InputAction mousePos;
    // InputAction을 사용하는 일반적인 방식으로 처리합니다.
    void Awake()
    {
        if( mousePos != null )
        {
            mousePos.started += OnMousePos;
            mousePos.performed += OnMousePos;
            mousePos.canceled += OnMousePos;
        }
    }
    private void OnEnable()
    {
        if (mousePos != null) mousePos.Enable();
    }
    private void OnDisable()
    {
        if (mousePos != null) mousePos.Disable();
    }

    public void OnMousePos(InputAction.CallbackContext context)
    {
        print( context.ReadValue<Vector2>() );
    }
    

    // 아래쪽 예제는 보편적으로 NewInputSystem에서 마우스의 위치를 가져와서 사용하는 예제입니다.
    //void Update()
    //{
    //    // 현재 마우스의 위치
    //    //print(Mouse.current.position.ReadValue() );

    //    // 휠 버튼을 스크롤할때의 값입니다.
    //    print(Mouse.current.scroll.ReadValue());

    //    // Mouse.current.rightButton.wasPressedThisFrame 
    //    if ( Mouse.current.leftButton.wasPressedThisFrame )
    //    {
    //        Vector2 mousePos = Mouse.current.position.ReadValue();
    //        print("Mouse Position : " + mousePos);
    //    }
        
    //}
}
