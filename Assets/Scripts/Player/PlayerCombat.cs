using UnityEngine;

/// <summary>
/// 플레이어 전투. 스킬 실행의 진입점이다.
/// 현재는 애니메이션 트리거만 넘기지만, 전투 로직(쿨다운·히트박스·데미지)이
/// 자라날 자리. Player는 이 진입점만 알고 내부 구현은 여기에 가둔다.
/// </summary>
[RequireComponent(typeof(PlayerAnimation))]
public class PlayerCombat : MonoBehaviour
{
    private PlayerAnimation anim;
    private void Awake() => anim = GetComponent<PlayerAnimation>();

    /// <summary>n번 스킬 실행. 지금은 애니 트리거만, 추후 쿨다운·데미지 판정이 여기 붙는다.</summary>
    public void UseSkill(int n) => anim.PlaySkill(n);

    // TODO: 쿨다운(스킬별 타이머), 히트박스 활성/판정, 데미지 계산,
    //       스킬 데이터 테이블 조회(ID로 계수·쿨타임 등 로드)
}
