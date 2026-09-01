using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectS.UI
{
    /// <summary>
    /// 네트워크가 붙기 전까지 초대 목록을 채우는 가짜 데이터원. 인스펙터에 적은 줄을 그대로 돌려준다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>이 클래스는 나중에 통째로 지운다.</b> 네트워크 담당자가 <see cref="IPartyMemberSource"/>를
    /// 구현한 컴포넌트를 만들어 팝업의 슬롯에 끼우면 UI 코드는 한 줄도 바뀌지 않는다.
    /// </para>
    /// <para>
    /// 빈 상태·긴 닉네임·회색 카드처럼 <b>실제로는 만들기 번거로운 화면을 여기서 만들어 본다.</b>
    /// 목록을 비워 두면 빈 상태 문구를, 초대 불가만 채우면 전부 회색인 목록을 확인할 수 있다.
    /// </para>
    /// </remarks>
    public class DummyPartyMemberSource : MonoBehaviour, IPartyMemberSource
    {
        /// <summary>인스펙터에서 적는 가짜 플레이어 한 줄.</summary>
        [Serializable]
        public class Entry
        {
            [SerializeField] private string nickname = "하루";
            [SerializeField, Min(1)] private int level = 1;

            [Tooltip("직업 아이콘 인덱스. CharacterSaveData.characterType과 같은 값이다.")]
            [SerializeField, Min(0)] private int characterType;

            [Tooltip("끄면 카드가 회색이 되고 ③에 '비접속'이 찍힌다. 최근 탭에서만 의미가 있다.")]
            [SerializeField] private bool isOnline = true;

            [SerializeField] private PartyInviteState inviteState = PartyInviteState.Invitable;

            /// <summary>인스펙터에 적은 값으로 목록 한 줄을 만든다.</summary>
            /// <param name="id">이 줄에 붙일 식별자(더미에서는 목록 순번으로 만든다)</param>
            public PartyMemberInfo ToInfo(string id)
                => new PartyMemberInfo(id, nickname, level, characterType, isOnline, inviteState);
        }

        /// <inheritdoc/>
        public event Action OnChanged;

        /// <inheritdoc/>
        /// <remarks>더미는 인스펙터 값이 곧 데이터라 기다릴 것이 없다. 항상 준비된 상태다.</remarks>
        public bool IsReady => true;

        [Header("접속 중 탭")]
        [Tooltip("비워 두면 '접속 중인 다른 플레이어가 없습니다' 빈 상태를 확인할 수 있다.")]
        [SerializeField] private List<Entry> onlineEntries = new();

        [Header("최근 탭")]
        [Tooltip("비접속 줄을 섞어 두면 최근 탭의 회색 카드를 확인할 수 있다.")]
        [SerializeField] private List<Entry> recentEntries = new();

        private readonly List<PartyMemberInfo> onlineCache = new();
        private readonly List<PartyMemberInfo> recentCache = new();

        private void Awake()
        {
            Rebuild();
        }

        /// <inheritdoc/>
        public IReadOnlyList<PartyMemberInfo> GetOnlineMembers() => onlineCache;

        /// <inheritdoc/>
        public IReadOnlyList<PartyMemberInfo> GetRecentMembers() => recentCache;

        /// <inheritdoc/>
        public void Refresh()
        {
            Rebuild();
            OnChanged?.Invoke();
        }

        // 인스펙터에서 값을 고치면 플레이 중에도 바로 반영된다. 목록 상태별 화면을 눈으로 맞춰 보기 위한 것이다.
        private void OnValidate()
        {
            if (!Application.isPlaying) return;

            Refresh();
        }

        private void Rebuild()
        {
            Fill(onlineEntries, onlineCache, "online");
            Fill(recentEntries, recentCache, "recent");
        }

        // 식별자는 목록 순번으로 만든다. 더미에서는 값 자체에 의미가 없고, 같은 줄을 같은 사람으로
        // 알아보기만 하면 된다(선택 유지·중복 판정이 Id 기준이므로 빈 값이면 안 된다).
        private static void Fill(List<Entry> from, List<PartyMemberInfo> into, string prefix)
        {
            into.Clear();
            if (from == null) return;

            for (int i = 0; i < from.Count; i++)
            {
                if (from[i] == null) continue;

                into.Add(from[i].ToInfo($"{prefix}-{i}"));
            }
        }
    }
}
