#if UNITY_EDITOR
using UnityEngine;
using ProjectS.Managers;

namespace ProjectS.Debugging
{
    /// <summary>
    /// 에디터에서 Bootstrap 씬을 거치지 않고 던전·튜토리얼 씬을 직접 재생했을 때,
    /// 빠져 있는 JsonManager를 대신 만들어 주는 테스트 편의 장치.
    /// 수정할 때마다 Bootstrap 씬으로 갔다 오는 왕복을 없애는 것이 목적이다.
    ///
    /// "데이터가 없으면 기본값" 대신 "데이터를 실제로 로드"하는 방식을 쓴다.
    /// 기본값 폴백은 테이블에 행이 빠진 진짜 사고까지 조용히 덮어버려서,
    /// 테스트에서 본 수치와 빌드에서 도는 수치가 달라지기 때문이다.
    ///
    /// UNITY_EDITOR 전용이라 빌드에는 포함되지 않는다. 빌드는 항상 Bootstrap 씬을 거친다.
    /// </summary>
    public static class EditorAutoBootstrap
    {
        // AfterSceneLoad는 씬의 모든 Awake 뒤, 모든 Start 앞에 호출된다.
        // JsonManager를 이 시점에 만들어야 PlayerStats.Start의 ReadyTask await가 성립한다
        // (BeforeSceneLoad는 아직 씬이 없어 "Bootstrap에서 시작했는지" 판별할 수 없다).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureDataManager()
        {
            // Bootstrap 씬에서 시작한 정상 경로면 이미 인스턴스가 있다 → 건드리지 않는다.
            if (JsonManager.Instance != null) return;

            // JsonManager는 인스펙터 참조가 없고 테이블을 전부 어드레서블에서 읽으므로
            // 빈 오브젝트에 붙이는 것만으로 Bootstrap 씬의 것과 똑같이 동작한다.
            // AddComponent가 Awake를 즉시 실행해 ReadyTask가 여기서 바로 시작된다.
            GameObject go = new GameObject("[EditorAutoBootstrap] JsonManager");
            go.AddComponent<JsonManager>();

            Debug.Log("[EditorAutoBootstrap] Bootstrap 씬을 거치지 않아 JsonManager를 자동 생성했습니다. " +
                      "데이터 로딩은 비동기라 첫 1~2프레임은 스킬이 잠깁니다.");
        }
    }
}
#endif
