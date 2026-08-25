using System.Text.Json;

namespace ProjectS.ExternalAssetsPublisher;

internal sealed class MainForm : Form
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly TextBox _projectPathTextBox = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly TextBox _outputPathTextBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _manifestPathTextBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _baseZipPathTextBox = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly TextBox _packageNameTextBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _driveFileIdTextBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _removedPathsTextBox = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        Height = 54,
        ScrollBars = ScrollBars.Vertical,
    };
    private readonly NumericUpDown _versionInput = new()
    {
        Minimum = 1,
        Maximum = 100000,
        Value = 2,
        Dock = DockStyle.Left,
        Width = 100,
    };
    private readonly ComboBox _packageTypeComboBox = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Dock = DockStyle.Left,
        Width = 100,
    };
    private readonly ListView _sourceList = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
        HideSelection = false,
        MultiSelect = true,
    };
    private readonly Label _externalRootLabel = new() { AutoSize = true };
    private readonly Label _statusLabel = new() { AutoSize = true };
    private readonly Label _resultLabel = new() { AutoSize = true, MaximumSize = new Size(900, 0) };
    private readonly ProgressBar _progressBar = new() { Width = 760, Height = 14, Minimum = 0, Maximum = 100, Style = ProgressBarStyle.Continuous, Margin = new Padding(0, 2, 0, 8) };
    private readonly Button _selectProjectButton = new() { Text = "찾기", AutoSize = true };
    private readonly Button _selectOutputButton = new() { Text = "찾기", AutoSize = true };
    private readonly Button _selectManifestButton = new() { Text = "찾기", AutoSize = true };
    private readonly Button _detectChangesButton = new() { Text = "변경 파일 자동 찾기", AutoSize = true };
    private readonly Button _addFilesButton = new() { Text = "파일 추가", AutoSize = true };
    private readonly Button _addFolderButton = new() { Text = "폴더 추가", AutoSize = true };
    private readonly Button _removeSourcesButton = new() { Text = "선택 제거", AutoSize = true };
    private readonly Button _createPackageButton = new() { Text = "ZIP 생성", AutoSize = true };
    private readonly Button _loadBaseZipButton = new() { Text = "Base Builder ZIP 선택", AutoSize = true };
    private readonly Button _saveManifestButton = new() { Text = "manifest.json 저장", AutoSize = true, Enabled = false };
    private readonly TextBox _snapshotPathTextBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _snapshotDriveIdTextBox = new() { Dock = DockStyle.Fill };
    private readonly Button _selectSnapshotButton = new() { Text = "찾기", AutoSize = true };
    private readonly Button _composePatchButton = new() { Text = "변경 비교 → 패치 자동 구성", AutoSize = true };
    private readonly Button _createSnapshotButton = new() { Text = "현재 스냅샷 생성", AutoSize = true };
    private readonly Button _attachSnapshotButton = new() { Text = "v1 스냅샷 업로드·연결", AutoSize = true, Enabled = false };
    private readonly TextBox _oauthPathTextBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _manifestDriveIdTextBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _releasesFolderIdTextBox = new() { Dock = DockStyle.Fill };
    private readonly Button _selectOAuthButton = new() { Text = "찾기", AutoSize = true };
    private readonly Button _signInButton = new() { Text = "Google 로그인(쓰기)", AutoSize = true };
    private readonly Button _signOutButton = new() { Text = "로그아웃", AutoSize = true };
    private readonly Button _compareDriveButton = new() { Text = "Drive와 비교 → 확인 후 게시", AutoSize = true };
    private readonly Button _autoPublishButton = new() { Text = "자동 업로드·게시", AutoSize = true };
    private readonly Label _loginLabel = new() { AutoSize = true };

    private readonly PublisherGoogleOAuthClient _oauthClient = new();
    private readonly DriveUploadClient _driveClient = new();
    private readonly ToolTip _fieldHelpTip = new();
    private readonly CheckBox _showAdvancedCheck = new() { Text = "고급 · 수동 옵션 표시", AutoSize = true, Margin = new Padding(16, 6, 0, 0) };

    // 기본 화면을 단순하게: 아래 컨트롤들은 '고급'일 때만 보인다. 소스 목록 행(Percent)은 별도로 접는다.
    private readonly List<Control> _advancedControls = [];
    private TableLayoutPanel _layout = null!;
    private const int SourceListRowIndex = 6;

    private readonly List<SourceSelection> _sources = [];
    private PackageBuildResult? _packageBuild;
    private string? _generatedSnapshotPath;
    private bool _isImportedBasePackage;
    private bool _isBusy;

    // 변경 자동 감지(체크리스트) 모드. 목록이 SourceSelection(수동) 대신 감지된 변경 파일로 채워진다.
    private bool _changeMode;
    private ReleaseSnapshot? _changeBaseline;
    private ReleaseSnapshot? _changeCurrent;

    private sealed record ChangeItem(string RelativePath, string Kind);

    public MainForm()
    {
        Text = "ProjectS 외부 에셋 배포자";
        StartPosition = FormStartPosition.CenterScreen;
        // 기본(간단) 화면은 짧게, 고급을 펼치면 길어진다. 최소 높이는 기본 화면 기준으로 낮춰,
        // 접었을 때 빈 공간이 남지 않게 한다(실제 높이는 고급 토글에서 조절).
        MinimumSize = new Size(1000, 560);
        AutoScaleMode = AutoScaleMode.Font;

        _packageTypeComboBox.Items.AddRange(["base", "patch"]);
        _packageTypeComboBox.SelectedItem = "patch";
        _packageNameTextBox.Text = "Patch_v2.zip";

        _sourceList.CheckBoxes = true;
        _sourceList.Columns.Add("종류", 90);
        _sourceList.Columns.Add("경로 (ExternalAssets 기준)", 700);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 3,
            RowCount = 20,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        AddRow(layout, 0, "프로젝트 폴더", _projectPathTextBox, _selectProjectButton,
            "ProjectS Unity 프로젝트의 최상위 폴더입니다(Assets·Packages·ProjectSettings가 있는 곳). 이 안의 Assets/ExternalAssets가 배포 대상이 됩니다.", advanced: true);
        AddRow(layout, 1, "외부 에셋 루트", _externalRootLabel, null,
            "실제로 배포되는 폴더(Assets/ExternalAssets)입니다. 프로젝트 폴더에서 자동으로 정해지며, 이 안의 파일만 패치에 담깁니다.", advanced: true);
        AddRow(layout, 2, "ZIP 저장 폴더", _outputPathTextBox, _selectOutputButton,
            "만든 패치 ZIP과 스냅샷(json)이 저장되는 로컬 폴더입니다. 여기 생긴 파일을 Drive에 올립니다(자동 업로드를 쓰면 알아서 올라갑니다).", advanced: true);
        AddRow(layout, 3, "manifest.json 저장 위치", _manifestPathTextBox, _selectManifestButton,
            "버전 목록 파일(manifest.json)의 로컬 경로입니다. 새 패치 정보를 여기에 추가하고, 이 내용을 Drive의 manifest 파일로 덮어써 팀원에게 게시합니다.", advanced: true);
        AddRow(layout, 4, "Base Builder ZIP 등록", _baseZipPathTextBox, _loadBaseZipButton,
            "최초 Base v1을 등록할 때만 씁니다. Base Builder가 만든 ZIP을 골라 검사·등록합니다. 패치(v2 이상)에는 사용하지 않습니다.", advanced: true);

        var packageOptions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        packageOptions.Controls.Add(new Label { Text = "버전", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
        packageOptions.Controls.Add(_versionInput);
        packageOptions.Controls.Add(new Label { Text = "종류", AutoSize = true, Padding = new Padding(12, 7, 0, 0) });
        packageOptions.Controls.Add(_packageTypeComboBox);
        packageOptions.Controls.Add(new Label { Text = "ZIP 이름", AutoSize = true, Padding = new Padding(12, 7, 0, 0) });
        packageOptions.Controls.Add(_packageNameTextBox);
        _packageNameTextBox.Width = 260;
        AddRow(layout, 5, "패치 정보", packageOptions, null,
            "이번에 만들 패키지의 버전·종류·ZIP 이름입니다. '변경 파일 자동 찾기'나 'Drive와 비교'를 쓰면 자동으로 채워지므로 보통 손대지 않아도 됩니다.", advanced: true);

        var sourceLabel = new Label { Text = "압축할 원본", AutoSize = true, Anchor = AnchorStyles.Left };
        layout.Controls.Add(sourceLabel, 0, 6);
        layout.Controls.Add(_sourceList, 1, 6);
        layout.SetColumnSpan(_sourceList, 2);
        _advancedControls.Add(sourceLabel);
        _advancedControls.Add(_sourceList);

        var sourceActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        sourceActions.Controls.Add(_detectChangesButton);
        sourceActions.Controls.Add(_addFilesButton);
        sourceActions.Controls.Add(_addFolderButton);
        sourceActions.Controls.Add(_removeSourcesButton);
        layout.Controls.Add(sourceActions, 1, 7);
        layout.SetColumnSpan(sourceActions, 2);
        _advancedControls.Add(sourceActions);

        AddRow(layout, 8, "삭제할 상대 경로", _removedPathsTextBox, null,
            "이 패치에서 '삭제'할 파일들의 경로입니다(ExternalAssets 기준, 한 줄에 하나). '변경 파일 자동 찾기'를 쓰면 지워진 파일이 자동으로 채워집니다.", advanced: true);
        AddRow(layout, 9, "ZIP Drive 파일 링크 / ID", _driveFileIdTextBox, null,
            "패치 ZIP을 Drive에 올린 뒤 그 파일의 링크나 ID를 넣는 칸입니다. '자동 업로드·게시'를 쓰면 자동으로 채워집니다(수동 게시할 때만 직접 입력).", advanced: true);

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        actions.Controls.Add(_createPackageButton);
        actions.Controls.Add(_saveManifestButton);
        layout.Controls.Add(actions, 1, 10);
        layout.SetColumnSpan(actions, 2);
        _advancedControls.Add(actions);

        AddRow(layout, 11, "직전 버전 스냅샷(json)", _snapshotPathTextBox, _selectSnapshotButton,
            "비교 기준이 되는 '직전 배포 버전의 스냅샷' 파일(release-snapshot-vN.json)입니다. Drive에서 최신 스냅샷을 받아 지정하세요. 이게 있어야 '변경 파일 자동 찾기'가 무엇이 바뀌었는지 계산합니다.", advanced: true);
        AddRow(layout, 12, "스냅샷 Drive 링크 / ID", _snapshotDriveIdTextBox, null,
            "새로 만든 스냅샷(json)을 Drive에 올린 뒤 그 파일의 링크/ID입니다. '자동 업로드·게시'를 쓰면 자동으로 채워집니다.", advanced: true);

        var autoActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        autoActions.Controls.Add(_composePatchButton);
        autoActions.Controls.Add(_createSnapshotButton);
        layout.Controls.Add(autoActions, 1, 13);
        layout.SetColumnSpan(autoActions, 2);
        _advancedControls.Add(autoActions);

        AddRow(layout, 14, "OAuth Desktop 앱 JSON", _oauthPathTextBox, _selectOAuthButton,
            "Google OAuth 데스크톱 클라이언트 파일(google-oauth-client.json)입니다. 런처가 쓰는 것과 같은 파일이며, exe 옆에 두면 자동으로 인식됩니다.");
        AddRow(layout, 15, "Google 계정", _loginLabel, null,
            "쓰기 권한 로그인 상태입니다. '자동 업로드·게시'를 하려면 편집 권한이 있는 계정으로 'Google 로그인(쓰기)'을 먼저 해야 합니다(런처의 읽기 전용 로그인과 별개).");
        AddRow(layout, 16, "manifest Drive 파일 ID", _manifestDriveIdTextBox, null,
            "팀원 런처가 읽는 Drive의 manifest 파일 ID입니다. 게시할 때 이 파일을 '같은 ID로' 덮어써야 런처 참조가 끊기지 않습니다.");
        AddRow(layout, 17, "릴리스 폴더 Drive ID", _releasesFolderIdTextBox, null,
            "패치 ZIP과 스냅샷을 새로 업로드할, 제한된 Drive 폴더의 ID입니다. 링크나 폴더 ID를 넣으면 됩니다.");

        var publishActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        publishActions.Controls.Add(_signInButton);
        publishActions.Controls.Add(_signOutButton);
        publishActions.Controls.Add(_compareDriveButton);
        publishActions.Controls.Add(_autoPublishButton);
        publishActions.Controls.Add(_attachSnapshotButton);
        publishActions.Controls.Add(_showAdvancedCheck);
        layout.Controls.Add(publishActions, 1, 18);
        layout.SetColumnSpan(publishActions, 2);
        _advancedControls.Add(_autoPublishButton);

        var help = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(900, 0),
            Text = "수동: 파일/폴더를 골라 ZIP 생성. 자동: 직전 버전 스냅샷(json)을 지정하고 '변경 비교 → 패치 자동 구성'을 누르면 바뀐 파일만 담은 패치 ZIP과 새 스냅샷을 만들어 줍니다. "
                + "최초 Base v1은 Base Builder ZIP 선택으로 등록합니다. 만든 ZIP·스냅샷을 제한된 Drive에 올린 뒤 각 링크/ID를 붙여넣고 manifest.json을 저장하세요.\r\n"
                + "완전 자동: 'Google 로그인(쓰기)' 후 manifest 파일 ID·릴리스 폴더 ID를 넣고 'Drive와 비교 → 확인 후 게시'를 누르면 — Drive 최신 상태를 자동으로 받아 현재 파일과 비교하고, 다른 파일 목록을 확인 창으로 보여준 뒤 동의하면 ZIP·스냅샷 업로드와 manifest 덮어쓰기까지 한 번에 처리합니다. "
                + "여러 배포자 충돌은 게시 전/직전 버전·리비전 확인으로 막습니다. (쓰기 스코프 최초 1회 재동의 필요)",
        };
        var footer = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
        };
        footer.Controls.Add(_progressBar);
        footer.Controls.Add(_statusLabel);
        footer.Controls.Add(_resultLabel);
        footer.Controls.Add(help);
        layout.Controls.Add(footer, 1, 19);
        layout.SetColumnSpan(footer, 2);

        Controls.Add(layout);
        _layout = layout;

        _versionInput.ValueChanged += (_, _) => UpdatePackageDefaults();
        _packageTypeComboBox.SelectedIndexChanged += (_, _) => UpdatePackageDefaults();
        _selectProjectButton.Click += (_, _) => SelectProjectFolder();
        _selectOutputButton.Click += (_, _) => SelectOutputFolder();
        _selectManifestButton.Click += (_, _) => SelectManifestPath();
        _detectChangesButton.Click += async (_, _) => await DetectChangesAsync();
        _addFilesButton.Click += (_, _) => AddFiles();
        _addFolderButton.Click += (_, _) => AddFolder();
        _removeSourcesButton.Click += (_, _) => RemoveSelectedSources();
        _createPackageButton.Click += async (_, _) => await CreatePackageAsync();
        _loadBaseZipButton.Click += async (_, _) => await SelectExistingBaseZipAsync();
        _saveManifestButton.Click += async (_, _) => await SaveManifestAsync();
        _selectSnapshotButton.Click += (_, _) => SelectSnapshotPath();
        _composePatchButton.Click += async (_, _) => await ComposePatchAsync();
        _createSnapshotButton.Click += async (_, _) => await CreateSnapshotAsync();
        _attachSnapshotButton.Click += async (_, _) => await AttachSnapshotAsync();
        _selectOAuthButton.Click += (_, _) => SelectOAuthPath();
        _signInButton.Click += async (_, _) => await SignInAsync();
        _signOutButton.Click += async (_, _) => await SignOutAsync();
        _compareDriveButton.Click += async (_, _) => await CompareWithDriveAsync();
        _autoPublishButton.Click += async (_, _) => await AutoPublishAsync();
        _showAdvancedCheck.CheckedChanged += (_, _) => { SetAdvancedVisible(_showAdvancedCheck.Checked); PersistSettings(); };
        _manifestDriveIdTextBox.Leave += (_, _) => { PersistSettings(); RefreshLoginLabel(); };
        _releasesFolderIdTextBox.Leave += (_, _) => { PersistSettings(); RefreshLoginLabel(); };
        _snapshotPathTextBox.Leave += (_, _) => PersistSettings();
        _oauthPathTextBox.Leave += (_, _) => PersistSettings();
        _outputPathTextBox.Leave += (_, _) => PersistSettings();
        _manifestPathTextBox.Leave += (_, _) => PersistSettings();
        Load += async (_, _) => await InitializeAsync();
    }

    private void AddRow(TableLayoutPanel layout, int row, string label, Control content, Control? trailingControl, string? helpText = null, bool advanced = false)
    {
        var labelControl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 5, 0, 0) };
        Control labelCell = labelControl;
        if (!string.IsNullOrWhiteSpace(helpText))
        {
            var panel = new FlowLayoutPanel
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0),
            };
            panel.Controls.Add(labelControl);
            panel.Controls.Add(CreateHelpIcon(label, helpText));
            labelCell = panel;
        }

        layout.Controls.Add(labelCell, 0, row);
        layout.Controls.Add(content, 1, row);
        if (trailingControl is not null)
        {
            layout.Controls.Add(trailingControl, 2, row);
        }

        if (advanced)
        {
            _advancedControls.Add(labelCell);
            _advancedControls.Add(content);
            if (trailingControl is not null)
            {
                _advancedControls.Add(trailingControl);
            }
        }
    }

    /// <summary>항목 옆의 물음표 아이콘. 클릭하면 그 항목이 무엇인지 설명 창을 띄운다.</summary>
    private Control CreateHelpIcon(string title, string helpText)
    {
        var icon = new Label
        {
            Text = "?",
            AutoSize = false,
            Size = new Size(18, 18),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Font.FontFamily, 8f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(120, 130, 205),
            Margin = new Padding(6, 3, 0, 0),
            Cursor = Cursors.Hand,
        };
        _fieldHelpTip.SetToolTip(icon, "클릭하면 설명");
        icon.Click += (_, _) => MessageBox.Show(this, helpText, $"도움말 — {title}", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return icon;
    }

    private async Task InitializeAsync()
    {
        var projectPath = PublisherServices.FindProjectRoot(AppContext.BaseDirectory) ?? string.Empty;
        _projectPathTextBox.Text = projectPath;
        if (PublisherServices.IsUnityProject(projectPath))
        {
            _outputPathTextBox.Text = Path.Combine(projectPath, "ExternalAssetsReleases");
            _manifestPathTextBox.Text = Path.Combine(projectPath, "ExternalAssetsReleases", "manifest.json");
        }

        if (File.Exists(PublisherGoogleOAuthClient.GetDefaultConfigurationPath()))
        {
            _oauthPathTextBox.Text = PublisherGoogleOAuthClient.GetDefaultConfigurationPath();
        }

        // 저장된 설정이 있으면 그 값으로 채워, 매번 다시 입력하지 않게 한다.
        var settings = await PublisherServices.LoadSettingsAsync();
        if (PublisherServices.IsUnityProject(settings.ProjectPath))
        {
            _projectPathTextBox.Text = settings.ProjectPath;
        }

        if (!string.IsNullOrWhiteSpace(settings.OutputPath))
        {
            _outputPathTextBox.Text = settings.OutputPath;
        }

        if (!string.IsNullOrWhiteSpace(settings.ManifestPath))
        {
            _manifestPathTextBox.Text = settings.ManifestPath;
        }

        if (File.Exists(settings.OAuthPath))
        {
            _oauthPathTextBox.Text = settings.OAuthPath;
        }

        _manifestDriveIdTextBox.Text = settings.ManifestDriveId;
        _releasesFolderIdTextBox.Text = settings.ReleasesFolderId;
        _snapshotPathTextBox.Text = settings.SnapshotPath;
        _showAdvancedCheck.Checked = settings.ShowAdvanced;

        SetAdvancedVisible(settings.ShowAdvanced);
        RefreshProjectLabels();
        RefreshLoginLabel();
    }

    /// <summary>기본 화면은 로그인·게시에 필요한 것만 보인다. '고급'을 켜면 수동 옵션·소스 목록이 나온다.</summary>
    private void SetAdvancedVisible(bool show)
    {
        foreach (var control in _advancedControls)
        {
            control.Visible = show;
        }

        // 소스 목록 행은 Percent라 숨겨도 자리를 차지하므로 행 높이 자체를 접는다.
        _layout.RowStyles[SourceListRowIndex] = show
            ? new RowStyle(SizeType.Percent, 100)
            : new RowStyle(SizeType.Absolute, 0);
        Height = show ? 1080 : 560;
    }

    private async void PersistSettings()
    {
        try
        {
            await PublisherServices.SaveSettingsAsync(new PublisherSettings
            {
                ProjectPath = _projectPathTextBox.Text.Trim(),
                OutputPath = _outputPathTextBox.Text.Trim(),
                ManifestPath = _manifestPathTextBox.Text.Trim(),
                OAuthPath = _oauthPathTextBox.Text.Trim(),
                ManifestDriveId = _manifestDriveIdTextBox.Text.Trim(),
                ReleasesFolderId = _releasesFolderIdTextBox.Text.Trim(),
                SnapshotPath = _snapshotPathTextBox.Text.Trim(),
                ShowAdvanced = _showAdvancedCheck.Checked,
            });
        }
        catch
        {
            // 설정 저장 실패는 조용히 무시(핵심 기능 아님).
        }
    }

    private void SelectProjectFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "ProjectS Unity 프로젝트 폴더를 선택하세요." };
        if (Directory.Exists(_projectPathTextBox.Text))
        {
            dialog.InitialDirectory = _projectPathTextBox.Text;
        }

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _projectPathTextBox.Text = dialog.SelectedPath;
        _sources.Clear();
        ResetPackageBuild();
        if (string.IsNullOrWhiteSpace(_outputPathTextBox.Text))
        {
            _outputPathTextBox.Text = Path.Combine(dialog.SelectedPath, "ExternalAssetsReleases");
        }

        if (string.IsNullOrWhiteSpace(_manifestPathTextBox.Text))
        {
            _manifestPathTextBox.Text = Path.Combine(dialog.SelectedPath, "ExternalAssetsReleases", "manifest.json");
        }

        RefreshProjectLabels();
        RefreshSourceList();
        PersistSettings();
    }

    private void SelectOutputFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "생성할 ZIP 파일의 저장 폴더를 선택하세요." };
        if (Directory.Exists(_outputPathTextBox.Text))
        {
            dialog.InitialDirectory = _outputPathTextBox.Text;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _outputPathTextBox.Text = dialog.SelectedPath;
        }
    }

    private void SelectManifestPath()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "저장하거나 갱신할 manifest.json을 선택하세요.",
            Filter = "JSON 파일 (*.json)|*.json",
            FileName = "manifest.json",
            AddExtension = true,
        };
        if (Directory.Exists(Path.GetDirectoryName(_manifestPathTextBox.Text)))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(_manifestPathTextBox.Text);
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _manifestPathTextBox.Text = dialog.FileName;
        }
    }

    private async Task SelectExistingBaseZipAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Base Builder가 생성한 Base ZIP을 선택하세요.",
            Filter = "ZIP 파일 (*.zip)|*.zip",
            Multiselect = false,
            CheckFileExists = true,
        };
        var initialDirectory = Path.GetDirectoryName(_baseZipPathTextBox.Text);
        if (Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }
        else if (Directory.Exists(_outputPathTextBox.Text))
        {
            dialog.InitialDirectory = _outputPathTextBox.Text;
        }

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // 새 Base를 고르는 순간 이전 ZIP을 저장 대상으로 남겨 두지 않는다.
        InvalidatePackageBuild();

        try
        {
            var projectPath = GetProjectPathOrThrow();
            var selectedZipPath = dialog.FileName;
            SetBusy(true);
            SetStatus("Base Builder ZIP의 경로, .meta, GUID, SHA-256을 확인하는 중입니다.");
            var package = await Task.Run(() => PublisherServices.LoadExistingBasePackage(selectedZipPath, projectPath));

            _packageBuild = package;
            _isImportedBasePackage = true;
            _versionInput.Value = 1;
            _packageTypeComboBox.SelectedItem = "base";
            _packageNameTextBox.Text = package.PackageName;
            _removedPathsTextBox.Clear();
            _driveFileIdTextBox.Clear();
            _baseZipPathTextBox.Text = package.ZipPath;
            _resultLabel.Text = $"검증된 Base Builder ZIP: {package.ZipPath}\r\n파일 {package.FileEntryCount:N0}개 / {FormatBytes(package.SizeBytes)}\r\nSHA-256: {package.Sha256}";
            SetStatus("Base v1 등록 준비 완료. 제한된 Drive에 이 ZIP 바이트 그대로 업로드한 뒤 파일 링크 또는 ID를 입력하고 manifest.json을 저장하세요.");
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

    private void AddFiles()
    {
        try
        {
            var externalAssetsPath = GetExternalAssetsPathOrThrow();
            using var dialog = new OpenFileDialog
            {
                Title = "ExternalAssets 안에서 패치할 파일을 선택하세요.",
                InitialDirectory = externalAssetsPath,
                Filter = "모든 파일 (*.*)|*.*",
                Multiselect = true,
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            foreach (var fileName in dialog.FileNames)
            {
                AddSource(new SourceSelection(fileName, false));
            }

            RefreshSourceList();
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void AddFolder()
    {
        try
        {
            var externalAssetsPath = GetExternalAssetsPathOrThrow();
            using var dialog = new FolderBrowserDialog
            {
                Description = "ExternalAssets 안에서 통째로 패키징할 폴더를 선택하세요.",
                InitialDirectory = externalAssetsPath,
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            AddSource(new SourceSelection(dialog.SelectedPath, true));
            RefreshSourceList();
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void RemoveSelectedSources()
    {
        if (_changeMode)
        {
            // 감지 목록에서는 선택 행을 목록에서 빼면 그만(체크 해제로 제외해도 됨).
            foreach (var item in _sourceList.SelectedItems.Cast<ListViewItem>().ToArray())
            {
                _sourceList.Items.Remove(item);
            }

            InvalidatePackageBuild();
            return;
        }

        var removedAny = false;
        foreach (ListViewItem item in _sourceList.SelectedItems)
        {
            if (item.Tag is SourceSelection selection)
            {
                removedAny |= _sources.Remove(selection);
            }
        }

        if (removedAny)
        {
            InvalidatePackageBuild();
        }

        RefreshSourceList();
    }

    /// <summary>
    /// 직전 버전 스냅샷과 현재 ExternalAssets를 비교해, 바뀐 파일(추가·수정)을 '압축할 원본' 목록에
    /// 체크박스로 채운다. 유니티 패키지 임포트 창처럼, 담을 파일만 체크로 골라 'ZIP 생성'하면 된다.
    /// 삭제된 파일은 '삭제할 상대 경로'에 자동으로 채운다.
    /// </summary>
    private async Task DetectChangesAsync()
    {
        try
        {
            if (_isImportedBasePackage)
            {
                throw new InvalidOperationException("Base 등록 모드입니다. 변경 감지는 패치 흐름에서만 씁니다.");
            }

            var projectPath = GetProjectPathOrThrow();
            var snapshotPath = _snapshotPathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(snapshotPath))
            {
                throw new InvalidOperationException(
                    "‘직전 버전 스냅샷(json)’을 지정하세요. Drive에서 최신 스냅샷을 받아 넣거나, 로그인 상태면 ‘Drive와 비교’를 쓰세요.");
            }

            SetBusy(true);
            SetStatus("직전 스냅샷을 읽고 현재 파일과 비교하는 중… (파일이 많으면 시간이 걸려요)");
            var baseline = await SnapshotService.ReadAsync(snapshotPath);
            var newVersion = baseline.Version + 1;
            var hashProgress = new Progress<SnapshotProgress>(OnSnapshotProgress);
            var current = await Task.Run(() => SnapshotService.CreateFromExternalAssets(projectPath, newVersion, baseline.ChannelId, hashProgress));
            var delta = SnapshotService.ComputeDelta(baseline, current);

            _changeMode = true;
            _changeBaseline = baseline;
            _changeCurrent = current;
            _sources.Clear();
            InvalidatePackageBuild();
            RenderChangeList(delta);

            _versionInput.Value = newVersion;
            _packageTypeComboBox.SelectedItem = "patch";
            _packageNameTextBox.Text = $"Patch_v{newVersion}.zip";
            _removedPathsTextBox.Text = string.Join("\r\n", delta.Removed);

            if (!delta.HasChanges)
            {
                SetStatus($"직전 스냅샷(v{baseline.Version})과 동일합니다. 바뀐 파일이 없어요.");
            }
            else
            {
                var removedNote = delta.Removed.Count > 0 ? " 삭제는 아래 ‘삭제할 상대 경로’에 채워졌습니다." : string.Empty;
                SetStatus($"변경 감지: 추가 {delta.Added.Count} · 수정 {delta.Modified.Count} · 삭제 {delta.Removed.Count}. "
                    + $"담을 파일만 체크하고 ‘ZIP 생성’을 누르세요.{removedNote}");
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

    private void RenderChangeList(SnapshotDelta delta)
    {
        _sourceList.BeginUpdate();
        _sourceList.Items.Clear();
        try
        {
            foreach (var path in delta.Added)
            {
                AddChangeRow("추가", path);
            }

            foreach (var path in delta.Modified)
            {
                AddChangeRow("수정", path);
            }
        }
        finally
        {
            _sourceList.EndUpdate();
        }
    }

    private void AddChangeRow(string kind, string relativePath)
    {
        var item = new ListViewItem(kind) { Tag = new ChangeItem(relativePath, kind), Checked = true };
        item.SubItems.Add(relativePath);
        _sourceList.Items.Add(item);
    }

    private async Task CreatePackageAsync()
    {
        try
        {
            if (_isImportedBasePackage)
            {
                throw new InvalidOperationException("현재는 Base Builder ZIP 등록 모드입니다. 패치를 만들려면 파일 또는 폴더를 추가해 새 패키지를 준비하세요.");
            }

            var projectPath = GetProjectPathOrThrow();
            var outputPath = _outputPathTextBox.Text.Trim();
            var packageName = _packageNameTextBox.Text;

            if (_changeMode)
            {
                if (_changeBaseline is null || _changeCurrent is null)
                {
                    throw new InvalidOperationException("먼저 ‘변경 파일 자동 찾기’로 목록을 채우세요.");
                }

                var changedFiles = _sourceList.CheckedItems.Cast<ListViewItem>()
                    .Where(item => item.Tag is ChangeItem)
                    .Select(item => ((ChangeItem)item.Tag!).RelativePath)
                    .ToArray();
                if (changedFiles.Length == 0)
                {
                    throw new InvalidOperationException("체크된 파일이 없습니다. 압축할 파일을 최소 1개 체크하세요.");
                }

                var removed = ParseRemovedPaths();
                var newVersion = Decimal.ToInt32(_versionInput.Value);
                SetBusy(true);
                SetStatus($"체크한 {changedFiles.Length:N0}개로 {packageName}을 만드는 중…");
                var package = await Task.Run(() => PublisherServices.CreatePatchPackageFromFiles(projectPath, changedFiles, outputPath, packageName));

                // 일부만 골랐을 수 있으니, 새 스냅샷은 baseline + '선택된 변경'만 반영해야 다음 비교가 어긋나지 않는다.
                var nextSnapshot = SnapshotService.BuildNextSnapshot(
                    _changeBaseline, _changeCurrent, changedFiles, removed, newVersion, _changeBaseline.ChannelId);
                var snapshotOutputPath = Path.Combine(outputPath, $"release-snapshot-v{newVersion}.json");
                await SnapshotService.WriteAsync(snapshotOutputPath, nextSnapshot);
                _generatedSnapshotPath = snapshotOutputPath;

                _packageBuild = package;
                _baseZipPathTextBox.Clear();
                _driveFileIdTextBox.Clear();
                _snapshotDriveIdTextBox.Clear();
                _resultLabel.Text = $"생성됨: {package.ZipPath}\r\n담긴 파일 {package.FileEntryCount:N0}개 / {FormatBytes(package.SizeBytes)}\r\n삭제 {removed.Count:N0} · 새 스냅샷: {snapshotOutputPath}";
                SetStatus($"v{newVersion} 패치 ZIP·스냅샷 생성 완료. ‘자동 업로드·게시’로 올리거나, Drive에 올린 뒤 링크를 넣고 manifest.json을 저장하세요.");
                return;
            }

            var sources = _sources.ToArray();
            SetBusy(true);
            SetStatus("선택한 원본의 상대 경로와 .meta를 수집해 ZIP을 만드는 중입니다.");
            _packageBuild = await Task.Run(() => PublisherServices.CreatePackage(
                projectPath,
                sources,
                outputPath,
                packageName));

            _baseZipPathTextBox.Clear();
            _driveFileIdTextBox.Clear();
            _resultLabel.Text = $"생성됨: {_packageBuild.ZipPath}\r\n파일 {_packageBuild.FileEntryCount:N0}개 / {FormatBytes(_packageBuild.SizeBytes)}\r\nSHA-256: {_packageBuild.Sha256}";
            SetStatus("ZIP 생성 완료. ZIP을 제한된 Google Drive에 올린 뒤 파일 링크 또는 ID를 입력하고 manifest.json을 저장하세요.");
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

    private async Task SaveManifestAsync()
    {
        if (_packageBuild is null)
        {
            return;
        }

        try
        {
            var manifestPath = _manifestPathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                throw new InvalidOperationException("manifest.json을 저장할 위치를 선택하세요.");
            }

            SetBusy(true);
            SetStatus("manifest.json에 새 패키지 정보를 기록하는 중입니다.");
            await PublisherServices.AppendPackageToManifestAsync(
                manifestPath,
                Decimal.ToInt32(_versionInput.Value),
                _packageTypeComboBox.SelectedItem?.ToString() ?? "patch",
                _packageBuild,
                _driveFileIdTextBox.Text.Trim(),
                ParseRemovedPaths(),
                _snapshotDriveIdTextBox.Text.Trim());
            SetStatus("manifest.json 저장 완료. Drive의 기존 manifest.json 파일은 새 버전으로 교체해 같은 파일 ID를 유지하세요.");
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

    private void SelectSnapshotPath()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "직전 배포 버전의 스냅샷(release-snapshot-vN.json)을 선택하세요.",
            Filter = "JSON 파일 (*.json)|*.json",
            CheckFileExists = true,
        };
        var initialDirectory = Path.GetDirectoryName(_snapshotPathTextBox.Text);
        if (Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }
        else if (Directory.Exists(_outputPathTextBox.Text))
        {
            dialog.InitialDirectory = _outputPathTextBox.Text;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _snapshotPathTextBox.Text = dialog.FileName;
        }
    }

    /// <summary>
    /// 직전 버전 스냅샷과 현재 ExternalAssets를 비교해, 바뀐 파일만 담은 패치 ZIP과
    /// 새 버전 스냅샷을 자동으로 만든다. 사람이 파일을 고르는 수동 흐름을 대신한다.
    /// 여러 배포자 환경이라, 시작 전에 "로컬이 서버 최신보다 뒤처졌는지"를 확인해
    /// 뒤처진 상태로 만들다 남의 추가 파일이 삭제로 잡히는 사고를 막는다.
    /// </summary>
    private async Task ComposePatchAsync()
    {
        try
        {
            var projectPath = GetProjectPathOrThrow();
            var manifestPath = _manifestPathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                throw new InvalidOperationException("manifest.json 위치를 지정하세요. 직전 버전 정보를 여기서 읽습니다.");
            }

            var outputPath = _outputPathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new InvalidOperationException("ZIP 저장 폴더를 지정하세요.");
            }

            SetBusy(true);
            SetStatus("manifest와 직전 스냅샷을 확인하는 중입니다.");
            var manifest = await PublisherServices.LoadManifestAsync(manifestPath);
            if (manifest.Packages.Count == 0)
            {
                throw new InvalidOperationException("manifest에 base(v1)가 없습니다. 최초 Base v1을 먼저 등록하세요. 패치는 v2부터입니다.");
            }

            var latestVersion = manifest.LatestVersion;
            var channelId = manifest.ChannelId;
            var baseline = await SnapshotService.ReadAsync(_snapshotPathTextBox.Text.Trim());
            if (baseline.Version != latestVersion)
            {
                throw new InvalidOperationException(
                    $"선택한 스냅샷은 v{baseline.Version}인데 manifest 최신은 v{latestVersion}입니다. 최신(v{latestVersion}) 스냅샷을 받아 사용하세요.");
            }

            if (!string.Equals(baseline.ChannelId, channelId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("스냅샷과 manifest의 channelId가 다릅니다. 같은 배포 채널의 파일을 사용하세요.");
            }

            var installedVersion = PublisherServices.TryReadInstalledVersion(projectPath);
            if (installedVersion is int installed && installed < latestVersion)
            {
                throw new InvalidOperationException(
                    $"로컬 외부 에셋이 v{installed}로 서버 최신 v{latestVersion}보다 뒤처져 있습니다. 런처로 최신 패치를 먼저 받은 뒤 다시 시도하세요. "
                    + "(뒤처진 상태로 만들면 다른 사람이 추가한 파일이 삭제로 잡힐 수 있습니다.)");
            }

            var newVersion = latestVersion + 1;
            SetStatus($"현재 ExternalAssets를 해시해 v{latestVersion}과 비교하는 중입니다… (파일이 많으면 시간이 걸려요)");
            var hashProgress = new Progress<SnapshotProgress>(OnSnapshotProgress);
            var current = await Task.Run(() => SnapshotService.CreateFromExternalAssets(projectPath, newVersion, channelId, hashProgress));
            var delta = SnapshotService.ComputeDelta(baseline, current);
            if (!delta.HasChanges)
            {
                SetStatus("변경 사항이 없습니다. 만들 패치가 없어요.");
                return;
            }

            var changedFiles = delta.Added.Concat(delta.Modified).ToArray();
            if (changedFiles.Length == 0)
            {
                throw new InvalidOperationException(
                    "이번 변경은 삭제만 있습니다. 현재 패치는 추가/수정 파일이 최소 1개 필요합니다(삭제 전용 패치는 아직 지원하지 않습니다).");
            }

            var packageName = $"Patch_v{newVersion}.zip";
            SetStatus($"바뀐 파일 {changedFiles.Length:N0}개로 {packageName}을 만드는 중입니다…");
            var package = await Task.Run(() => PublisherServices.CreatePatchPackageFromFiles(
                projectPath, changedFiles, outputPath, packageName));

            var snapshotOutputPath = Path.Combine(outputPath, $"release-snapshot-v{newVersion}.json");
            await SnapshotService.WriteAsync(snapshotOutputPath, current);
            _generatedSnapshotPath = snapshotOutputPath;

            // 자동 구성 결과를 저장 흐름에 연결: 버전·이름·삭제 경로를 채우고 패키지를 확정한다.
            _isImportedBasePackage = false;
            _packageBuild = package;
            _versionInput.Value = newVersion;
            _packageTypeComboBox.SelectedItem = "patch";
            _packageNameTextBox.Text = packageName;
            _removedPathsTextBox.Text = string.Join("\r\n", delta.Removed);
            _baseZipPathTextBox.Clear();
            _driveFileIdTextBox.Clear();
            _snapshotDriveIdTextBox.Clear();

            _resultLabel.Text =
                $"v{newVersion} 패치 구성 완료\r\n"
                + $"추가 {delta.Added.Count:N0} · 수정 {delta.Modified.Count:N0} · 삭제 {delta.Removed.Count:N0}\r\n"
                + $"ZIP: {package.ZipPath}  ({FormatBytes(package.SizeBytes)}, SHA-256 {package.Sha256})\r\n"
                + $"새 스냅샷: {snapshotOutputPath}";

            var removedWarning = delta.Removed.Count > 0
                ? $" ⚠️ 삭제 {delta.Removed.Count}건 — '삭제할 상대 경로'를 꼭 확인하세요."
                : string.Empty;
            SetStatus(
                $"v{newVersion} 자동 구성 완료.{removedWarning} ZIP과 새 스냅샷을 Drive에 올린 뒤 각 링크/ID를 입력하고 manifest.json을 저장하세요.");
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

    /// <summary>
    /// 현재 ExternalAssets를 manifest 최신 버전의 스냅샷으로 생성한다.
    /// 최초 1회(v1) 기준선을 만들 때 사용한다(로컬이 정확히 그 버전일 때).
    /// 이후 버전 스냅샷은 <see cref="ComposePatchAsync"/>가 자동으로 만든다.
    /// </summary>
    private async Task CreateSnapshotAsync()
    {
        try
        {
            var projectPath = GetProjectPathOrThrow();
            var manifestPath = _manifestPathTextBox.Text.Trim();
            var outputPath = _outputPathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(manifestPath) || string.IsNullOrWhiteSpace(outputPath))
            {
                throw new InvalidOperationException("manifest.json 위치와 ZIP 저장 폴더를 먼저 지정하세요.");
            }

            SetBusy(true);
            var manifest = await PublisherServices.LoadManifestAsync(manifestPath);
            if (manifest.Packages.Count == 0)
            {
                throw new InvalidOperationException("manifest에 등록된 버전이 없습니다. 먼저 Base v1을 등록하세요.");
            }

            var version = manifest.LatestVersion;
            SetStatus($"현재 ExternalAssets를 v{version} 스냅샷으로 만드는 중입니다… (파일이 많으면 시간이 걸려요)");
            var hashProgress = new Progress<SnapshotProgress>(OnSnapshotProgress);
            var snapshot = await Task.Run(() => SnapshotService.CreateFromExternalAssets(projectPath, version, manifest.ChannelId, hashProgress));
            var snapshotOutputPath = Path.Combine(outputPath, $"release-snapshot-v{version}.json");
            await SnapshotService.WriteAsync(snapshotOutputPath, snapshot);
            _generatedSnapshotPath = snapshotOutputPath;
            _resultLabel.Text = $"v{version} 스냅샷 생성: {snapshotOutputPath}\r\n파일 {snapshot.Entries.Count:N0}개";
            SetStatus(
                $"v{version} 스냅샷 생성 완료. 'v1 스냅샷 업로드·연결'을 눌러 Drive와 manifest에 한 번에 연결하세요.");
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

    /// <summary>
    /// 최초 Base v1 스냅샷을 Drive에 올리고, 같은 manifest 파일의 v1 패키지에 그 ID를 연결한다.
    /// manifest를 마지막에 덮어써 ZIP은 올라갔는데 기준선이 없는 중간 상태를 피한다.
    /// </summary>
    private async Task AttachSnapshotAsync()
    {
        try
        {
            var snapshotPath = _generatedSnapshotPath;
            if (string.IsNullOrWhiteSpace(snapshotPath) || !File.Exists(snapshotPath))
            {
                throw new InvalidOperationException("먼저 '현재 스냅샷 생성'으로 v1 스냅샷을 만드세요.");
            }

            var manifestPath = _manifestPathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                throw new InvalidOperationException("manifest.json 저장 위치를 먼저 지정하세요.");
            }

            var oauthPath = _oauthPathTextBox.Text.Trim();
            if (!File.Exists(oauthPath))
            {
                throw new InvalidOperationException("OAuth Desktop 앱 JSON을 지정하세요.");
            }
            var manifestFileId = DriveUploadClient.ExtractId(_manifestDriveIdTextBox.Text);
            var folderId = DriveUploadClient.ExtractId(_releasesFolderIdTextBox.Text);

            SetBusy(true);
            SetStatus("Drive의 최신 manifest를 확인 중입니다…");
            var accessToken = await _oauthClient.GetAccessTokenAsync(oauthPath, CancellationToken.None);
            var driveManifestText = await _driveClient.DownloadTextAsync(accessToken, manifestFileId, CancellationToken.None);
            var driveManifest = JsonSerializer.Deserialize<PublisherManifest>(driveManifestText, ManifestJsonOptions)
                ?? throw new InvalidOperationException("Drive manifest를 읽을 수 없습니다.");

            if (driveManifest.LatestVersion != 1)
            {
                throw new InvalidOperationException(
                    $"이 버튼은 최초 v1 기준선 연결용입니다. Drive 최신은 v{driveManifest.LatestVersion}이므로 사용하지 마세요.");
            }

            var basePackage = driveManifest.Packages.SingleOrDefault(package => package.Version == 1)
                ?? throw new InvalidOperationException("Drive manifest에 Base v1 항목이 없습니다.");
            if (!string.IsNullOrWhiteSpace(basePackage.SnapshotDriveFileId))
            {
                throw new InvalidOperationException("Base v1 스냅샷은 이미 Drive manifest에 연결되어 있습니다.");
            }

            var driveInfo = await _driveClient.GetFileInfoAsync(accessToken, manifestFileId, CancellationToken.None);
            var snapshotName = Path.GetFileName(snapshotPath);
            SetStatus($"v1 스냅샷 업로드 중: {snapshotName}…");
            var snapshotFileId = await _driveClient.UploadNewFileAsync(
                accessToken, folderId, snapshotPath, snapshotName, "application/json", CancellationToken.None);

            // Drive에서 방금 받은 정확한 manifest 위에만 기록한다. 다른 배포자의 변경을 덮어쓰지 않는다.
            await File.WriteAllTextAsync(manifestPath, driveManifestText, CancellationToken.None);
            await PublisherServices.AttachSnapshotToPackageAsync(manifestPath, 1, snapshotFileId);

            var recheck = await _driveClient.GetFileInfoAsync(accessToken, manifestFileId, CancellationToken.None);
            if (!string.Equals(recheck.HeadRevisionId, driveInfo.HeadRevisionId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "업로드 도중 다른 사람이 manifest를 바꿨습니다. 스냅샷은 Drive에 올라갔지만 manifest는 덮어쓰지 않았습니다. 다시 확인 후 시도하세요.");
            }

            SetStatus("Drive manifest에 v1 스냅샷을 연결하는 중…");
            await _driveClient.UpdateFileMediaAsync(accessToken, manifestFileId, manifestPath, "application/json", CancellationToken.None);
            _snapshotDriveIdTextBox.Text = snapshotFileId;
            _resultLabel.Text = $"Base v1 스냅샷 연결 완료\r\n스냅샷 fileId: {snapshotFileId}";
            SetStatus("✅ v1 기준선 연결 완료. 이제 Unity에서 수정한 뒤 'Drive와 비교 → 확인 후 게시'를 누르세요.");
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

    private void SelectOAuthPath()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Google OAuth Desktop 앱 JSON을 선택하세요.",
            Filter = "JSON 파일 (*.json)|*.json",
            CheckFileExists = true,
        };
        var directory = Path.GetDirectoryName(_oauthPathTextBox.Text);
        if (Directory.Exists(directory))
        {
            dialog.InitialDirectory = directory;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _oauthPathTextBox.Text = dialog.FileName;
        }
    }

    private async Task SignInAsync()
    {
        try
        {
            var oauthPath = _oauthPathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(oauthPath))
            {
                throw new InvalidOperationException("OAuth Desktop 앱 JSON을 지정하세요.");
            }

            SetBusy(true);
            SetStatus("브라우저에서 Google 로그인(쓰기 권한)을 진행하세요…");
            await _oauthClient.SignInAsync(oauthPath, CancellationToken.None);
            SetStatus("Google 로그인 완료. 이제 '자동 업로드·게시'를 쓸 수 있습니다.");
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally
        {
            SetBusy(false);
            RefreshLoginLabel();
        }
    }

    private async Task SignOutAsync()
    {
        try
        {
            SetBusy(true);
            await _oauthClient.SignOutAsync(CancellationToken.None);
            SetStatus("Google 로그아웃 완료.");
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally
        {
            SetBusy(false);
            RefreshLoginLabel();
        }
    }

    /// <summary>지금 상태에서 '다음에 할 일'을 안내한다(신규 배포자가 헤매지 않게).</summary>
    private void RefreshLoginLabel()
    {
        if (!_oauthClient.HasSavedCredentials)
        {
            _loginLabel.Text = "① 편집 권한이 있는 계정으로 'Google 로그인(쓰기)'을 먼저 하세요.";
            return;
        }

        var ready = !string.IsNullOrWhiteSpace(_manifestDriveIdTextBox.Text)
            && !string.IsNullOrWhiteSpace(_releasesFolderIdTextBox.Text);
        _loginLabel.Text = ready
            ? "로그인됨 · 준비 완료 — 'Drive와 비교 → 확인 후 게시'를 누르면 됩니다."
            : "로그인됨 — manifest 파일 ID와 릴리스 폴더 ID를 넣으세요.";
    }

    /// <summary>
    /// 자동 구성으로 만든 패치 ZIP·스냅샷을 Drive에 올리고, manifest를 새 버전으로 덮어써 게시한다.
    /// 여러 배포자 충돌을 막기 위해 (1) 게시 전 Drive 최신 버전이 예상과 같은지, (2) 업로드 도중
    /// manifest headRevision이 바뀌지 않았는지 두 번 확인한다. manifest는 같은 파일 ID로 덮어써
    /// 팀원 런처의 참조가 끊기지 않게 한다.
    /// </summary>
    private async Task AutoPublishAsync()
    {
        try
        {
            if (_packageBuild is null || _generatedSnapshotPath is null)
            {
                throw new InvalidOperationException("먼저 '변경 비교 → 패치 자동 구성'으로 패치와 스냅샷을 만든 뒤 자동 게시하세요.");
            }

            var manifestPath = _manifestPathTextBox.Text.Trim();
            var oauthPath = _oauthPathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                throw new InvalidOperationException("manifest.json(로컬) 위치를 지정하세요.");
            }

            if (string.IsNullOrWhiteSpace(oauthPath))
            {
                throw new InvalidOperationException("OAuth Desktop 앱 JSON을 지정하세요.");
            }

            var manifestFileId = DriveUploadClient.ExtractId(_manifestDriveIdTextBox.Text);
            var folderId = DriveUploadClient.ExtractId(_releasesFolderIdTextBox.Text);
            var package = _packageBuild;
            var snapshotPath = _generatedSnapshotPath;
            var targetVersion = Decimal.ToInt32(_versionInput.Value);
            var removedPaths = ParseRemovedPaths();

            SetBusy(true);
            SetStatus("Google 인증 토큰을 확인하는 중…");
            var accessToken = await _oauthClient.GetAccessTokenAsync(oauthPath, CancellationToken.None);

            SetStatus("Drive의 현재 manifest를 확인하는 중…");
            var driveInfo = await _driveClient.GetFileInfoAsync(accessToken, manifestFileId, CancellationToken.None);
            var driveManifestText = await _driveClient.DownloadTextAsync(accessToken, manifestFileId, CancellationToken.None);
            var driveManifest = JsonSerializer.Deserialize<PublisherManifest>(driveManifestText, ManifestJsonOptions)
                ?? throw new InvalidOperationException("Drive manifest를 해석할 수 없습니다.");
            if (driveManifest.LatestVersion != targetVersion - 1)
            {
                throw new InvalidOperationException(
                    $"Drive 최신이 v{driveManifest.LatestVersion}입니다(이 패치는 v{targetVersion}). 그새 누가 먼저 게시했을 수 있어요. "
                    + "런처로 최신을 받고 '변경 비교 → 패치 자동 구성'을 다시 눌러 재구성하세요.");
            }

            var (zipFileId, snapshotFileId) = await PublishComposedAsync(
                accessToken, manifestPath, manifestFileId, folderId,
                driveManifestText, driveInfo.HeadRevisionId,
                targetVersion, package, snapshotPath, removedPaths);

            _resultLabel.Text =
                $"게시 완료 v{targetVersion}\r\n"
                + $"ZIP fileId: {zipFileId}\r\n"
                + $"스냅샷 fileId: {snapshotFileId}\r\n"
                + "manifest: 같은 파일 ID로 덮어씀";
            SetStatus($"✅ v{targetVersion} 자동 업로드·게시 완료. 팀원 런처에서 바로 받을 수 있습니다.");
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally
        {
            SetBusy(false);
            RefreshLoginLabel();
        }
    }

    /// <summary>
    /// Drive에 올라간 최신 상태(manifest가 가리키는 최신 스냅샷)를 자동으로 받아 현재 ExternalAssets와
    /// 비교하고, 다른 파일 목록을 확인 창으로 보여준 뒤 동의하면 패치를 만들어 업로드·게시한다.
    /// '직전 스냅샷 json'을 손으로 지정하던 단계를 없앤, 가장 자동화된 배포 경로.
    /// </summary>
    private async Task CompareWithDriveAsync()
    {
        try
        {
            var projectPath = GetProjectPathOrThrow();
            var manifestPath = _manifestPathTextBox.Text.Trim();
            var oauthPath = _oauthPathTextBox.Text.Trim();
            var outputPath = _outputPathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                throw new InvalidOperationException("manifest.json(로컬) 위치를 지정하세요.");
            }

            if (string.IsNullOrWhiteSpace(oauthPath))
            {
                throw new InvalidOperationException("OAuth Desktop 앱 JSON을 지정하세요.");
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new InvalidOperationException("ZIP 저장 폴더를 지정하세요.");
            }

            var manifestFileId = DriveUploadClient.ExtractId(_manifestDriveIdTextBox.Text);
            var folderId = DriveUploadClient.ExtractId(_releasesFolderIdTextBox.Text);

            SetBusy(true);
            SetStatus("Google 인증 토큰을 확인하는 중…");
            var accessToken = await _oauthClient.GetAccessTokenAsync(oauthPath, CancellationToken.None);

            SetStatus("Drive의 최신 manifest·스냅샷을 받는 중…");
            var driveInfo = await _driveClient.GetFileInfoAsync(accessToken, manifestFileId, CancellationToken.None);
            var driveManifestText = await _driveClient.DownloadTextAsync(accessToken, manifestFileId, CancellationToken.None);
            var driveManifest = JsonSerializer.Deserialize<PublisherManifest>(driveManifestText, ManifestJsonOptions)
                ?? throw new InvalidOperationException("Drive manifest를 해석할 수 없습니다.");
            if (driveManifest.Packages.Count == 0)
            {
                throw new InvalidOperationException("Drive manifest에 base(v1)가 없습니다. 먼저 Base v1을 등록하세요.");
            }

            var latestVersion = driveManifest.LatestVersion;
            var channelId = driveManifest.ChannelId;
            var latestPackage = driveManifest.Packages.FirstOrDefault(package => package.Version == latestVersion);
            var baselineSnapshotId = latestPackage?.SnapshotDriveFileId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(baselineSnapshotId))
            {
                throw new InvalidOperationException(
                    $"Drive manifest의 v{latestVersion} 항목에 스냅샷(snapshotDriveFileId)이 없습니다. "
                    + "최초 1회 '현재 스냅샷 생성'으로 기준선을 올리고 manifest에 ID를 넣으세요.");
            }

            var baselineText = await _driveClient.DownloadTextAsync(accessToken, baselineSnapshotId, CancellationToken.None);
            var baseline = JsonSerializer.Deserialize<ReleaseSnapshot>(baselineText, ManifestJsonOptions)
                ?? throw new InvalidOperationException("Drive 스냅샷을 해석할 수 없습니다.");
            if (!string.Equals(baseline.ChannelId, channelId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Drive 스냅샷과 manifest의 channelId가 다릅니다. 배포 채널을 확인하세요.");
            }

            var newVersion = latestVersion + 1;
            SetStatus($"현재 ExternalAssets를 해시해 Drive(v{latestVersion})와 비교하는 중… (파일이 많으면 시간이 걸려요)");
            var hashProgress = new Progress<SnapshotProgress>(OnSnapshotProgress);
            var current = await Task.Run(() => SnapshotService.CreateFromExternalAssets(projectPath, newVersion, channelId, hashProgress));
            var delta = SnapshotService.ComputeDelta(baseline, current);
            if (!delta.HasChanges)
            {
                SetStatus($"✅ 현재 파일이 Drive(v{latestVersion})와 동일합니다. 올릴 것이 없어요.");
                return;
            }

            var changedFiles = delta.Added.Concat(delta.Modified).ToArray();
            if (changedFiles.Length == 0)
            {
                throw new InvalidOperationException(
                    "이번 변경은 삭제만 있습니다. 현재 패치는 추가/수정 파일이 최소 1개 필요합니다(삭제 전용 패치는 아직 지원하지 않습니다).");
            }

            // 확인 창: 무엇이 다른지 보여주고 올릴지 묻는다(삭제가 있으면 특히 강조).
            var confirm = MessageBox.Show(
                this,
                BuildCompareMessage(latestVersion, newVersion, delta),
                "Drive와 다른 파일 — 업로드할까요?",
                MessageBoxButtons.YesNo,
                delta.Removed.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes)
            {
                _removedPathsTextBox.Text = string.Join("\r\n", delta.Removed);
                _resultLabel.Text = $"미리보기: 추가 {delta.Added.Count:N0} · 수정 {delta.Modified.Count:N0} · 삭제 {delta.Removed.Count:N0} (업로드 취소됨)";
                SetStatus("업로드를 취소했어요. 검토 후 다시 눌러 진행하세요.");
                return;
            }

            var packageName = $"Patch_v{newVersion}.zip";
            SetStatus($"바뀐 파일 {changedFiles.Length:N0}개로 {packageName}을 만드는 중…");
            var package = await Task.Run(() => PublisherServices.CreatePatchPackageFromFiles(projectPath, changedFiles, outputPath, packageName));
            var snapshotOutputPath = Path.Combine(outputPath, $"release-snapshot-v{newVersion}.json");
            await SnapshotService.WriteAsync(snapshotOutputPath, current);
            _generatedSnapshotPath = snapshotOutputPath;
            _isImportedBasePackage = false;
            _packageBuild = package;
            _versionInput.Value = newVersion;
            _packageTypeComboBox.SelectedItem = "patch";
            _packageNameTextBox.Text = packageName;
            _removedPathsTextBox.Text = string.Join("\r\n", delta.Removed);

            var (zipFileId, snapshotUploadedId) = await PublishComposedAsync(
                accessToken, manifestPath, manifestFileId, folderId,
                driveManifestText, driveInfo.HeadRevisionId,
                newVersion, package, snapshotOutputPath, delta.Removed);

            _resultLabel.Text =
                $"게시 완료 v{newVersion} (Drive 비교 기반)\r\n"
                + $"추가 {delta.Added.Count:N0} · 수정 {delta.Modified.Count:N0} · 삭제 {delta.Removed.Count:N0}\r\n"
                + $"ZIP fileId: {zipFileId}\r\n스냅샷 fileId: {snapshotUploadedId}";
            SetStatus($"✅ v{newVersion} 업로드·게시 완료. 팀원 런처에서 바로 받을 수 있습니다.");
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally
        {
            SetBusy(false);
            RefreshLoginLabel();
        }
    }

    private static string BuildCompareMessage(int latestVersion, int newVersion, SnapshotDelta delta)
    {
        const int maxList = 12;
        var builder = new System.Text.StringBuilder();
        builder.AppendLine($"Drive 최신 v{latestVersion}과 현재 파일이 다릅니다.");
        builder.AppendLine();
        builder.AppendLine($"  추가 {delta.Added.Count}  ·  수정 {delta.Modified.Count}  ·  삭제 {delta.Removed.Count}");
        builder.AppendLine();
        AppendSample(builder, "＋ 추가", delta.Added, maxList);
        AppendSample(builder, "～ 수정", delta.Modified, maxList);
        if (delta.Removed.Count > 0)
        {
            AppendSample(builder, "－ 삭제(주의)", delta.Removed, maxList);
        }

        builder.Append($"패치 v{newVersion}로 만들어 Drive에 업로드하고 게시할까요?");
        return builder.ToString();
    }

    private static void AppendSample(System.Text.StringBuilder builder, string title, IReadOnlyList<string> items, int max)
    {
        if (items.Count == 0)
        {
            return;
        }

        builder.AppendLine($"[{title}]");
        foreach (var item in items.Take(max))
        {
            builder.AppendLine($"  {item}");
        }

        if (items.Count > max)
        {
            builder.AppendLine($"  … 외 {items.Count - max}개");
        }

        builder.AppendLine();
    }

    /// <summary>
    /// 자동 구성한 패치 ZIP·스냅샷을 Drive에 올리고 manifest를 덮어써 게시하는 공통 로직.
    /// AutoPublish와 CompareWithDrive가 함께 쓴다. manifest는 같은 파일 ID로 덮어써 런처 참조를 유지하고,
    /// 게시 직전 headRevision을 다시 확인해 그새 남이 바꿨으면 manifest는 건드리지 않는다.
    /// </summary>
    private async Task<(string ZipFileId, string SnapshotFileId)> PublishComposedAsync(
        string accessToken,
        string manifestLocalPath,
        string manifestFileId,
        string folderId,
        string driveManifestText,
        string driveHeadRevisionBefore,
        int targetVersion,
        PackageBuildResult package,
        string snapshotPath,
        IReadOnlyList<string> removedPaths)
    {
        // Drive의 진실을 로컬 manifest로 내려, 정확히 그 위에 새 버전을 append 한다.
        await File.WriteAllTextAsync(manifestLocalPath, driveManifestText, CancellationToken.None);

        SetStatus($"패치 ZIP 업로드 중: {package.PackageName}…");
        var zipFileId = await _driveClient.UploadNewFileAsync(
            accessToken, folderId, package.ZipPath, package.PackageName, "application/zip", CancellationToken.None);

        var snapshotName = Path.GetFileName(snapshotPath);
        SetStatus($"스냅샷 업로드 중: {snapshotName}…");
        var snapshotFileId = await _driveClient.UploadNewFileAsync(
            accessToken, folderId, snapshotPath, snapshotName, "application/json", CancellationToken.None);

        _driveFileIdTextBox.Text = zipFileId;
        _snapshotDriveIdTextBox.Text = snapshotFileId;

        SetStatus("로컬 manifest에 새 버전을 기록하는 중…");
        await PublisherServices.AppendPackageToManifestAsync(
            manifestLocalPath, targetVersion, "patch", package, zipFileId, removedPaths, snapshotFileId);

        // 게시 직전 재확인: 내가 받은 뒤 다른 사람이 manifest를 바꿨으면 덮어쓰지 않는다.
        var recheck = await _driveClient.GetFileInfoAsync(accessToken, manifestFileId, CancellationToken.None);
        if (!string.Equals(recheck.HeadRevisionId, driveHeadRevisionBefore, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "업로드 도중 Drive manifest가 다른 사람에 의해 바뀌었습니다. ZIP·스냅샷은 올라갔지만 manifest는 덮어쓰지 않았습니다. "
                + "재동기화 후 manifest만 다시 게시하세요.");
        }

        SetStatus("Drive manifest를 새 버전으로 덮어쓰는 중(게시)…");
        await _driveClient.UpdateFileMediaAsync(accessToken, manifestFileId, manifestLocalPath, "application/json", CancellationToken.None);
        return (zipFileId, snapshotFileId);
    }

    private void AddSource(SourceSelection selection)
    {
        if (_isImportedBasePackage)
        {
            ResetPackageBuild();
        }

        if (_changeMode)
        {
            // 수동 추가로 전환: 자동 감지 목록을 비우고 일반 원본 목록 모드로 돌아간다.
            _changeMode = false;
            _changeBaseline = null;
            _changeCurrent = null;
            _sourceList.Items.Clear();
        }

        var fullPath = Path.GetFullPath(selection.FullPath);
        if (_sources.Any(existing => existing.IsFolder == selection.IsFolder
            && string.Equals(existing.FullPath, fullPath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _sources.Add(selection with { FullPath = fullPath });
        InvalidatePackageBuild();
    }

    private void RefreshProjectLabels()
    {
        var projectPath = _projectPathTextBox.Text.Trim();
        _externalRootLabel.Text = PublisherServices.IsUnityProject(projectPath)
            ? PublisherServices.GetExternalAssetsPath(projectPath)
            : "올바른 Unity 프로젝트 폴더를 선택하세요.";
    }

    private void RefreshSourceList()
    {
        _sourceList.BeginUpdate();
        _sourceList.Items.Clear();
        try
        {
            var projectPath = GetProjectPathOrThrow();
            foreach (var source in _sources)
            {
                var relativePath = PublisherServices.GetRelativePathForDisplay(projectPath, source);
                var item = new ListViewItem(source.IsFolder ? "폴더" : "파일") { Tag = source, Checked = true };
                item.SubItems.Add(source.IsFolder ? relativePath + "/**" : relativePath);
                _sourceList.Items.Add(item);
            }
        }
        catch (Exception)
        {
            // 프로젝트를 새로 선택하는 중에는 목록을 비워 둔다.
        }
        finally
        {
            _sourceList.EndUpdate();
        }
    }

    private void UpdatePackageDefaults()
    {
        if (_isImportedBasePackage && _packageBuild is not null)
        {
            _packageNameTextBox.Text = _packageBuild.PackageName;
            return;
        }

        var version = Decimal.ToInt32(_versionInput.Value);
        if (version < 2)
        {
            _versionInput.Value = 2;
            return;
        }

        var type = _packageTypeComboBox.SelectedItem?.ToString() ?? "patch";
        if (!string.Equals(type, "patch", StringComparison.OrdinalIgnoreCase))
        {
            _packageTypeComboBox.SelectedItem = "patch";
            return;
        }

        var expectedName = $"Patch_v{version}.zip";
        if (string.IsNullOrWhiteSpace(_packageNameTextBox.Text)
            || _packageNameTextBox.Text.StartsWith("Base_v", StringComparison.OrdinalIgnoreCase)
            || _packageNameTextBox.Text.StartsWith("Patch_v", StringComparison.OrdinalIgnoreCase))
        {
            _packageNameTextBox.Text = expectedName;
        }
    }

    private string GetProjectPathOrThrow()
    {
        var projectPath = _projectPathTextBox.Text.Trim();
        if (!PublisherServices.IsUnityProject(projectPath))
        {
            throw new InvalidOperationException("Assets, Packages, ProjectSettings 폴더가 있는 Unity 프로젝트를 선택하세요.");
        }

        return projectPath;
    }

    private string GetExternalAssetsPathOrThrow() =>
        PublisherServices.GetExternalAssetsPath(GetProjectPathOrThrow());

    private IReadOnlyList<string> ParseRemovedPaths() =>
        _removedPathsTextBox.Text.Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        if (!isBusy)
        {
            _progressBar.Value = 0;
        }

        UpdateControlState();
    }

    private void ResetPackageBuild()
    {
        InvalidatePackageBuild();
    }

    private void InvalidatePackageBuild()
    {
        _packageBuild = null;
        _isImportedBasePackage = false;
        _baseZipPathTextBox.Clear();
        _driveFileIdTextBox.Clear();
        UpdateControlState();
    }

    private void UpdateControlState()
    {
        var canInteract = !_isBusy;
        var canEditPackageIdentity = !_isBusy && !_isImportedBasePackage && _packageBuild is null;
        var canEditRemovedPaths = !_isBusy && !_isImportedBasePackage;
        _projectPathTextBox.Enabled = canInteract;
        _outputPathTextBox.Enabled = canInteract;
        _manifestPathTextBox.Enabled = canInteract;
        _baseZipPathTextBox.Enabled = canInteract;
        _driveFileIdTextBox.Enabled = canInteract;
        _sourceList.Enabled = canInteract;
        _selectProjectButton.Enabled = canInteract;
        _selectOutputButton.Enabled = canInteract;
        _selectManifestButton.Enabled = canInteract;
        _detectChangesButton.Enabled = canInteract && !_isImportedBasePackage;
        _addFilesButton.Enabled = canInteract;
        _addFolderButton.Enabled = canInteract;
        _removeSourcesButton.Enabled = canInteract;
        _versionInput.Enabled = canEditPackageIdentity;
        _packageTypeComboBox.Enabled = canEditPackageIdentity;
        _packageNameTextBox.Enabled = canEditPackageIdentity;
        _removedPathsTextBox.Enabled = canEditRemovedPaths;
        _loadBaseZipButton.Enabled = !_isBusy;
        _createPackageButton.Enabled = !_isBusy && !_isImportedBasePackage;
        _saveManifestButton.Enabled = !_isBusy && _packageBuild is not null;
        _snapshotPathTextBox.Enabled = canInteract;
        _snapshotDriveIdTextBox.Enabled = canInteract;
        _selectSnapshotButton.Enabled = canInteract;
        _composePatchButton.Enabled = !_isBusy && !_isImportedBasePackage;
        _createSnapshotButton.Enabled = canInteract;
        _attachSnapshotButton.Enabled = canInteract && !string.IsNullOrWhiteSpace(_generatedSnapshotPath);
        _oauthPathTextBox.Enabled = canInteract;
        _manifestDriveIdTextBox.Enabled = canInteract;
        _releasesFolderIdTextBox.Enabled = canInteract;
        _selectOAuthButton.Enabled = canInteract;
        _signInButton.Enabled = canInteract;
        _signOutButton.Enabled = canInteract && _oauthClient.HasSavedCredentials;
        _compareDriveButton.Enabled = canInteract;
        _autoPublishButton.Enabled = !_isBusy && _packageBuild is not null && _generatedSnapshotPath is not null;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        PersistSettings();
        base.OnFormClosed(e);
        _oauthClient.Dispose();
        _driveClient.Dispose();
    }

    private void SetStatus(string status)
    {
        _statusLabel.Text = status;
    }

    /// <summary>스냅샷 해싱 중 '지금 파일'과 진행률을 실시간으로 보여준다(Progress&lt;T&gt;로 UI 스레드에서 호출됨).</summary>
    private void OnSnapshotProgress(SnapshotProgress progress)
    {
        if (progress.Total > 0)
        {
            _progressBar.Value = (int)Math.Clamp((long)progress.Processed * 100 / progress.Total, 0, 100);
        }

        _statusLabel.Text = $"스냅샷 해싱 {progress.Processed:N0}/{progress.Total:N0} — {progress.CurrentFile}";
    }

    private void ShowError(string message)
    {
        SetStatus($"오류: {message}");
        MessageBox.Show(this, message, "외부 에셋 배포자", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var size = (double)bytes;
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.##} {units[unitIndex]}";
    }
}
