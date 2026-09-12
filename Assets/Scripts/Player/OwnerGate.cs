using Mirror;
using ProjectS.Cameras;
using ProjectS.Managers;
using ProjectS.Players;
using ProjectS.Debugging;
using System.Collections.Generic;
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
            return;
        }

        foreach (var b in ownerOnly)
        {
            if (b)
                b.enabled = false;
        }

        if (cc) cc.enabled = false;
    }

    private void AddGetComponent<T>() where T : Behaviour
    {
        T behaviour = GetComponentInChildren<T>();
        if (behaviour == null) return;

        ownerOnly.Add(behaviour);
    }
}
