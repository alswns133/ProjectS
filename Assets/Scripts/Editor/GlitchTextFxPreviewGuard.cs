// 'Editor' 폴더 필수(UnityEditor 참조). 네임스페이스는 UnityEditor.Editor 가림 회피로 EditorTools.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using ProjectS.UI;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 씬을 저장하기 직전에 <see cref="GlitchTextFx"/>의 에디터 미리보기를 걷어내, 텍스트의 머티리얼 참조가
    /// <b>빈 값으로 저장되는 것을 막는다.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// 미리보기는 TMP의 <c>fontMaterial</c> 인스턴스를 쓰고, 그 인스턴스에는 씬이 부풀지 않도록
    /// <see cref="HideFlags.DontSave"/>가 걸려 있다. 그런데 TMP는 그 인스턴스를 <b>직렬화되는 필드</b>
    /// (<c>m_sharedMaterial</c>)에 넣기 때문에, 저장 시점에는 "저장할 수 없는 오브젝트를 가리키는 참조" —
    /// 즉 <c>{fileID: 0}</c>이 기록된다.
    /// </para>
    /// <para>
    /// 그 상태의 씬을 재생하면 머티리얼이 없는 채로 시작한다. TMP도 자기 <c>Awake</c>에서 폰트 기본값으로
    /// 복구하지만 <b>같은 GameObject의 Awake 순서는 보장되지 않아</b>, 순서에 따라 글리치가 걸리기도 하고
    /// 통째로 빠지기도 한다 — "될 때도 있고 씹힐 때도 있는" 가장 찾기 어려운 형태의 버그다.
    /// (2026-08-05, 사망 팝업 TitleText에서 실제로 발생)
    /// </para>
    /// <para>
    /// 그래서 <b>저장 직전에만</b> 원래 폰트 머티리얼로 되돌린다. 미리보기는 저장이 끝나면
    /// <c>GlitchTextFx.Update</c>가 다음 프레임에 다시 만들어 주므로 작업자는 아무 차이를 느끼지 않는다.
    /// 컴포넌트 쪽에도 null 방어가 들어가 있지만, 그것은 <b>이미 잘못 저장된 씬</b>을 위한 안전망이고
    /// 애초에 잘못 저장되지 않게 하는 것이 이 클래스의 역할이다.
    /// </para>
    /// </remarks>
    [InitializeOnLoad]
    public static class GlitchTextFxPreviewGuard
    {
        static GlitchTextFxPreviewGuard()
        {
            // 짝을 맞춰 등록한다. 도메인 리로드마다 이 생성자가 다시 도는데, 먼저 빼지 않으면
            // 구독이 겹겹이 쌓여 저장 한 번에 같은 처리가 여러 번 돈다.
            EditorSceneManager.sceneSaving -= OnSceneSaving;
            EditorSceneManager.sceneSaving += OnSceneSaving;
        }

        private static void OnSceneSaving(Scene scene, string path)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                // 사망 팝업처럼 평소 꺼져 있는 UI 안에 있는 경우가 대부분이라 비활성 포함으로 찾는다.
                foreach (GlitchTextFx fx in root.GetComponentsInChildren<GlitchTextFx>(true))
                    fx.RestoreEditorPreview();
            }
        }
    }
}
