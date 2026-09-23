using System;
using UnityEngine;
using ProjectS.Events;

namespace ProjectS.Settings
{
    /// <summary>
    /// 옵션 창에서 바꾸는 유저 로컬 설정 한 벌(그래픽 · 사운드 · 게임).
    /// 기획 밸런스가 아니라 기기/취향별 값이라 JSON 테이블이 아니라 PlayerPrefs에 JSON 한 덩어리로 저장한다
    /// (키를 항목별로 흩뿌리면 리셋·버전 관리 때 빠뜨리는 키가 생긴다).
    /// </summary>
    /// <remarks>
    /// 흐름: 옵션 창은 <see cref="Clone"/>으로 사본을 만들어 편집하고, 미리보기는 <see cref="Preview"/>,
    /// 확정은 <see cref="Apply"/>, 취소는 <see cref="Revert"/>로 한다.
    /// 각 시스템(SoundManager, 카메라, 데미지 텍스트)은 <see cref="Current"/>를 읽거나
    /// <see cref="SettingsEvents.OnChanged"/>를 구독한다 — 옵션 UI를 알 필요가 없게 하기 위함이다.
    /// </remarks>
    [Serializable]
    public class GameSettings
    {
        private const string PrefsKey = "ProjectS.GameSettings";

        /// <summary>마우스 감도 배율 슬라이더 범위. UI와 로드 시 보정이 같은 값을 쓰도록 여기 둔다.</summary>
        public const float MinMouseSensitivity = 0.1f;
        /// <summary>마우스 감도 배율 슬라이더 최대값.</summary>
        public const float MaxMouseSensitivity = 3f;

        // ── 그래픽 ──────────────────────────────────────────────
        /// <summary>화면 모드. 전체화면(독점) / 테두리 없는 창(FullScreenWindow) / 창 모드.</summary>
        public FullScreenMode ScreenMode = FullScreenMode.FullScreenWindow;

        /// <summary>해상도 가로. 0이면 "모니터 기본 해상도"를 뜻한다(리셋 시 모니터마다 알맞게 가기 위함).</summary>
        public int ResolutionWidth;
        /// <summary>해상도 세로. 0이면 모니터 기본.</summary>
        public int ResolutionHeight;

        /// <summary>수직동기화. 켜져 있으면 프레임 제한은 무시된다(Unity 사양).</summary>
        public bool VSync = true;

        /// <summary>프레임 제한(fps). 0이면 무제한.</summary>
        public int FrameLimit;

        // ── 사운드 (0~100, SoundManager의 슬라이더 규약과 같다) ─────
        /// <summary>전체 볼륨. 믹서 Master 그룹에 걸려 아래 네 채널 모두에 곱해진다.</summary>
        public int MasterVolume = 100;
        /// <summary>배경음 볼륨.</summary>
        public int BgmVolume = 80;
        /// <summary>환경음 볼륨.</summary>
        public int AmbientVolume = 80;
        /// <summary>효과음 볼륨.</summary>
        public int SfxVolume = 80;
        /// <summary>음성 볼륨.</summary>
        public int VoiceVolume = 80;

        // 채널별 음소거(옵션 창 행마다 붙은 MuteToggle). 볼륨 값과 따로 두는 이유: 음소거를 풀었을 때
        // 원래 볼륨으로 돌아가야 하기 때문이다(볼륨을 0으로 덮어쓰면 되돌릴 값이 사라진다).
        /// <summary>전체 음소거.</summary>
        public bool MasterMuted;
        /// <summary>배경음 음소거.</summary>
        public bool BgmMuted;
        /// <summary>환경음 음소거.</summary>
        public bool AmbientMuted;
        /// <summary>효과음 음소거.</summary>
        public bool SfxMuted;
        /// <summary>음성 음소거.</summary>
        public bool VoiceMuted;

        // ── 게임 ────────────────────────────────────────────────
        /// <summary>마우스 감도 배율. CameraPivotController의 기본 감도에 곱한다(1 = 기획 기본값).</summary>
        public float MouseSensitivity = 1f;

        /// <summary>카메라 상하 반전.</summary>
        public bool InvertY;

        /// <summary>데미지 숫자 표시.</summary>
        public bool ShowDamageNumbers = true;

        private static GameSettings current;

        /// <summary>
        /// 현재 확정된 설정. 처음 접근할 때 PlayerPrefs에서 읽고, 없거나 깨졌으면 기본값을 쓴다.
        /// 읽기 전용으로 다룰 것 — 바꾸려면 <see cref="Clone"/> 후 <see cref="Apply"/>.
        /// </summary>
        public static GameSettings Current => current ??= Load();

        /// <summary>기본값 한 벌. 리셋·첫 실행·저장값 손상 복구가 모두 이 경로를 쓴다.</summary>
        public static GameSettings CreateDefault() => new GameSettings();

        /// <summary>편집용 사본. 옵션 창이 확정 전까지 Current를 건드리지 않게 하기 위함이다.</summary>
        public GameSettings Clone() => (GameSettings)MemberwiseClone();

        /// <summary>
        /// 설정을 확정한다: Current 교체 → 그래픽 적용 → 저장 → <see cref="SettingsEvents.FireChanged"/>.
        /// 해상도 변경이 화면을 깜빡이게 하므로 옵션 창의 [적용]/[닫기]에서만 부른다.
        /// </summary>
        /// <param name="settings">확정할 설정(호출 뒤 호출자가 계속 편집하지 않도록 내부에서 사본을 보관한다)</param>
        public static void Apply(GameSettings settings)
        {
            current = settings.Clone();
            current.Sanitize();
            ApplyGraphics(current);
            Save(current);
            SettingsEvents.FireChanged(current);
        }

        /// <summary>
        /// 저장·그래픽 적용 없이 편집 중인 값을 즉시 들려준다/보여준다(볼륨 슬라이더, 감도 등).
        /// 해상도·품질은 여기서 적용하지 않는다 — 드래그마다 화면이 깜빡이기 때문이다.
        /// </summary>
        /// <param name="draft">편집 중인 사본</param>
        public static void Preview(GameSettings draft) => SettingsEvents.FireChanged(draft);

        /// <summary>미리보기를 취소하고 확정된 값으로 되돌린다. 옵션 창을 [적용] 없이 닫을 때 부른다.</summary>
        public static void Revert() => SettingsEvents.FireChanged(Current);

        /// <summary>모든 설정을 기본값으로 되돌려 확정한다(옵션 하단 [설정 초기화]).</summary>
        public static void ResetToDefault() => Apply(CreateDefault());

        /// <summary>
        /// 이 설정이 가리키는 실제 해상도. 0(모니터 기본)이면 현재 모니터 해상도로 풀어 준다.
        /// 옵션 창 드롭다운의 초기 선택값을 맞출 때도 쓴다.
        /// </summary>
        public Vector2Int GetEffectiveResolution()
        {
            if (ResolutionWidth > 0 && ResolutionHeight > 0) return new Vector2Int(ResolutionWidth, ResolutionHeight);

            Resolution native = Screen.currentResolution;
            return new Vector2Int(native.width, native.height);
        }

        // 부팅 직후 첫 씬이 뜨기 전에 저장된 그래픽 설정을 적용한다.
        // 사운드는 여기서 하지 않는다 — AudioMixer.SetFloat는 Awake 이전/도중엔 무시되는 경우가 있어
        // SoundManager.Start가 Current를 읽어 직접 적용한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyOnBoot()
        {
            current = null;   // 도메인 리로드를 꺼도 이전 플레이의 값이 남지 않게
            ApplyGraphics(Current);
        }

        private static GameSettings Load()
        {
            string json = PlayerPrefs.GetString(PrefsKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return CreateDefault();

            try
            {
                // FromJsonOverwrite: 기본값 인스턴스 위에 덮어쓴다 → 나중에 항목이 추가돼도
                // 예전 저장본엔 없는 항목이 0/false가 아니라 기본값으로 채워진다.
                GameSettings loaded = CreateDefault();
                JsonUtility.FromJsonOverwrite(json, loaded);
                loaded.Sanitize();
                return loaded;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GameSettings] 저장된 설정을 읽지 못해 기본값을 씁니다: {e.Message}");
                return CreateDefault();
            }
        }

        private static void Save(GameSettings settings)
        {
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(settings));
            PlayerPrefs.Save();
        }

        // 저장본 손상·수동 편집·품질 단계 삭제 같은 이유로 범위를 벗어난 값을 안전한 값으로 되돌린다.
        private void Sanitize()
        {
            if (!Enum.IsDefined(typeof(FullScreenMode), ScreenMode) || ScreenMode == FullScreenMode.MaximizedWindow)
                ScreenMode = FullScreenMode.FullScreenWindow;   // MaximizedWindow는 macOS 전용이라 옵션에 없다

            if (ResolutionWidth <= 0 || ResolutionHeight <= 0) ResolutionWidth = ResolutionHeight = 0;
            if (FrameLimit < 0) FrameLimit = 0;

            MasterVolume = Mathf.Clamp(MasterVolume, 0, 100);
            BgmVolume = Mathf.Clamp(BgmVolume, 0, 100);
            AmbientVolume = Mathf.Clamp(AmbientVolume, 0, 100);
            SfxVolume = Mathf.Clamp(SfxVolume, 0, 100);
            VoiceVolume = Mathf.Clamp(VoiceVolume, 0, 100);

            MouseSensitivity = Mathf.Clamp(MouseSensitivity, MinMouseSensitivity, MaxMouseSensitivity);
        }

        // 그래픽 품질 단계는 옵션에서 뺐다(2026-09-23, 품질 프리셋 제작 일정 부족). 나중에 넣는다면
        // QualitySettings.SetQualityLevel이 vSyncCount를 자기 값으로 덮어쓰므로 반드시 VSync보다 먼저 적용할 것.
        private static void ApplyGraphics(GameSettings settings)
        {
            Vector2Int resolution = settings.GetEffectiveResolution();
            Screen.SetResolution(resolution.x, resolution.y, settings.ScreenMode);

            QualitySettings.vSyncCount = settings.VSync ? 1 : 0;

            // VSync 중엔 targetFrameRate가 무시되지만, -1로 명시해 두면 나중에 VSync를 끌 때 옛 제한값이 살아나지 않는다.
            Application.targetFrameRate = !settings.VSync && settings.FrameLimit > 0 ? settings.FrameLimit : -1;
        }
    }
}
