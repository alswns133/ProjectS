using UnityEngine;
using UnityEngine.Animations.Rigging;
using System.Collections;
using System.Collections.Generic;
using System.Timers;

public enum WeaponType
{
    None,
    Rifle,
    PlasmaRifle,
    Shotgun,
    Machinegun,
    RocketLuncher,
}

public class WeaponManager : MonoBehaviour
{
    private WeaponHolder weaponHolder;
    private Animator animator;
    private Rig rig;
    private WeaponType current = WeaponType.None;
    private Dictionary<WeaponType, Weapon> weaponDic = new Dictionary<WeaponType, Weapon>();

    public void Initialize()
    {
        weaponHolder = GetComponentInChildren<WeaponHolder>(true);
        weaponHolder?.Initialize();
        animator = GetComponentInChildren<Animator>(true);
        rig = GetComponentInChildren<Rig>(true);
    }

    // 현재 착용중인 무기를 삭제하는 메서드
    public void DestroyWeapon()
    {
        if (weaponDic.ContainsKey(current))
        {
            Destroy(weaponDic[current].model.gameObject);
        }
    }

    private bool rigUpdate = false;
    // 전체 무기를 삭제하는 메서드
    public void DestroyAll() => weaponHolder?.DestroyWeapon();

    private IEnumerator RigCoroutine(float weight, float time)
    {
        // Mathf.Epsilon : 0에 가까운 수
        if (Mathf.Abs(rig.weight - weight) <= Mathf.Epsilon)
            yield break;
        rigUpdate = true;
        float rigWeight = rig.weight;
        float targetWeight = weight;
        float elapsed = 0;
        while (true)
        {
            elapsed += Time.deltaTime / time;
            rig.weight = Mathf.Lerp(rigWeight, targetWeight, elapsed);
            if (elapsed >= 1.0f)
                break;
            yield return null;
        }
        rigUpdate = false;
    }

    // 무기 착용 키를 입력 -> 무기가 있는지 체크 -> 무기를 착용하도록 처리
    public void PressSwitch(string key)
    {
        if (System.Enum.TryParse(key, out WeaponType weaponType))
        {
            if (weaponDic.ContainsKey(weaponType))
            {
                // 무기를 변경했을 때 current 값을 변경해줘야 함
                Equip(weaponDic[weaponType]);
            }
        }
    }

    private void Switch(Weapon weapon, float updateTime = 0.3f)
    {
        // 현재 무기 교체 코루틴이 실행중이라면 실행되지 않도록 처리
        if (rigUpdate)
            return;

        // 무기를 착용할 때 착용중인 값으로 변경해야 함
        current = weapon.weaponType;

        // 현재 무기 Root의 하단에 배치된 프리팹과 동일한 이름을 갖는 Transform 영역에서 TopdownHandIK 클래스를 찾음
        TopdownHandIK handIK = weaponHolder.transform.Find(weapon.prefab.name).GetComponent<TopdownHandIK>();
        
        // TopdownHandIK 하단에 배치된 Weapon을 찾음
        Transform parent = handIK.transform.Find("Weapon");

        print(weapon.prefab.name);

        // 모델이 생성되었을 때는 프리팹 이름(Clone) 형식으로 지정
        Model model = Instantiate(weapon.prefab, parent);
        model.name = weapon.prefab.name;
        model.player = transform;
        model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        
        // 생성된 모델을 무기에 넣어줘야 함
        weapon.model = model;
        // 현재 애니메이터의 애니메이터 컨트롤러를 변경
        animator.runtimeAnimatorController = model.controller;

        // 현재 WeaponHolder의 IKTarget, IKHint값을 현재 선택한 무기의 IKTarget, IKHint 값으로 변경
        weaponHolder.SetIKTarget(handIK.HandIKTarget);
        weaponHolder.SetIKHint(handIK.HandIKHint);
        StartCoroutine(RigCoroutine(1, updateTime));
    }

    public void Equip(Weapon weapon)
    {
        DestroyWeapon();
        Switch(weapon);
    }

    public void Pickup(PickupWeapon pickupWeapon)
    {
        // 프로젝트 작성할 때 코드 규약에 따라서 표준 방식에 맞게 코드를 작성하는 것이 좋음
        if (!weaponDic.ContainsKey(pickupWeapon.weapon.weaponType))
        {
            // 로그가 잘 출력된다면 무기 저장은 정상 작동 중
            print(pickupWeapon.weapon.weaponType);
            print(pickupWeapon.weapon.prefab);
            // 현재 무기의 소유자를 넣어줌
            // pickupWeapon.weapon.model.player = player;
            weaponDic.Add(pickupWeapon.weapon.weaponType, pickupWeapon.weapon.Clone());
        }
        Destroy(pickupWeapon.gameObject);
    }
    
    public void BulletShot()
    {
        if (weaponDic.ContainsKey(current) && weaponDic[current].ammoCount > 0)
        {
            // 총알 수 감소
            Weapon weapon = weaponDic[current];
            weapon.ammoCount--;
            // 실질적인 이펙트 발사되도록 처리
            weapon.model.Fire();
        }
    }
    public void Fire()
    {
        // 무기가 등록되어 있지 않다면 메서드 종료
        if (!weaponDic.ContainsKey(current))
            return;

        // 총알이 없다면 메서드 종료
        Weapon weapon = weaponDic[current];
        if (weapon.ammoCount <= 0)
            return;

        float elapsed = Time.time - weapon.prevTime;
        if (elapsed >= weapon.timeBetweenShots)
        {
            weapon.prevTime = Time.time;
            // 애니메이션 실행
            animator.SetTrigger("Fire");
        }
    }
}
