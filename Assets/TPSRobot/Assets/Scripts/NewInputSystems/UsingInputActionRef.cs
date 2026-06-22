using UnityEngine;
using UnityEngine.InputSystem;

// XRToolkit에서 이러한 방식을 많이 사용합니다.
// 외부에서 이미 선언되어 있는 키의 정보를 받아오기 위한 목적으로 사용합니다.
public class UsingInputActionRef : MonoBehaviour
{
    
    public InputActionReference gKeyRef;
    public InputActionReference hKeyRef;
    public InputActionReference axisRef;

    void Awake()
    {
        if (gKeyRef != null)
        {
            gKeyRef.action.started += GKey;
            gKeyRef.action.canceled += GKey;
        }
        if (hKeyRef != null)
        {
            hKeyRef.action.started += HKey;
            hKeyRef.action.canceled += HKey;
        }
        if (axisRef != null)
        {
            axisRef.action.started += Axis;
            axisRef.action.performed += Axis;
            axisRef.action.canceled += Axis;
        }
    }
    private void OnEnable()
    {
        if( gKeyRef != null ) gKeyRef.action.Enable();
        if (hKeyRef != null) hKeyRef.action.Enable();
        if (axisRef != null) axisRef.action.Enable();
    }
    private void OnDisable()
    {
        if (gKeyRef != null) gKeyRef.action.Disable();
        if (hKeyRef != null) hKeyRef.action.Disable();
        if (axisRef != null) axisRef.action.Disable();
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
