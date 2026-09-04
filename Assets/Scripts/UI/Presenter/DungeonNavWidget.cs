using UnityEngine;
using ProjectS.Enemies;
using ProjectS.Players;

namespace ProjectS.UI
{
    /// <summary>
    /// 던전 "다음 지역" 안내 위젯(HUD). 플레이어가 있는 전투방(<see cref="DungeonNav.CurrentRoom"/>)을 읽어,
    /// 방을 클리어하면 다음 지역 방향으로 화살표를 돌리고 안내 묶음을 켠다. 전투 중에는 숨긴다.
    ///
    /// 표시 요소(화살표·"GO!"·안내문)는 모두 <see cref="root"/> 자식으로 묶어 둔다. 이 스크립트는 <see cref="root"/>를
    /// 통째로 켜고 끄고, 화살표(<see cref="arrowRect"/>)만 매 프레임 회전시킨다 — "GO!"와 안내문은 정적 텍스트라
    /// 코드가 건드릴 게 없으므로 root에 얹혀 함께 나타났다 사라진다.
    ///
    /// <b>배치</b>: 이 스크립트는 <b>항상 켜져 있는 부모</b>에 붙이고(예: OverlayCanvas 직속 자식), <see cref="root"/>에는
    /// 그 자식(안내 묶음)을 넣는다. root를 이 오브젝트 자신으로 두면 숨길 때 <c>SetActive(false)</c>로 자기
    /// <see cref="LateUpdate"/>가 멈춰 다시 못 켠다. 마을 등 던전 밖에서는 CurrentRoom이 null이라 자동으로 숨는다.
    ///
    /// 목표 방향 계산은 퀘스트 나침반과 같은 판정(<see cref="QuestNavResolver.BearingRelativeToCamera"/>, XZ 평면)을 쓴다.
    /// </summary>
    public class DungeonNavWidget : MonoBehaviour
    {
        [Tooltip("화살표·GO!·안내문을 묶은 표시 루트. 전투 중/안내 없음이면 통째로 끈다. 이 스크립트가 붙은 오브젝트가 아니라 그 자식이어야 한다.")]
        [SerializeField] private GameObject root;

        [Tooltip("목표 방향으로 회전시킬 화살표 RectTransform. 위(+Y)가 화면 정면 기준이다.")]
        [SerializeField] private RectTransform arrowRect;

        [Tooltip("화살표 스프라이트의 기본 방향 보정(도). 이미지가 '위(↑)'를 향해 그려졌으면 0. " +
                 "오른쪽(→)이면 -90, 아래(↓)면 180, 왼쪽(←)이면 90. bearing과 무관하게 UI 회전만 보정한다.")]
        [SerializeField] private float arrowAngleOffset;

        // 지속 플레이어라 한 번 잡으면 유지된다. 씬 전환으로 파괴되면 Unity가 null로 만들어 다시 잡는다.
        private Player player;

        // 방위각 계산용 카메라. 파괴되면 다음 프레임에 다시 잡는다(QuestTrackerHud와 같은 방침).
        private Camera navCamera;

        // 진단용: 숨김/표시 사유가 바뀔 때만 로그를 찍어 매 프레임 스팸을 막는다.
        private string lastReason;

        // 표시 갱신은 LateUpdate에서. 목록 관리가 없어 방 하나만 보면 되므로 가볍다.
        private void LateUpdate()
        {
            EnemyRoom room = DungeonNav.CurrentRoom;

            // 방 없음(던전 밖·미진입) 또는 전투 중(아직 클리어 전) → 숨김.
            // room이 파괴된 방이어도 Unity의 == 오버로드가 null로 잡아 준다.
            if (room == null)
            {
                Report("숨김 — CurrentRoom=null (던전 밖·방 미진입)");
                SetVisible(false);
                return;
            }
            if (!room.IsCleared)
            {
                Report($"숨김 — '{room.name}'(RoomIndex={room.RoomIndex}) 아직 전투 중(미클리어)");
                SetVisible(false);
                return;
            }

            Transform playerTransform = ResolvePlayerTransform();
            Camera cam = ResolveNavCamera();
            if (playerTransform == null || cam == null)
            {
                Report($"숨김 — player/cam 못 찾음 (player={(playerTransform != null)}, cam={(cam != null)})");
                SetVisible(false);
                return;
            }

            Vector3 from = playerTransform.position;

            // 클리어됐지만 가리킬 대상이 없으면(다음 방·exitDoor 둘 다 없음, 예: 최종 방) 안내하지 않는다.
            if (!room.TryGetExitTarget(from, out Vector3 target, out Transform targetTransform))
            {
                Report($"숨김 — '{room.name}'(RoomIndex={room.RoomIndex}) 클리어됐지만 가리킬 대상 없음: GetRoom({room.RoomIndex + 1})={(DungeonNav.GetRoom(room.RoomIndex + 1) != null ? "있음" : "null")}, 열린 exitDoor 폴백도 실패. → 방{room.RoomIndex + 1}의 RoomIndex 설정/등록 또는 방{room.RoomIndex}의 exitDoors 배선 확인.", room);
                SetVisible(false);
                return;
            }

            Report($"표시 — '{room.name}'(RoomIndex={room.RoomIndex}) → 대상 '{(targetTransform != null ? targetTransform.name : "?")}'", room);

            Vector3 to = target - from;
            to.y = 0f;   // 방위각·방향은 XZ 평면 기준(높이 차 무시).

            SetVisible(true);

            // UI에서 위(+Y)가 화면 정면이므로, 시계방향 방위각을 Z축 음수 회전으로 바꾼다(QuestCompassEntry와 동일).
            // arrowAngleOffset은 스프라이트 기본 방향이 '위'가 아닐 때 보정한다.
            if (arrowRect != null)
            {
                float bearing = QuestNavResolver.BearingRelativeToCamera(to, cam);
                arrowRect.localRotation = Quaternion.Euler(0f, 0f, -bearing + arrowAngleOffset);
            }
        }

        private void SetVisible(bool value)
        {
            if (root != null && root.activeSelf != value) root.SetActive(value);
        }

        // 사유가 직전과 다를 때만 로그를 남긴다(LateUpdate라 매 프레임 도는 것을 감안). 원인 추적용.
        private void Report(string reason, Object context = null)
        {
            if (reason == lastReason) return;
            lastReason = reason;
            ProjectS.Debugging.DevLog.Log($"[DungeonNavWidget] {reason}", context != null ? context : this);
        }

        private Transform ResolvePlayerTransform()
        {
            if (player == null) player = FindAnyObjectByType<Player>();
            return player != null ? player.transform : null;
        }

        private Camera ResolveNavCamera()
        {
            if (navCamera == null) navCamera = Camera.main;
            return navCamera;
        }
    }
}
