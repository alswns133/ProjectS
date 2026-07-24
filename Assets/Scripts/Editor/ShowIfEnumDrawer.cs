// 이 스크립트는 반드시 'Editor' 폴더 안에 있어야 한다(UnityEditor 참조가 플레이어 빌드로 새지 않게).
// 네임스페이스로 EditorTools를 쓰는 이유는 EnhancePanelBuilder와 같다(UnityEditor.Editor 가림 회피).
using UnityEditor;
using UnityEngine;
using ProjectS.Core;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// <see cref="ShowIfEnumAttribute"/>가 붙은 필드를 조건이 맞을 때만 그린다.
    /// 조건 enum 필드는 같은 부모(같은 컴포넌트 또는 같은 직렬화 클래스 인스턴스) 안에서 찾는다
    /// → 배열 요소마다 자기 요소의 enum 값을 보므로, 슬롯별로 다른 모드를 섞어 둘 수 있다.
    /// </summary>
    [CustomPropertyDrawer(typeof(ShowIfEnumAttribute))]
    public class ShowIfEnumDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (IsVisible(property)) return EditorGUI.GetPropertyHeight(property, label, true);

            // 0을 그대로 돌려주면 항목 사이 간격(standardVerticalSpacing)만 남아 빈 줄이 생긴다.
            // 그 간격만큼 빼서 줄이 통째로 사라지게 한다.
            return -EditorGUIUtility.standardVerticalSpacing;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (!IsVisible(property)) return;

            // true: 배열이나 중첩 클래스를 접었다 펼 수 있게 자식까지 그린다.
            EditorGUI.PropertyField(position, property, label, true);
        }

        private bool IsVisible(SerializedProperty property)
        {
            var showIf = (ShowIfEnumAttribute)attribute;
            SerializedProperty condition = FindSibling(property, showIf.EnumFieldName);

            // 필드 이름 오타나 타입 불일치처럼 설정이 잘못된 경우는 숨기지 않는다.
            // 숨기면 인스펙터에서 필드가 조용히 사라져 원인을 찾기 어렵기 때문이다.
            if (condition == null || condition.propertyType != SerializedPropertyType.Enum) return true;
            if (showIf.Values == null || showIf.Values.Length == 0) return true;

            // enumValueIndex가 아닌 intValue를 쓴다. 전자는 이름 목록의 순번이라
            // 명시적 값을 가진 enum(예: None = 0, Fire = 10)에서 비교가 어긋난다.
            int current = condition.intValue;

            for (int i = 0; i < showIf.Values.Length; i++)
            {
                if (showIf.Values[i] == current) return true;
            }

            return false;
        }

        // 같은 부모 아래의 형제 프로퍼티를 찾는다.
        // 배열 요소 안의 필드는 경로가 "attacks.Array.data[2].hitBox"처럼 되므로,
        // 마지막 '.' 뒤만 갈아끼우면 그 요소 자신의 enum 필드를 가리킨다.
        private static SerializedProperty FindSibling(SerializedProperty property, string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName)) return null;

            string path = property.propertyPath;
            int lastDot = path.LastIndexOf('.');
            string siblingPath = lastDot < 0 ? fieldName : path.Substring(0, lastDot + 1) + fieldName;

            return property.serializedObject.FindProperty(siblingPath);
        }
    }
}
