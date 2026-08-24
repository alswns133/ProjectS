using System.Diagnostics;

namespace ProjectS.ExternalAssetsLauncher;

internal sealed class MainForm : Form
{
    private readonly TextBox _projectPathTextBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _oauthClientConfigurationPathTextBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _manifestFileIdTextBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _unityPathTextBox = new() { Dock = DockStyle.Fill };
    private readonly Label _installPathLabel = new() { AutoSize = true };
    private readonly Label _loginLabel = new() { AutoSize = true };
    private readonly Label _versionLabel = new() { AutoSize = true };
    private readonly Label _statusLabel = new() { AutoSize = true };
    private readonly ProgressBar _progressBar = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100 };
    // 버튼 앞의 ①②③④는 로그인 → 확인 → 설치 → 실행이 순서대로 진행하는 파이프라인임을 드러낸다.
    // (로그아웃은 파이프라인 단계가 아니라 번호를 붙이지 않는다.)
    private readonly Button _signInButton = new() { Text = "① Google 로그인", AutoSize = true };
    private readonly Button _signOutButton = new() { Text = "로그아웃", AutoSize = true };
    private readonly Button _checkButton = new() { Text = "② 업데이트 확인", AutoSize = true };
    private readonly Button _installButton = new() { Text = "③ 업데이트 설치", AutoSize = true, Enabled = false };
    private readonly Button _launchButton = new() { Text = "④ Unity 실행", AutoSize = true, Enabled = false };

    private readonly GoogleDriveClient _driveClient = new(new GoogleOAuthClient());
    private UpdatePlan? _updatePlan;

    public MainForm()
    {
        Text = "ProjectS 외부 에셋 런처";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 500);
        AutoScaleMode = AutoScaleMode.Font;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 3,
            RowCount = 12,
            AutoSize = true,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddRow(layout, 0, "프로젝트 폴더", _projectPathTextBox, CreateButton("찾기", SelectProjectFolder));
        AddRow(layout, 1, "설치 경로", _installPathLabel, null);
        AddRow(layout, 2, "OAuth Desktop 앱 JSON", _oauthClientConfigurationPathTextBox, CreateButton("찾기", SelectOAuthClientConfiguration));
        AddRow(layout, 3, "Google 계정", _loginLabel, null);
        AddRow(layout, 4, "manifest Drive 링크 / ID", _manifestFileIdTextBox, null);
        AddRow(layout, 5, "Unity.exe", _unityPathTextBox, CreateButton("찾기", SelectUnityExecutable));
        AddRow(layout, 6, "설치/서버 버전", _versionLabel, null);
        AddRow(layout, 7, "상태", _statusLabel, null);
        AddRow(layout, 8, "진행률", _progressBar, null);

        var authenticationActions = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
        };
        authenticationActions.Controls.AddRange([_signInButton, _signOutButton]);
        layout.Controls.Add(authenticationActions, 1, 9);
        layout.SetColumnSpan(authenticationActions, 2);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
        };
        actions.Controls.AddRange([_checkButton, _installButton, _launchButton]);
        layout.Controls.Add(actions, 1, 10);
        layout.SetColumnSpan(actions, 2);

        var help = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(730, 0),
            Text = "Google 로그인한 팀원 계정에 Drive 권한이 있어야 합니다. 런처는 제한된 Drive에서 ZIP을 검사한 뒤 Assets/ExternalAssets에 반영하며, Unity Editor가 실행 중이면 설치를 막습니다.",
        };
        layout.Controls.Add(help, 1, 11);
        layout.SetColumnSpan(help, 2);

        Controls.Add(layout);
        _signInButton.Click += async (_, _) => await SignInAsync();
        _signOutButton.Click += async (_, _) => await SignOutAsync();
        _checkButton.Click += async (_, _) => await CheckForUpdatesAsync();
        _installButton.Click += async (_, _) => await InstallUpdatesAsync();
        _launchButton.Click += (_, _) => LaunchUnity();
        Load += async (_, _) => await InitializeAsync();
        FormClosed += (_, _) => _driveClient.Dispose();
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control content, Control? trailingControl)
    {
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        layout.Controls.Add(content, 1, row);
        if (trailingControl is not null)
        {
            layout.Controls.Add(trailingControl, 2, row);
        }
    }

    private static Button CreateButton(string text, Action onClick)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) => onClick();
        return button;
    }

    private async Task InitializeAsync()
    {
        var settings = await ProjectServices.LoadSettingsAsync();
        _projectPathTextBox.Text = ProjectServices.IsUnityProject(settings.ProjectPath)
            ? settings.ProjectPath
            : ProjectServices.FindProjectRoot(AppContext.BaseDirectory) ?? string.Empty;
        _oauthClientConfigurationPathTextBox.Text = File.Exists(settings.OAuthClientConfigurationPath)
            ? settings.OAuthClientConfigurationPath
            : File.Exists(GoogleOAuthClient.GetDefaultConfigurationPath())
                ? GoogleOAuthClient.GetDefaultConfigurationPath()
                : string.Empty;
        _manifestFileIdTextBox.Text = settings.ManifestFileId;
        _unityPathTextBox.Text = File.Exists(settings.UnityExecutablePath)
            ? settings.UnityExecutablePath
            : ProjectServices.FindUnityExecutable(_projectPathTextBox.Text) ?? string.Empty;
        RefreshProjectLabels();
        RefreshLoginLabel();
        SetStatus("👋 OAuth Desktop 앱 JSON을 지정한 뒤 ① 로그인 → ② 업데이트 확인 → ③ 업데이트 설치 순서로 진행해 주세요.");
    }

    private void SelectProjectFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "ProjectS Unity 프로젝트 폴더를 선택하세요.",
        };
        if (Directory.Exists(_projectPathTextBox.Text))
        {
            dialog.InitialDirectory = _projectPathTextBox.Text;
        }

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _projectPathTextBox.Text = dialog.SelectedPath;
        if (string.IsNullOrWhiteSpace(_unityPathTextBox.Text))
        {
            _unityPathTextBox.Text = ProjectServices.FindUnityExecutable(dialog.SelectedPath) ?? string.Empty;
        }

        RefreshProjectLabels();
    }

    private void SelectOAuthClientConfiguration()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Google OAuth JSON (*.json)|*.json|모든 파일 (*.*)|*.*",
            Title = "Google Cloud에서 내려받은 OAuth Desktop 앱 JSON을 선택하세요.",
        };
        if (File.Exists(_oauthClientConfigurationPathTextBox.Text))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(_oauthClientConfigurationPathTextBox.Text);
            dialog.FileName = Path.GetFileName(_oauthClientConfigurationPathTextBox.Text);
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _oauthClientConfigurationPathTextBox.Text = dialog.FileName;
        }
    }

    private void SelectUnityExecutable()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Unity Editor (Unity.exe)|Unity.exe|실행 파일 (*.exe)|*.exe",
            Title = "Unity.exe를 선택하세요.",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _unityPathTextBox.Text = dialog.FileName;
        }
    }

    private async Task SignInAsync()
    {
        try
        {
            var oauthClientConfigurationPath = await SaveAndValidateOAuthSettingsAsync();
            SetBusy(true);
            SetStatus("기본 브라우저에서 Google 로그인을 완료하세요.");
            await _driveClient.SignInAsync(oauthClientConfigurationPath, CancellationToken.None);
            RefreshLoginLabel();
            SetStatus("Google 로그인 완료. 이제 업데이트를 확인하세요.");
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task SignOutAsync()
    {
        try
        {
            SetBusy(true);
            await _driveClient.SignOutAsync(CancellationToken.None);
            RefreshLoginLabel();
            SetStatus("이 Windows 사용자에 저장된 Google 로그인 정보를 삭제하고 Google 권한 취소를 요청했습니다.");
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var (projectPath, manifestFileId, oauthClientConfigurationPath) = await SaveAndValidateSettingsAsync();
            SetBusy(true);
            SetStatus("⏳ 제한된 Drive의 manifest.json을 확인하고 있어요…");
            _updatePlan = await new ExternalAssetsUpdater(_driveClient)
                .CheckForUpdatesAsync(projectPath, manifestFileId, oauthClientConfigurationPath, CancellationToken.None);

            // 버전 행은 "설치됨 vX · 서버 vY — <상태>" 형태로, 지금 무엇을 해야 하는지가
            // 한 줄에 드러나게 한다. 상태 라벨은 다음에 눌러야 할 단계 번호까지 안내한다.
            var installed = _updatePlan.InstalledVersion;
            var server = _updatePlan.RequiredVersion;
            if (_updatePlan.RequiresReset)
            {
                _versionLabel.Text = $"설치됨 v{installed} · 서버 v{server} — ⚠️ 설치본이 서버보다 최신 (복구 필요)";
                SetStatus($"⚠️ 설치된 v{installed}이 서버 v{server}보다 새롭습니다. 전체본 복구가 필요해요.");
                _installButton.Enabled = false;
                _launchButton.Enabled = false;
                return;
            }

            _installButton.Enabled = !_updatePlan.IsCurrent;
            _launchButton.Enabled = _updatePlan.IsCurrent;
            if (_updatePlan.IsCurrent)
            {
                _versionLabel.Text = $"설치됨 v{installed} · 서버 v{server} — ✅ 최신 상태";
                SetStatus("✅ 최신 상태예요. 바로 ④ Unity 실행으로 넘어가면 됩니다.");
            }
            else if (_updatePlan.RequiresLegacyMigration)
            {
                _versionLabel.Text = $"설치됨 v{installed}(구버전 방식) · 서버 v{server} — 🔒 보안 전환 재설치 필요";
                SetStatus("🔒 보안 배포 방식으로 전환합니다. 기존 상태를 신뢰하지 않아 전체본(v1)부터 다시 설치해요. 준비되면 ③ 업데이트 설치를 눌러 주세요.");
            }
            else
            {
                _versionLabel.Text = $"설치됨 v{installed} · 서버 v{server} — ⬇️ 업데이트 필요 ({_updatePlan.Packages.Count}개)";
                SetStatus($"⬇️ 서버 v{server}까지 패키지 {_updatePlan.Packages.Count}개를 받아야 해요. ③ 업데이트 설치를 눌러 주세요.");
            }
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task InstallUpdatesAsync()
    {
        if (_updatePlan is null || _updatePlan.IsCurrent)
        {
            return;
        }

        try
        {
            var (projectPath, _, oauthClientConfigurationPath) = await SaveAndValidateSettingsAsync();
            if (_updatePlan.RequiresLegacyMigration
                && MessageBox.Show(
                    this,
                    "보안 배포 방식으로 전환하려면 기존 Assets/ExternalAssets 폴더를 프로젝트의 ExternalAssetsLegacyBackups 폴더로 이동한 뒤 전체본을 다시 설치해야 합니다. 기존 파일은 삭제하지 않으며, 설치 후 확인할 때까지 백업으로 남깁니다. 계속할까요?",
                    "외부 에셋 보안 전환",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                SetStatus("보안 배포 방식 전환 설치를 취소했어요.");
                return;
            }

            if (ProjectServices.IsUnityEditorRunning())
            {
                throw new InvalidOperationException(
                    "Unity Editor가 실행 중입니다. 열려 있는 Unity Editor를 모두 종료한 뒤 업데이트 설치를 다시 시도하세요.");
            }

            SetBusy(true);
            var progress = new Progress<DownloadProgress>(UpdateProgress);
            // 설치 본체(압축 해제·메타 검증·수천 개 파일 복사)는 동기 CPU/디스크 작업이라
            // UI 스레드에서 그대로 await하면 그동안 메시지 펌프가 멈춰 창이 "응답 없음"으로 표시된다.
            // Task.Run으로 스레드풀에 넘겨 UI 스레드를 살려두고, 진행 상황은
            // Progress<T>가 UI 스레드로 마샬링해 보고한다.
            UpdatePlan plan = _updatePlan;
            var updater = new ExternalAssetsUpdater(_driveClient);
            var installationResult = await Task.Run(() =>
                updater.InstallAsync(projectPath, plan, oauthClientConfigurationPath, progress, CancellationToken.None));

            _progressBar.Value = 100;
            SetStatus("✅ 외부 에셋 설치가 완료됐어요. 업데이트를 다시 확인합니다…");
            await CheckForUpdatesAsync();
            if (installationResult.LegacyBackupPath is not null)
            {
                SetStatus($"✅ 보안 전환 설치가 완료됐어요. 기존 파일은 '{installationResult.LegacyBackupPath}'에 백업해 뒀으니, 새 에셋이 잘 뜨는지 확인한 뒤 직접 삭제하면 됩니다.");
            }
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<(string ProjectPath, string ManifestFileId, string OAuthClientConfigurationPath)> SaveAndValidateSettingsAsync()
    {
        var projectPath = _projectPathTextBox.Text.Trim();
        if (!ProjectServices.IsUnityProject(projectPath))
        {
            throw new InvalidOperationException("Assets, Packages, ProjectSettings 폴더가 있는 Unity 프로젝트를 선택하세요.");
        }

        var manifestFileId = GoogleDriveFileId.Parse(_manifestFileIdTextBox.Text);
        var oauthClientConfigurationPath = await SaveAndValidateOAuthSettingsAsync();
        await ProjectServices.SaveSettingsAsync(new LauncherSettings
        {
            ProjectPath = projectPath,
            ManifestFileId = manifestFileId,
            OAuthClientConfigurationPath = oauthClientConfigurationPath,
            UnityExecutablePath = _unityPathTextBox.Text.Trim(),
        });
        RefreshProjectLabels();
        return (projectPath, manifestFileId, oauthClientConfigurationPath);
    }

    private async Task<string> SaveAndValidateOAuthSettingsAsync()
    {
        var oauthClientConfigurationPath = _oauthClientConfigurationPathTextBox.Text.Trim();
        if (!File.Exists(oauthClientConfigurationPath))
        {
            throw new InvalidOperationException("Google Cloud에서 내려받은 OAuth Desktop 앱 JSON 파일을 선택하세요.");
        }

        var settings = await ProjectServices.LoadSettingsAsync();
        settings.OAuthClientConfigurationPath = Path.GetFullPath(oauthClientConfigurationPath);
        settings.ProjectPath = _projectPathTextBox.Text.Trim();
        settings.ManifestFileId = GoogleDriveFileId.TryParse(_manifestFileIdTextBox.Text, out var manifestFileId)
            ? manifestFileId
            : string.Empty;
        settings.UnityExecutablePath = _unityPathTextBox.Text.Trim();
        await ProjectServices.SaveSettingsAsync(settings);
        return settings.OAuthClientConfigurationPath;
    }

    private void LaunchUnity()
    {
        var projectPath = _projectPathTextBox.Text.Trim();
        var unityPath = _unityPathTextBox.Text.Trim();
        if (!ProjectServices.IsUnityProject(projectPath))
        {
            ShowError("먼저 올바른 Unity 프로젝트 폴더를 선택하세요.");
            return;
        }

        if (!File.Exists(unityPath))
        {
            ShowError("Unity.exe 경로를 선택하세요.");
            return;
        }

        Process.Start(new ProcessStartInfo(unityPath, $"-projectPath \"{projectPath}\"")
        {
            UseShellExecute = true,
        });
        Close();
    }

    private void RefreshProjectLabels()
    {
        var projectPath = _projectPathTextBox.Text.Trim();
        _installPathLabel.Text = ProjectServices.IsUnityProject(projectPath)
            ? ProjectServices.GetExternalAssetsPath(projectPath)
            : "Unity 프로젝트 폴더를 선택하세요.";
    }

    private void RefreshLoginLabel()
    {
        _loginLabel.Text = _driveClient.HasSavedCredentials
            ? "로그인 토큰이 이 Windows 사용자 계정에 저장되어 있습니다. 다른 Google 계정이면 로그아웃 후 다시 로그인하세요."
            : "로그인 필요";
        _signOutButton.Enabled = _driveClient.HasSavedCredentials;
    }

    private void UpdateProgress(DownloadProgress progress)
    {
        _statusLabel.Text = progress.Status;
        if (progress.TotalBytes is > 0)
        {
            // 총량을 아는 단계(다운로드·압축 해제·파일 복사)는 퍼센트 막대로 보여준다.
            // 앞 단계에서 마퀴로 바뀌어 있을 수 있으니 결정형으로 되돌린다.
            _progressBar.Style = ProgressBarStyle.Continuous;
            var percent = (int)Math.Clamp(progress.BytesReceived * 100 / progress.TotalBytes.Value, 0, 100);
            _progressBar.Value = percent;
        }
        else
        {
            // 총량을 모르는 단계(메타 검증 등)는 흐르는 막대로 "작업 중"임을 보여준다.
            _progressBar.Style = ProgressBarStyle.Marquee;
        }
    }

    private void SetBusy(bool isBusy)
    {
        _signInButton.Enabled = !isBusy;
        _signOutButton.Enabled = !isBusy && _driveClient.HasSavedCredentials;
        _checkButton.Enabled = !isBusy;
        _installButton.Enabled = !isBusy && _updatePlan is { IsCurrent: false, RequiresReset: false };
        _launchButton.Enabled = !isBusy && _updatePlan?.IsCurrent == true;
        if (!isBusy)
        {
            _progressBar.Style = ProgressBarStyle.Continuous;
        }
    }

    private void SetStatus(string status)
    {
        _statusLabel.Text = status;
    }

    private void ShowError(string message)
    {
        _progressBar.Style = ProgressBarStyle.Continuous;
        SetStatus($"오류: {message}");
        MessageBox.Show(this, message, "외부 에셋 런처", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
