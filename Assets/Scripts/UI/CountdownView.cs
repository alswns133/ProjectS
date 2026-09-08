using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 남은 시간을 숫자와 진행 바로 보여주는 작은 위젯. 결성창의 두 카운트다운
    /// (초대 응답 20초 · 출발 30초)이 같은 것을 쓰되 색과 문구만 다르게 둔다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>숫자만으로는 부족하다.</b> 30초는 "읽고 판단할 시간"이라 남은 양이 감각적으로 보여야 한다.
    /// 그래서 진행 바를 함께 두고, <see cref="hurryThreshold"/> 아래로 내려가면 색을 바꿔 강조한다.
    /// </para>
    /// <para>
    /// <b>스스로 시간을 세지 않는다.</b> 값을 가진 쪽이 <see cref="Set"/>로 밀어 넣는다.
    /// 위젯이 자체 타이머를 돌리면 실제 남은 시간과 화면이 조금씩 어긋나고, 창을 닫았다 열 때
    /// 어느 쪽을 믿어야 할지 모호해진다.
    /// </para>
    /// </remarks>
    public class CountdownView : MonoBehaviour
    {
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text numberText;

        [Tooltip("Image Type을 Filled로 둔다. 남은 비율만큼 채운다.")]
        [SerializeField] private Image fillImage;

        [Header("강조")]
        [Tooltip("이 시간 아래로 내려가면 색이 바뀐다.")]
        [SerializeField, Min(0f)] private float hurryThreshold = 5f;
        [SerializeField] private Color normalColor = new Color32(0x4F, 0xD8, 0xE8, 0xFF);
        [SerializeField] private Color hurryColor = new Color32(0xF0, 0xB4, 0x29, 0xFF);

        [Tooltip("아직 시작되지 않았을 때의 색. 활성 색과 한눈에 갈리는 회색을 둔다.")]
        [SerializeField] private Color inactiveColor = new Color32(0x6B, 0x70, 0x76, 0xFF);

        /// <summary>위에 찍을 문구를 바꾼다("응답 제한", "파티원 대기 중" 등).</summary>
        /// <param name="title">표시할 문구</param>
        public void SetTitle(string title)
        {
            if (titleText != null) titleText.text = title;
        }

        /// <summary>남은 시간을 반영한다. 매 프레임 불러도 된다.</summary>
        /// <param name="remaining">남은 시간(초)</param>
        /// <param name="duration">전체 시간(초). 0 이하면 진행 바를 채운 채로 둔다</param>
        public void Set(float remaining, float duration)
        {
            remaining = Mathf.Max(0f, remaining);

            if (numberText != null) numberText.text = Format(remaining);

            if (fillImage != null)
            {
                fillImage.fillAmount = duration > 0f ? Mathf.Clamp01(remaining / duration) : 1f;
            }

            // [진단] 게이지 원인 파악용(안정화 후 삭제). 0.5초마다: 들어온 값·fillImage 연결·Image Type·현재 fill.
            // ★ Image Type이 Filled가 아니면 fillAmount가 시각적으로 안 먹는다.
            if (Time.frameCount % 30 == 0)
            {
                Debug.Log($"[진단][CountdownView] Set r={remaining:0.0} d={duration:0.0} " +
                          $"fillImage={(fillImage != null)} type={(fillImage != null ? fillImage.type.ToString() : "null")} " +
                          $"fill={(fillImage != null ? fillImage.fillAmount : -1f):0.00}", this);
            }

            // 색은 숫자와 바에 함께 준다. 한쪽만 바뀌면 강조가 아니라 오류처럼 보인다.
            Color color = remaining <= hurryThreshold ? hurryColor : normalColor;
            if (numberText != null) numberText.color = color;
            if (fillImage != null) fillImage.color = color;
        }

        /// <summary>
        /// 아직 시작되지 않은 상태로 그린다. 숫자와 진행 바를 숨기지 않고 회색으로 죽여 둔다 —
        /// 창에서 사라지면 "이 창에 카운트다운이 있다"는 것 자체를 모르고, 출발을 걸었을 때
        /// 없던 요소가 튀어나와 배치가 흔들린다.
        /// </summary>
        /// <param name="duration">시작되면 돌게 될 전체 시간(초). 표시 전용이다</param>
        /// <remarks>
        /// <b>진행 바를 비우지 않고 가득 채운다.</b> 빈 바는 "이미 다 지났다"로 읽히지만,
        /// 여기서 말하려는 것은 "아직 한 칸도 안 줄었다"이기 때문이다. 색만으로 활성/비활성을 가른다.
        /// </remarks>
        public void SetInactive(float duration)
        {
            if (numberText != null)
            {
                numberText.text = Format(Mathf.Max(0f, duration));
                numberText.color = inactiveColor;
            }

            if (fillImage != null)
            {
                fillImage.fillAmount = 1f;
                fillImage.color = inactiveColor;
            }
        }

        // 30초든 20초든 한 자리로 줄지 않게 mm:ss로 고정한다. 자릿수가 변하면 숫자가 좌우로 흔들린다.
        private static string Format(float seconds)
        {
            int total = Mathf.CeilToInt(seconds);
            return $"{total / 60:00}:{total % 60:00}";
        }
    }
}
