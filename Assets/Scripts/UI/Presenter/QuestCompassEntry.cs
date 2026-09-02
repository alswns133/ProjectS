using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ProjectS.UI
{
    /// <summary>
    /// 퀘스트 카드(<see cref="QuestTrackerEntry"/>) 안에 들어가는 방향/거리 표시 위젯. 한 퀘스트의 방향 화살표·거리·상태
    /// 아이콘을 보여준다. <see cref="QuestTrackerHud"/>가 매 프레임 상태를 밀어 넣는다. 표시 전용이라 목표 해석 같은
    /// 로직은 갖지 않는다(해석은 <see cref="QuestNavResolver"/>).
    ///
    /// 프리팹: <see cref="iconImage"/>와 <see cref="arrowRect"/>는 보통 같은 오브젝트를 가리킨다(아이콘을 그대로 회전).
    /// 던전 내부 상태에서는 스프라이트를 갈아 끼우고 회전을 0으로 되돌린다. 안내할 목표가 없으면 <see cref="Hide"/>로 끈다.
    /// </summary>
    public class QuestCompassEntry : MonoBehaviour
    {
        [Tooltip("나침반 표시 묶음의 루트(선택). 지정하면 Hide/표시 때 이 오브젝트를 통째로 끄고 켠다. 비우면 아이콘·거리만 끈다.")]
        [SerializeField] private GameObject root;

        [Tooltip("방향 화살표를 회전시킬 RectTransform. 보통 아이콘 이미지의 RectTransform.")]
        [SerializeField] private RectTransform arrowRect;

        [Tooltip("상태 아이콘. 화살표/던전 내부 스프라이트를 갈아 끼운다.")]
        [SerializeField] private Image iconImage;

        [Tooltip("목표까지 거리 텍스트(예: 35m). 던전 내부 상태에서는 숨긴다.")]
        [SerializeField] private TMP_Text distanceText;

        [Header("아이콘 스프라이트")]
        [Tooltip("방향을 가리키는 화살표.")]
        [SerializeField] private Sprite arrowSprite;

        [Tooltip("목표 던전 안에 있을 때 화살표 대신 보일 아이콘.")]
        [SerializeField] private Sprite inDungeonSprite;

        /// <summary>화살표 상태: 목표를 향해 회전하고 거리를 표시한다.</summary>
        /// <param name="angleDeg">카메라 기준 목표 방위각(0=화면 정면, 시계방향 +).</param>
        /// <param name="distance">목표까지 거리(m).</param>
        public void ShowArrow(float angleDeg, float distance)
        {
            SetRootActive(true);

            if (iconImage != null)
            {
                if (arrowSprite != null) iconImage.sprite = arrowSprite;
                iconImage.enabled = true;
            }

            // UI에서 위(+Y)가 화면 정면이므로, 시계방향 방위각을 Z축 음수 회전으로 바꾼다.
            if (arrowRect != null) arrowRect.localRotation = Quaternion.Euler(0f, 0f, -angleDeg);

            if (distanceText != null)
            {
                distanceText.enabled = true;
                distanceText.text = Mathf.RoundToInt(distance) + "m";
            }
        }

        /// <summary>던전 내부 상태: 화살표를 정적 아이콘으로 바꾸고 회전·거리를 끈다.</summary>
        public void ShowInDungeon()
        {
            SetRootActive(true);

            if (iconImage != null)
            {
                if (inDungeonSprite != null) iconImage.sprite = inDungeonSprite;
                iconImage.enabled = true;
            }

            if (arrowRect != null) arrowRect.localRotation = Quaternion.identity;
            if (distanceText != null) distanceText.enabled = false;
        }

        /// <summary>안내할 목표가 없을 때(레벨 도달, 게이트/웨이포인트 미배치 등) 나침반을 숨긴다.</summary>
        public void Hide()
        {
            if (root != null)
            {
                SetRootActive(false);
                return;
            }

            // 루트를 안 쓰면 개별로 끈다.
            if (iconImage != null) iconImage.enabled = false;
            if (distanceText != null) distanceText.enabled = false;
        }

        private void SetRootActive(bool value)
        {
            if (root != null && root.activeSelf != value) root.SetActive(value);
        }
    }
}
