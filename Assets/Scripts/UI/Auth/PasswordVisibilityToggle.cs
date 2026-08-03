using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ProjectS.UI
{
    /// <summary>
    /// 비밀번호 필드의 표시/숨김을 토글하는 버튼(눈 아이콘 대용). 클릭마다 Password ↔ Standard 전환.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class PasswordVisibilityToggle : MonoBehaviour
    {
        [SerializeField] private TMP_InputField passwordField;
        [SerializeField] private TMP_Text label;   // "표시"/"숨김" 텍스트(선택)

        private Button button;
        private bool visible;

        private void Awake() => button = GetComponent<Button>();

        private void OnEnable()
        {
            button.onClick.AddListener(Toggle);
            Apply();
        }

        private void OnDisable() => button.onClick.RemoveListener(Toggle);

        private void Toggle()
        {
            visible = !visible;
            Apply();
        }

        private void Apply()
        {
            if (passwordField == null) return;

            passwordField.contentType = visible
                ? TMP_InputField.ContentType.Standard
                : TMP_InputField.ContentType.Password;
            passwordField.ForceLabelUpdate();   // contentType 변경을 표시에 즉시 반영

            if (label != null) label.text = visible ? "숨김" : "표시";
        }
    }
}
