using System.Collections.Generic;
using UnityEngine;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 던전 네비게이션의 상태 홀더. 두 가지를 든다.
    ///   ① <see cref="CurrentRoom"/> — 플레이어가 지금 들어와 있는 전투방(<see cref="EnemyRoom"/>).
    ///   ② 인덱스별 방 레지스트리 — "현재 방 다음(roomIndex+1)이 어느 방인가"를 자동으로 찾기 위한 것.
    ///
    /// 이렇게 두면 방마다 다음 목표를 손으로 지정할 필요 없이, 각 방에 <see cref="EnemyRoom.RoomIndex"/>(순번)만
    /// 매기면 나침반이 "현재 방 → 그 다음 방"을 자동으로 잇는다(Room1 → Room2 → Room3 …). 방이 씬 언로드로
    /// 파괴되면 Unity가 참조를 null로 만들고, 레지스트리는 <see cref="Unregister"/>로 함께 비운다.
    /// </summary>
    public static class DungeonNav
    {
        // roomIndex → 그 순번의 방. 다음 방 조회(GetRoom(index+1))에 쓴다.
        private static readonly Dictionary<int, EnemyRoom> roomsByIndex = new();

        /// <summary>플레이어가 마지막으로 진입한 전투방. 아직 어느 방에도 안 들어갔거나 던전 밖이면 null.</summary>
        public static EnemyRoom CurrentRoom { get; private set; }

        /// <summary>현재 방을 등록한다. <see cref="EnemyRoom"/>이 플레이어 진입을 감지한 순간 호출한다.</summary>
        /// <param name="room">플레이어가 들어온 방.</param>
        public static void SetCurrentRoom(EnemyRoom room) => CurrentRoom = room;

        /// <summary>현재 방을 비운다. 던전을 나가 마을로 돌아갈 때 등 안내를 즉시 끄고 싶을 때 호출한다(선택).</summary>
        public static void Clear() => CurrentRoom = null;

        /// <summary>
        /// 방을 순번 레지스트리에 등록한다. <see cref="EnemyRoom.OnEnable"/>에서 호출한다.
        /// 같은 순번이 둘이면 나중 것이 이기며(경고), 그 경우 인덱스가 겹치지 않게 인스펙터에서 고쳐야 한다.
        /// </summary>
        /// <param name="room">등록할 방. null이면 무시.</param>
        public static void Register(EnemyRoom room)
        {
            if (room == null) return;

            if (roomsByIndex.TryGetValue(room.RoomIndex, out EnemyRoom existing) && existing != null && existing != room)
                ProjectS.Debugging.DevLog.Warning($"DungeonNav: RoomIndex {room.RoomIndex} 중복 — '{existing.name}' → '{room.name}'로 덮어씀. 인덱스를 유일하게 매기세요.", room);

            roomsByIndex[room.RoomIndex] = room;
        }

        /// <summary>방을 순번 레지스트리에서 뺀다. <see cref="EnemyRoom.OnDisable"/>(씬 언로드 포함)에서 호출한다.</summary>
        /// <param name="room">해제할 방. null이면 무시.</param>
        public static void Unregister(EnemyRoom room)
        {
            if (room == null) return;

            // 자기 자리를 이미 다른 방이 차지했으면(중복 인덱스) 건드리지 않는다.
            if (roomsByIndex.TryGetValue(room.RoomIndex, out EnemyRoom existing) && existing == room)
                roomsByIndex.Remove(room.RoomIndex);
        }

        /// <summary>해당 순번의 방을 돌려준다(없으면 null). 다음 방 조회는 <c>GetRoom(현재.RoomIndex + 1)</c>.</summary>
        /// <param name="index">찾을 방의 순번.</param>
        public static EnemyRoom GetRoom(int index) => roomsByIndex.TryGetValue(index, out EnemyRoom room) ? room : null;

        // 플레이 모드 진입 시 static 상태를 비운다. 도메인 리로드를 끈 프로젝트에서는 이전 플레이의 방 참조가
        // 남을 수 있으므로, 이벤트 허브·QuestWaypointRegistry와 같은 방침으로 초기화한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            CurrentRoom = null;
            roomsByIndex.Clear();
        }
    }
}
