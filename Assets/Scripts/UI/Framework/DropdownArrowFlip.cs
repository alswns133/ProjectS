using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ProjectS.UI.Framework
{
    /// <summary>
    /// 드롭다운 화살표를 접힘=아래(▼), 펼침=위(▲)로 돌린다. TMP_Dropdown과 같은 오브젝트에 붙인다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TMP_Dropdown은 펼침/닫힘 이벤트가 없어 매 프레임 상태를 보고, 바뀐 순간에만 회전을 적용한다.
    /// </para>
    /// <para>
    /// <b>펼침 판정에 <c>IsExpanded</c>만 쓰지 않는 이유:</b> <c>IsExpanded</c>는 목록 복제본이 실제로 파괴될 때까지
    /// true라, 항목을 고른 뒤 페이드아웃(기본 0.15초) 동안 화살표가 위를 향한 채 늦게 돌아온다.
    /// 그래서 "EventSystem 선택이 펼친 목록 안에 있는가"를 함께 본다 — 펼칠 때 TMP가 현재 항목을 선택하고,
    /// 닫을 때(<c>Hide</c>) 선택을 드롭다운 자신으로 되돌리므로 닫히는 순간 바로 아래로 돌아온다.
    /// 목록은 드롭다운의 자식으로 생성되고 Template은 꺼져 있어, "드롭다운 자신을 뺀 자손이 선택됨" = 목록 안이다.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(TMP_Dropdown))]
    public class DropdownArrowFlip : MonoBehaviour
    {
        [Tooltip("돌릴 화살표. 비우면 자식 중 'Arrow'를 찾는다(Dropdown - TextMeshPro 기본 구조).")]
        [SerializeField] private RectTransform arrow;

        [Tooltip("접혀 있을 때 Z 회전. 기본 화살표 스프라이트가 아래를 향하므로 0.")]
        [SerializeField] private float collapsedZ = 0f;

        [Tooltip("펼쳤을 때 Z 회전. 180이면 위를 향한다.")]
        [SerializeField] private float expandedZ = 180f;

        private TMP_Dropdown dropdown;
        private bool wasExpanded;

        private void Awake()
        {
            dropdown = GetComponent<TMP_Dropdown>();
            if (arrow == null) arrow = transform.Find("Arrow") as RectTransform;
            if (arrow == null) Debug.LogWarning($"[DropdownArrowFlip] {name}: 화살표를 못 찾음 — arrow 슬롯을 지정하세요.", this);
        }

        private void OnEnable()
        {
            // 꺼졌다 켜지면 목록은 이미 닫혀 있으므로 접힘으로 맞춰 시작한다.
            wasExpanded = false;
            Apply(false);
        }

        // EventSystem(선택 변경)과 다른 스크립트의 Update가 끝난 뒤에 상태를 읽도록 LateUpdate에서 본다.
        private void LateUpdate()
        {
            bool expanded = IsListOpen();
            if (expanded == wasExpanded) return;

            wasExpanded = expanded;
            Apply(expanded);
        }

        private bool IsListOpen()
        {
            if (!dropdown.IsExpanded) return false;

            EventSystem eventSystem = EventSystem.current;
            GameObject selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
            return selected != null && selected != gameObject && selected.transform.IsChildOf(transform);
        }

        private void Apply(bool expanded)
        {
            if (arrow != null) arrow.localEulerAngles = new Vector3(0f, 0f, expanded ? expandedZ : collapsedZ);
        }
    }
}
