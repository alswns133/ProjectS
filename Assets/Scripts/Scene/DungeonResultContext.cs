using UnityEngine;

namespace ProjectS.Scenes
{
    /// <summary>
    /// 던전 한 판의 결과를 결과 화면이 읽을 수 있는 형태로 담은 스냅샷. <b>표시용 값만</b> 들어 있고,
    /// 어떻게 계산했는지는 담지 않는다(점수·등급 산정 규칙은 아직 기획 미결 — 문서 6장).
    /// </summary>
    [System.Serializable]
    public struct DungeonResultData
    {
        /// <summary>플레이 점수. 결과 화면 좌측에 대형으로 표시한다.</summary>
        public int playScore;

        /// <summary>클리어 점수(스테이지 자체 배점).</summary>
        public int clearScore;

        /// <summary>클리어에 걸린 시간(초). 표시 포맷은 결과 화면이 만든다.</summary>
        public float clearTime;

        /// <summary>난이도(1 노말 · 2 하드 · 3 매니악). 던전 ID 뒷자리와 같은 값이다.</summary>
        public int difficulty;

        /// <summary>이번 판의 최대 콤보.</summary>
        public int maxCombo;

        /// <summary>원형 퍼포먼스 게이지 채움 비율(0~1).</summary>
        public float performanceRatio;

        /// <summary>표시할 던전 이름.</summary>
        public string dungeonName;

        /// <summary>던전 단계 표시(예: 3단계).</summary>
        public int stage;

        /// <summary>달성현황 바 비율(0~1).</summary>
        public float achieveRatio;

        /// <summary>성과 등급 문자(S/A/B…).</summary>
        public string grade;

        /// <summary>완료 보상 경험치.</summary>
        public int exp;

        /// <summary>완료 보상 재화(재니 = 골드).</summary>
        public int gold;

        /// <summary>퇴장 선택창에 안내할 남은 미션 수.</summary>
        public int remainingMissions;
    }

    /// <summary>
    /// 방금 끝난 던전의 결과를 담아 두는 전역 홀더. 집계하는 쪽(던전 씬)과 보여주는 쪽(결과 화면)이
    /// 서로를 참조하지 않게 사이에 둔 것이다 — <see cref="DungeonContext"/>·GameSession과 같은 방식.
    /// </summary>
    /// <remarks>
    /// UIManager에는 패널 인스턴스를 타입으로 꺼내는 통로가 없어(팝업만 <c>GetPopup</c>이 있다)
    /// 패널에 값을 직접 먹일 수 없다. 그래서 값을 여기 실어두고 패널이 <c>OnShow</c>에서 읽어간다.
    /// 이 홀더가 비어 있으면 결과 화면은 기본값(0)으로 뜨므로, 씬을 직접 열어 배치를 보는 데도 지장이 없다.
    /// </remarks>
    public static class DungeonResultContext
    {
        /// <summary>가장 최근 던전 결과. <see cref="HasResult"/>가 false면 의미 없는 기본값이다.</summary>
        public static DungeonResultData Current { get; private set; }

        /// <summary>결과가 실려 있는지. 결과 화면이 "실제 판을 마친 것인지"를 판단하는 데 쓴다.</summary>
        public static bool HasResult { get; private set; }

        /// <summary>클리어 판정을 내린 쪽이 집계 결과를 싣는다. 결과 화면을 열기 전에 호출해야 한다.</summary>
        /// <param name="data">이번 판의 결과 스냅샷</param>
        public static void Set(DungeonResultData data)
        {
            Current = data;
            HasResult = true;
        }

        /// <summary>결과를 비운다. 마을 복귀·재도전으로 판이 끝났을 때 호출한다.</summary>
        public static void Clear()
        {
            Current = default;
            HasResult = false;
        }

        // 플레이 모드 리로드 후 이전 판의 결과가 남지 않게 초기화한다(static 리셋 방침).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Clear();
    }
}
