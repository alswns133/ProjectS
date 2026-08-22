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
    private readonly Button _signInButton = new() { Text = "Google 로그인", AutoSize = true };
    private readonly Button _signOutButton = new() { Text = "로그아웃", AutoSize = true };
    private readonly Button _checkButton = new() { Text = "업데이트 확인", AutoSize = true };
    private readonly Button _installButton = new() { Text = "업데이트 설치", AutoSize = true, Enabled = false };
    private readonly Button _launchButton = new() { Text = "Unity 실행", AutoSize = true, Enabled = false };

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
        SetStatus("OAuth Desktop 앱 JSON을 선택하고 Google 로그인한 뒤 업데이트를 확인하세요.");
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
            SetStatus("제한된 Drive의 manifest.json을 확인 중입니다.");
            _updatePlan = await new ExternalAssetsUpdater(_driveClient)
                .CheckForUpdatesAsync(projectPath, manifestFileId, oauthClientConfigurationPath, CancellationToken.None);

            if (_updatePlan.RequiresReset)
            {
                SetStatus($"설치된 v{_updatePlan.InstalledVersion}이 프로젝트 요구 v{_updatePlan.RequiredVersion}보다 새롭습니다. 전체본 복구가 필요합니다.");
                _installButton.Enabled = false;
                _launchButton.Enabled = false;
                return;
            }

            _versionLabel.Text = $"설치됨 v{_updatePlan.InstalledVersion} / 필요 v{_updatePlan.RequiredVersion}";
            _installButton.Enabled = !_updatePlan.IsCurrent;
            _launchButton.Enabled = _updatePlan.IsCurrent;
            SetStatus(_updatePlan.IsCurrent
                ? "최신 상태입니다. Unity를 실행할 수 있습니다."
                : _updatePlan.RequiresLegacyMigration
                    ? "보안 배포 방식으로 전환합니다. 기존 상태를 신뢰하지 않으므로 전체본(v1)부터 다시 설치해야 합니다."
                    : $"v{_updatePlan.RequiredVersion}까지 패키지 {_updatePlan.Packages.Count}개를 설치해야 합니다.");
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
                SetStatus("보안 배포 방식 전환 설치를 취소했습니다.");
                return;
            }

            if (ProjectServices.IsUnityEditorRunning())
            {
                throw new InvalidOperationException(
                    "Unity Editor가 실행 중입니다. 열려 있는 Unity Editor를 모두 종료한 뒤 업데이트 설치를 다시 시도하세요.");
            }

            SetBusy(true);
            var progress = new Progress<DownloadProgress>(UpdateProgress);
            var installationResult = await new ExternalAssetsUpdater(_driveClient)
                .InstallAsync(projectPath, _updatePlan, oauthClientConfigurationPath, progress, CancellationToken.None);

            _progressBar.Value = 100;
            SetStatus("외부 에셋 설치가 완료되었습니다. 업데이트를 다시 확인합니다.");
            await CheckForUpdatesAsync();
            if (installationResult.LegacyBackupPath is not null)
            {
                SetStatus($"보안 전환 설치가 완료되었습니다. 기존 파일은 '{installationResult.LegacyBackupPath}'에 백업되어 있습니다. 확인 후 직접 삭제하세요.");
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
            var percent = (int)Math.Clamp(progress.BytesReceived * 100 / progress.TotalBytes.Value, 0, 100);
            _progressBar.Value = percent;
        }
        else
        {
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
