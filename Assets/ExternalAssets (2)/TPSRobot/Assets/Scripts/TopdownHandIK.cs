using UnityEngine;

public class TopdownHandIK : MonoBehaviour
{
    public Transform handIKTarget;
    public Transform handIKHint;
    public Transform weaponTransform;

    public Transform HandIKTarget => handIKTarget;
    public Transform HandIKHint => handIKHint;
    void Awake()
    {
        handIKTarget = transform.Find("IK_target");
        handIKHint = transform.Find("IK_hint");
        weaponTransform = transform.Find("Weapon");
    }
}
