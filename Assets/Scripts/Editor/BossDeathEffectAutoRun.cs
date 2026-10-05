// 'Editor' 폴더 필수(UnityEditor 참조). 네임스페이스는 UnityEditor.Editor 가림 회피로 EditorTools.
using UnityEditor;
using UnityEngine;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// <see cref="BossDeathEffectGenerator.Generate"/>를 <b>딱 한 번</b> 자동 실행하는 일회용 실행기.
    /// 메뉴를 사람이 누를 수 없는 상황(외부 도구가 코드만 넣어 둔 경우)에서 결과물까지 나오게 하려는 것.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>한 번만 돈다.</b> 실행 여부를 EditorPrefs에 프로젝트 경로별로 적어 두고, 이미 돌았으면
    /// 그 뒤로는 아무것도 하지 않는다. 도메인 리로드(스크립트 수정·플레이 진입)마다 폭발 프리팹을
    /// 다시 뽑아 사람이 손본 값을 덮어쓰는 일이 없어야 하기 때문이다.
    /// </para>
    /// <para>
    /// <b>delayCall로 미루는 이유.</b> InitializeOnLoadMethod는 에셋 import가 끝나기 전에 불릴 수 있다.
    /// 그 타이밍에 AssetDatabase에 쓰면 텍스처를 못 찾거나 생성이 반쯤 날아간다.
    /// </para>
    /// <para>
    /// <b>이 파일은 지워도 된다.</b> 한 번 돌고 나면 하는 일이 없다. 다시 뽑고 싶으면
    /// Tools ▸ ProjectS ▸ Generate Boss Death Effect 를 직접 누르면 된다
    /// (또는 Tools ▸ ProjectS ▸ Reset Boss Death Auto Run 으로 이 실행기를 되살린다).
    /// </para>
    /// </remarks>
    [InitializeOnLoad]
    internal static class BossDeathEffectAutoRun
    {
        // 프로젝트마다 따로 기억한다. EditorPrefs는 머신 전역이라 경로를 섞지 않으면
        // 다른 클론에서 "이미 돌았다"로 잘못 판단한다.
        private static string PrefKey => "ProjectS.BossDeathEffect.AutoRan." + Application.dataPath.GetHashCode();

        static BossDeathEffectAutoRun()
        {
            if (EditorPrefs.GetBool(PrefKey, false)) return;

            EditorApplication.delayCall += RunOnce;
        }

        private static void RunOnce()
        {
            // delayCall이 쌓여 두 번 불려도 안전하게.
            if (EditorPrefs.GetBool(PrefKey, false)) return;
            EditorPrefs.SetBool(PrefKey, true);

            Debug.Log("[BossDeathEffect] 자동 실행 — 사망 이펙트를 생성합니다. " +
                      "이후로는 자동으로 돌지 않습니다(Tools ▸ ProjectS ▸ Generate Boss Death Effect 로 수동 실행).");

            BossDeathEffectGenerator.Generate();
        }

        /// <summary>자동 실행 기록을 지운다. 다음 스크립트 리로드 때 한 번 더 자동 생성된다.</summary>
        [MenuItem("Tools/ProjectS/Reset Boss Death Auto Run")]
        private static void Reset()
        {
            EditorPrefs.DeleteKey(PrefKey);
            Debug.Log("[BossDeathEffect] 자동 실행 기록을 지웠습니다. 다음 스크립트 리로드 때 한 번 더 생성됩니다.");
        }
    }
}
