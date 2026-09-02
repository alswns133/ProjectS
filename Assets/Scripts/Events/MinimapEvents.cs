using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectS.Events
{
    /// <summary>미니맵에 표시할 대상의 종류. 마커 아이콘·색을 이 값으로 고른다.</summary>
    public enum MinimapMarkerType
    {
        Player, //플레이어
        Enemy,  //적
        Npc,    // 기본/ 일반 NPC
        NpcShop, // 상점 NPC
        NpcEnhance, // 강화 NPC
        NpcQuest, // 퀘스트 NPC
        // ★ 새 항목은 반드시 맨 끝에 추가한다. Unity는 enum을 정수값으로 직렬화하므로,
        //   중간에 끼우면 기존 프리팹/씬에 저장된 값이 밀려 다른 종류로 뒤바뀐다.
        Boss,   // 보스(일반 적과 다른 마커)
    }

    /// <summary>
    /// 미니맵에 "무엇이 존재하는지"를 알리는 static 이벤트 허브.
    /// 스폰/사망 같은 존재 변화만 발행하고, 위치 갱신(매 프레임)은 미니맵 View가 직접 처리한다.
    /// <para>
    /// 다른 이벤트 허브(CombatEvents 등)와 달리 현재 등록 목록(<see cref="Active"/>)을 보관한다.
    /// 미니맵은 "지금 무엇이 있나"라는 상태가 필요하고, View/Presenter가 적보다 늦게 켜지면
    /// 이미 발행된 등록 이벤트를 놓치기 때문이다. 늦게 붙는 구독자는 이 목록을 한 번 훑어 따라잡는다.
    /// </para>
    /// </summary>
    public static class MinimapEvents
    {
        /// <summary>등록된 대상 하나. Transform으로 매 프레임 위치를 읽고, Type으로 마커를 고른다.</summary>
        public readonly struct Entry
        {
            public readonly Transform Target;
            public readonly MinimapMarkerType Type;

            public Entry(Transform target, MinimapMarkerType type)
            {
                Target = target;
                Type = type;
            }
        }

        // 현재 미니맵에 올라와 있는 대상들. 늦게 켜진 View가 초기 상태를 복원하는 데 쓴다.
        private static readonly List<Entry> active = new List<Entry>();

        /// <summary>현재 등록된 대상 목록. View가 켜질 때 한 번 훑어 이미 존재하는 마커를 만든다.</summary>
        public static IReadOnlyList<Entry> Active => active;

        /// <summary>대상이 미니맵에 등록됨(스폰 등). Presenter가 받아 View에 마커를 추가한다.</summary>
        public static event Action<Transform, MinimapMarkerType> OnRegistered;

        /// <summary>대상이 미니맵에서 해제됨(사망/비활성). Presenter가 받아 View의 마커를 제거한다.</summary>
        public static event Action<Transform> OnUnregistered;

        /// <summary>
        /// 스테이지가 바뀌어 미니맵 배경·범위·이름을 교체함(마을→던전 등). 인자는 (스냅샷, 월드중심, 월드크기, 맵이름).
        /// 씬 진입 시 발행하고, Presenter가 받아 View에 배경과 이름을 적용한다.
        /// </summary>
        public static event Action<Sprite, Vector2, Vector2, string> OnStageChanged;

        // 마지막으로 적용된 스테이지 배경. 씬이 HUD보다 먼저 발행하거나 View가 늦게 켜져도
        // 따라잡을 수 있게 캐싱한다(Active 목록과 같은 이유의 상태 보관).
        private static bool hasStage;
        private static Sprite stageSprite;
        private static Vector2 stageCenter;
        private static Vector2 stageSize;
        private static string stageName;

        /// <summary>스테이지 배경이 한 번이라도 지정됐는지. View가 켜질 때 초기 배경 복원 여부 판단에 쓴다.</summary>
        public static bool HasStage => hasStage;
        /// <summary>마지막으로 지정된 스테이지 스냅샷.</summary>
        public static Sprite StageSprite => stageSprite;
        /// <summary>마지막으로 지정된 스테이지 월드 중심.</summary>
        public static Vector2 StageCenter => stageCenter;
        /// <summary>마지막으로 지정된 스테이지 월드 크기.</summary>
        public static Vector2 StageSize => stageSize;
        /// <summary>마지막으로 지정된 스테이지 이름.</summary>
        public static string StageName => stageName;

        /// <summary>
        /// 스테이지 미니맵 배경·범위를 교체한다. 씬 진입 시(BaseScene.Enter) 그 씬의 MinimapData로 호출한다.
        /// </summary>
        /// <param name="snapshot">배경 스냅샷. null이면 그림은 유지하고 범위만 바꾼다.</param>
        /// <param name="worldCenter">스냅샷이 담는 월드 사각형 중심(XZ).</param>
        /// <param name="worldSize">스냅샷이 담는 월드 사각형 크기(XZ).</param>
        /// <param name="mapName">미니맵에 표시할 스테이지 이름. null/빈 문자열이면 이름 라벨은 유지한다.</param>
        public static void FireStageChanged(Sprite snapshot, Vector2 worldCenter, Vector2 worldSize, string mapName)
        {
            hasStage = true;
            stageSprite = snapshot;
            stageCenter = worldCenter;
            stageSize = worldSize;
            stageName = mapName;
            OnStageChanged?.Invoke(snapshot, worldCenter, worldSize, mapName);
        }

        /// <summary>
        /// 대상을 미니맵에 등록한다. 보통 <see cref="MinimapMarkerSource"/>가 OnEnable에서 호출한다.
        /// 같은 Transform이 중복 등록되면 무시한다.
        /// </summary>
        public static void Register(Transform target, MinimapMarkerType type)
        {
            if (target == null) return;

            for (int i = 0; i < active.Count; i++)
                if (active[i].Target == target) return;

            active.Add(new Entry(target, type));
            OnRegistered?.Invoke(target, type);
        }

        /// <summary>
        /// 대상을 미니맵에서 해제한다. 보통 <see cref="MinimapMarkerSource"/>가 OnDisable에서 호출한다.
        /// </summary>
        public static void Unregister(Transform target)
        {
            if (target == null) return;

            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].Target == target)
                {
                    active.RemoveAt(i);
                    break;
                }
            }

            OnUnregistered?.Invoke(target);
        }

        // 도메인 리로드를 꺼도 플레이 시작 시 깨끗한 상태를 보장한다.
        // (이전 세션의 죽은 Transform이 목록/구독에 남는 것을 방지)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            active.Clear();
            OnRegistered = null;
            OnUnregistered = null;

            OnStageChanged = null;
            hasStage = false;
            stageSprite = null;
            stageName = null;
        }
    }
}
