// Editor 폴더 안에 있어 플레이어 빌드에는 포함되지 않는다(EnhancePanelBuilder와 같은 규칙).
// 네임스페이스도 UnityEditor.Editor 가림을 피해 EditorTools를 쓴다.
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using ProjectS.UI;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 씬의 Renderer(및 Terrain)들을 감싸는 범위를 재서 미니맵의 World Center/Size(XZ)를 계산하는 도구.
    /// 손으로 맵 양 끝 좌표를 찾아 빼는 대신 버튼 한 번으로 값을 뽑고, MinimapData에 바로 써 넣는다.
    /// 메뉴: ProjectS > Minimap > 맵 범위 측정.
    /// </summary>
    public class MinimapBoundsTool : EditorWindow
    {
        // 측정에 포함할 레이어. 이펙트·UI처럼 미니맵 범위에 넣으면 안 되는 것을 빼는 데 쓴다.
        private LayerMask includeLayers = ~0;

        // 파티클은 순간적으로 멀리 튀어 범위를 부풀리므로 기본 제외한다.
        private bool skipParticles = true;

        // Terrain은 Renderer가 없어 따로 범위를 더해야 한다.
        private bool includeTerrains = true;

        private bool hasResult;
        private Vector2 resultCenter;
        private Vector2 resultSize;

        // 결과를 바로 써 넣을 대상. 비워 두면 값만 확인하고 수동으로 옮겨도 된다.
        private MinimapData target;

        [MenuItem("ProjectS/Minimap/맵 범위 측정")]
        private static void Open() => GetWindow<MinimapBoundsTool>("Minimap Bounds");

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "씬의 Renderer/Terrain을 감싸는 범위를 재서 미니맵 World Center/Size(XZ)를 계산합니다.\n" +
                "이펙트·UI 레이어는 '포함 레이어'에서 빼세요.",
                MessageType.Info);

            // LayerMask는 IMGUI에서 바로 그릴 수 없어 내부 유틸로 실제 레이어 목록과 변환한다.
            int mask = InternalEditorUtility.LayerMaskToConcatenatedLayersMask(includeLayers);
            mask = EditorGUILayout.MaskField("포함 레이어", mask, InternalEditorUtility.layers);
            includeLayers = InternalEditorUtility.ConcatenatedLayersMaskToLayerMask(mask);

            skipParticles = EditorGUILayout.Toggle("파티클 제외", skipParticles);
            includeTerrains = EditorGUILayout.Toggle("Terrain 포함", includeTerrains);

            EditorGUILayout.Space();
            if (GUILayout.Button("씬 전체 측정")) Measure(false);
            if (GUILayout.Button("선택한 오브젝트만 측정")) Measure(true);

            if (!hasResult) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("결과", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Vector2Field("World Center (X,Z)", resultCenter);
                EditorGUILayout.Vector2Field("World Size (X,Z)", resultSize);
            }

            EditorGUILayout.Space();
            target = (MinimapData)EditorGUILayout.ObjectField(
                "적용 대상 MinimapData", target, typeof(MinimapData), false);

            using (new EditorGUI.DisabledScope(target == null))
            {
                if (GUILayout.Button("MinimapData에 적용"))
                    ApplyTo(target);
            }
        }

        // 씬(또는 선택) 안의 Renderer/Terrain을 훑어 합친 범위를 구한다.
        private void Measure(bool selectionOnly)
        {
            bool has = false;
            Bounds bounds = new Bounds();

            Renderer[] renderers = selectionOnly
                ? GetComponentsInSelection<Renderer>()
                : FindObjectsByType<Renderer>(FindObjectsSortMode.None);

            foreach (Renderer r in renderers)
            {
                if (r == null || !r.enabled) continue;
                if (((1 << r.gameObject.layer) & includeLayers.value) == 0) continue;
                if (skipParticles && r is ParticleSystemRenderer) continue;

                Encapsulate(ref bounds, ref has, r.bounds);
            }

            if (includeTerrains)
            {
                Terrain[] terrains = selectionOnly
                    ? GetComponentsInSelection<Terrain>()
                    : FindObjectsByType<Terrain>(FindObjectsSortMode.None);

                foreach (Terrain t in terrains)
                {
                    if (t == null || t.terrainData == null) continue;

                    // Terrain의 transform.position은 코너(최소점)라 중심은 +size/2 지점이다.
                    Vector3 size = t.terrainData.size;
                    Encapsulate(ref bounds, ref has, new Bounds(t.transform.position + size * 0.5f, size));
                }
            }

            if (!has)
            {
                EditorUtility.DisplayDialog("Minimap Bounds",
                    "측정할 Renderer/Terrain을 찾지 못했습니다. 포함 레이어나 선택을 확인하세요.", "확인");
                hasResult = false;
                return;
            }

            // 미니맵은 XZ 평면만 쓴다. 높이(Y)는 버린다.
            resultCenter = new Vector2(bounds.center.x, bounds.center.z);
            resultSize = new Vector2(bounds.size.x, bounds.size.z);
            hasResult = true;

            Debug.Log($"[MinimapBounds] Center=({resultCenter.x:0.##}, {resultCenter.y:0.##})  " +
                      $"Size=({resultSize.x:0.##}, {resultSize.y:0.##})");
        }

        private void ApplyTo(MinimapData data)
        {
            // private [SerializeField] 필드라 SerializedObject로 접근한다(필드명은 MinimapData와 계약).
            var so = new SerializedObject(data);
            so.FindProperty("worldCenter").vector2Value = resultCenter;
            so.FindProperty("worldSize").vector2Value = resultSize;
            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(data);
            Debug.Log($"[MinimapBounds] '{data.name}'에 적용했습니다.", data);
        }

        private static void Encapsulate(ref Bounds bounds, ref bool has, Bounds add)
        {
            if (!has)
            {
                bounds = add;
                has = true;
            }
            else
            {
                bounds.Encapsulate(add);
            }
        }

        private static T[] GetComponentsInSelection<T>() where T : Component
        {
            var list = new System.Collections.Generic.List<T>();

            foreach (GameObject go in Selection.gameObjects)
                list.AddRange(go.GetComponentsInChildren<T>(true));

            return list.ToArray();
        }
    }
}
