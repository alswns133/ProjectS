using System;
using UnityEngine;
using UnityEngine.Serialization;
using ProjectS.Data;

namespace ProjectS.UI
{
    /// <summary>
    /// 아이템 등급의 겉모습(외곽 라인·내부 글로우 색, 레어 이상 테두리 이펙트의 색·머티리얼)을 한곳에 모은 설정 에셋.
    /// 인벤 슬롯·퀘스트 보상 칸·던전 결과 보상 칸이 같은 에셋을 가리켜, 등급 색을 바꿀 때 프리팹마다
    /// 따로 고치다 어긋나는 일을 막는다. 각 슬롯의 <see cref="ItemSlotGradeView"/>가 참조한다.
    /// </summary>
    /// <remarks>
    /// JSON 테이블이 아니라 에셋인 이유: 기획 밸런스 수치가 아니라 머티리얼 참조가 낀 연출 값이라
    /// 인스펙터에 남기는 쪽(데이터/에셋 운영 방침)에 해당한다.
    /// </remarks>
    [CreateAssetMenu(fileName = "ItemGradeStyle", menuName = "ProjectS/UI/Item Grade Style")]
    public class ItemGradeStyle : ScriptableObject
    {
        /// <summary>
        /// 등급 하나의 이펙트 표현. 색은 Image 색으로, 굵기·무늬·속도 같은 쉐이더 프로퍼티는 머티리얼로 가른다.
        /// 프로퍼티를 슬롯마다 인스턴스 머티리얼에 써넣지 않고 등급별 머티리얼 에셋을 갈아 끼우는 이유는,
        /// 같은 등급 슬롯끼리 머티리얼을 공유해 배칭이 유지되고, 쉐이더 토글(키워드) 변형도 빌드에 포함되기 때문이다.
        /// </summary>
        [Serializable]
        public struct SpecialStyle
        {
            [Tooltip("이펙트 색. 쉐이더가 Image 색을 그대로 선 색으로 쓴다.")]
            public Color color;

            [Tooltip("이 등급에서 쓸 머티리얼. 쉐이더 프로퍼티 값은 이 머티리얼 에셋에서 조정한다. 비우면 슬롯의 현재 머티리얼을 유지한다.")]
            public Material material;
        }

        [Header("기본 슬롯 색")]
        [Tooltip("아이템이 없는 칸(빈 슬롯, 골드·경험치 같은 비아이템 보상)의 외곽 라인 색. 내부 글로우는 빈칸에서 꺼진다.")]
        [SerializeField] private Color defaultColor = new Color(0f, 0.562f, 0.902f, 1f);

        [Header("등급별 외곽 라인 색 (Frame)")]
        [FormerlySerializedAs("normalColor")]
        [SerializeField] private Color normalFrameColor = new Color32(0xAD, 0xAD, 0xAD, 0xFF);
        [FormerlySerializedAs("magicColor")]
        [SerializeField] private Color magicFrameColor = new Color32(0x00, 0x1F, 0x4D, 0xFF);
        [FormerlySerializedAs("rareColor")]
        [SerializeField] private Color rareFrameColor = new Color32(0x25, 0x00, 0x3F, 0xFF);
        [FormerlySerializedAs("relicColor")]
        [SerializeField] private Color relicFrameColor = new Color32(0x3F, 0x07, 0x00, 0xFF);

        [Header("등급별 내부 글로우 색 (Light)")]
        [SerializeField] private Color normalLightColor = new Color32(0xAD, 0xAD, 0xAD, 0xFF);
        [SerializeField] private Color magicLightColor = new Color32(0x00, 0x1F, 0x4D, 0xFF);
        [SerializeField] private Color rareLightColor = new Color32(0x25, 0x00, 0x3F, 0xFF);
        [SerializeField] private Color relicLightColor = new Color32(0x3F, 0x07, 0x00, 0xFF);

        [Header("특수 이펙트 (레어 이상)")]
        [SerializeField] private SpecialStyle rareSpecial = new SpecialStyle { color = new Color(0.6f, 0.2f, 1f, 1f) };
        [SerializeField] private SpecialStyle relicSpecial = new SpecialStyle { color = new Color(1f, 0.28f, 0f, 1f) };

        /// <summary>
        /// 인스펙터에서 값이 바뀌었을 때 발행된다(에디터 전용 경로). 플레이 중 색을 조정하면
        /// 이미 떠 있는 슬롯들이 바로 다시 그리게 하기 위함이다. 빌드에서는 발행되지 않는다.
        /// </summary>
        public event Action OnChanged;

        /// <summary>아이템이 없는 칸의 외곽 라인 색(빈 슬롯, 비아이템 보상).</summary>
        public Color DefaultColor => defaultColor;

        /// <summary>등급에 해당하는 슬롯 외곽 라인(Frame) 색.</summary>
        /// <param name="grade">아이템 등급</param>
        /// <returns>외곽 라인 색</returns>
        public Color FrameColor(ItemGrade grade)
        {
            switch (grade)
            {
                case ItemGrade.Magic: return magicFrameColor;
                case ItemGrade.Rare: return rareFrameColor;
                case ItemGrade.Relic: return relicFrameColor;
                default: return normalFrameColor;
            }
        }

        /// <summary>등급에 해당하는 슬롯 내부 글로우(Light) 색.</summary>
        /// <param name="grade">아이템 등급</param>
        /// <returns>내부 글로우 색</returns>
        public Color LightColor(ItemGrade grade)
        {
            switch (grade)
            {
                case ItemGrade.Magic: return magicLightColor;
                case ItemGrade.Rare: return rareLightColor;
                case ItemGrade.Relic: return relicLightColor;
                default: return normalLightColor;
            }
        }

        /// <summary>등급에 테두리 이펙트가 있으면 그 표현을 돌려준다(레어 이상만 있음).</summary>
        /// <param name="grade">아이템 등급</param>
        /// <param name="style">이펙트 색·머티리얼</param>
        /// <returns>이펙트를 켜야 하는 등급이면 true</returns>
        public bool TryGetSpecial(ItemGrade grade, out SpecialStyle style)
        {
            switch (grade)
            {
                case ItemGrade.Rare:
                    style = rareSpecial;
                    return true;
                case ItemGrade.Relic:
                    style = relicSpecial;
                    return true;
                default:
                    style = default;
                    return false;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            OnChanged?.Invoke();
        }
#endif
    }
}
