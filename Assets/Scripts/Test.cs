using UnityEngine;
using UnityEngine.InputSystem;

public class Test : MonoBehaviour
{
    public InputAction a;
    public InputAction s;
    [SerializeField] private int hp = 100;
    [SerializeField] private int mp = 50;

    private void Start()
    {
        if(a != null)
        {
            a.started += TakeDamage;
            a.canceled += TakeDamage;
        }

        if (s != null)
        {
            s.started += Mp;
            s.canceled += Mp;
        }
    }

    private void OnEnable()
    {
        a?.Enable();
        s?.Enable();
    }

    private void OnDisable()
    {
        a?.Disable();
        s?.Disable();
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
