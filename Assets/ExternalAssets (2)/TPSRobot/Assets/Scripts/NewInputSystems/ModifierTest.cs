using UnityEngine;
using UnityEngine.InputSystem;

// LeftCtrl + A => One Modifier
// Ctrl + Left Shift + V => Two Modifier
public class ModifierTest : MonoBehaviour
{
    public InputAction oneModifier;
    public InputAction twoModifier;
    void Awake()
    {
        if (oneModifier != null)
        {
            oneModifier.started += OnOneModifier;
            oneModifier.performed += OnOneModifier;
            oneModifier.canceled += OnOneModifier;
        }
        if( twoModifier != null )
        {
            twoModifier.started += OnTwoModifier;
            twoModifier.performed += OnTwoModifier;
            twoModifier.canceled += OnTwoModifier;
        }
    }
    private void OnEnable()
    {
        if (oneModifier != null) oneModifier.Enable();
        if (twoModifier != null) twoModifier.Enable();
    }
    private void OnDisable()
    {
        if (oneModifier != null) oneModifier.Disable();
        if (twoModifier != null) twoModifier.Disable();
    }

    public void OnOneModifier(InputAction.CallbackContext context)
    {
        print(context.phase);
    }
    public void OnTwoModifier(InputAction.CallbackContext context)
    {
        print(context.phase);
    }
}
