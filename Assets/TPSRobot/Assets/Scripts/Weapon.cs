using UnityEngine;
[System.Serializable]

public class Weapon
{
    public Model prefab;
    public Model model;
    public WeaponType weaponType;

    // 발사될 무기에 대한 제어 변수 목록이 필요
    // 총 발사 간격 시간
    public float timeBetweenShots = 0.1f;
    // 이전 발사 시간
    public float prevTime = 0;
    // 총알 수
    public float ammoCount = 20;

    public Weapon Clone()
    {
        Weapon weapon = new Weapon();
        weapon.prefab = prefab;
        weapon.weaponType = weaponType;
        weapon.timeBetweenShots = timeBetweenShots;
        weapon.prevTime = 0;
        weapon.ammoCount = ammoCount;
        return weapon;
    }

   
}
