#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DoubleL
{
    public class WeaponBoneRetargeter : EditorWindow
    {
        public AnimationClip sourceClip;
        public GameObject sourceRoot;
        public GameObject targetRoot;
        public string sourceWeaponName = "weapon_r";
        public string targetWeaponName = "weapon_r_socket";

        [MenuItem("Tools/DoubleL/Weapon Bone Retargeter")]
        public static void ShowWindow() => GetWindow<WeaponBoneRetargeter>("Weapon Bone Retargeter");

        void OnGUI()
        {
            GUILayout.Label("Inverse Transformation", EditorStyles.boldLabel);
            sourceClip = (AnimationClip)EditorGUILayout.ObjectField("Source AnimationClip", sourceClip, typeof(AnimationClip), false);
            sourceRoot = (GameObject)EditorGUILayout.ObjectField("Source character (Scene)", sourceRoot, typeof(GameObject), true);
            targetRoot = (GameObject)EditorGUILayout.ObjectField("Target character (Scene)", targetRoot, typeof(GameObject), true);

            EditorGUILayout.Space();
            GUILayout.Label("Weapon Bone Name", EditorStyles.boldLabel);
            sourceWeaponName = EditorGUILayout.TextField("Source Weapon Bone Name", sourceWeaponName);
            targetWeaponName = EditorGUILayout.TextField("Target Weapon Bone Name", targetWeaponName);

            EditorGUILayout.Space();
            if (GUILayout.Button("Run conversion", GUILayout.Height(40)))
            {
                if (sourceClip && sourceRoot && targetRoot) ExecuteBake();
            }
        }

        void ExecuteBake()
        {
            Transform sWeapon = FindRecursive(sourceRoot.transform, sourceWeaponName);
            Transform tWeapon = FindRecursive(targetRoot.transform, targetWeaponName);
            if (!sWeapon || !tWeapon) { Debug.LogError("Bone not found."); return; }

            Transform tParent = tWeapon.parent;
            string tPath = GetFixedRelativePath(targetRoot.transform, tWeapon);

            AnimationClip newClip = Object.Instantiate(sourceClip);
            newClip.name = sourceClip.name + "_Retarget";

            ClearExistingCurves(newClip, tPath);

            List<Keyframe> px = new List<Keyframe>(), py = new List<Keyframe>(), pz = new List<Keyframe>();
            List<Keyframe> rx = new List<Keyframe>(), ry = new List<Keyframe>(), rz = new List<Keyframe>(), rw = new List<Keyframe>();

            float fps = sourceClip.frameRate;
            int totalFrames = Mathf.CeilToInt(sourceClip.length * fps);

            for (int i = 0; i <= totalFrames; i++)
            {
                float time = i / fps;
                sourceClip.SampleAnimation(sourceRoot, time);
                sourceClip.SampleAnimation(targetRoot, time);

                Vector3 worldPos = sWeapon.position;
                Quaternion worldRot = sWeapon.rotation;

                Vector3 finalLocalPos = tParent.InverseTransformPoint(worldPos);
                Quaternion finalLocalRot = Quaternion.Inverse(tParent.rotation) * worldRot;

                finalLocalPos = new Vector3(
                    Mathf.Round(finalLocalPos.x * 100000f) / 100000f,
                    Mathf.Round(finalLocalPos.y * 100000f) / 100000f,
                    Mathf.Round(finalLocalPos.z * 100000f) / 100000f
                );

                px.Add(new Keyframe(time, finalLocalPos.x)); py.Add(new Keyframe(time, finalLocalPos.y)); pz.Add(new Keyframe(time, finalLocalPos.z));
                rx.Add(new Keyframe(time, finalLocalRot.x)); ry.Add(new Keyframe(time, finalLocalRot.y)); rz.Add(new Keyframe(time, finalLocalRot.z)); rw.Add(new Keyframe(time, finalLocalRot.w));
            }

            ApplyPerfectCurve(newClip, tPath, px, py, pz, rx, ry, rz, rw);

            AssetDatabase.CreateAsset(newClip, "Assets/" + newClip.name + ".anim");
            AssetDatabase.SaveAssets();
            Debug.Log("Conversion complete!");
        }

        void ApplyPerfectCurve(AnimationClip clip, string path, List<Keyframe> px, List<Keyframe> py, List<Keyframe> pz, List<Keyframe> rx, List<Keyframe> ry, List<Keyframe> rz, List<Keyframe> rw)
        {
            SetCurve(clip, path, "m_LocalPosition.x", px); SetCurve(clip, path, "m_LocalPosition.y", py); SetCurve(clip, path, "m_LocalPosition.z", pz);
            SetCurve(clip, path, "m_LocalRotation.x", rx); SetCurve(clip, path, "m_LocalRotation.y", ry); SetCurve(clip, path, "m_LocalRotation.z", rz); SetCurve(clip, path, "m_LocalRotation.w", rw);
        }

        void SetCurve(AnimationClip clip, string path, string prop, List<Keyframe> keys)
        {
            AnimationCurve curve = new AnimationCurve(keys.ToArray());
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);

                if (i > 0 && Mathf.Approximately(curve.keys[i].value, curve.keys[i - 1].value))
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
                    AnimationUtility.SetKeyRightTangentMode(curve, i - 1, AnimationUtility.TangentMode.Constant);
                }
            }
            clip.SetCurve(path, typeof(Transform), prop, curve);
        }

        void ClearExistingCurves(AnimationClip clip, string path)
        {
            string[] props = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z", "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
            foreach (var p in props) AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), p), null);
        }

        string GetFixedRelativePath(Transform root, Transform target)
        {
            List<string> elements = new List<string>(); Transform current = target;
            while (current != null && current != root) { elements.Add(current.name); current = current.parent; }
            elements.Reverse(); return string.Join("/", elements);
        }

        Transform FindRecursive(Transform p, string n) { if (p.name == n) return p; foreach (Transform c in p) { var r = FindRecursive(c, n); if (r) return r; } return null; }
    }
}
#endif