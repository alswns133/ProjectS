using UnityEngine;

namespace ProjectS.Debugging
{
    /// <summary>
    /// 발판(바닥 콜라이더)에 붙여 "이 구역에서 쓸 애니메이터 컨트롤러"를 지정하는 마커.
    /// <see cref="AnimatorZoneTester"/>가 플레이어 발밑을 훑어 이 컴포넌트를 찾고 컨트롤러를 교체한다.
    ///
    /// Tag 대신 컴포넌트를 쓰는 이유: Tag Manager에 전역 태그를 추가할 필요가 없고,
    /// 구역-컨트롤러 매핑을 코드에 하드코딩하지 않아 구역을 늘릴 때 인스펙터만 만지면 된다.
    ///
    /// ★ 테스트 전용이다. 실제 마을/던전은 씬을 분리하므로, 통합 시 이 컴포넌트와
    ///   AnimatorZoneTester를 함께 제거한다.
    /// </summary>
    public class AnimatorZone : MonoBehaviour
    {
        [Header("이 구역에서 사용할 설정")]
        [SerializeField] private RuntimeAnimatorController controller;

        [Tooltip("마을 구역이면 체크 해제. 전투 조율자(FreeCombatController)를 함께 끈다.")]
        [SerializeField] private bool combatEnabled = true;

        [Tooltip("전환 시 콘솔에 표시할 이름. 비우면 오브젝트 이름을 쓴다.")]
        [SerializeField] private string zoneLabel;

        /// <summary>이 구역에 진입했을 때 Animator에 대입할 컨트롤러.</summary>
        public RuntimeAnimatorController Controller => controller;

        /// <summary>이 구역에서 전투 조율자를 활성화할지 여부.</summary>
        public bool CombatEnabled => combatEnabled;

        /// <summary>로그용 표시 이름.</summary>
        public string Label => string.IsNullOrEmpty(zoneLabel) ? name : zoneLabel;
    }
}
