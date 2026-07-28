using System;
using UnityEngine;

namespace ProjectS.Core
{
    /// <summary>
    /// 같은 오브젝트(또는 같은 직렬화 클래스) 안의 enum 필드가 특정 값일 때만
    /// 인스펙터에 이 필드를 표시한다. 값이 다르면 줄 자체가 사라진다.
    /// <para>
    /// 용도: 한 클래스가 여러 모드를 겸할 때 모드별로 필요한 설정만 보여주기 위함이다.
    /// 모드마다 클래스를 나누는 대신 이 방식을 쓰는 이유는 Unity 직렬화가 다형성을 지원하지 않아
    /// 클래스를 나누려면 <c>[SerializeReference]</c>가 필요하고, 그러면 직렬화 포맷이 바뀌어
    /// 기존 프리팹에 저장된 값이 전부 사라지기 때문이다. 데이터 구조가 아니라 표시의 문제로 푼다.
    /// </para>
    /// <para>
    /// 비교는 enum의 '기반 정수 값'으로 한다. 사용 예:
    /// <c>[ShowIfEnum(nameof(kind), (int)AttackKind.Melee)] public Transform hitBox;</c>
    /// </para>
    /// <para>
    /// 주의: 숨겨진 필드도 값은 그대로 직렬화되어 남는다. 표시만 감추는 것이지 초기화하지 않는다
    /// → 모드를 바꿨다가 되돌리면 이전에 넣어 둔 참조가 그대로 돌아온다.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public class ShowIfEnumAttribute : PropertyAttribute
    {
        /// <summary>조건으로 볼 enum 필드의 이름. 오타 방지를 위해 <c>nameof</c>로 넘긴다.</summary>
        public string EnumFieldName { get; }

        /// <summary>이 값들 중 하나와 같을 때만 표시한다. 여러 모드에서 공유하는 필드를 위해 복수로 받는다.</summary>
        public int[] Values { get; }

        /// <param name="enumFieldName">조건으로 볼 enum 필드 이름(<c>nameof</c> 권장).</param>
        /// <param name="values">표시할 enum 값들. <c>(int)EnumType.Member</c> 형태로 넘긴다.</param>
        public ShowIfEnumAttribute(string enumFieldName, params int[] values)
        {
            EnumFieldName = enumFieldName;
            Values = values;
        }
    }
}
