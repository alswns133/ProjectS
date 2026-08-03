using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

namespace ProjectS.UI
{
    /// <summary>
    /// 폼(로그인+가입)의 키보드 내비게이션을 한곳에서 담당한다. AuthRoot에 하나 두고
    /// <see cref="order"/>에 포커스 순서(필드들 + 제출 버튼)를 담는다.
    /// - 활성화 시 첫 요소 자동 포커스.
    /// - Tab / Shift+Tab: 순서대로 이동(끝에서 순환).
    /// - Enter: 다음이 필드면 이동, 다음이 버튼이면 그 버튼 제출(=마지막 필드에서 엔터→제출).
    /// 여러 폼이 한 화면에 있어도 내비게이터 하나로 다루어 포커스 충돌을 없앤다.
    /// </summary>
    public class FormNavigation : MonoBehaviour
    {
        [SerializeField] private List<Selectable> order = new();

        private void OnEnable()
        {
            for (int i = 0; i < order.Count; i++)
            {
                if (order[i] is TMP_InputField field)
                {
                    int index = i;
                    field.onSubmit.AddListener(_ => OnFieldSubmit(index));
                }
            }
            StartCoroutine(FocusFirstNextFrame());
        }

        private void OnDisable()
        {
            foreach (Selectable s in order)
                if (s is TMP_InputField field) field.onSubmit.RemoveAllListeners();
        }

        // OnEnable 프레임엔 레이아웃·EventSystem이 아직 안정되지 않을 수 있어 한 프레임 뒤 포커스한다.
        private IEnumerator FocusFirstNextFrame()
        {
            yield return null;
            if (order.Count > 0) Select(order[0]);
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (kb.tabKey.wasPressedThisFrame)
                Move(kb.shiftKey.isPressed ? -1 : +1);
        }

        // 엔터=이동: 다음이 필드면 포커스 이동, 다음이 버튼이면 그 버튼을 제출한다.
        private void OnFieldSubmit(int index)
        {
            int next = index + 1;
            if (next >= order.Count) return;

            if (order[next] is Button button) button.onClick.Invoke();
            else Select(order[next]);
        }

        private void Move(int dir)
        {
            if (order.Count == 0) return;

            int current = IndexOfSelected();
            int next = current < 0 ? 0 : (((current + dir) % order.Count) + order.Count) % order.Count;
            Select(order[next]);
        }

        private int IndexOfSelected()
        {
            GameObject sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            for (int i = 0; i < order.Count; i++)
                if (order[i] != null && order[i].gameObject == sel) return i;
            return -1;
        }

        private void Select(Selectable s)
        {
            if (s == null || EventSystem.current == null) return;

            EventSystem.current.SetSelectedGameObject(s.gameObject);
            if (s is TMP_InputField field) field.ActivateInputField();
        }
    }
}
