using UnityEngine;
using UnityEngine.UI;
using ProjectS.Core;
using ProjectS.Players;

namespace ProjectS.UI
{
    /// <summary>
    /// 총기 캐릭터(Erwin 등) 전용 화면 중앙 조준점. (2026-09-28 TH 추가)
    /// 조작 중인 플레이어(<see cref="LocalPlayer.Current"/>)의 캐릭터 이름이 <see cref="visibleCharacters"/>에
    /// 있을 때만 보이고, 카메라가 수평에서 위아래로 벗어날수록 흐려지며 회색으로 바뀐다.
    /// 화면 중앙이 몬스터(<see cref="IDamageable"/>)를 가리키면 조준 대상 색으로 바뀐다.
    /// </summary>
    /// <remarks>
    /// 사격은 캐릭터의 수평 정면(<see cref="PlayerMovement.SnapToCameraForward"/>가 yaw만 맞춘다)으로 나가므로,
    /// 카메라를 위아래로 돌리면 화면 중앙과 실제 탄도가 어긋난다. 그 어긋남을 "조준점이 죽는다"로 보여 주려는 것이다.
    /// 이벤트를 구독하지 않고 매 프레임 폴링한다 — 캐릭터 교체(멀티 아바타 스폰, 캐릭터 선택)와
    /// 스탯 테이블 로딩(CharacterName이 늦게 채워짐) 시점을 따로 알릴 신호가 없어, 폴링이 가장 덜 깨진다.
    /// 오브젝트 자체를 끄면 Update가 멈춰 다시 켜지지 않으므로, 숨김은 CanvasGroup 알파로 처리한다.
    /// </remarks>
    [RequireComponent(typeof(CanvasGroup))]
    public class AimCrosshair : MonoBehaviour
    {
        [Header("표시 대상")]
        [Tooltip("조준점을 보여 줄 캐릭터 이름(PlayerStatTable.CharacterName). 대소문자 무시.")]
        [SerializeField] private string[] visibleCharacters = { "Erwin" };

        [Tooltip("마을 등 전투 비활성 구역에서는 숨긴다(사격 자체가 불가).")]
        [SerializeField] private bool hideOutsideCombat = true;

        [Tooltip("마우스 모드(커서 해제)에서는 숨긴다. HUD를 클릭하는 동안 화면 중앙 점이 거슬리지 않게 하기 위함.")]
        [SerializeField] private bool hideInMouseMode = true;

        [Tooltip("색을 입힐 조준점 그래픽들(점·선 등). 비우면 자식의 Graphic을 전부 모은다.")]
        [SerializeField] private Graphic[] parts;

        [Header("각도 판정")]
        [Tooltip("탄도와 일치한다고 보는 카메라 pitch(도). 기본 카메라가 살짝 내려다보는 구도라면 그 각도를 넣는다.\n" +
                 "양수 = 위를 봄, 음수 = 아래를 봄.")]
        [SerializeField, Range(-30f, 30f)] private float neutralPitch = 0f;

        [Tooltip("중립에서 이 각도까지는 조준점이 완전히 선명하다(오차 허용 구간).")]
        [SerializeField, Range(0f, 45f)] private float clearAngle = 5f;

        [Tooltip("중립에서 이 각도 이상 벗어나면 최대로 흐려지고 완전히 회색이 된다.")]
        [SerializeField, Range(1f, 90f)] private float fadeAngle = 25f;

        [Header("몬스터 조준")]
        [Tooltip("화면 중앙 레이가 멈출 대상. 몬스터 레이어와 벽·지형처럼 시야를 가리는 레이어를 함께 넣는다\n" +
                 "(몬스터만 넣으면 벽 너머 몬스터에도 색이 바뀐다). Player/BodyPart는 반드시 뺀다 — 3인칭 카메라라\n" +
                 "레이가 캐릭터 몸을 먼저 맞혀 몬스터를 영영 못 잡는다.")]
        [SerializeField] private LayerMask targetMask;

        [Tooltip("몬스터를 인식할 최대 거리(카메라 기준).")]
        [SerializeField, Min(0f)] private float targetDistance = 60f;

        [Tooltip("화면 중앙이 몬스터를 가리킬 때 색. 각도가 벗어나면 이 색에서 offAxisColor로 흐려진다.")]
        [SerializeField] private Color targetColor = new Color(1f, 0.25f, 0.2f, 1f);

        [Header("모양")]
        [Tooltip("정면(탄도 일치)일 때 색.")]
        [SerializeField] private Color alignedColor = Color.white;

        [Tooltip("최대로 벗어났을 때 색. 알파는 아래 minAlpha가 따로 정한다.")]
        [SerializeField] private Color offAxisColor = new Color(0.5f, 0.5f, 0.5f, 1f);

        [Tooltip("최대로 벗어났을 때 알파. 0이면 완전히 사라진다.")]
        [SerializeField, Range(0f, 1f)] private float minAlpha = 0.25f;

        [Tooltip("알파·색 변화의 추종 속도. 클수록 즉각 반응한다. 0이면 보간 없이 즉시 적용.")]
        [SerializeField, Min(0f)] private float blendSpeed = 20f;

        [Header("디버그")]
        [Tooltip("켜면 조준 레이가 처음 멈춘 표면을 콘솔에 찍는다(대상이 바뀔 때만). 조준 색이 안 바뀔 때 원인 확인용.")]
        [SerializeField] private bool logAimHit;

        private CanvasGroup group;
        private Transform cam;

        // 조준 레이 결과 버퍼. 매 프레임 쏘므로 미리 할당해 GC를 막는다(전투 판정 버퍼와 같은 규칙).
        private readonly RaycastHit[] hitBuffer = new RaycastHit[16];
        // Array.Sort의 구간 정렬은 Comparison이 아니라 IComparer만 받는다.
        private static readonly System.Collections.Generic.IComparer<RaycastHit> HitDistanceComparer =
            System.Collections.Generic.Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));
        private Collider lastLoggedHit;

        // 0 = 정면, 1 = 최대로 벗어남. 보간된 현재값.
        private float offAxis;
        // 0 = 빈 곳, 1 = 몬스터 조준 중. 보간된 현재값(색이 딱딱 끊기지 않게).
        private float onTarget;
        private bool wasVisible;

        private void Awake()
        {
            group = GetComponent<CanvasGroup>();

            // 순수 표시용. 화면 중앙에 있어 클릭을 가로채면 HUD 조작이 막힌다.
            group.interactable = false;
            group.blocksRaycasts = false;

            // 빈 칸만 있는 배열(인스펙터에서 +만 누르고 비워 둔 상태)도 "미지정"으로 본다.
            // 길이만 보면 None 한 칸짜리 배열이 통과해 칠할 대상이 0개가 되고, 색만 조용히 안 바뀐다.
            parts = System.Array.FindAll(parts ?? new Graphic[0], part => part != null);
            if (parts.Length == 0) parts = GetComponentsInChildren<Graphic>(true);
            foreach (Graphic part in parts)
            {
                if (part != null) part.raycastTarget = false;
            }

            group.alpha = 0f;
        }

        private void Update()
        {
            Player player = LocalPlayer.Current;
            if (!ShouldShow(player))
            {
                group.alpha = 0f;
                wasVisible = false;
                return;
            }

            float axisTarget = EvaluateOffAxis();
            float onTargetTarget = IsAimingAtTarget(player) ? 1f : 0f;

            // 막 나타난 프레임은 보간 없이 바로 맞춘다(0→현재 각도로 쓸려 오는 깜빡임 방지).
            // unscaled: 히트스톱으로 timeScale이 떨어져도 조준점 반응은 늦으면 안 된다.
            if (!wasVisible || blendSpeed <= 0f)
            {
                offAxis = axisTarget;
                onTarget = onTargetTarget;
            }
            else
            {
                float t = 1f - Mathf.Exp(-blendSpeed * Time.unscaledDeltaTime);
                offAxis = Mathf.Lerp(offAxis, axisTarget, t);
                onTarget = Mathf.Lerp(onTarget, onTargetTarget, t);
            }
            wasVisible = true;

            group.alpha = Mathf.Lerp(1f, minAlpha, offAxis);

            // 몬스터 조준 색을 먼저 고르고, 그 위에 각도 이탈 회색을 덮는다. 카메라를 위아래로 돌린 상태면
            // 몬스터를 가리켜도 탄이 그쪽으로 안 가므로, 조준 색보다 "탄도 어긋남" 표시가 이겨야 한다.
            Color baseColor = Color.Lerp(alignedColor, targetColor, onTarget);
            Color color = Color.Lerp(baseColor, offAxisColor, offAxis);
            foreach (Graphic part in parts)
            {
                if (part != null) part.color = color;
            }
        }

        // 조작 중인 캐릭터가 조준점 대상이고, 지금 사격 가능한 화면인지.
        private bool ShouldShow(Player player)
        {
            if (player == null || !player.isActiveAndEnabled) return false;
            if (player.Stats.IsDead) return false;
            if (hideOutsideCombat && !player.IsCombatEnabled) return false;
            if (hideInMouseMode && Cursor.lockState != CursorLockMode.Locked) return false;
            if (player.InCutscene) return false;

            return IsTargetCharacter(player.Stats.CharacterName);
        }

        private bool IsTargetCharacter(string characterName)
        {
            // 테이블 로딩 전에는 이름이 비어 있다 → 로딩이 끝나는 프레임부터 자연히 보이기 시작한다.
            if (string.IsNullOrEmpty(characterName) || visibleCharacters == null) return false;

            foreach (string name in visibleCharacters)
            {
                if (string.Equals(name, characterName, System.StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        // 매 프레임 Camera.main을 다시 읽는다. Bootstrap과 던전 씬에 각각 MainCamera가 있어, 한 번 캐싱하면
        // 씬 전환 뒤에도 살아 있는 옛 카메라를 계속 볼 수 있다. Camera.main은 Unity 내부 캐시라 매 프레임 호출해도 싸다.
        private bool TryCacheCamera()
        {
            Camera main = Camera.main;
            cam = main != null ? main.transform : null;
            return cam != null;
        }

        // 화면 중앙이 가리키는 첫 "보이는" 표면이 몬스터인지.
        private bool IsAimingAtTarget(Player player)
        {
            if (targetMask.value == 0 || !TryCacheCamera()) return false;

            // 카메라 forward 레이 = 화면 정중앙(조준점 위치)을 지나는 레이.
            // Collide: 이 프로젝트의 적 피격 콜라이더는 Is Trigger다. 무시하면 몬스터를 그냥 통과한다(LaserSight와 같은 이유).
            // 단일 Raycast가 아니라 전부 모아 가까운 순으로 보는 이유: Collide로 쏘면 문 감지(DoorTrigger, Wall 레이어)·
            // 방 잠금 구역 같은 투명한 감지용 트리거에 먼저 걸려, 그 뒤 몬스터를 영영 못 잡는다.
            int count = Physics.RaycastNonAlloc(cam.position, cam.forward, hitBuffer,
                                                targetDistance, targetMask, QueryTriggerInteraction.Collide);
            if (count == 0)
            {
                LogAimHit(null);
                return false;
            }
            System.Array.Sort(hitBuffer, 0, count, HitDistanceComparer);

            Transform self = player != null ? player.transform : null;
            for (int i = 0; i < count; i++)
            {
                Collider col = hitBuffer[i].collider;

                // 내 캐릭터 몸(CharacterController·무기)은 3인칭 카메라 레이가 먼저 스친다. 투명 취급한다
                // (PlayerStats도 IDamageable이라 안 거르면 내 몸에 조준 색이 뜬다).
                if (self != null && col.transform.IsChildOf(self)) continue;

                // 피격 콜라이더가 자식에 있어도 잡히게 부모까지 찾는다.
                if (col.GetComponentInParent<IDamageable>() != null)
                {
                    LogAimHit(col);
                    return true;
                }

                // 감지용 트리거는 눈에 안 보이므로 통과시키고, 실체 콜라이더(벽·바닥)에서 멈춘다.
                if (col.isTrigger) continue;

                LogAimHit(col);
                return false;
            }

            LogAimHit(null);
            return false;
        }

        // 조준점이 흰색으로만 남을 때 무엇에 막히는지 확인하는 진단 로그. 대상이 바뀔 때만 찍는다.
        private void LogAimHit(Collider col)
        {
            if (!logAimHit || col == lastLoggedHit) return;
            lastLoggedHit = col;

            if (col == null) Debug.Log("[AimCrosshair] 조준 대상 없음(아무것도 안 맞음/트리거만 통과)", this);
            else Debug.Log($"[AimCrosshair] 첫 표면: {col.name} (layer={LayerMask.LayerToName(col.gameObject.layer)}, " +
                           $"trigger={col.isTrigger}, 몬스터={col.GetComponentInParent<IDamageable>() != null})", col);
        }

        // 카메라가 탄도(수평 정면)에서 얼마나 벗어났는지를 0~1로 돌려준다.
        private float EvaluateOffAxis()
        {
            if (!TryCacheCamera()) return 0f;

            // pitch를 오일러각이 아니라 forward.y로 구한다. Cinemachine이 최종 회전을 쓰므로 오일러의
            // 0/360 경계 문제 없이 "실제로 화면이 보는 각도"를 얻는다. 양수 = 위를 봄.
            float pitch = Mathf.Asin(Mathf.Clamp(cam.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            float deviation = Mathf.Abs(pitch - neutralPitch);

            return Mathf.InverseLerp(clearAngle, Mathf.Max(clearAngle + 0.01f, fadeAngle), deviation);
        }
    }
}
