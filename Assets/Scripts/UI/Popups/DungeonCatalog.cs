using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectS.UI
{
    /// <summary>
    /// 입장 화면이 어느 콘텐츠를 위한 것인지. 던전 게이트와 레이드 게이트가 같은 팝업 프리팹을 공유하고,
    /// 이 값으로 타이틀·카탈로그·전환할 씬만 갈라 쓴다.
    /// </summary>
    public enum EntryMode
    {
        /// <summary>일반 던전(메이즈).</summary>
        Dungeon = 0,

        /// <summary>레이드. 기획서에 전용 입장 화면 명세가 없어, 지금은 분기 지점만 열어 둔 상태다.</summary>
        Raid = 1,
    }

    /// <summary>
    /// 입장 화면 목록에 한 줄로 뜨는 에피소드 하나.
    /// </summary>
    [Serializable]
    public class EpisodeInfo
    {
        [Tooltip("던전 번호. ID 규칙(docs/ID_NUMBERING.md §4)의 앞자리이며, 난이도와 합쳐져 2자리 던전 ID가 된다.")]
        [SerializeField, Min(1)] private int dungeonNumber = 1;

        [SerializeField] private string displayName = "에피소드명";

        [Tooltip("메인 스토리 에피소드면 목록에 MAIN 태그를 단다.")]
        [SerializeField] private bool isMain;

        [Tooltip("입장 가능 레벨. 플레이어 레벨이 이보다 낮으면 잠금으로 표시된다.")]
        [SerializeField, Min(1)] private int requiredLevel = 1;

        [Header("표시")]
        [Tooltip("목록 왼쪽 헥사곤 배지 그림. 비우면 라벨(EP.N / Lv.N)만 보인다.")]
        [SerializeField] private Sprite hexIcon;

        [Tooltip("선택 시 사이드 패널에 뜨는 던전 이미지.")]
        [SerializeField] private Sprite previewImage;

        [Tooltip("이미지 아래 한 줄 요약. 비우면 캡션 칸이 숨는다.")]
        [SerializeField, TextArea(1, 3)] private string caption;

        /// <summary>던전 번호(던전 ID의 앞자리).</summary>
        public int DungeonNumber => dungeonNumber;

        /// <summary>목록에 표시할 에피소드 이름.</summary>
        public string DisplayName => displayName;

        /// <summary>메인 스토리 에피소드인지(MAIN 태그 표시 여부).</summary>
        public bool IsMain => isMain;

        /// <summary>입장 가능 레벨.</summary>
        public int RequiredLevel => requiredLevel;

        /// <summary>목록 헥사곤 배지 그림. null이면 라벨만 표시한다.</summary>
        public Sprite HexIcon => hexIcon;

        /// <summary>사이드 패널에 띄울 던전 이미지. null이면 이미지 칸이 비워진다.</summary>
        public Sprite PreviewImage => previewImage;

        /// <summary>이미지 아래 한 줄 요약.</summary>
        public string Caption => caption;
    }

    /// <summary>
    /// 난이도 탭 하나. 값은 ID 규칙의 난이도 자리(1=노말 · 2=하드 · 3=매니악)와 같아야 한다.
    /// </summary>
    [Serializable]
    public class DifficultyInfo
    {
        [Tooltip("ID 규칙의 난이도 자리. 1=노말 · 2=하드 · 3=매니악.")]
        [SerializeField, Range(1, 9)] private int value = 1;

        [SerializeField] private string label = "NORMAL";

        [Tooltip("이 난이도로 입장 가능한 레벨. 0이면 제한 없음.")]
        [SerializeField, Min(0)] private int requiredLevel;

        /// <summary>난이도 값(던전 ID의 뒷자리).</summary>
        public int Value => value;

        /// <summary>탭에 표시할 이름.</summary>
        public string Label => label;

        /// <summary>입장 가능 레벨. 0이면 제한 없음.</summary>
        public int RequiredLevel => requiredLevel;
    }

    /// <summary>
    /// 입장 화면이 읽는 던전(또는 레이드) 목록 에셋. 한 게이트가 하나씩 참조한다.
    ///
    /// <para>
    /// 던전은 JSON 테이블을 만들지 않기로 한 결정(docs/DUNGEON_AND_MULTIPLAYER.md §1)에 따라
    /// ScriptableObject로 둔다. 담기는 값의 절반이 프리팹·스프라이트 참조라 어차피 인스펙터 대상이고,
    /// 이 화면은 <c>JsonManager</c>가 없는 시점에도(직접 씬 테스트) 열려야 하기 때문이다. 선례: <see cref="MinimapData"/>.
    /// </para>
    /// <para>
    /// 스키마는 <b>mode 중립</b>으로 둔다 — 나중에 레이드 배치가 던전과 갈려 프리팹을 쪼개더라도
    /// 데이터는 그대로 쓰기 위함이다.
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "DungeonCatalog", menuName = "ProjectS/Dungeon Catalog")]
    public class DungeonCatalog : ScriptableObject
    {
        [Tooltip("이 카탈로그가 던전용인지 레이드용인지. 게이트가 넘기는 모드와 다르면 경고를 남긴다.")]
        [SerializeField] private EntryMode mode = EntryMode.Dungeon;

        [Tooltip("입장 화면 상단에 뜨는 던전명.")]
        [SerializeField] private string title = "던전명";

        [SerializeField] private List<EpisodeInfo> episodes = new();

        [SerializeField] private List<DifficultyInfo> difficulties = new();

        /// <summary>이 카탈로그가 어느 모드용인지.</summary>
        public EntryMode Mode => mode;

        /// <summary>상단에 표시할 던전명.</summary>
        public string Title => title;

        /// <summary>에피소드 목록. 순서가 그대로 화면 순서이자 W/S 이동 순서다.</summary>
        public IReadOnlyList<EpisodeInfo> Episodes => episodes;

        /// <summary>난이도 탭 목록. 순서가 그대로 A/D 이동 순서다.</summary>
        public IReadOnlyList<DifficultyInfo> Difficulties => difficulties;

        /// <summary>
        /// 에피소드와 난이도를 합쳐 2자리 던전 ID를 만든다(docs/ID_NUMBERING.md §4 <c>[던전1][난이도1]</c>).
        /// 예: 던전1 · 노말 = 11, 던전1 · 매니악 = 13.
        /// </summary>
        /// <remarks>
        /// 이 값이 <c>GameSession.SelectedDungeonId</c>를 거쳐 던전 씬의 <c>DungeonContext</c>까지 그대로 간다.
        /// 몬스터 ID 4자리의 앞 2자리와 같은 값이라, 스폰 쪽 규칙과 자동으로 맞물린다.
        /// </remarks>
        /// <param name="dungeonNumber">던전 번호(앞자리)</param>
        /// <param name="difficulty">난이도 값(뒷자리)</param>
        /// <returns>2자리 던전 ID</returns>
        public static int MakeDungeonId(int dungeonNumber, int difficulty) => dungeonNumber * 10 + difficulty;
    }
}
