using UnityEngine;
using ProjectS.NPCs;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// NPC 상호작용(대화·허브·리스트·보상) 동안 지정한 UI 묶음(HUD·미니맵 등)을 숨기고, 끝나면 다시 켠다.
    /// 컨트롤러에 하드 의존하지 않도록 <see cref="NpcInteractionController.ActiveChanged"/>를 구독한다
    /// (상호작용 시작=컨트롤러, 종료=null).
    ///
    /// 숨길 대상 리스트는 재사용 컨테이너 <see cref="UIGroup{T}"/>로 분리했다 — 이 컴포넌트는 "이벤트→토글"만
    /// 담당한다. <c>Transform</c> 묶음이라 타입과 무관하게 어떤 UI GameObject든 통째로 껐다 켠다
    /// (모든 GameObject엔 Transform이 1:1로 있으므로 아무 UI 오브젝트나 드래그하면 된다).
    ///
    /// 배치: 항상 켜진 오브젝트(UIManager 등)에 붙이고, 숨길 UI들을 targets에 연결한다
    /// (이 스크립트가 붙은 오브젝트는 넣지 않는다 — 자기가 꺼지면 구독이 풀려 다시 못 켠다).
    /// </summary>
    public class HideDuringNpcInteraction : MonoBehaviour
    {
        [Tooltip("상호작용 동안 숨길 UI 묶음(HUD·미니맵 등). 아무 UI 오브젝트나 드래그하면 된다.")]
        [SerializeField] private UIGroup<Transform> targets = new();

        private void OnEnable()
        {
            NpcInteractionController.ActiveChanged += OnActiveChanged;
            OnActiveChanged(NpcInteractionController.Active);   // 켜질 때 현재 상태 반영
        }

        private void OnDisable()
        {
            NpcInteractionController.ActiveChanged -= OnActiveChanged;
        }

        // 상호작용 중이면(active != null) 묶음을 끄고, 끝나면(null) 다시 켠다.
        private void OnActiveChanged(NpcInteractionController active)
        {
            targets.SetActive(active == null);
        }
    }
}
