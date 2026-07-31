using System;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.NPCs;

namespace ProjectS.UI
{
    /// <summary>
    /// 허브 고유기능 버튼 하나(상점/강화/…). 씬에 미리 배치해 두고, 어떤 기능인지(<see cref="feature"/>)만
    /// 인스펙터로 지정한다. 라벨 텍스트·이미지 색은 씬에서 직접 꾸미고(기능마다 색이 정해져 있음),
    /// 이 스크립트는 클릭을 그 기능으로 전달만 한다. 허브가 열릴 때 <see cref="NpcHubView"/>가
    /// 이 버튼의 기능을 NPC가 제공하는지 보고 켜고 끈다(복제 없음 — 소수 고정 버튼이라 활성/비활성으로 처리).
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class NpcHubFeatureButton : MonoBehaviour
    {
        [Tooltip("이 버튼이 나타내는 허브 기능.")]
        [SerializeField] private NpcHubFeature feature;

        private Button button;
        private Action<NpcHubFeature> onClick;

        /// <summary>이 버튼이 나타내는 기능.</summary>
        public NpcHubFeature Feature => feature;

        private void Awake()
        {
            button = GetComponent<Button>();
            button.onClick.AddListener(() => onClick?.Invoke(feature));
        }

        /// <summary>클릭 시 호출할 콜백을 건다(허브 뷰가 1회 등록).</summary>
        /// <param name="handler">기능 선택 콜백</param>
        public void Bind(Action<NpcHubFeature> handler) => onClick = handler;
    }
}
