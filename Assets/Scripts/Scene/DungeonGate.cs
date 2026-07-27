using UnityEngine;
using ProjectS.Managers;
using ProjectS.Players;
using ProjectS.UI;

namespace ProjectS.Scenes
{
    /// <summary>
    /// 마을의 던전 입구. 플레이어가 근접하면 던전 선택 팝업(<see cref="DungeonSelectPopup"/>)을 띄우고,
    /// 벗어나면 닫는다. 실제 씬 전환은 팝업 버튼이 담당하므로 이 게이트는 "근접 감지 → 팝업 열고 닫기"만 한다.
    ///
    /// 매 프레임 거리 계산 대신 트리거 콜라이더에 판정을 맡긴다(NpcOutlineTrigger와 같은 방침).
    /// 게이트 오브젝트(또는 전용 자식)에 붙이고 SphereCollider가 자동으로 트리거로 설정된다.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class DungeonGate : MonoBehaviour
    {
        // 실제 판정은 SphereCollider가 하고, 이 값으로 반지름을 인스펙터 한곳에서 관리한다.
        [SerializeField, Min(0f)] private float radius = 3f;

        private SphereCollider trigger;

        // 팝업을 이미 열었는지. 진입/이탈이 짝을 이루므로 중복 ShowPopup(활성 목록 중복 등록)을 막는다.
        private bool popupOpen;

        private void Awake()
        {
            trigger = GetComponent<SphereCollider>();
            trigger.isTrigger = true;   // 물리 충돌이 아니라 감지 전용
            trigger.radius = radius;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (IsPlayer(other)) OpenPopup();
        }

        private void OnTriggerExit(Collider other)
        {
            if (IsPlayer(other)) CloseGatePopup();
        }

        // 게이트가 비활성화되거나 씬이 정리될 때는 OnTriggerExit이 오지 않는다.
        // 그대로 두면 팝업이 열린 채로 남을 수 있어 여기서 정리한다.
        private void OnDisable()
        {
            CloseGatePopup();
        }

        private void OpenPopup()
        {
            if (popupOpen || UIManager.Instance == null) return;

            UIManager.Instance.ShowPopup<DungeonSelectPopup>();
            popupOpen = true;
        }

        private void CloseGatePopup()
        {
            if (!popupOpen) return;

            if (UIManager.Instance != null) UIManager.Instance.ClosePopup<DungeonSelectPopup>();
            popupOpen = false;
        }

        // 플레이어 판정은 태그가 아니라 컴포넌트로 한다(오타를 컴파일러가 못 잡는 태그 회피).
        // 무기·히트박스처럼 자식 콜라이더가 들어와도 잡히도록 부모까지 훑는다.
        private static bool IsPlayer(Collider other) => other.GetComponentInParent<Player>() != null;

#if UNITY_EDITOR
        // 인스펙터에서 radius를 만지면 즉시 콜라이더에 반영해 씬 뷰에서 범위를 확인할 수 있게 한다.
        private void OnValidate()
        {
            if (!TryGetComponent(out SphereCollider c)) return;
            c.isTrigger = true;
            c.radius = radius;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, radius * Mathf.Max(
                Mathf.Abs(transform.lossyScale.x),
                Mathf.Abs(transform.lossyScale.y),
                Mathf.Abs(transform.lossyScale.z)));
        }
#endif
    }
}
