using UnityEngine;

/// <summary>
/// 몬스터 공격 판정과 쿨다운. 히트 프레임은 Animation Event로 연결한다.
/// </summary>
public class EnemyCombat : MonoBehaviour
{
    private readonly Collider[] buffer = new Collider[16];
}
