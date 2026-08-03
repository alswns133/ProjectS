using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

namespace ProjectS.UI
{
    /// <summary>
    /// 비밀번호 입력 중 Caps Lock이 켜져 있으면 경고를 표시한다.
    /// Caps Lock '상태' 감지는 크로스플랫폼 API가 없어 Windows에서만 동작한다(타깃이 PC라 충분).
    /// </summary>
    public class CapsLockIndicator : MonoBehaviour
    {
        [SerializeField] private GameObject warning;                 // 경고 표시 오브젝트(텍스트 등). 기본 비활성.
        [SerializeField] private TMP_InputField[] passwordFields;    // 이 필드들 중 하나가 포커스일 때만 경고

        private void Update()
        {
            if (warning == null) return;
            warning.SetActive(IsCapsLockOn() && IsAnyPasswordFocused());
        }

        private bool IsAnyPasswordFocused()
        {
            GameObject sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (sel == null || passwordFields == null) return false;

            foreach (TMP_InputField f in passwordFields)
                if (f != null && f.gameObject == sel) return true;

            return false;
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetKeyState(int keyCode);

        // 0x14 = VK_CAPITAL. 최하위 비트가 토글(잠금) 상태.
        private static bool IsCapsLockOn() => (GetKeyState(0x14) & 1) != 0;
#else
        private static bool IsCapsLockOn() => false;
#endif
    }
}
