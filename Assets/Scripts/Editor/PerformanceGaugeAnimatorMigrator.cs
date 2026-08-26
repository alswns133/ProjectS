// 'Editor' 폴더 필수(UnityEditor 참조). 네임스페이스는 UnityEditor.Editor 가림 회피로 EditorTools.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ProjectS.UI;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 원형 퍼포먼스 게이지의 Animator를 <c>GaugeLockFx</c>에서 프리팹 루트로 올리는 이관 툴.
    /// 메뉴: Tools ▸ ProjectS ▸ Move Performance Gauge Animator To Root
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 손으로 옮기면 안 되는가</b>: 커브 경로는 Animator가 붙은 오브젝트 기준 <b>상대 경로</b>다.
    /// Animator를 한 칸 위로 올리면 <c>PiecePivot00/Piece00</c>이 <c>GaugeLockFx/PiecePivot00/Piece00</c>으로
    /// 바뀌어야 하는데 Unity는 따라 고쳐주지 않는다 — 경고도 에러도 없이 그 커브만 죽는다.
    /// .anim을 텍스트로 고치는 것도 안 된다. 문자열 경로와 별개로 <c>m_ClipBindingConstant</c>에
    /// <b>경로 해시</b>가 박혀 있어, 문자열만 바꾸면 런타임 바인딩이 옛 경로를 가리킨 채 남는다.
    /// 그래서 <see cref="AnimationUtility"/>로 커브를 읽어 지우고 새 경로로 다시 넣는다(해시는 Unity가 다시 만든다).
    /// </para>
    /// <para>
    /// <b>오일러 회전 확인용 로그를 함께 찍는다.</b> 이 클립은 조각을 -3600도(10바퀴) 돌린다.
    /// 회전 커브가 오일러에서 쿼터니언으로 바뀌면 최단경로 보간이 되어 10바퀴가 조용히 사라지므로,
    /// 이관 전후의 값 범위를 콘솔에 남겨 눈으로 대조할 수 있게 했다.
    /// </para>
    /// <para>두 번 돌려도 안전하다 — 이미 옮겨진 클립과 Animator는 건너뛴다.</para>
    /// <para>
    /// <b>실행 뒤 남는 일</b>: 이 툴은 프리팹만 고친다. 씬 인스턴스에 남은 오버라이드(씬에서 추가된
    /// Animator, <c>lockAnimator</c> 배선)는 Inspector의 Overrides ▸ Revert All로 걷어내야 한다.
    /// </para>
    /// (2026-08-26 TH)
    /// </remarks>
    public static class PerformanceGaugeAnimatorMigrator
    {
        private const string PrefabPath = "Assets/Prefabs/UI/PerformanceGauge.prefab";

        // Animator가 지금 붙어 있는 자식. 그대로 커브 경로에 붙일 접두사가 된다.
        private const string FxChildName = "GaugeLockFx";

        private const string LogTag = "[GaugeAnimatorMigrator]";

        [MenuItem("Tools/ProjectS/Move Performance Gauge Animator To Root")]
        public static void Migrate()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null)
            {
                EditorUtility.DisplayDialog("게이지 Animator 이관",
                    $"{PrefabPath} 를 열지 못했습니다.", "확인");
                return;
            }

            try
            {
                Transform fx = root.transform.Find(FxChildName);
                if (fx == null)
                {
                    Debug.LogWarning($"{LogTag} 프리팹에서 '{FxChildName}' 자식을 찾지 못했습니다. 중단합니다.");
                    return;
                }

                Animator fxAnimator = fx.GetComponent<Animator>();
                Animator rootAnimator = root.GetComponent<Animator>();

                if (fxAnimator == null)
                {
                    Debug.Log(rootAnimator != null
                        ? $"{LogTag} Animator가 이미 루트에 있습니다. 할 일이 없습니다."
                        : $"{LogTag} '{FxChildName}'에도 루트에도 Animator가 없습니다. 중단합니다.");
                    return;
                }

                RuntimeAnimatorController controller = fxAnimator.runtimeAnimatorController;
                AnimatorUpdateMode updateMode = fxAnimator.updateMode;
                AnimatorCullingMode cullingMode = fxAnimator.cullingMode;

                if (controller == null)
                {
                    Debug.LogWarning($"{LogTag} 컨트롤러가 비어 있어 클립 경로는 손대지 않습니다.");
                }
                else
                {
                    foreach (AnimationClip clip in controller.animationClips)
                    {
                        if (clip == null) continue;

                        ReportEulerCurves(clip, "이관 전");
                        if (RebaseClip(clip, FxChildName))
                            Debug.Log($"{LogTag} '{clip.name}'의 커브 경로 앞에 '{FxChildName}/'를 붙였습니다.");
                        ReportEulerCurves(clip, "이관 후");
                    }
                }

                Object.DestroyImmediate(fxAnimator);

                if (rootAnimator == null) rootAnimator = root.AddComponent<Animator>();

                rootAnimator.runtimeAnimatorController = controller;
                rootAnimator.updateMode = updateMode;      // 결과 화면은 unscaled여야 한다. 값을 그대로 옮긴다.
                rootAnimator.cullingMode = cullingMode;
                rootAnimator.applyRootMotion = false;

                RewireView(root, rootAnimator);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();

                Debug.Log($"{LogTag} 완료. Animator가 루트('{root.name}')로 올라갔습니다. " +
                          "씬 인스턴스의 오버라이드를 Revert All 하세요.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// 게이지 뷰의 <c>lockAnimator</c> 슬롯을 새 Animator로 다시 물린다.
        /// 빠뜨리면 <c>PlayLock()</c>이 파괴된 참조를 들고 조용히 아무것도 하지 않는다.
        /// </summary>
        private static void RewireView(GameObject root, Animator animator)
        {
            PerformanceGaugeView view = root.GetComponent<PerformanceGaugeView>();
            if (view == null)
            {
                Debug.LogWarning($"{LogTag} 루트에 PerformanceGaugeView가 없어 lockAnimator를 물리지 못했습니다.");
                return;
            }

            SerializedObject so = new(view);
            SerializedProperty prop = so.FindProperty("lockAnimator");
            if (prop == null)
            {
                Debug.LogWarning($"{LogTag} PerformanceGaugeView에 lockAnimator 필드가 없습니다(리네임됨?).");
                return;
            }

            prop.objectReferenceValue = animator;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>클립의 모든 커브 경로 앞에 접두사를 붙인다. 이미 붙어 있으면 아무것도 하지 않는다.</summary>
        /// <param name="clip">고칠 클립</param>
        /// <param name="prefix">앞에 붙일 경로 조각</param>
        /// <returns>실제로 고쳤으면 true</returns>
        private static bool RebaseClip(AnimationClip clip, string prefix)
        {
            EditorCurveBinding[] floatBindings = AnimationUtility.GetCurveBindings(clip);
            EditorCurveBinding[] objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);

            if (floatBindings.Length == 0 && objectBindings.Length == 0) return false;

            if (AlreadyRebased(floatBindings, prefix) || AlreadyRebased(objectBindings, prefix))
            {
                Debug.Log($"{LogTag} '{clip.name}'은 이미 옮겨져 있어 건너뜁니다.");
                return false;
            }

            List<(EditorCurveBinding binding, AnimationCurve curve)> floats = new();
            foreach (EditorCurveBinding b in floatBindings)
                floats.Add((Rebase(b, prefix), AnimationUtility.GetEditorCurve(clip, b)));

            List<(EditorCurveBinding binding, ObjectReferenceKeyframe[] keys)> objects = new();
            foreach (EditorCurveBinding b in objectBindings)
                objects.Add((Rebase(b, prefix), AnimationUtility.GetObjectReferenceCurve(clip, b)));

            // 지우기와 넣기를 섞으면 중간 상태에서 바인딩이 겹칠 수 있다. 전부 지운 뒤 전부 넣는다.
            foreach (EditorCurveBinding b in floatBindings) AnimationUtility.SetEditorCurve(clip, b, null);
            foreach (EditorCurveBinding b in objectBindings) AnimationUtility.SetObjectReferenceCurve(clip, b, null);

            foreach ((EditorCurveBinding binding, AnimationCurve curve) entry in floats)
                AnimationUtility.SetEditorCurve(clip, entry.binding, entry.curve);
            foreach ((EditorCurveBinding binding, ObjectReferenceKeyframe[] keys) entry in objects)
                AnimationUtility.SetObjectReferenceCurve(clip, entry.binding, entry.keys);

            EditorUtility.SetDirty(clip);
            return true;
        }

        private static bool AlreadyRebased(EditorCurveBinding[] bindings, string prefix)
        {
            foreach (EditorCurveBinding b in bindings)
            {
                if (b.path == prefix) return true;
                if (b.path != null && b.path.StartsWith(prefix + "/")) return true;
            }

            return false;
        }

        // EditorCurveBinding은 구조체라 값 복사본을 고쳐 돌려준다.
        // 빈 경로(= Animator 자기 자신을 애니메이트하는 커브)는 접두사 그 자체가 된다.
        private static EditorCurveBinding Rebase(EditorCurveBinding binding, string prefix)
        {
            binding.path = string.IsNullOrEmpty(binding.path) ? prefix : $"{prefix}/{binding.path}";
            return binding;
        }

        /// <summary>
        /// 오일러 회전 커브의 값 범위를 찍는다. -3600 같은 여러 바퀴 회전이 살아있는지 대조하는 용도다.
        /// </summary>
        private static void ReportEulerCurves(AnimationClip clip, string tag)
        {
            foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip))
            {
                if (!b.propertyName.Contains("Euler")) continue;

                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, b);
                if (curve == null || curve.length == 0) continue;

                float min = float.MaxValue;
                float max = float.MinValue;
                foreach (Keyframe key in curve.keys)
                {
                    min = Mathf.Min(min, key.value);
                    max = Mathf.Max(max, key.value);
                }

                string path = string.IsNullOrEmpty(b.path) ? "(루트)" : b.path;
                Debug.Log($"{LogTag} {tag}  {path} : {b.propertyName} = {min:0.#} ~ {max:0.#}");
            }
        }
    }
}
