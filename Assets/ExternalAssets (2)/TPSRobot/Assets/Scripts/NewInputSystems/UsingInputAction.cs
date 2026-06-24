using UnityEngine;
using UnityEngine.InputSystem;

// 인터펙터뷰에서 키를 설정하겠다.!
public class UsingInputAction : MonoBehaviour
{
    public InputAction gKey;
    public InputAction hKey;
    public InputAction axis;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        if( gKey != null )
        {
            gKey.started += GKey;
            gKey.canceled += GKey;
        }
        if( hKey != null )
        {
            hKey.started += HKey;
            hKey.canceled += HKey;
        }
        if( axis != null )
        {
            axis.started += Axis;
            axis.performed += Axis;
            axis.canceled += Axis;
        }
    }
    void OnEnable()
    {
        if (gKey != null) gKey.Enable();
        if (hKey != null) hKey.Enable();
        if (axis != null) axis.Enable();
    }
    void OnDisable()
    {
        if (gKey != null) gKey.Disable();
        if (hKey != null) hKey.Disable();
        if (axis != null) axis.Disable();
    }
    public void GKey(InputAction.CallbackContext context)
    {
        print(context.phase);
    }
    public void HKey(InputAction.CallbackContext context)
    {
        print(context.phase);
    }
    public void Axis(InputAction.CallbackContext context)
    {
        print("Axis : " + context.ReadValue<Vector2>());
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
