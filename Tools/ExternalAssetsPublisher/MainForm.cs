namespace ProjectS.ExternalAssetsPublisher;

internal sealed class MainForm : Form
{
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
    private readonly Button _selectProjectButton = new() { Text = "찾기", AutoSize = true };
    private readonly Button _selectOutputButton = new() { Text = "찾기", AutoSize = true };
    private readonly Button _selectManifestButton = new() { Text = "찾기", AutoSize = true };
    private readonly Button _addFilesButton = new() { Text = "파일 추가", AutoSize = true };
    private readonly Button _addFolderButton = new() { Text = "폴더 추가", AutoSize = true };
    private readonly Button _removeSourcesButton = new() { Text = "선택 제거", AutoSize = true };
    private readonly Button _createPackageButton = new() { Text = "ZIP 생성", AutoSize = true };
    private readonly Button _loadBaseZipButton = new() { Text = "Base Builder ZIP 선택", AutoSize = true };
    private readonly Button _saveManifestButton = new() { Text = "manifest.json 저장", AutoSize = true, Enabled = false };

    private readonly List<SourceSelection> _sources = [];
    private PackageBuildResult? _packageBuild;
    private bool _isImportedBasePackage;
    private bool _isBusy;

    public MainForm()
    {
        Text = "ProjectS 외부 에셋 배포자";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 700);
        AutoScaleMode = AutoScaleMode.Font;

        _packageTypeComboBox.Items.AddRange(["base", "patch"]);
        _packageTypeComboBox.SelectedItem = "patch";
        _packageNameTextBox.Text = "Patch_v2.zip";

        _sourceList.Columns.Add("종류", 80);
        _sourceList.Columns.Add("원본 위치 (ExternalAssets 기준)", 460);
        _sourceList.Columns.Add("ZIP 내부 기록", 360);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 3,
            RowCount = 12,
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

        AddRow(layout, 0, "프로젝트 폴더", _projectPathTextBox, _selectProjectButton);
        AddRow(layout, 1, "외부 에셋 루트", _externalRootLabel, null);
        AddRow(layout, 2, "ZIP 저장 폴더", _outputPathTextBox, _selectOutputButton);
        AddRow(layout, 3, "manifest.json 저장 위치", _manifestPathTextBox, _selectManifestButton);
        AddRow(layout, 4, "Base Builder ZIP 등록", _baseZipPathTextBox, _loadBaseZipButton);

        var packageOptions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        packageOptions.Controls.Add(new Label { Text = "버전", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
        packageOptions.Controls.Add(_versionInput);
        packageOptions.Controls.Add(new Label { Text = "종류", AutoSize = true, Padding = new Padding(12, 7, 0, 0) });
        packageOptions.Controls.Add(_packageTypeComboBox);
        packageOptions.Controls.Add(new Label { Text = "ZIP 이름", AutoSize = true, Padding = new Padding(12, 7, 0, 0) });
        packageOptions.Controls.Add(_packageNameTextBox);
        _packageNameTextBox.Width = 260;
        AddRow(layout, 5, "패치 정보", packageOptions, null);

        layout.Controls.Add(new Label { Text = "압축할 원본", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 6);
        layout.Controls.Add(_sourceList, 1, 6);
        layout.SetColumnSpan(_sourceList, 2);

        var sourceActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        sourceActions.Controls.Add(_addFilesButton);
        sourceActions.Controls.Add(_addFolderButton);
        sourceActions.Controls.Add(_removeSourcesButton);
        layout.Controls.Add(sourceActions, 1, 7);
        layout.SetColumnSpan(sourceActions, 2);

        AddRow(layout, 8, "삭제할 상대 경로", _removedPathsTextBox, null);
        AddRow(layout, 9, "ZIP Drive 파일 링크 / ID", _driveFileIdTextBox, null);

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        actions.Controls.Add(_createPackageButton);
        actions.Controls.Add(_saveManifestButton);
        layout.Controls.Add(actions, 1, 10);
        layout.SetColumnSpan(actions, 2);

        var help = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(900, 0),
            Text = "일반 ZIP 생성은 Patch v2부터입니다. 최초 Base v1은 Base Builder ZIP 선택으로 다시 압축하지 않고 검사·등록합니다. 둘 다 제한된 Drive에 올린 뒤 파일 링크 또는 ID를 붙여넣고 manifest.json을 저장하세요.",
        };
        var footer = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
        };
        footer.Controls.Add(_statusLabel);
        footer.Controls.Add(_resultLabel);
        footer.Controls.Add(help);
        layout.Controls.Add(footer, 1, 11);
        layout.SetColumnSpan(footer, 2);

        Controls.Add(layout);

        _versionInput.ValueChanged += (_, _) => UpdatePackageDefaults();
        _packageTypeComboBox.SelectedIndexChanged += (_, _) => UpdatePackageDefaults();
        _selectProjectButton.Click += (_, _) => SelectProjectFolder();
        _selectOutputButton.Click += (_, _) => SelectOutputFolder();
        _selectManifestButton.Click += (_, _) => SelectManifestPath();
        _addFilesButton.Click += (_, _) => AddFiles();
        _addFolderButton.Click += (_, _) => AddFolder();
        _removeSourcesButton.Click += (_, _) => RemoveSelectedSources();
        _createPackageButton.Click += async (_, _) => await CreatePackageAsync();
        _loadBaseZipButton.Click += async (_, _) => await SelectExistingBaseZipAsync();
        _saveManifestButton.Click += async (_, _) => await SaveManifestAsync();
        Load += (_, _) => Initialize();
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

    private void Initialize()
    {
        var projectPath = PublisherServices.FindProjectRoot(AppContext.BaseDirectory) ?? string.Empty;
        _projectPathTextBox.Text = projectPath;
        if (PublisherServices.IsUnityProject(projectPath))
        {
            _outputPathTextBox.Text = Path.Combine(projectPath, "ExternalAssetsReleases");
            _manifestPathTextBox.Text = Path.Combine(projectPath, "ExternalAssetsReleases", "manifest.json");
        }

        RefreshProjectLabels();
        SetStatus("파일 또는 폴더를 추가한 뒤 ZIP을 생성하세요.");
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

    private async Task CreatePackageAsync()
    {
        try
        {
            if (_isImportedBasePackage)
            {
                throw new InvalidOperationException("현재는 Base Builder ZIP 등록 모드입니다. 패치를 만들려면 파일 또는 폴더를 추가해 새 패키지를 준비하세요.");
            }

            var projectPath = GetProjectPathOrThrow();
            var sources = _sources.ToArray();
            var outputPath = _outputPathTextBox.Text.Trim();
            var packageName = _packageNameTextBox.Text;
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
                ParseRemovedPaths());
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

    private void AddSource(SourceSelection selection)
    {
        if (_isImportedBasePackage)
        {
            ResetPackageBuild();
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
                var item = new ListViewItem(source.IsFolder ? "폴더" : "파일") { Tag = source };
                item.SubItems.Add(relativePath);
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
    }

    private void SetStatus(string status)
    {
        _statusLabel.Text = status;
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
