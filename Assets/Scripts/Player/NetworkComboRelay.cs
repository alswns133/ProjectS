using Mirror;
using UnityEngine;


namespace ProjectS.Players
{
    public class NetworkComboRelay : NetworkBehaviour
    {
        private PlayerAnimation anim;

        private void Awake()
        {
            anim = GetComponent<PlayerAnimation>();
        }

        /// <summary>오너가 콤보 타수를 확정할 때 호출. 오너만 서버로 올린다.</summary>
        public void BroadcastAttackStep(int step)
        {
            Debug.Log($"[진단][Combo] BroadcastAttackStep step={step}, isOwned={isOwned}");
            if (!isOwned) return;
            CmdAttackStep(step);
        }

        [Command]
        private void CmdAttackStep(int step) => RpcAttackStep(step);

        // includeOwner=false: 오너는 이미 로컬 콤보로 재생 중 → 중복 방지, 관찰자에게만.
        [ClientRpc(includeOwner = false)]
        private void RpcAttackStep(int step) => anim.PlayAttackStepNetworked(step);
    }
}

