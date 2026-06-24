using UnityEngine;
using UnityEngine.InputSystem;

public class RPGInputReaderOther : MonoBehaviour
{
    private RPGInput rpgInput;
    public Vector2 inputDirection;
    void Awake()
    {
        rpgInput = new RPGInput();
        rpgInput.RPG.Axis.started += OnAxis;
        rpgInput.RPG.Axis.performed += OnAxis;
        rpgInput.RPG.Axis.canceled += OnAxis;
    }
    private void OnEnable()
    {
        rpgInput.RPG.Axis.Enable();
    }
    private void OnDisable()
    {
        rpgInput.RPG.Axis.Disable();
    } 
    // 현재 키가 입력되거나, 업데이트가 되었을때 한번씩만 호출되는 메서드입니다.
    public void OnAxis(InputAction.CallbackContext context)
    {
        print("Axis : " + context.ReadValue<Vector2>());
        inputDirection = context.ReadValue<Vector2>();
    }
    public void Update()
    {
        // 얻어온 값으로 이동처리하시면 됩니다.
        print(inputDirection);
    }

 
}
