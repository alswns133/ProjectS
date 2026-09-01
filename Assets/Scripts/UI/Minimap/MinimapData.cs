using UnityEngine;

namespace ProjectS.UI
{
    /// <summary>
    /// 한 스테이지(마을·던전·레이드 등)의 미니맵 설정. 배경 스냅샷과 그 스냅샷이 담는 월드 범위를 한 세트로 묶는다.
    /// 스테이지마다 이 에셋을 하나씩 만들고, 씬 진입 시 MinimapEvents.FireStageChanged로 미니맵에 적용한다.
    /// <para>
    /// snapshot과 worldCenter/worldSize는 반드시 짝이 맞아야 한다 — 스냅샷을 찍은 직교 카메라가 커버한
    /// 월드 사각형(중심·크기)을 그대로 여기에 적어야, 마커가 그림 위 제자리에 찍힌다.
    /// 값이 어긋나면 마커가 건물 위가 아니라 엉뚱한 곳에 찍힌다.
    /// </para>
    /// <para>
    /// 스테이지 단위로 바뀌는 에셋이라(데이터/에셋 운영 방침) 나중에 어드레서블로 옮기기 좋다.
    /// 지금은 직접 참조로 두고, 스테이지 수가 늘면 씬 그룹과 함께 어드레서블로 승격한다.
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "MinimapData", menuName = "ProjectS/Minimap Data")]
    public class MinimapData : ScriptableObject
    {
        // 미니맵에 표시할 이 스테이지의 이름(예: "시작의 마을", "던전 1층"). 기획이 정하는 표시명이라
        // 씬 이름 자동 유도가 아니라 여기에 직접 적는다. 비워 두면 미니맵은 이름 라벨을 그대로 둔다.
        [SerializeField] private string displayName;

        // 미니맵 배경 그림. 맵을 직교 탑다운 카메라로 찍은 스냅샷. 아직 없으면 비워 둔다(placeholder 유지).
        [SerializeField] private Sprite snapshot;

        // 스냅샷이 담는 월드 범위(XZ). 캡처 카메라의 중심과 가로/세로 커버 크기와 같아야 한다.
        [SerializeField] private Vector2 worldCenter = Vector2.zero;
        [SerializeField] private Vector2 worldSize = new Vector2(100f, 100f);

        /// <summary>미니맵에 표시할 스테이지 이름. 비어 있으면 미니맵 이름 라벨을 바꾸지 않는다.</summary>
        public string DisplayName => displayName;
        /// <summary>미니맵 배경 스냅샷. null이면 배경을 바꾸지 않고 범위만 적용한다.</summary>
        public Sprite Snapshot => snapshot;
        /// <summary>스냅샷이 담는 월드 사각형의 중심(XZ).</summary>
        public Vector2 WorldCenter => worldCenter;
        /// <summary>스냅샷이 담는 월드 사각형의 크기(XZ).</summary>
        public Vector2 WorldSize => worldSize;
    }
}
