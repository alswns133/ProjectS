using UnityEngine;

public class Model : MonoBehaviour
{
    // 총구 위치
    public Transform muzzleTransform;
    // 총구에 출력될 파티클
    public ParticleSystem muzzleEffect;
    // 발사될 Bullet 이펙트
    public Transform ammoEffect;

    // 현재 무기의 애니메이터 컨트롤러
    public RuntimeAnimatorController controller;

    // 현재 무기가 발사될 힘 ( 둘 사이의 값을 랜덤하게 사용 )
    public float min = 2800;
    public float max = 3100;

    public bool playerDirection = false;
    public bool shotgun = false;
    public Transform[] shotgunLocator;
    public Transform player;

    public void Fire()
    {
        if (ammoEffect == null)
            return;

        if (!shotgun)
        {
            Transform t = Instantiate(ammoEffect, muzzleTransform.position, muzzleTransform.rotation);
            Rigidbody rigid = t.GetComponent<Rigidbody>();

            // 플레이어의 방향으로 총 발사
            if (playerDirection)
            {
                rigid?.AddForce(player.forward * Random.Range(min, max));
            }
            // 총구 방향으로 총을 발사
            else
                rigid?.AddForce(muzzleTransform.forward * Random.Range(min, max));
        }
        else
        {
            if (shotgunLocator.Length == 0) return;
            for (int i = 0; i < shotgunLocator.Length; ++i)
            {
                Transform t = Instantiate(ammoEffect, muzzleTransform.position, muzzleTransform.rotation);
                Rigidbody rigid = t.GetComponent<Rigidbody>();
                rigid.AddForce(shotgunLocator[i].forward * Random.Range(min, max));
            }
        }
    }

}
