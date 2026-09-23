using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Managers;
using ProjectS.Settings;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 옵션 창(게임 · 그래픽 · 사운드 탭 + 하단 설정 초기화/캐릭터 선택/게임 종료).
    /// 값의 원천은 <see cref="GameSettings"/>이고, 이 팝업은 사본(draft)을 편집해 적용하는 View다.
    /// </summary>
    /// <remarks>
    /// 적용 시점(별도 [적용] 버튼이 없는 레이아웃이라 이렇게 나눴다):
    /// · 슬라이더(볼륨·감도)는 드래그마다 저장하면 PlayerPrefs 쓰기가 매 프레임 일어나므로
    ///   <see cref="GameSettings.Preview"/>로 즉시 들려주기만 하고, 창을 닫을 때 한 번에 확정한다.
    /// · 드롭다운·토글은 한 번의 선택이 곧 결정이라 바로 <see cref="GameSettings.Apply"/>한다.
    ///   해상도를 바꾸고 결과를 확인하려고 창을 닫아야 하는 불편을 피하기 위함이다.
    /// 하단 [캐릭터 선택] 버튼은 <see cref="ReturnToSelectButton"/>, [게임 종료]는 <see cref="QuitGameButton"/>을
    /// 버튼 오브젝트에 직접 붙여 쓴다 — 저장·세션 정리 순서를 그 컴포넌트들이 이미 책임지고 있어서다.
    /// </remarks>
    public class OptionsPopup : BasePopup
    {
        private enum Tab
        {
            Game,
            Graphic,
            Sound,
        }

        /// <summary>탭 버튼과 그 탭이 켜는 페이지 한 쌍.</summary>
        [Serializable]
        private class TabEntry
        {
            public Button button;
            public GameObject page;
        }

        /// <summary>사운드 한 줄(라벨 · 슬라이더 · 숫자 · 음소거 토글).</summary>
        [Serializable]
        private class VolumeRow
        {
            public Slider slider;
            public TMP_Text valueText;
            public CyberIconToggleView muteToggle;
        }

        private const string TabPrefKey = "options.tab";

        // 프레임 제한 선택지. 0 = 무제한. 144/165는 주사율이 흔한 모니터 기준.
        private static readonly int[] FrameLimits = { 30, 60, 120, 144, 165, 240, 0 };

        // 화면 모드 선택지. MaximizedWindow는 macOS 전용이라 뺐다.
        private static readonly FullScreenMode[] ScreenModes =
        {
            FullScreenMode.ExclusiveFullScreen,
            FullScreenMode.FullScreenWindow,
            FullScreenMode.Windowed,
        };
        private static readonly string[] ScreenModeLabels = { "전체 화면", "테두리 없는 창", "창 모드" };

        [Header("탭 (순서: 게임, 그래픽, 사운드)")]
        [SerializeField] private TabEntry gameTab;
        [SerializeField] private TabEntry graphicTab;
        [SerializeField] private TabEntry soundTab;
        [SerializeField] private Color tabNormalColor = new Color(0.4f, 0.42f, 0.5f, 1f);
        [SerializeField] private Color tabSelectedColor = Color.white;

        [Header("게임")]
        [SerializeField] private Slider sensitivitySlider;
        [SerializeField] private TMP_Text sensitivityValueText;
        [SerializeField] private Toggle invertYToggle;
        [SerializeField] private Toggle damageNumbersToggle;

        [Header("그래픽")]
        [SerializeField] private TMP_Dropdown screenModeDropdown;
        [SerializeField] private TMP_Dropdown resolutionDropdown;
        [SerializeField] private TMP_Dropdown qualityDropdown;
        [SerializeField] private Toggle vSyncToggle;
        [SerializeField] private TMP_Dropdown frameLimitDropdown;

        [Header("사운드")]
        [SerializeField] private VolumeRow masterRow;
        [SerializeField] private VolumeRow bgmRow;
        [SerializeField] private VolumeRow ambientRow;
        [SerializeField] private VolumeRow sfxRow;
        [SerializeField] private VolumeRow voiceRow;

        [Header("하단 · 닫기")]
        [SerializeField] private Button resetButton;
        [SerializeField] private Button closeButton;

        [Tooltip("설정 초기화 확인 문구.")]
        [SerializeField, TextArea] private string resetConfirmMessage = "모든 설정을 기본값으로 되돌릴까요?";

        // 편집 중인 사본. 창이 열릴 때 Current에서 복제하고, 닫힐 때(또는 드롭다운·토글 선택 시) 확정한다.
        private GameSettings draft;

        // 슬라이더로만 바뀐, 아직 저장되지 않은 값이 있는지. 닫을 때 이게 true일 때만 확정(저장)한다.
        private bool hasPendingPreview;

        // 드롭다운 인덱스 → 실제 해상도. Screen.resolutions는 주사율별로 같은 해상도가 중복돼 따로 추린다.
        private readonly List<Vector2Int> resolutions = new List<Vector2Int>();

        // 창을 열기 전 커서 상태. 닫을 때 TPS(잠금) 모드였으면 되돌려 준다.
        private bool wasMouseMode;

        private Tab currentTab = Tab.Game;

        // 리스너 연결은 최초 1회만. BasePopup이 OnInit을 한 번만 호출해 주므로 중복 구독이 쌓이지 않는다.
        protected override void OnInit()
        {
            BindTab(gameTab, Tab.Game);
            BindTab(graphicTab, Tab.Graphic);
            BindTab(soundTab, Tab.Sound);

            // 게임
            if (sensitivitySlider != null)
            {
                sensitivitySlider.minValue = GameSettings.MinMouseSensitivity;
                sensitivitySlider.maxValue = GameSettings.MaxMouseSensitivity;
                sensitivitySlider.wholeNumbers = false;
                sensitivitySlider.onValueChanged.AddListener(v =>
                {
                    draft.MouseSensitivity = v;
                    SetSensitivityText(v);
                    MarkPreview();
                });
            }
            BindToggle(invertYToggle, on => draft.InvertY = on);
            BindToggle(damageNumbersToggle, on => draft.ShowDamageNumbers = on);

            // 그래픽
            BuildDropdown(screenModeDropdown, ScreenModeLabels);
            BindDropdown(screenModeDropdown, i => draft.ScreenMode = ScreenModes[i]);

            BindDropdown(resolutionDropdown, i =>
            {
                draft.ResolutionWidth = resolutions[i].x;
                draft.ResolutionHeight = resolutions[i].y;
            });

            BuildDropdown(qualityDropdown, QualitySettings.names);
            BindDropdown(qualityDropdown, i => draft.QualityLevel = i);

            BindToggle(vSyncToggle, on => draft.VSync = on);

            var frameLabels = new string[FrameLimits.Length];
            for (int i = 0; i < FrameLimits.Length; i++)
                frameLabels[i] = FrameLimits[i] > 0 ? $"{FrameLimits[i]} FPS" : "무제한";
            BuildDropdown(frameLimitDropdown, frameLabels);
            BindDropdown(frameLimitDropdown, i => draft.FrameLimit = FrameLimits[i]);

            // 사운드
            BindVolume(masterRow, v => draft.MasterVolume = v, m => draft.MasterMuted = m);
            BindVolume(bgmRow, v => draft.BgmVolume = v, m => draft.BgmMuted = m);
            BindVolume(ambientRow, v => draft.AmbientVolume = v, m => draft.AmbientMuted = m);
            BindVolume(sfxRow, v => draft.SfxVolume = v, m => draft.SfxMuted = m);
            BindVolume(voiceRow, v => draft.VoiceVolume = v, m => draft.VoiceMuted = m);

            // 하단 · 닫기
            if (resetButton != null) resetButton.onClick.AddListener(OnResetClicked);
            if (closeButton != null) closeButton.onClick.AddListener(RequestClose);
        }

        protected override void OnShow()
        {
            draft = GameSettings.Current.Clone();
            hasPendingPreview = false;

            // 해상도 목록은 열 때마다 새로 만든다 — 창 모드↔전체화면 전환이나 모니터 변경으로 목록이 바뀔 수 있다.
            BuildResolutionOptions();
            RefreshAll();

            currentTab = (Tab)Mathf.Clamp(PlayerPrefs.GetInt(TabPrefKey, (int)Tab.Game), 0, (int)Tab.Sound);
            ApplyTabVisual();

            // 옵션 창은 마우스로 조작하므로 커서를 풀어 준다. 원래 TPS 모드였으면 닫을 때 되돌린다.
            wasMouseMode = Cursor.lockState != CursorLockMode.Locked;
            SetMouseMode(true);
        }

        protected override void OnHide()
        {
            // 슬라이더로 미리보기만 한 값을 여기서 확정한다(ESC로 닫아도 이 경로를 탄다).
            if (hasPendingPreview) GameSettings.Apply(draft);
            hasPendingPreview = false;

            if (!wasMouseMode) SetMouseMode(false);
        }

        // ── 탭 ────────────────────────────────────────────────

        private void BindTab(TabEntry entry, Tab tab)
        {
            if (entry?.button != null) entry.button.onClick.AddListener(() => SelectTab(tab));
        }

        private void SelectTab(Tab tab)
        {
            currentTab = tab;
            PlayerPrefs.SetInt(TabPrefKey, (int)tab);
            ApplyTabVisual();
        }

        private void ApplyTabVisual()
        {
            ApplyTabVisual(gameTab, currentTab == Tab.Game);
            ApplyTabVisual(graphicTab, currentTab == Tab.Graphic);
            ApplyTabVisual(soundTab, currentTab == Tab.Sound);
        }

        private void ApplyTabVisual(TabEntry entry, bool selected)
        {
            if (entry == null) return;
            if (entry.page != null) entry.page.SetActive(selected);
            if (entry.button != null && entry.button.targetGraphic != null)
                entry.button.targetGraphic.color = selected ? tabSelectedColor : tabNormalColor;
        }

        // ── 화면 갱신 (이벤트 발화 없이 값만 맞춘다) ────────────────

        private void RefreshAll()
        {
            if (sensitivitySlider != null) sensitivitySlider.SetValueWithoutNotify(draft.MouseSensitivity);
            SetSensitivityText(draft.MouseSensitivity);
            if (invertYToggle != null) invertYToggle.SetIsOnWithoutNotify(draft.InvertY);
            if (damageNumbersToggle != null) damageNumbersToggle.SetIsOnWithoutNotify(draft.ShowDamageNumbers);

            SetDropdown(screenModeDropdown, Mathf.Max(0, Array.IndexOf(ScreenModes, draft.ScreenMode)));
            SetDropdown(resolutionDropdown, Mathf.Max(0, resolutions.IndexOf(draft.GetEffectiveResolution())));
            SetDropdown(qualityDropdown, draft.QualityLevel >= 0 ? draft.QualityLevel : QualitySettings.GetQualityLevel());
            if (vSyncToggle != null) vSyncToggle.SetIsOnWithoutNotify(draft.VSync);
            SetDropdown(frameLimitDropdown, Mathf.Max(0, Array.IndexOf(FrameLimits, draft.FrameLimit)));
            RefreshFrameLimitInteractable();

            RefreshVolume(masterRow, draft.MasterVolume, draft.MasterMuted);
            RefreshVolume(bgmRow, draft.BgmVolume, draft.BgmMuted);
            RefreshVolume(ambientRow, draft.AmbientVolume, draft.AmbientMuted);
            RefreshVolume(sfxRow, draft.SfxVolume, draft.SfxMuted);
            RefreshVolume(voiceRow, draft.VoiceVolume, draft.VoiceMuted);
        }

        // VSync가 켜져 있으면 Unity가 프레임 제한을 무시하므로, 먹히지 않는 설정을 만지지 못하게 잠근다.
        private void RefreshFrameLimitInteractable()
        {
            if (frameLimitDropdown != null) frameLimitDropdown.interactable = !draft.VSync;
        }

        private void SetSensitivityText(float value)
        {
            if (sensitivityValueText != null) sensitivityValueText.text = value.ToString("0.0");
        }

        private static void RefreshVolume(VolumeRow row, int volume, bool muted)
        {
            if (row == null) return;
            if (row.slider != null) row.slider.SetValueWithoutNotify(volume);
            if (row.valueText != null) row.valueText.text = volume.ToString();
            if (row.muteToggle != null) row.muteToggle.SetWithoutNotify(!muted);   // 토글 on = 소리 켜짐
        }

        private static void SetDropdown(TMP_Dropdown dropdown, int index)
        {
            if (dropdown == null || dropdown.options.Count == 0) return;
            dropdown.SetValueWithoutNotify(Mathf.Clamp(index, 0, dropdown.options.Count - 1));
        }

        // ── 바인딩 헬퍼 ─────────────────────────────────────────

        private void BindVolume(VolumeRow row, Action<int> setVolume, Action<bool> setMuted)
        {
            if (row == null) return;

            if (row.slider != null)
            {
                row.slider.minValue = 0;
                row.slider.maxValue = 100;
                row.slider.wholeNumbers = true;
                row.slider.onValueChanged.AddListener(v =>
                {
                    int volume = Mathf.RoundToInt(v);
                    setVolume(volume);
                    if (row.valueText != null) row.valueText.text = volume.ToString();
                    MarkPreview();
                });
            }

            // 음소거는 클릭 한 번이 결정이라 드롭다운처럼 바로 확정한다.
            if (row.muteToggle != null) row.muteToggle.OnToggled += on =>
            {
                setMuted(!on);
                Commit();
            };
        }

        private void BindToggle(Toggle toggle, Action<bool> set)
        {
            if (toggle == null) return;
            toggle.onValueChanged.AddListener(on =>
            {
                set(on);
                Commit();
            });
        }

        private void BindDropdown(TMP_Dropdown dropdown, Action<int> set)
        {
            if (dropdown == null) return;
            dropdown.onValueChanged.AddListener(i =>
            {
                set(i);
                Commit();
            });
        }

        private static void BuildDropdown(TMP_Dropdown dropdown, IEnumerable<string> labels)
        {
            if (dropdown == null) return;
            dropdown.ClearOptions();
            dropdown.AddOptions(new List<string>(labels));
        }

        // 주사율만 다른 중복을 걸러 큰 해상도부터 나열한다. 창 모드에서 임의 크기로 늘린 현재 해상도가
        // 목록에 없으면 그것도 넣는다 — 없으면 드롭다운이 엉뚱한 항목을 선택된 것처럼 보여 준다.
        private void BuildResolutionOptions()
        {
            resolutions.Clear();
            foreach (Resolution r in Screen.resolutions)
            {
                var size = new Vector2Int(r.width, r.height);
                if (!resolutions.Contains(size)) resolutions.Add(size);
            }

            Vector2Int current = draft.GetEffectiveResolution();
            if (!resolutions.Contains(current)) resolutions.Add(current);

            resolutions.Sort((a, b) => a.x != b.x ? b.x.CompareTo(a.x) : b.y.CompareTo(a.y));

            var labels = new List<string>(resolutions.Count);
            foreach (Vector2Int size in resolutions) labels.Add($"{size.x} × {size.y}");
            BuildDropdown(resolutionDropdown, labels);
        }

        // ── 적용 ────────────────────────────────────────────────

        // 슬라이더 드래그 중: 저장 없이 들려주기만 한다. 확정은 OnHide.
        private void MarkPreview()
        {
            hasPendingPreview = true;
            GameSettings.Preview(draft);
        }

        // 드롭다운·토글 선택: 바로 확정(저장 + 그래픽 적용). 그 전에 쌓인 슬라이더 미리보기도 함께 저장된다.
        private void Commit()
        {
            GameSettings.Apply(draft);
            hasPendingPreview = false;
            RefreshFrameLimitInteractable();
        }

        private void OnResetClicked()
        {
            // 대화상자가 없는 씬(단독 테스트)에서는 확인 없이 진행한다 — 버튼이 먹통으로 보이는 편이 더 나쁘다.
            if (ConfirmDialog.Instance == null)
            {
                ResetAll();
                return;
            }

            ConfirmDialog.Instance.Show(resetConfirmMessage, ResetAll);
        }

        private void ResetAll()
        {
            GameSettings.ResetToDefault();
            draft = GameSettings.Current.Clone();
            hasPendingPreview = false;

            // 해상도가 모니터 기본으로 돌아가며 목록의 선택 위치가 바뀌므로 다시 만든다.
            BuildResolutionOptions();
            RefreshAll();
        }

        // Alt 토글과 같은 경로(Player.SetCursorMode)를 타야 HUD 위젯이 받는 커서 모드 이벤트가 빠지지 않는다.
        // 플레이어가 없는 씬(캐릭터 선택 등)에서는 커서를 직접 바꾼다.
        private static void SetMouseMode(bool useMouse)
        {
            var player = PlayerManager.Instance != null ? PlayerManager.Instance.Player : null;
            if (player != null)
            {
                player.SetCursorMode(useMouse);
                return;
            }

            Cursor.lockState = useMouse ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = useMouse;
        }
    }
}
