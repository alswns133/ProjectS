using Mirror;
using ProjectS.Networking;
using ProjectS.Players;
using Unity.Cinemachine;
using UnityEngine;

namespace ProjectS.Cameras
{
    /// <summary>
    /// 레이드에서 <b>더 이상 일어설 수 없는(다운)</b> 동안의 관전 카메라. 살아 있는 파티원의 시점을 빌려 보고,
    /// 볼 사람이 없어지면 내 몸(시체)으로 돌아온다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>규칙은 하나다</b>(2026-09-22 사용자 확정, 로스트아크식):
    /// <i>"다운 중이면 살아 있는 파티원을, 없으면 내 몸을 본다."</i>
    /// 기획에서 말한 세 경우 — 다운 시 파티원에게 넘어가고, 그 파티원이 죽으면 내 시체로 돌아오고,
    /// 누군가 부활하면 다시 그쪽으로 — 가 전부 이 한 줄에서 나온다. 전환마다 분기를 두지 않는 이유다.
    /// </para>
    /// <para>
    /// <b>어떻게 바꾸나.</b> 아바타마다 <see cref="CinemachineCamera"/>가 하나씩 붙어 있고
    /// <c>OwnerGate</c>가 남의 것을 꺼 둔다. 관전은 그중 하나를 <b>잠시 켜는</b> 것이고, 한 번에 하나만
    /// 켜 두므로 Brain이 알아서 블렌딩한다. 빌려 쓴 카메라는 반드시 다시 꺼 원래 상태로 돌려 놓는다.
    /// </para>
    /// <para>
    /// <b>누가 살아 있는지</b>는 <see cref="PlayerPresence.HpRatio"/>(SyncVar)로 본다. 아바타의 HP는 복제되지
    /// 않지만 이 비율은 파티원 상태 HUD 때문에 이미 전원에게 복제되고 있다. 그 사람의 몸은
    /// <see cref="PlayerPresence.AvatarNetId"/>로 찾는다 — 클라는 남의 아바타 소유권을 알 수 없어
    /// 서버가 심어 준 이 링크가 유일한 연결고리다.
    /// </para>
    /// <para>
    /// 마우스룩은 빌려 온 아바타의 피벗 컨트롤러가 꺼져 있어 동작하지 않는다. 관전 중에는 그 사람 등 뒤를
    /// 따라가는 고정 시점이 된다(관전자가 시점을 돌릴 수 있게 하려면 그 컴포넌트를 함께 켜면 된다).
    /// </para>
    /// </remarks>
    public class SpectatorCamera : MonoBehaviour
    {
        // 대상을 매 프레임 다시 고르지 않는다. HP 복제·아바타 스폰은 프레임 단위로 급하지 않고,
        // 너무 자주 고르면 사망 순간 두 카메라를 오가며 깜빡일 수 있다.
        private const float RetargetInterval = 0.4f;

        private static SpectatorCamera instance;

        /// <summary>지금 관전 중인지.</summary>
        public static bool IsSpectating => instance != null && instance.active;

        private bool active;
        private CinemachineCamera currentCam;
        private float nextRetarget;

        // 플레이 모드 리로드 후 이전 판의 인스턴스 참조가 남지 않게 한다(static 리셋 방침).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        /// <summary>
        /// 관전을 시작한다. 다운 상태에 들어갈 때 <c>RaidFailFlow.ReportDown(true)</c>가 부른다.
        /// </summary>
        public static void Begin()
        {
            if (instance == null)
            {
                // 씬(레이드)보다 오래 살아야 한다 — 재시도로 씬이 바뀌는 동안에도 정리 책임이 남는다.
                GameObject host = new GameObject("[SpectatorCamera]");
                DontDestroyOnLoad(host);
                instance = host.AddComponent<SpectatorCamera>();
            }

            instance.active = true;
            instance.nextRetarget = 0f;   // 다음 프레임에 곧바로 대상을 고른다
        }

        /// <summary>
        /// 관전을 끝내고 내 카메라로 되돌린다. 부활·마을 복귀·재시도에서 부른다.
        /// 빌려 쓴 남의 카메라를 다시 꺼 주는 것이 이 호출의 핵심이라, 빠뜨리면 그 카메라가 계속 켜져 있는다.
        /// </summary>
        public static void End()
        {
            if (instance == null) return;

            instance.active = false;
            instance.SwitchTo(OwnCam());
        }

        private void Update()
        {
            if (!active || Time.unscaledTime < nextRetarget) return;
            nextRetarget = Time.unscaledTime + RetargetInterval;

            CinemachineCamera own = OwnCam();

            // 내 아바타가 사라졌다(재시도로 인스턴스 재생성·이탈). 더 볼 것도 되돌릴 것도 없다.
            if (own == null)
            {
                active = false;
                SwitchTo(null);
                return;
            }

            SwitchTo(PickTarget(own));
        }

        // "살아 있는 같은 파티원" 중 첫 번째. 없으면 내 몸.
        private static CinemachineCamera PickTarget(CinemachineCamera own)
        {
            PlayerPresence me = PlayerPresence.Local;

            foreach (PlayerPresence member in PlayerPresence.All)
            {
                if (member == null || member == me) continue;
                if (me != null && member.PartyId != me.PartyId) continue;
                if (member.HpRatio <= 0f) continue;   // 죽어 있는 사람은 볼 대상이 아니다

                CinemachineCamera cam = ResolveCam(member.AvatarNetId);
                if (cam != null) return cam;
            }

            return own;
        }

        // 한 번에 하나만 켜 둔다. 둘이 켜져 있으면 Brain이 무엇을 고를지 불분명해진다.
        private void SwitchTo(CinemachineCamera next)
        {
            if (next == currentCam) return;

            CinemachineCamera own = OwnCam();

            // 빌려 쓰던 남의 카메라는 원래대로 꺼 둔다(OwnerGate가 꺼 놓은 것이 기본 상태다).
            if (currentCam != null && currentCam != own) currentCam.enabled = false;

            if (next != null)
            {
                next.enabled = true;
                if (own != null && next != own) own.enabled = false;
            }
            else if (own != null)
            {
                own.enabled = true;
            }

            currentCam = next;

            Debug.Log($"[진단][관전] 시점 전환 → {(next == null ? "없음" : next == own ? "내 캐릭터" : next.transform.root.name)}");
        }

        private static CinemachineCamera ResolveCam(uint avatarNetId)
        {
            if (avatarNetId == 0) return null;
            if (!NetworkClient.spawned.TryGetValue(avatarNetId, out NetworkIdentity identity) || identity == null) return null;

            return identity.GetComponentInChildren<CinemachineCamera>(true);
        }

        // 내가 조종하는 아바타의 카메라. 멀티에서 "내 몸"은 지속 캐릭터가 아니라 네트워크 아바타다.
        private static CinemachineCamera OwnCam()
        {
            Player avatar = OwnerGate.LocalAvatar;
            return avatar != null ? avatar.GetComponentInChildren<CinemachineCamera>(true) : null;
        }
    }
}
