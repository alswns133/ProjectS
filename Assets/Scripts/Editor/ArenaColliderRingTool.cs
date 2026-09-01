using UnityEditor;
using UnityEngine;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 레이드 아레나 경계를 두르는 Box Collider 링을 생성하는 도구.
    /// Tools ▸ ProjectS ▸ Arena Collider Ring.
    ///
    /// 배리어 시각은 원통 메시 하나로 두고 물리만 박스 여러 개로 나누는 구성을 전제한다.
    /// 이유는 ProjectS.Effects.ArenaBarrier가 Collider.ClosestPoint로 발광 지점을 구하기 때문이다.
    ///   - Convex MeshCollider는 속이 꽉 찬 원기둥이 되어 플레이어를 밖으로 밀어낸다.
    ///   - Convex가 아닌 MeshCollider는 ClosestPoint가 입력을 그대로 돌려줘 발광이 몸에 붙는다.
    /// 링 안쪽 플레이어가 각 박스의 "바깥"에 있어야 표면 좌표가 제대로 나온다.
    ///
    /// 박스는 각도가 아니라 호 길이로 등간격 배치한다. 타원에서 각도 등간격으로 놓으면
    /// 곡률이 큰 양 끝에서 박스가 성기게 깔려 오차가 커지기 때문이다.
    /// </summary>
    public class ArenaColliderRingTool : EditorWindow
    {
        // 둘레와 호 길이를 적분할 표본 수. 이 정도면 100 유닛대 아레나에서 오차가 보이지 않는다.
        private const int SampleCount = 2048;

        private Transform parent;
        private float radiusX = 65f;
        private float radiusZ = 65f;
        private int count = 20;
        private float height = 12f;
        private float thickness = 0.5f;
        private float overlap = 0.08f;
        private float yOffset;
        private bool clearExisting = true;

        private float[] cumulativeArc;
        private float perimeter;

        [MenuItem("Tools/ProjectS/Arena Collider Ring")]
        private static void Open()
        {
            GetWindow<ArenaColliderRingTool>("Arena Collider Ring").minSize = new Vector2(340f, 430f);
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            if (parent == null) parent = Selection.activeTransform;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "배리어 콜라이더 전용 부모를 지정하세요. 생성물이 그 자식으로 들어갑니다.\n" +
                "원을 만들려면 X와 Z 반지름을 같게 두면 됩니다.",
                MessageType.None);

            EditorGUILayout.Space();
            parent = (Transform)EditorGUILayout.ObjectField("부모", parent, typeof(Transform), true);

            if (GUILayout.Button("선택한 오브젝트를 부모로"))
            {
                parent = Selection.activeTransform;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("타원", EditorStyles.boldLabel);
            radiusX = EditorGUILayout.FloatField("X 반지름", radiusX);
            radiusZ = EditorGUILayout.FloatField("Z 반지름", radiusZ);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("박스", EditorStyles.boldLabel);
            count = EditorGUILayout.IntSlider("개수", count, 3, 128);
            height = EditorGUILayout.FloatField("높이", height);
            thickness = EditorGUILayout.FloatField("두께", thickness);
            overlap = EditorGUILayout.Slider("겹침 비율", overlap, 0f, 0.5f);
            yOffset = EditorGUILayout.FloatField("바닥 Y 오프셋", yOffset);

            EditorGUILayout.Space();
            clearExisting = EditorGUILayout.Toggle("생성 전 기존 자식 삭제", clearExisting);

            EditorGUILayout.Space();
            DrawReport();

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!IsValid()))
            {
                if (GUILayout.Button("생성", GUILayout.Height(30f)))
                {
                    Generate();
                }
            }

            if (!IsValid())
            {
                EditorGUILayout.HelpBox("부모를 지정하고 반지름·높이·두께를 0보다 크게 설정하세요.", MessageType.Warning);
            }
        }

        private bool IsValid()
        {
            return parent != null && radiusX > 0.01f && radiusZ > 0.01f
                && height > 0.01f && thickness > 0.001f && count >= 3;
        }

        /// <summary>
        /// 지금 설정으로 생기는 최대 오차를 미리 알려준다.
        /// 이 값이 셰이더의 Glow Radius에 비해 크면 박스 중앙 부근에서 발광이 흐려진다.
        /// </summary>
        private void DrawReport()
        {
            if (!IsValid()) return;

            BuildArcTable();

            float worst = 0f;
            float shortest = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                GetSegment(i, out Vector2 p0, out Vector2 p1, out Vector2 mid, out Vector2 normal);
                Vector2 chordMid = (p0 + p1) * 0.5f;
                worst = Mathf.Max(worst, Vector2.Dot(mid - chordMid, normal));
                shortest = Mathf.Min(shortest, Vector2.Distance(p0, p1));
            }

            // 안쪽 면을 호의 중간에 맞추므로 실제 오차는 절반으로 갈린다.
            EditorGUILayout.LabelField($"둘레 약 {perimeter:F1},  박스 길이 최소 {shortest:F1}");
            EditorGUILayout.LabelField($"타원과의 최대 오차 ±{worst * 0.5f:F2}");
        }

        private void Generate()
        {
            BuildArcTable();

            if (clearExisting && parent.childCount > 0)
            {
                bool ok = EditorUtility.DisplayDialog(
                    "기존 자식 삭제",
                    $"'{parent.name}'의 자식 {parent.childCount}개를 지우고 새로 만듭니다. 진행할까요?",
                    "삭제하고 생성", "취소");

                if (!ok) return;

                for (int i = parent.childCount - 1; i >= 0; i--)
                {
                    Undo.DestroyObjectImmediate(parent.GetChild(i).gameObject);
                }
            }

            for (int i = 0; i < count; i++)
            {
                GetSegment(i, out Vector2 p0, out Vector2 p1, out Vector2 mid, out Vector2 normal);

                Vector2 chordMid = (p0 + p1) * 0.5f;
                float sagitta = Vector2.Dot(mid - chordMid, normal);

                // 안쪽 면을 현과 호의 중간에 두어 오차를 양쪽으로 반씩 나눈다.
                // 현에 딱 맞추면 박스 가운데가 통째로 타원 안쪽으로 들어간다.
                Vector2 innerFace = chordMid + normal * (sagitta * 0.5f);
                Vector2 center = innerFace + normal * (thickness * 0.5f);

                GameObject go = new GameObject($"Wall{i:00}");
                Undo.RegisterCreatedObjectUndo(go, "Create Arena Collider Ring");

                go.transform.SetParent(parent, false);
                go.transform.localPosition = new Vector3(center.x, yOffset + height * 0.5f, center.y);
                go.transform.localRotation = Quaternion.LookRotation(new Vector3(normal.x, 0f, normal.y), Vector3.up);
                go.layer = parent.gameObject.layer;

                BoxCollider box = go.AddComponent<BoxCollider>();
                // 옆 박스와 겹쳐야 모서리 틈으로 빠져나가지 않는다.
                box.size = new Vector3(Vector2.Distance(p0, p1) * (1f + overlap), height, thickness);
            }

            Selection.activeTransform = parent;
            Debug.Log($"[ArenaColliderRing] '{parent.name}' 아래에 Box Collider {count}개를 만들었다.", parent);
        }

        /// <summary>
        /// 타원 둘레를 따라 누적 호 길이 표를 만든다. 각도가 아니라 호 길이로 등분하기 위한 준비.
        /// </summary>
        private void BuildArcTable()
        {
            if (cumulativeArc == null || cumulativeArc.Length != SampleCount + 1)
            {
                cumulativeArc = new float[SampleCount + 1];
            }

            cumulativeArc[0] = 0f;
            Vector2 previous = PointAt(0f);

            for (int i = 1; i <= SampleCount; i++)
            {
                Vector2 current = PointAt(i / (float)SampleCount * Mathf.PI * 2f);
                cumulativeArc[i] = cumulativeArc[i - 1] + Vector2.Distance(previous, current);
                previous = current;
            }

            perimeter = cumulativeArc[SampleCount];
        }

        /// <summary>i번째 구간의 양 끝점, 호의 중간점, 그 지점의 바깥 방향을 구한다.</summary>
        private void GetSegment(int i, out Vector2 p0, out Vector2 p1, out Vector2 mid, out Vector2 normal)
        {
            float s0 = perimeter * i / count;
            float s1 = perimeter * (i + 1) / count;
            float midAngle = AngleAtArc((s0 + s1) * 0.5f);

            p0 = PointAt(AngleAtArc(s0));
            p1 = PointAt(AngleAtArc(s1));
            mid = PointAt(midAngle);
            normal = NormalAt(midAngle);
        }

        private Vector2 PointAt(float angle)
        {
            return new Vector2(radiusX * Mathf.Cos(angle), radiusZ * Mathf.Sin(angle));
        }

        /// <summary>
        /// 타원 위 한 점의 바깥 방향. 원과 달리 중심에서 그은 방향과 다르므로 따로 구해야 한다.
        /// x^2/a^2 + z^2/b^2 = 1 의 기울기에서 나온다.
        /// </summary>
        private Vector2 NormalAt(float angle)
        {
            return new Vector2(Mathf.Cos(angle) / radiusX, Mathf.Sin(angle) / radiusZ).normalized;
        }

        /// <summary>누적 표를 이진 탐색해 주어진 호 길이에 해당하는 각도를 찾는다.</summary>
        private float AngleAtArc(float arc)
        {
            arc = Mathf.Clamp(arc, 0f, perimeter);

            int low = 0;
            int high = SampleCount;

            while (high - low > 1)
            {
                int mid = (low + high) / 2;
                if (cumulativeArc[mid] < arc) low = mid; else high = mid;
            }

            float span = cumulativeArc[high] - cumulativeArc[low];
            float t = span > 1e-6f ? (arc - cumulativeArc[low]) / span : 0f;
            return (low + t) / SampleCount * Mathf.PI * 2f;
        }

        // 생성 전에 어디에 깔릴지 씬에서 미리 보여준다. 반지름을 눈으로 맞추기 위함이다.
        private void OnSceneGUI(SceneView view)
        {
            if (!IsValid()) return;

            BuildArcTable();

            Matrix4x4 previousMatrix = Handles.matrix;
            Handles.matrix = Matrix4x4.TRS(parent.position, parent.rotation, Vector3.one);

            Handles.color = new Color(0.1f, 0.85f, 1f, 0.5f);
            Vector3 previousPoint = ToLocal(PointAt(0f));

            for (int i = 1; i <= 128; i++)
            {
                Vector3 point = ToLocal(PointAt(i / 128f * Mathf.PI * 2f));
                Handles.DrawLine(previousPoint, point);
                previousPoint = point;
            }

            Handles.color = new Color(1f, 0.75f, 0.1f, 0.9f);

            for (int i = 0; i < count; i++)
            {
                GetSegment(i, out Vector2 p0, out Vector2 p1, out Vector2 mid, out Vector2 normal);
                Vector2 chordMid = (p0 + p1) * 0.5f;
                Vector2 innerFace = chordMid + normal * (Vector2.Dot(mid - chordMid, normal) * 0.5f);
                Vector2 half = (p1 - p0).normalized * (Vector2.Distance(p0, p1) * 0.5f * (1f + overlap));

                Vector3 a = ToLocal(innerFace - half);
                Vector3 b = ToLocal(innerFace + half);
                Vector3 up = Vector3.up * height;

                Handles.DrawLine(a, b);
                Handles.DrawLine(a + up, b + up);
                Handles.DrawLine(a, a + up);
                Handles.DrawLine(b, b + up);
            }

            Handles.matrix = previousMatrix;
        }

        private Vector3 ToLocal(Vector2 flat)
        {
            return new Vector3(flat.x, yOffset, flat.y);
        }
    }
}
