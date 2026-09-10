using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 파티 현황 HUD의 파티원 한 칸. 기획서 2-2의 ① UI_MP_011(초상화+HP바) ·
    /// ② UI_MP_012(이름+레벨) · ③ UI_MP_013(사망 슬롯)을 한 덩어리로 담는다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>표시만 한다.</b> 파티 구성과 HP 동기화는 네트워크(Mirror) 쪽이 맡고, 이 컴포넌트는
    /// <see cref="SetMember"/>·<see cref="SetHp"/>·<see cref="SetDead"/>로 받은 값을 그리기만 한다.
    /// 여기서 직접 <c>PlayerStats</c>를 찾아 읽지 않는 이유는 파티원이 원격 플레이어라
    /// 로컬에 그 스탯 객체가 없기 때문이다 — 찾아 읽는 코드는 싱글 테스트에서만 동작하고
    /// 실제 파티에서는 조용히 자기 자신의 HP를 그린다.
    /// </para>
    /// <para>
    /// 슬롯은 <see cref="PartyStatusView"/>가 켜고 끈다. 직접 SetActive하지 말 것 —
    /// 파티가 비었을 때 뷰 전체를 숨기는 판단이 한 곳에 모여 있어야 한다.
    /// </para>
    /// </remarks>
    public class PartyMemberSlot : MonoBehaviour
    {
        [Header("① UI_MP_011 — 초상화 + HP바")]
        [Tooltip("파티원 초상화. 스프라이트가 없으면 기본 색만 남는다.")]
        [SerializeField] private Image portrait;

        [Tooltip("Image Type = Filled(Horizontal). fillAmount로 남은 HP 비율을 그린다.")]
        [SerializeField] private Image hpFill;

        [Tooltip("SG(자원) 바. HP 바와 같은 Filled(Horizontal). 비우면 SG 없이 HP만 그린다.")]
        [SerializeField] private Image sgFill;

        [Header("% 표기(선택)")]
        [Tooltip("HP 바 위 % 텍스트(이미지 목업). 비우면 게이지만 그린다.")]
        [SerializeField] private TMP_Text hpPercentText;
        [Tooltip("SG 바 위 % 텍스트. 비우면 게이지만 그린다.")]
        [SerializeField] private TMP_Text sgPercentText;

        [Header("② UI_MP_012 — 이름 + 레벨")]
        [Tooltip("레벨을 이름과 따로 윗줄에 둘 때 쓴다(이미지 목업: Lv 위·닉네임 아래). "
               + "비우면 nameText 한 줄에 이름·레벨을 합쳐 그린다.")]
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private TMP_Text nameText;

        [Header("③ UI_MP_013 — 사망 처리")]
        [Tooltip("사망처리할 이펙트의 오브젝트")]
        [SerializeField] private GameObject dieEffectObj;


        // {0}=이름, {1}=레벨. 기획서 목업의 "파티원 이름 · Lv" 표기(levelText를 비웠을 때의 한 줄 표기).
        private const string NameFormat = "{0} · Lv.{1}";

        // 0~1 → "100%". 게이지 위 표기(이미지 목업). 반올림해 99.6%가 "100%"로 보이게 한다.
        private static string Percent(float ratio) => $"{Mathf.RoundToInt(Mathf.Clamp01(ratio) * 100f)}%";

        /// <summary>이 슬롯이 사망 표시 상태인가. 부활 투표 팝업을 띄울지 판단하는 쪽에서 읽는다.</summary>
        public bool IsDead { get; private set; }

        /// <summary>
        /// 슬롯에 파티원을 앉힌다. HP는 <see cref="SetHp"/>로 따로 갱신한다
        /// (입장 직후에는 풀피가 아닐 수도 있어 여기서 1로 가정하지 않는다).
        /// </summary>
        /// <param name="memberName">파티원 닉네임</param>
        /// <param name="level">파티원 레벨</param>
        /// <param name="portraitSprite">초상화. null이면 기존 스프라이트를 유지한다.</param>
        public void SetMember(string memberName, int level, Sprite portraitSprite = null)
        {
            // levelText가 있으면 이미지 목업처럼 두 줄로 나눠 그린다(Lv 윗줄·닉네임 아랫줄).
            // 없으면 기존처럼 nameText 한 줄에 이름·레벨을 합쳐 그린다.
            if (levelText != null)
            {
                levelText.text = $"Lv.{level}";
                if (nameText != null) nameText.text = memberName;
            }
            else if (nameText != null)
            {
                nameText.text = string.Format(NameFormat, memberName, level);
            }

            if (portraitSprite != null && portrait != null)
                portrait.sprite = portraitSprite;

            SetDead(false);
        }

        /// <summary>남은 HP 비율을 그린다.</summary>
        /// <param name="ratio">0~1. 범위를 벗어난 값도 안전하게 클램프한다.</param>
        public void SetHp(float ratio)
        {
            ratio = Mathf.Clamp01(ratio);
            if (hpFill != null) hpFill.fillAmount = ratio;
            if (hpPercentText != null) hpPercentText.text = Percent(ratio);
        }

        /// <summary>남은 SG(자원) 비율을 그린다. sgFill을 비워 두면 아무 것도 하지 않는다.</summary>
        /// <param name="ratio">0~1. 범위를 벗어난 값도 안전하게 클램프한다.</param>
        public void SetSg(float ratio)
        {
            ratio = Mathf.Clamp01(ratio);
            if (sgFill != null) sgFill.fillAmount = ratio;
            if (sgPercentText != null) sgPercentText.text = Percent(ratio);
        }

        /// <summary>
        /// ③ UI_MP_013. 사망하면 슬롯을 어둡게 하고 HP바를 비운 뒤 '사망' 표기를 띄운다.
        /// 부활하면 같은 메서드에 false를 넘겨 되돌린다.
        /// </summary>
        /// <param name="dead">사망 상태인가</param>
        public void SetDead(bool dead)
        {
            IsDead = dead;
            // 사망 시 두 바를 모두 비운다. 되살아날 때의 값은 SetHp/SetSg를 다시 받아 채운다.
            if (dead)
            {
                SetHp(0f);
                SetSg(0f);
                dieEffectObj.SetActive(dead);
            }
        }
    }
}
