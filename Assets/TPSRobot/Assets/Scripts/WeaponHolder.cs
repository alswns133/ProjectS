using UnityEngine;

public class WeaponHolder : MonoBehaviour
{
    private Transform ikTarget;
    private Transform ikHint;
    public void Initialize()
    {
        ikTarget = transform.Find("IK_target");
        ikHint = transform.Find("IK_hint");
    }

    public void SetIKTarget(Transform target)
    {
        ikTarget.position = target.position;
        ikTarget.rotation = target.rotation;
    }
    
    public void SetIKHint(Transform target)
    {
        ikHint.position = target.position;
        ikHint.rotation = target.rotation;
    }

    // WeaponHolder의 하단에 배치된 모든 무기를 삭제하는 메서드
    public void DestroyWeapon()
    {
        var renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers)
        {
            Destroy(renderer.gameObject);
        }
    }
}
