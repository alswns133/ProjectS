using System.Diagnostics;

namespace ProjectS.ExternalAssetsLauncher;

internal sealed class MainForm : Form
{
    private readonly TextBox _projectPathTextBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _manifestUrlTextBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _unityPathTextBox = new() { Dock = DockStyle.Fill };
    private readonly Label _installPathLabel = new() { AutoSize = true };
    private readonly Label _versionLabel = new() { AutoSize = true };
    private readonly Label _statusLabel = new() { AutoSize = true };
    private readonly ProgressBar _progressBar = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100 };
    private readonly Button _checkButton = new() { Text = "업데이트 확인", AutoSize = true };
    private readonly Button _installButton = new() { Text = "업데이트 설치", AutoSize = true, Enabled = false };
    private readonly Button _launchButton = new() { Text = "Unity 실행", AutoSize = true, Enabled = false };

    private readonly RemoteManifestClient _manifestClient = new();
    private UpdatePlan? _updatePlan;

    public MainForm()
    {
        Text = "ProjectS 외부 에셋 런처";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 420);
        AutoScaleMode = AutoScaleMode.Font;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 3,
            RowCount = 9,
            AutoSize = true,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddRow(layout, 0, "프로젝트 폴더", _projectPathTextBox, CreateButton("찾기", SelectProjectFolder));
        AddRow(layout, 1, "설치 경로", _installPathLabel, null);
        AddRow(layout, 2, "manifest.json 링크", _manifestUrlTextBox, null);
        AddRow(layout, 3, "Unity.exe", _unityPathTextBox, CreateButton("찾기", SelectUnityExecutable));
        AddRow(layout, 4, "설치/서버 버전", _versionLabel, null);
        AddRow(layout, 5, "상태", _statusLabel, null);
        AddRow(layout, 6, "진행률", _progressBar, null);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
        };
        actions.Controls.AddRange([_checkButton, _installButton, _launchButton]);
        layout.Controls.Add(actions, 1, 7);
        layout.SetColumnSpan(actions, 2);

        var help = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(680, 0),
            Text = "런처는 ZIP을 임시 폴더에서 검사한 뒤 Assets/ExternalAssets에 반영합니다. Unity Editor가 실행 중이면 업데이트 설치를 막습니다.",
        };
        layout.Controls.Add(help, 1, 8);
        layout.SetColumnSpan(help, 2);

        Controls.Add(layout);
        _checkButton.Click += async (_, _) => await CheckForUpdatesAsync();
        _installButton.Click += async (_, _) => await InstallUpdatesAsync();
        _launchButton.Click += (_, _) => LaunchUnity();
        Load += async (_, _) => await InitializeAsync();
        FormClosed += (_, _) => _manifestClient.Dispose();
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
        _manifestUrlTextBox.Text = settings.ManifestUrl;
        _unityPathTextBox.Text = File.Exists(settings.UnityExecutablePath)
            ? settings.UnityExecutablePath
            : ProjectServices.FindUnityExecutable(_projectPathTextBox.Text) ?? string.Empty;
        RefreshProjectLabels();
        SetStatus("프로젝트와 manifest.json 링크를 확인한 뒤 업데이트를 검사하세요.");
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

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var (projectPath, manifestUrl) = await SaveAndValidateSettingsAsync();
            SetBusy(true);
            SetStatus("서버의 manifest.json을 확인 중입니다.");
            _updatePlan = await new ExternalAssetsUpdater(_manifestClient)
                .CheckForUpdatesAsync(projectPath, manifestUrl, CancellationToken.None);

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
            var (projectPath, _) = await SaveAndValidateSettingsAsync();
            if (ProjectServices.IsUnityEditorRunning())
            {
                throw new InvalidOperationException(
                    "Unity Editor가 실행 중입니다. 열려 있는 Unity Editor를 모두 종료한 뒤 업데이트 설치를 다시 시도하세요.");
            }

            SetBusy(true);
            var progress = new Progress<DownloadProgress>(UpdateProgress);
            await new ExternalAssetsUpdater(_manifestClient)
                .InstallAsync(projectPath, _updatePlan, progress, CancellationToken.None);

            _progressBar.Value = 100;
            SetStatus("외부 에셋 설치가 완료되었습니다. 업데이트를 다시 확인합니다.");
            await CheckForUpdatesAsync();
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

    private async Task<(string ProjectPath, string ManifestUrl)> SaveAndValidateSettingsAsync()
    {
        var projectPath = _projectPathTextBox.Text.Trim();
        var manifestUrl = _manifestUrlTextBox.Text.Trim();
        if (!ProjectServices.IsUnityProject(projectPath))
        {
            throw new InvalidOperationException("Assets, Packages, ProjectSettings 폴더가 있는 Unity 프로젝트를 선택하세요.");
        }

        if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("올바른 manifest.json 공유 링크를 입력하세요.");
        }

        await ProjectServices.SaveSettingsAsync(new LauncherSettings
        {
            ProjectPath = projectPath,
            ManifestUrl = manifestUrl,
            UnityExecutablePath = _unityPathTextBox.Text.Trim(),
        });
        RefreshProjectLabels();
        return (projectPath, manifestUrl);
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
