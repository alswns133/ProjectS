using UnityEngine;
using UnityEngine.InputSystem;

public class Test : MonoBehaviour
{
    public InputAction a;
    public InputAction s;
    [SerializeField] private int hp = 100;
    [SerializeField] private int mp = 50;

    private void OnEnable()
    {
        if(a != null)
        {
            a.started += TakeDamage;
            a.canceled += TakeDamage;
            a.Enable();
        }

        if (s != null)
        {
            s.started += Mp;
            s.canceled += Mp;
            s.Enable();
        }
    }

    private void OnDisable()
    {
        if (a != null)
        {
            a.started -= TakeDamage;
            a.canceled -= TakeDamage;
            a.Disable();
        }

        if (s != null)
        {
            s.started -= Mp;
            s.canceled -= Mp;
            s.Disable();
        }
    }

    private void TakeDamage(InputAction.CallbackContext context)
    {
        if(context.phase == InputActionPhase.Started)
        {
            hp--;
            PlayerEvents.FireHpChanged(hp, 100);
        }
        
    }

    private void Mp(InputAction.CallbackContext context)
    {
        if (context.phase == InputActionPhase.Started)
        {
            mp--;
            PlayerEvents.FireSgChanged(mp, 50);
        }
    }
}
