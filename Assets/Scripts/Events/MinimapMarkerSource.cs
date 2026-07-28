using UnityEngine;

namespace ProjectS.Events
{
    /// <summary>
    /// 이 오브젝트를 미니맵에 표시하겠다고 등록하는 브릿지 컴포넌트.
    /// 미니맵에 띄우고 싶은 것(플레이어·몬스터·NPC)에 붙이고 type만 고르면 된다.
    /// <para>
    /// Enemy/Player 같은 게임플레이 클래스에 등록 코드를 넣지 않고 이 컴포넌트로 분리한 이유:
    /// 전투·이동 로직이 미니맵(UI)의 존재를 몰라야 경계가 유지되기 때문이다.
    /// 붙이고 떼는 것만으로 미니맵 표시 여부가 정해지고, 클래스는 아무것도 알 필요가 없다.
    /// </para>
    /// <para>
    /// OnEnable/OnDisable에 짝지어 등록/해제하므로, 풀링으로 켜졌다 꺼지는 몬스터도
    /// 활성 상태와 미니맵 마커가 자동으로 일치한다(사망해 비활성되면 마커도 사라진다).
    /// </para>
    /// </summary>
    public class MinimapMarkerSource : MonoBehaviour
    {
        // 이 대상이 미니맵에서 어떤 마커로 보일지. 마커 아이콘 자체는 MinimapView가 type별로 들고 있다.
        [SerializeField] private MinimapMarkerType type = MinimapMarkerType.Enemy;

        private void OnEnable() => MinimapEvents.Register(transform, type);

        private void OnDisable() => MinimapEvents.Unregister(transform);
    }
}
