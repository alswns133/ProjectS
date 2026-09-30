using Mirror;
using ProjectS.Cameras;
using ProjectS.Managers;
using ProjectS.Players;
using ProjectS.Debugging;
using ProjectS.Events;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>네트워크 아바타에서 조작 컴포넌트(입력·이동·전투·애니·카메라)를 <b>오너 클라에서만</b> 켜 두는 게이트. 남의 아바타와 서버 쪽 사본에서는 꺼서 로컬 입력·물리가 동기화 위치와 싸우지 않게 한다.</summary>
public class OwnerGate : NetworkBehaviour
{
    private readonly List<Behaviour> ownerOnly = new(); // Input·Movement·Combat·PlayerAnimation·카메라
    private CharacterController cc;

    /// <summary>
    /// 이 클라가 조작하는 네트워크 아바타의 <see cref="Player"/>. 없으면 null.
    /// 멀티에선 <c>PlayerManager.Player</c>가 숨겨진 마을 캐릭터라, 입력 잠금 같은 "내 캐릭터" 처리는 이 값을 봐야 한다
    /// (<see cref="LocalPlayer.Current"/>가 싱글/멀티를 가려 돌려준다).
    /// </summary>
    public static Player LocalAvatar { get; private set; }

    // 플레이 모드 리로드 후 이전 판의 아바타 참조가 남지 않게 한다(static 리셋 방침).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => LocalAvatar = null;

    /// <summary>오너 전용으로 관리할 조작 컴포넌트들을 모아 둔다(끄고 켜는 판단은 네트워크 시작 콜백에서 한다).</summary>
    public void Awake()
    {
        AddGetComponent<PlayerInputHandler>();
        AddGetComponent<PlayerMovement>();
        AddGetComponent<PlayerCombat>();
        AddGetComponent<PlayerAnimation>();
        AddGetComponent<Player>();
        AddGetComponent<PlayerStats>();
        AddGetComponent<CameraRig>();
        AddGetComponent<CinemachineCamera>();       // ★ Brain이 실제로 보는 가상 카메라
        AddGetComponent<CameraPivotController>();    // ★ 남의 마우스룩이 남의 피벗 돌리는 것 방지
        cc = GetComponent<CharacterController>();
    }

    /// <summary>
    /// 서버가 가진 <b>원격 클라 소유 아바타</b>(서버 쪽 사본)의 조작 컴포넌트를 끈다.
    /// </summary>
    /// <remarks>
    /// <para><b>왜 여기서도 끄나.</b> 조작 컴포넌트 끄기는 <see cref="OnStartClient"/>에 있는데, 전용 서버는 클라가 아니라
    /// 그게 아예 안 불린다. 그러면 서버 사본의 <c>Player</c>/<c>PlayerMovement</c>가 켜진 채 매 프레임 <c>controller.Move</c>(중력)를
    /// 부르고, Auto Sync Transforms가 꺼져 있어 그 Move가 NetworkTransform이 넣은 위치를 이전 내부 위치로 덮어쓴다.
    /// 결과로 서버 사본이 실제 위치를 못 따라가, 보스 판정(서버)이 "멀리 있는데 맞고 가까이 있는데 안 맞는" 어긋남이 났다(2026-09-28).</para>
    /// <para><b>CharacterController는 켜 둔다.</b> 보스 히트박스(OverlapBox)가 잡는 유일한 몸통 콜라이더라, 끄면 서버가
    /// 이 플레이어를 아예 못 맞힌다. Move를 부르는 쪽(PlayerMovement)만 꺼지면 NT가 준 위치 그대로 판정된다.</para>
    /// <para>호스트 자신의 아바타(서버=오너)는 여기서 건드리지 않는다 — 호스트가 직접 조작하는 캐릭터다.</para>
    /// </remarks>
    public override void OnStartServer()
    {
        base.OnStartServer();

        if (connectionToClient == null || connectionToClient == NetworkServer.localConnection) return;

        foreach (var b in ownerOnly)
        {
            if (b)
                b.enabled = false;
        }
    }

    /// <summary>클라에 아바타가 스폰될 때 호출. 내 것이면 로컬 마을 캐릭터를 숨기고 던전 설정·레이드 줌·부활 기회·스탯 리프레시를 걸어 LocalAvatar로 등록하고, 남의 것이면 조작 컴포넌트를 끈다(순수 관찰자는 CharacterController도 끈다).</summary>
    public override void OnStartClient()
    {
        base.OnStartClient();

        // ★ 분기 전에 찍어 "이 클라에 스폰된 모든 아바타"가 로그된다(자기 것 isOwned=True + 남의 것 False).
        Debug.Log($"[진단][OwnerGate] netId={netId}, isOwned={isOwned}, isLocalPlayer={isLocalPlayer}");

        if (isOwned)
        {
            // 내 아바타: 로컬 마을 캐릭터를 숨긴다(캐릭터 둘 방지).
            if (PlayerManager.Instance != null) PlayerManager.Instance.Hide();

            // ★ 방금 스폰된 프리팹 인스턴스라 Animator가 마을 컨트롤러 기본값이다.
            //   던전 전환은 RaidGather가 '숨겨진 로컬 플레이어'에게만 걸었으므로, 조종할 이 아바타에 직접 건다.
            GetComponentInChildren<Player>(true)?.EnterDungeon();

            // ★ 이 아바타의 CameraRig는 씬 진입보다 늦게 스폰돼 GameSceneManager의 레이드 줌아웃 적용
            //   (SetZoomOutDistance, 씬 진입 시 1회)을 못 받는다 → 기본 max(10) 그대로. 레이드 전용
            //   아바타이므로 여기서 레이드 줌아웃(20)을 직접 건다.
            GetComponentInChildren<CameraRig>(true)?.ApplyRaidZoomOut();

            LocalAvatar = GetComponentInChildren<Player>(true);

            // 레이드 한 판마다 부활 기회 1회. 원격 클라는 RaidGather.Enter에서도 받지만, 호스트는 서버가 인스턴스를 직접
            // 로드해 그 진입 흐름을 타지 않아 기회를 못 받고 첫 사망에 바로 마을로 튕겼다. 내 아바타가 뜨는 순간이 곧
            // 레이드 입장이므로 여기서도 준다(최대치가 1이라 두 번 받아도 1이다).
            ReviveBudget.GrantOnDungeonEnter();

            // ★ 장비 보너스는 착용/해제 때만 재계산돼, 새로 스폰된 아바타는 레벨 기본치만 가진 채 시작한다
            //   (마을과 수치가 달라지던 원인). LocalAvatar를 세운 직후 리프레시를 요청해 InventoryManager가
            //   장비 스탯을 이 아바타에 다시 건다. 패시브는 아바타 PlayerStats.Start의 SkillState.RestoreFrom이 건다.
            PlayerEvents.FireStatsRefreshRequested();

            Debug.Log($"[진단][OwnerGate] OWNED 컨트롤러={GetComponentInChildren<Animator>(true)?.runtimeAnimatorController?.name}");

            return;
        }

        foreach (var b in ownerOnly)
        {
            if (b)
                b.enabled = false;
        }

        // 순수 관찰자 클라에서만 끈다. 호스트는 서버이기도 해서, 여기서 끄면 호스트가 돌리는 보스가 원격 플레이어의
        // 몸통을 못 맞힌다(OnStartServer 설명 참고). 관찰자 클라는 판정을 안 하므로 꺼서 내 캐릭터와 부딪히지 않게 한다.
        if (cc && !isServer) cc.enabled = false;

        // NetworkAnimator는 파라미터·State만 동기화하고, 컨트롤러 교체(마을→던전)는 동기화하지 않는다.
        // 관찰자 클라에서 이 아바타가 마을 컨트롤러인 채면 넘어온 던전 로코모션이 엉뚱한 클립에 매핑돼 안 나온다.
        // EnterDungeon() 전체(전투 입력 on + 전역 HUD 이벤트)는 관찰자 HUD를 오염시키므로, 컨트롤러 교체만 건다.
        GetComponentInChildren<PlayerAnimation>(true)?.UseDungeonController();

        Debug.Log($"[진단][OwnerGate] NON-OWNED 컨트롤러={GetComponentInChildren<Animator>(true)?.runtimeAnimatorController?.name}");
    }

    /// <summary>내 아바타가 사라지면(인스턴스 이탈·접속 종료) LocalAvatar를 비워 싱글 캐릭터로 되돌아가게 한다.</summary>
    public override void OnStopClient()
    {
        base.OnStopClient();

        // 내 아바타가 사라지면(인스턴스 이탈·접속 종료) 참조를 비워 싱글 캐릭터로 되돌아가게 한다.
        if (LocalAvatar != null && LocalAvatar.transform.IsChildOf(transform)) LocalAvatar = null;
    }

    private void AddGetComponent<T>() where T : Behaviour
    {
        T behaviour = GetComponentInChildren<T>();
        if (behaviour == null) return;

        ownerOnly.Add(behaviour);
    }
}
