using Mirror;
using ProjectS.Cameras;
using ProjectS.Managers;
using ProjectS.Players;
using ProjectS.Debugging;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

public class OwnerGate : NetworkBehaviour
{
    private readonly List<Behaviour> ownerOnly = new(); // Input·Movement·Combat·PlayerAnimation·카메라
    private CharacterController cc;

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

            Debug.Log($"[진단][OwnerGate] OWNED 컨트롤러={GetComponentInChildren<Animator>(true)?.runtimeAnimatorController?.name}");

            return;
        }

        foreach (var b in ownerOnly)
        {
            if (b)
                b.enabled = false;
        }

        if (cc) cc.enabled = false;

        // NetworkAnimator는 파라미터·State만 동기화하고, 컨트롤러 교체(마을→던전)는 동기화하지 않는다.
        // 관찰자 클라에서 이 아바타가 마을 컨트롤러인 채면 넘어온 던전 로코모션이 엉뚱한 클립에 매핑돼 안 나온다.
        // EnterDungeon() 전체(전투 입력 on + 전역 HUD 이벤트)는 관찰자 HUD를 오염시키므로, 컨트롤러 교체만 건다.
        GetComponentInChildren<PlayerAnimation>(true)?.UseDungeonController();

        Debug.Log($"[진단][OwnerGate] NON-OWNED 컨트롤러={GetComponentInChildren<Animator>(true)?.runtimeAnimatorController?.name}");
    }

    private void AddGetComponent<T>() where T : Behaviour
    {
        T behaviour = GetComponentInChildren<T>();
        if (behaviour == null) return;

        ownerOnly.Add(behaviour);
    }
}
