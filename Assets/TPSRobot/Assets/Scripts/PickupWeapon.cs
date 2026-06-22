using UnityEngine;

public enum ItemType
{
    Weapon,
    Item,
}

public class PickupWeapon : MonoBehaviour
{
    public ItemType itemType;
    public Weapon weapon;

   
}
