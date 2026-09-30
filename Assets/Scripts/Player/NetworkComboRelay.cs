using Mirror;
using UnityEngine;


namespace ProjectS.Players
{
    /// <summary>멀티에서 오너가 확정한 콤보 타수를 관찰자 화면에 전파해, 원격 아바타도 같은 공격 모션을 재생하게 한다.</summary>
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

