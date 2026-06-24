using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using Unity.Cinemachine;
using System;

public class Player : MonoBehaviour
{
    public RuntimeAnimatorController stander;
    public Animator animator;
    public WeaponManager weaponManager;
    public InputAction pickupAction;
    public InputAction fireAction;
    public InputAction cameraAction;
    public List<InputAction> weaponKeyList;
    public bool pressFire = false;

    public CinemachineCamera aim;
    public CinemachineCamera freelook;
    private int aimCameraOrder = 20;
    public FreeLookMovement freelookMovement;


    void Start()
    {
       
        freelookMovement = GetComponent<FreeLookMovement>();
        animator.runtimeAnimatorController = stander;
        if (weaponManager) weaponManager.Initialize();
        

        // G 키를 입력했을 때 호출될 메서드 연결
        if (pickupAction != null)
        {
            pickupAction.started += OnPickup;
            pickupAction.canceled += OnPickup;
        }
        // 마우스 왼쪽 버튼이 눌려진 상태일 때 호출될 메서드를 연결
        if (fireAction != null)
        {
            fireAction.started += OnFire;
            fireAction.canceled += OnFire;
        }

        if (weaponKeyList != null)
        {
            foreach (var key in weaponKeyList)
            {
                key.started += OnSwitch;
                key.canceled += OnSwitch;
            }
        }

        if (cameraAction != null)
        {
            cameraAction.started += OnAim;
            cameraAction.canceled += OnAim;
        }
    }

    void OnEnable()
    {
        if (fireAction != null) fireAction.Enable();
        if (pickupAction != null) pickupAction.Enable();
        if (cameraAction != null) cameraAction.Enable();
        if (weaponKeyList != null)
        {
            foreach (var key in weaponKeyList)
                key.Enable();
        }
    }

    void OnDisable()
    {
        if (fireAction != null) fireAction.Disable();
        if (pickupAction != null) pickupAction.Disable();
        if (cameraAction != null) cameraAction.Disable();
        if (weaponKeyList != null)
        {
            foreach (var key in weaponKeyList)
                key.Disable();
        }
    }

    // 애니메이션에서 호출되는 메서드
    void FireEvent()
    {
        weaponManager?.BulletShot();
    }

    public void OnSwitch(InputAction.CallbackContext context)
    {
        if (context.phase == InputActionPhase.Started)
        {
            // 현재 바인딩 된 키 값을 얻어오는 메서드
            string weaponKey = context.action.GetBindingDisplayString();
            // 무기를 가지고 있다면 해당 키에 해당하는 무기로 변경
            weaponManager.PressSwitch(weaponKey);

        }
    }

    // 마우스 좌클릭 시 호출되는 메서드
    public void OnFire(InputAction.CallbackContext context)
    {
        if (context.phase == InputActionPhase.Started)
            pressFire = true;
        else if (context.phase == InputActionPhase.Canceled)
            pressFire = false;
    }

    public void OnPickup(InputAction.CallbackContext context)
    {
        // 픽업 애니메이션 실행
        // 애니메이션의 모션에서 특정 시점에 PickupItem 호출
        if (context.phase == InputActionPhase.Started)
            PickupItem();
    }

    // 키가 입력되면 Aim 모드로 변경, 키가 입력되지 않으면 0으로 변경
    public void OnAim(InputAction.CallbackContext context)
    {
        if (context.phase == InputActionPhase.Started)
        {
            freelookMovement.SetTargetingState(true);
            aim.Priority = aimCameraOrder;
        }
        else if (context.phase == InputActionPhase.Canceled)
        {
            freelookMovement.SetTargetingState(false);
            aim.Priority = 0;
        }
    }

    public LayerMask pickupLayer;
    public void PickupItem()
    {
        // Collider[] colliders = Physics.OverlapSphere(transform.position, 0.5f, 1 << LayerMask.NameToLayer("PickupItem");
        Collider[] colliders = Physics.OverlapSphere(transform.position, 0.5f, pickupLayer);
        if (colliders.Length == 0)
            return;

        PickupWeapon item = colliders[0].GetComponent<PickupWeapon>();
        if (item.itemType == ItemType.Weapon)
        {
            weaponManager.Pickup(item);
        }
    }


    void Update()
    {
        
        

        if (pressFire)
        {
            weaponManager?.Fire();
        }
    }
}
