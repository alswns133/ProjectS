using UnityEngine;
using ProjectS.Managers;
using ProjectS.Players;
using ProjectS.UI;

namespace ProjectS.Scenes
{
    /// <summary>
    /// 마을의 던전(레이드) 입구. 플레이어가 근접하면 입장 팝업(<see cref="DungeonEntryPopup"/>)을 띄우고,
    /// 벗어나면 닫는다. 실제 씬 전환은 팝업이 담당하므로 이 게이트는 "근접 감지 → 팝업 열고 닫기"만 한다.
    ///
    /// 던전과 레이드가 같은 팝업 프리팹을 공유하므로, 어느 쪽 입구인지는 이 게이트가 <see cref="mode"/>와
    /// <see cref="catalog"/>로 알려준다.
    ///
    /// 매 프레임 거리 계산 대신 트리거 콜라이더에 판정을 맡긴다(NpcOutlineTrigger와 같은 방침).
    /// 게이트 오브젝트(또는 전용 자식)에 붙이고 SphereCollider가 자동으로 트리거로 설정된다.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class DungeonGate : MonoBehaviour
    {
        [Header("입장 화면")]
        [Tooltip("이 입구가 던전용인지 레이드용인지. 팝업의 타이틀·전환할 씬이 이 값으로 갈린다.")]
        [SerializeField] private EntryMode mode = EntryMode.Dungeon;

        [Tooltip("이 입구에서 고를 수 있는 에피소드·난이도 목록. 비면 팝업이 빈 화면으로 뜬다.")]
        [SerializeField] private DungeonCatalog catalog;

        [Header("감지")]
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

            // ★ 데이터를 먼저 먹이고 그 다음에 연다. 순서가 뒤집히면 팝업의 OnShow가
            //    카탈로그 null 상태로 목록을 그려 빈 화면이 뜬다.
            DungeonEntryPopup popup = UIManager.Instance.GetPopup<DungeonEntryPopup>();
            if (popup == null)
            {
                // 조용히 넘어가면 "게이트에 가도 아무 일이 없다"로만 보여 원인을 찾기 어렵다.
                // 대개 팝업 프리팹이 부트스트랩 씬의 UIManager 아래에 없다는 뜻이다(UIManager는 자기 자식만 수집).
                Debug.LogWarning("[DungeonGate] DungeonEntryPopup이 등록돼 있지 않다 — 부트스트랩 씬 UIManager 아래에 프리팹이 있는지 확인할 것");
                return;
            }

            popup.SetMode(mode, catalog);
            UIManager.Instance.ShowPopup<DungeonEntryPopup>();
            popupOpen = true;
        }

        private void CloseGatePopup()
        {
            if (!popupOpen) return;

            if (UIManager.Instance != null) UIManager.Instance.ClosePopup<DungeonEntryPopup>();
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
