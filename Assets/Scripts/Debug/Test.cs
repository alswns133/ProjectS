using UnityEngine;
using UnityEngine.InputSystem;
using ProjectS.Events;

namespace ProjectS.Debugging
{
    /// <summary>HUD 게이지 확인용 임시 테스트. 입력 a로 HP, s로 SG를 1씩 깎아 PlayerEvents로 발행한다.</summary>
    public class Test : MonoBehaviour
    {
        /// <summary>누를 때마다 HP를 1 깎는 테스트 입력.</summary>
        public InputAction a;

        /// <summary>누를 때마다 SG를 1 깎는 테스트 입력.</summary>
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
}
