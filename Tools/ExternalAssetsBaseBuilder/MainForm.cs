using System.Diagnostics;
using System.Text.Json;
using ProjectS.ExternalAssetsDelta;

namespace ProjectS.ExternalAssetsBaseBuilder;

internal sealed class MainForm : Form
{
    private readonly TextBox _baselinePathTextBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _seedIndexPathTextBox = new() { Dock = DockStyle.Fill };
    private readonly ListBox _contributionZipListBox = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true };
    private readonly ListBox _additionalSourcesListBox = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true };
    private readonly TextBox _outputPathTextBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _zipNameTextBox = new() { Dock = DockStyle.Fill, Text = "Base_v1.zip" };
    private readonly Label _summaryLabel = new() { AutoSize = true, MaximumSize = new Size(1040, 0) };
    private readonly Label _conflictDetailLabel = new() { AutoSize = true, MaximumSize = new Size(1040, 0) };
    private readonly Label _statusLabel = new() { AutoSize = true, MaximumSize = new Size(1040, 0) };
    private readonly ProgressBar _progressBar = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100 };
    private readonly ListView _conflictList = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
        HideSelection = false,
    };
    private readonly ComboBox _candidateSourceComboBox = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 310,
    };
    private readonly Button _analyzeButton = new() { Text = "병합 계획 분석", AutoSize = true };
    private readonly Button _selectBaselineButton;
    private readonly Button _selectSeedIndexButton;
    private readonly Button _createSeedIndexButton = new() { Text = "기준 인덱스 생성", AutoSize = true };
    private readonly Button _selectOutputButton;
    private readonly Button _addContributionButton = new() { Text = "Contribution ZIP 추가", AutoSize = true };
    private readonly Button _removeContributionButton = new() { Text = "선택 Contribution 제거", AutoSize = true };
    private readonly Button _addSourceButton = new() { Text = "추가 원본 선택", AutoSize = true };
    private readonly Button _removeSourceButton = new() { Text = "선택 원본 제거", AutoSize = true };
    private readonly Button _applySelectionButton = new() { Text = "선택 원본 사용", AutoSize = true, Enabled = false };
    private readonly Button _keepBaselineButton = new() { Text = "미해결 충돌: 기준 원본 유지", AutoSize = true, Enabled = false };
    private readonly Button _validateButton = new() { Text = "병합 결과 검증", AutoSize = true, Enabled = false };
    private readonly Button _createZipButton = new() { Text = "검증된 Base ZIP 생성", AutoSize = true, Enabled = false };
    private readonly Button _openOutputButton = new() { Text = "출력 폴더 열기", AutoSize = true };

    private readonly List<string> _additionalSourcePaths = [];
    private readonly List<string> _contributionZipPaths = [];
    private readonly List<StagedContributionSource> _stagedContributionSources = [];
    private readonly Dictionary<string, string> _conflictSelections = new(StringComparer.Ordinal);
    private BaseMergePlan? _plan;
    private bool _isBusy;
    private bool _planValidated;
    private bool _invalidateAfterBusy;

    public MainForm()
    {
        Text = "ProjectS External Assets Base Builder";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1100, 900);
        AutoScaleMode = AutoScaleMode.Font;

        _conflictList.Columns.Add("상대 경로", 340);
        _conflictList.Columns.Add("종류", 145);
        _conflictList.Columns.Add("후보 원본", 210);
        _conflictList.Columns.Add("선택", 210);

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
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _selectBaselineButton = CreateButton("찾기", SelectBaseline);
        _selectSeedIndexButton = CreateButton("찾기", SelectSeedIndex);
        _selectOutputButton = CreateButton("찾기", SelectOutputFolder);
        AddRow(layout, 0, "기준 ExternalAssets", _baselinePathTextBox, _selectBaselineButton);

        var seedActions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        seedActions.Controls.AddRange([_selectSeedIndexButton, _createSeedIndexButton]);
        AddRow(layout, 1, "기준 인덱스", _seedIndexPathTextBox, seedActions);

        layout.Controls.Add(new Label { Text = "Contribution ZIP", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        layout.Controls.Add(_contributionZipListBox, 1, 2);
        var contributionActions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        contributionActions.Controls.AddRange([_addContributionButton, _removeContributionButton]);
        layout.Controls.Add(contributionActions, 2, 2);

        layout.Controls.Add(new Label { Text = "추가 ExternalAssets", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        layout.Controls.Add(_additionalSourcesListBox, 1, 3);
        var sourceActions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        sourceActions.Controls.AddRange([_addSourceButton, _removeSourceButton]);
        layout.Controls.Add(sourceActions, 2, 3);

        AddRow(layout, 4, "출력 폴더", _outputPathTextBox, _selectOutputButton);
        AddRow(layout, 5, "Base ZIP 이름", _zipNameTextBox, null);

        var analysisActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        analysisActions.Controls.AddRange([_analyzeButton, _validateButton, _createZipButton, _openOutputButton]);
        layout.Controls.Add(analysisActions, 1, 6);
        layout.SetColumnSpan(analysisActions, 2);

        layout.Controls.Add(new Label { Text = "병합 요약", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 7);
        var conflictArea = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        conflictArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        conflictArea.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        conflictArea.Controls.Add(_conflictList, 0, 0);
        conflictArea.Controls.Add(_summaryLabel, 0, 1);
        layout.Controls.Add(conflictArea, 1, 7);
        layout.SetColumnSpan(conflictArea, 2);

        layout.Controls.Add(new Label { Text = "충돌 선택", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 8);
        var resolutionActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        resolutionActions.Controls.AddRange([_candidateSourceComboBox, _applySelectionButton, _keepBaselineButton]);
        layout.Controls.Add(resolutionActions, 1, 8);
        layout.SetColumnSpan(resolutionActions, 2);

        layout.Controls.Add(new Label { Text = "충돌 상세", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 9);
        layout.Controls.Add(_conflictDetailLabel, 1, 9);
        layout.SetColumnSpan(_conflictDetailLabel, 2);

        AddRow(layout, 10, "상태", _statusLabel, null);
        AddRow(layout, 11, "진행률", _progressBar, null);

        Controls.Add(layout);

        _addSourceButton.Click += (_, _) => AddAdditionalSource();
        _removeSourceButton.Click += (_, _) => RemoveSelectedSource();
        _addContributionButton.Click += (_, _) => AddContributionZip();
        _removeContributionButton.Click += (_, _) => RemoveSelectedContributionZip();
        _createSeedIndexButton.Click += async (_, _) => await CreateSeedIndexAsync();
        _analyzeButton.Click += async (_, _) => await AnalyzeAsync();
        _validateButton.Click += (_, _) => ValidatePlan();
        _createZipButton.Click += async (_, _) => await CreateZipAsync();
        _openOutputButton.Click += (_, _) => OpenOutputDirectory();
        _conflictList.SelectedIndexChanged += (_, _) => RefreshConflictSelectionControls();
        _applySelectionButton.Click += (_, _) => ApplySelectedConflictChoice();
        _keepBaselineButton.Click += (_, _) => KeepBaselineForUnresolvedConflicts();
        _baselinePathTextBox.TextChanged += (_, _) => InvalidatePlan();
        _seedIndexPathTextBox.TextChanged += (_, _) => InvalidateForSeedIndexChange();
        _zipNameTextBox.TextChanged += (_, _) => _createZipButton.Enabled = false;
        Load += (_, _) => Initialize();
        FormClosing += OnFormClosing;
        FormClosed += (_, _) => ClearContributionStaging();
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

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (!_isBusy)
        {
            return;
        }

        eventArgs.Cancel = true;
        MessageBox.Show(
            this,
            "현재 분석 또는 ZIP 생성이 진행 중입니다. 완료된 뒤 창을 닫아 주세요.",
            "작업 진행 중",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void Initialize()
    {
        CleanupStaleContributionStaging();
        var projectRoot = BaseBuilderServices.FindProjectRoot(AppContext.BaseDirectory);
        if (projectRoot is not null)
        {
            var externalAssetsPath = Path.Combine(projectRoot, "Assets", "ExternalAssets");
            if (Directory.Exists(externalAssetsPath))
            {
                _baselinePathTextBox.Text = externalAssetsPath;
            }

            _outputPathTextBox.Text = Path.Combine(projectRoot, "ExternalAssetsBaseBuilds");
        }

        SetStatus("기준 원본과 추가 원본을 선택한 뒤 병합 계획을 분석하세요. 입력 원본은 수정하지 않습니다.");
    }

    private void SelectSeedIndex()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "기준 인덱스 JSON|*.json|모든 파일|*.*",
            Title = "Contribution ZIP이 기준으로 삼은 seed-index.json을 선택하세요.",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (File.Exists(_seedIndexPathTextBox.Text))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(_seedIndexPathTextBox.Text);
            dialog.FileName = Path.GetFileName(_seedIndexPathTextBox.Text);
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _seedIndexPathTextBox.Text = dialog.FileName;
        }
    }

    private async Task CreateSeedIndexAsync()
    {
        try
        {
            var baselinePath = BaseBuilderServices.NormalizeExternalAssetsPath(_baselinePathTextBox.Text);
            using var dialog = new SaveFileDialog
            {
                Filter = "기준 인덱스 JSON|*.json",
                Title = "팀원에게 배포할 seed-index.json 저장 위치를 선택하세요.",
                FileName = "seed-index.json",
                AddExtension = true,
                DefaultExt = "json",
                OverwritePrompt = true,
            };
            if (Directory.Exists(_outputPathTextBox.Text))
            {
                dialog.InitialDirectory = _outputPathTextBox.Text;
            }

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            if (IsPathInside(dialog.FileName, baselinePath))
            {
                throw new InvalidOperationException("기준 인덱스는 입력 Assets/ExternalAssets 폴더 내부에 저장할 수 없습니다.");
            }

            SetBusy(true);
            var progress = new Progress<DeltaProgress>(UpdateProgress);
            SetStatus("기준 ExternalAssets의 경로, SHA-256, .meta GUID를 읽어 seed-index.json을 만드는 중입니다.");
            var seed = await ExternalAssetsDeltaServices.GenerateSeedIndexAsync(
                baselinePath,
                Path.GetFileNameWithoutExtension(dialog.FileName),
                progress,
                CancellationToken.None);
            await WriteSeedIndexAsync(dialog.FileName, seed, CancellationToken.None);
            _seedIndexPathTextBox.Text = dialog.FileName;
            _progressBar.Value = 100;
            SetStatus($"기준 인덱스 생성 완료: {dialog.FileName}{Environment.NewLine}baselineId: {seed.BaselineId}{Environment.NewLine}이 JSON을 제한된 Drive에 올린 뒤 팀원 Contributor 도구에 전달하세요.");
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

    private static async Task WriteSeedIndexAsync(string destinationPath, SeedIndex seed, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("기준 인덱스 출력 폴더를 확인할 수 없습니다.");
        Directory.CreateDirectory(directory);
        var temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    seed,
                    new JsonSerializerOptions { WriteIndented = true },
                    cancellationToken);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private void AddContributionZip()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Contribution ZIP|*.zip|모든 파일|*.*",
            Title = "팀원이 제출한 Contribution ZIP을 선택하세요.",
            CheckFileExists = true,
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var addedCount = 0;
        foreach (var selectedPath in dialog.FileNames)
        {
            var fullPath = Path.GetFullPath(selectedPath);
            if (_contributionZipPaths.Any(path => PathsEqual(path, fullPath)))
            {
                continue;
            }

            _contributionZipPaths.Add(fullPath);
            addedCount++;
        }

        if (addedCount == 0)
        {
            ShowError("새로 추가된 Contribution ZIP이 없습니다.");
            return;
        }

        RefreshContributionZips();
        InvalidatePlan();
    }

    private void RemoveSelectedContributionZip()
    {
        if (_contributionZipListBox.SelectedItem is not string selectedPath)
        {
            return;
        }

        _contributionZipPaths.Remove(selectedPath);
        RefreshContributionZips();
        InvalidatePlan();
    }

    private void SelectBaseline()
    {
        var selectedPath = SelectExternalAssetsFolder("기준이 될 Unity 프로젝트 또는 Assets/ExternalAssets 폴더를 선택하세요.");
        if (selectedPath is null)
        {
            return;
        }

        _baselinePathTextBox.Text = selectedPath;
    }

    private void AddAdditionalSource()
    {
        var selectedPath = SelectExternalAssetsFolder("병합할 추가 Unity 프로젝트 또는 Assets/ExternalAssets 폴더를 선택하세요.");
        if (selectedPath is null)
        {
            return;
        }

        if (_additionalSourcePaths.Any(path => PathsEqual(path, selectedPath))
            || (!string.IsNullOrWhiteSpace(_baselinePathTextBox.Text) && PathsEqual(_baselinePathTextBox.Text, selectedPath)))
        {
            ShowError("이미 선택된 원본입니다.");
            return;
        }

        _additionalSourcePaths.Add(selectedPath);
        RefreshAdditionalSources();
        InvalidatePlan();
    }

    private void RemoveSelectedSource()
    {
        if (_additionalSourcesListBox.SelectedItem is not string selectedPath)
        {
            return;
        }

        _additionalSourcePaths.Remove(selectedPath);
        RefreshAdditionalSources();
        InvalidatePlan();
    }

    private string? SelectExternalAssetsFolder(string description)
    {
        using var dialog = new FolderBrowserDialog { Description = description };
        if (Directory.Exists(_baselinePathTextBox.Text))
        {
            dialog.InitialDirectory = _baselinePathTextBox.Text;
        }

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return null;
        }

        try
        {
            return BaseBuilderServices.NormalizeExternalAssetsPath(dialog.SelectedPath);
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
            return null;
        }
    }

    private void SelectOutputFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "Base_v1.zip과 병합 보고서를 저장할 폴더를 선택하세요." };
        if (Directory.Exists(_outputPathTextBox.Text))
        {
            dialog.InitialDirectory = _outputPathTextBox.Text;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _outputPathTextBox.Text = dialog.SelectedPath;
        }
    }

    private async Task AnalyzeAsync()
    {
        try
        {
            ClearContributionStaging();
            _plan = null;
            _conflictSelections.Clear();
            _planValidated = false;
            SetBusy(true);
            SetStatus("원본 파일, .meta, GUID와 상대 경로를 읽는 중입니다. 원본 용량에 따라 시간이 걸릴 수 있습니다.");
            var baselinePath = BaseBuilderServices.NormalizeExternalAssetsPath(_baselinePathTextBox.Text);
            var additionalSources = await PrepareAdditionalSourcesAsync(baselinePath);
            _plan = await Task.Run(() => BaseBuilderServices.Analyze(baselinePath, additionalSources));
            _conflictSelections.Clear();
            _planValidated = false;
            RefreshConflictList();

            var issueCount = _plan.SourceValidationIssues.Count;
            SetStatus($"분석 완료: 기준 파일 {_plan.BaselineFileCount:N0}개, 추가 후보 {_plan.UniqueAdditionFileCount:N0}개, 충돌 {_plan.Conflicts.Count:N0}개, 원본 검증 오류 {issueCount:N0}개, Contribution {_stagedContributionSources.Count:N0}개.");
        }
        catch (Exception exception)
        {
            ClearContributionStaging();
            _plan = null;
            _conflictSelections.Clear();
            _planValidated = false;
            RefreshConflictList();
            ShowError(exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<IReadOnlyList<AdditionalExternalAssetsInput>> PrepareAdditionalSourcesAsync(string baselinePath)
    {
        var sources = _additionalSourcePaths
            .Select((path, index) => new AdditionalExternalAssetsInput(
                $"추가 원본 {index + 1}",
                path,
                ExternalAssetsSourceKind.FullExternalAssets))
            .ToList();
        if (_contributionZipPaths.Count == 0)
        {
            return sources;
        }

        var seedIndexPath = _seedIndexPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(seedIndexPath))
        {
            throw new InvalidOperationException("Contribution ZIP을 병합하려면 해당 ZIP이 기준으로 삼은 seed-index.json을 선택하세요.");
        }

        SetStatus("기준 인덱스와 실제 기준 ExternalAssets가 일치하는지 검사하는 중입니다.");
        var progress = new Progress<DeltaProgress>(UpdateProgress);
        var seed = await ExternalAssetsDeltaServices.LoadSeedIndexAsync(seedIndexPath, CancellationToken.None);
        var baselineComparison = await ExternalAssetsDeltaServices.CompareLocalExternalAssetsAsync(
            baselinePath,
            seed,
            progress,
            CancellationToken.None);
        if (!baselineComparison.IsExactMatch)
        {
            var firstChanged = baselineComparison.Entries.FirstOrDefault(entry => entry.ChangeKind != DeltaComparisonKind.Unchanged);
            var firstIssue = baselineComparison.Issues.FirstOrDefault(issue => issue.Severity == DeltaIssueSeverity.Error);
            var detail = firstIssue?.Message
                ?? (firstChanged is null
                    ? "기준 인덱스 검증에 실패했습니다."
                    : $"{firstChanged.RelativePath}: {GetBaselineMismatchDescription(firstChanged.ChangeKind)}");
            throw new InvalidOperationException(
                $"현재 기준 ExternalAssets가 seed-index.json과 일치하지 않습니다. Contribution을 병합할 수 없습니다. {detail}");
        }

        var contributionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var archiveHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // The full 20GB baseline scan above is intentionally performed once.
        // Per-package checks below compare every declared baseline precondition
        // to that verified seed, so adding several Contribution ZIPs does not
        // re-hash the entire baseline for each ZIP.
        foreach (var zipPath in _contributionZipPaths)
        {
            SetStatus($"Contribution ZIP을 검사하고 안전한 임시 영역에 푸는 중입니다: {Path.GetFileName(zipPath)}");
            var loadedPackage = await ExternalAssetsDeltaServices.LoadContributionPackageAsync(zipPath, CancellationToken.None);
            if (string.IsNullOrWhiteSpace(loadedPackage.Manifest.ContributionId)
                || !contributionIds.Add(loadedPackage.Manifest.ContributionId))
            {
                throw new InvalidOperationException(
                    $"{Path.GetFileName(zipPath)}: 같은 contributionId를 가진 Contribution ZIP이 이미 선택되었습니다.");
            }

            if (!archiveHashes.Add(loadedPackage.ArchiveSha256))
            {
                throw new InvalidOperationException(
                    $"{Path.GetFileName(zipPath)}: 같은 ZIP 바이트가 이미 선택되었습니다.");
            }

            var manifestValidation = BaseBuilderServices.ValidateContributionManifestAgainstBaseline(seed, loadedPackage.Manifest);
            if (!manifestValidation.IsValid)
            {
                throw new InvalidOperationException(
                    $"{Path.GetFileName(zipPath)}: {manifestValidation.Errors[0]}");
            }

            var stagingRoot = CreateContributionStagingRoot();
            try
            {
                var stagedPackage = await CopyContributionZipToStagingAsync(
                    zipPath,
                    loadedPackage,
                    stagingRoot,
                    CancellationToken.None);
                var destinationPath = Path.Combine(stagingRoot, "ExternalAssets");
                ContributionExtractionResult extraction;
                LoadedContributionPackage package;
                await using (var archiveLock = new FileStream(
                                 stagedPackage.ZipPath,
                                 FileMode.Open,
                                 FileAccess.Read,
                                 FileShare.Read,
                                 bufferSize: 1,
                                 FileOptions.SequentialScan))
                {
                    package = await ExternalAssetsDeltaServices.LoadContributionPackageAsync(
                        stagedPackage.ZipPath,
                        CancellationToken.None);
                    if (!string.Equals(package.ArchiveSha256, loadedPackage.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"Contribution ZIP이 staging 뒤 변경되었습니다: {Path.GetFileName(zipPath)}");
                    }

                    extraction = await ExternalAssetsDeltaServices.ExtractContributionPayloadAsync(
                        package,
                        destinationPath,
                        CancellationToken.None);
                }
                var requiredDirectoryMetaPaths = GetRequiredDirectoryMetaPaths(package);
                var expectedPayloadSha256 = GetExpectedPayloadSha256(package);
                var displayName = $"Contribution: {package.Manifest.ContributorName} ({Path.GetFileName(zipPath)})";
                _stagedContributionSources.Add(new StagedContributionSource(
                    displayName,
                    zipPath,
                    stagingRoot,
                    extraction.DestinationExternalAssetsPath,
                    requiredDirectoryMetaPaths,
                    expectedPayloadSha256,
                    package.Manifest.ContributionId));
                sources.Add(new AdditionalExternalAssetsInput(
                    displayName,
                    extraction.DestinationExternalAssetsPath,
                    ExternalAssetsSourceKind.ContributionPartial,
                    requiredDirectoryMetaPaths,
                    expectedPayloadSha256));
            }
            catch
            {
                TryDeleteManagedStagingRoot(stagingRoot);
                throw;
            }
        }

        return sources;
    }

    private static IReadOnlySet<string> GetRequiredDirectoryMetaPaths(LoadedContributionPackage package)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in package.Manifest.Entries.Where(entry => entry.MetaTargetKind == ContributionMetaTargetKind.Folder))
        {
            if (!entry.RelativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"{entry.RelativePath}: 폴더 .meta 항목의 경로가 올바르지 않습니다.");
            }

            paths.Add(entry.RelativePath[..^".meta".Length]);
        }

        return paths;
    }

    private static IReadOnlyDictionary<string, string> GetExpectedPayloadSha256(LoadedContributionPackage package)
    {
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in package.Manifest.Entries)
        {
            if (!hashes.TryAdd(entry.RelativePath, entry.Sha256))
            {
                throw new InvalidOperationException($"{entry.RelativePath}: Contribution ZIP의 payload 경로가 중복됩니다.");
            }
        }

        return hashes;
    }

    private static async Task<LoadedContributionPackage> CopyContributionZipToStagingAsync(
        string inputZipPath,
        LoadedContributionPackage loadedPackage,
        string stagingRoot,
        CancellationToken cancellationToken)
    {
        var stagedZipPath = Path.Combine(stagingRoot, "contribution.zip");
        await using (var input = new FileStream(
                         inputZipPath,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.Read,
                         bufferSize: 128 * 1024,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var output = new FileStream(
                         stagedZipPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 128 * 1024,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await input.CopyToAsync(output, 128 * 1024, cancellationToken);
        }

        var stagedPackage = await ExternalAssetsDeltaServices.LoadContributionPackageAsync(
            stagedZipPath,
            cancellationToken);
        if (!string.Equals(stagedPackage.ArchiveSha256, loadedPackage.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Contribution ZIP이 검사 뒤 변경되었습니다: {Path.GetFileName(inputZipPath)}");
        }

        return stagedPackage;
    }

    private static string GetBaselineMismatchDescription(DeltaComparisonKind kind) => kind switch
    {
        DeltaComparisonKind.Added => "현재 기준에 인덱스에 없던 파일이 있습니다.",
        DeltaComparisonKind.Modified => "현재 기준의 파일 또는 .meta 해시가 인덱스와 다릅니다.",
        DeltaComparisonKind.Missing => "인덱스에 있던 파일 또는 폴더가 현재 기준에 없습니다.",
        _ => "기준 인덱스와 다릅니다.",
    };

    private void RefreshAdditionalSources()
    {
        _additionalSourcesListBox.BeginUpdate();
        _additionalSourcesListBox.Items.Clear();
        _additionalSourcesListBox.Items.AddRange(_additionalSourcePaths.Cast<object>().ToArray());
        _additionalSourcesListBox.EndUpdate();
    }

    private void RefreshContributionZips()
    {
        _contributionZipListBox.BeginUpdate();
        _contributionZipListBox.Items.Clear();
        _contributionZipListBox.Items.AddRange(_contributionZipPaths.Cast<object>().ToArray());
        _contributionZipListBox.EndUpdate();
    }

    private static string CreateContributionStagingRoot()
    {
        var stagingParent = GetContributionStagingParentPath();
        Directory.CreateDirectory(stagingParent);
        var stagingRoot = Path.Combine(stagingParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);
        return stagingRoot;
    }

    private void ClearContributionStaging()
    {
        foreach (var stagingRoot in _stagedContributionSources
                     .Select(source => source.StagingRootPath)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .ToArray())
        {
            TryDeleteManagedStagingRoot(stagingRoot);
        }

        _stagedContributionSources.Clear();
    }

    private static void TryDeleteManagedStagingRoot(string stagingRoot)
    {
        try
        {
            var stagingParent = Path.TrimEndingDirectorySeparator(GetContributionStagingParentPath());
            var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stagingRoot));
            var relativePath = Path.GetRelativePath(stagingParent, fullRoot);
            if (Path.IsPathRooted(relativePath)
                || relativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Length != 1
                || !Guid.TryParseExact(relativePath, "N", out _))
            {
                return;
            }

            if (Directory.Exists(fullRoot))
            {
                if (ContainsReparsePoint(fullRoot))
                {
                    Debug.WriteLine($"Contribution staging cleanup skipped due to a reparse point: {fullRoot}");
                    return;
                }

                Directory.Delete(fullRoot, recursive: true);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Contribution staging cleanup failed: {exception}");
        }
    }

    private static string GetContributionStagingParentPath() => Path.Combine(
        Path.GetTempPath(),
        "ProjectSExternalAssetsBaseBuilder",
        "contribution-staging");

    private static void CleanupStaleContributionStaging()
    {
        try
        {
            var stagingParent = GetContributionStagingParentPath();
            if (!Directory.Exists(stagingParent)
                || (new DirectoryInfo(stagingParent).Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return;
            }

            var cutoffUtc = DateTime.UtcNow.AddDays(-1);
            foreach (var directory in new DirectoryInfo(stagingParent).EnumerateDirectories())
            {
                if (!Guid.TryParseExact(directory.Name, "N", out _)
                    || directory.LastWriteTimeUtc >= cutoffUtc)
                {
                    continue;
                }

                TryDeleteManagedStagingRoot(directory.FullName);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Stale contribution staging cleanup failed: {exception}");
        }
    }

    private static bool ContainsReparsePoint(string rootPath)
    {
        var pending = new Stack<string>();
        pending.Push(rootPath);
        while (pending.Count > 0)
        {
            var currentPath = pending.Pop();
            var currentDirectory = new DirectoryInfo(currentPath);
            if ((currentDirectory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }

            foreach (var file in currentDirectory.EnumerateFiles())
            {
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }
            }

            foreach (var directory in currentDirectory.EnumerateDirectories())
            {
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }

                pending.Push(directory.FullName);
            }
        }

        return false;
    }

    private void InvalidatePlan()
    {
        if (_isBusy)
        {
            return;
        }

        ClearContributionStaging();
        if (_plan is null)
        {
            return;
        }

        _plan = null;
        _conflictSelections.Clear();
        _planValidated = false;
        RefreshConflictList();
        SetStatus("입력 원본 또는 ZIP 이름이 변경되었습니다. 병합 계획을 다시 분석하세요.");
    }

    private void InvalidateForSeedIndexChange()
    {
        if (_isBusy)
        {
            _invalidateAfterBusy = true;
            return;
        }

        InvalidatePlan();
    }

    private void RefreshConflictList()
    {
        _conflictList.BeginUpdate();
        _conflictList.Items.Clear();
        if (_plan is not null)
        {
            foreach (var conflict in _plan.Conflicts.OrderBy(conflict => conflict.LogicalPath, StringComparer.OrdinalIgnoreCase))
            {
                var candidateNames = GetSelectableSources(conflict)
                    .Select(source => source.DisplayName)
                    .ToArray();
                var selectedName = _conflictSelections.TryGetValue(conflict.Id, out var selectedSourceId)
                    ? GetSource(selectedSourceId)?.DisplayName ?? selectedSourceId
                    : "미해결";
                var item = new ListViewItem(conflict.LogicalPath)
                {
                    Tag = conflict,
                };
                item.SubItems.Add(GetConflictKindText(conflict.Kind));
                item.SubItems.Add(string.Join(", ", candidateNames));
                item.SubItems.Add(selectedName);
                _conflictList.Items.Add(item);
            }

            _summaryLabel.Text = $"기준 파일: {_plan.BaselineFileCount:N0} | 중복 없이 추가될 파일: {_plan.UniqueAdditionFileCount:N0} | 충돌: {_plan.Conflicts.Count:N0} | 원본 검증 오류: {_plan.SourceValidationIssues.Count:N0}";
        }
        else
        {
            _summaryLabel.Text = "분석 전";
        }

        _conflictList.EndUpdate();
        RefreshConflictSelectionControls();
    }

    private void RefreshConflictSelectionControls()
    {
        _candidateSourceComboBox.Items.Clear();
        _conflictDetailLabel.Text = string.Empty;
        if (_plan is null || _conflictList.SelectedItems.Count == 0 || _conflictList.SelectedItems[0].Tag is not MergeConflict conflict)
        {
            _applySelectionButton.Enabled = false;
            _keepBaselineButton.Enabled = !_isBusy && _plan is not null && _plan.Conflicts.Count > 0;
            return;
        }

        var selectableSources = GetSelectableSources(conflict).ToArray();
        _candidateSourceComboBox.Items.AddRange(selectableSources.Cast<object>().ToArray());
        if (_conflictSelections.TryGetValue(conflict.Id, out var selectedSourceId))
        {
            _candidateSourceComboBox.SelectedItem = selectableSources.FirstOrDefault(source => source.Id == selectedSourceId);
        }

        if (_candidateSourceComboBox.SelectedIndex < 0 && _candidateSourceComboBox.Items.Count > 0)
        {
            _candidateSourceComboBox.SelectedIndex = 0;
        }

        _applySelectionButton.Enabled = !_isBusy && _candidateSourceComboBox.SelectedItem is SourceChoice;
        _keepBaselineButton.Enabled = !_isBusy && _plan.Conflicts.Count > 0;
        var details = conflict.Candidates
            .OrderBy(candidate => candidate.SourceId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => $"{GetSource(candidate.SourceId)?.DisplayName ?? candidate.SourceId}: {candidate.FullPath}");
        _conflictDetailLabel.Text = string.Join(Environment.NewLine, details);
    }

    private void ApplySelectedConflictChoice()
    {
        if (_conflictList.SelectedItems.Count == 0
            || _conflictList.SelectedItems[0].Tag is not MergeConflict conflict
            || _candidateSourceComboBox.SelectedItem is not SourceChoice source)
        {
            return;
        }

        _conflictSelections[conflict.Id] = source.Id;
        _planValidated = false;
        RefreshConflictList();
    }

    private void KeepBaselineForUnresolvedConflicts()
    {
        if (_plan is null)
        {
            return;
        }

        var baseline = _plan.Sources.Single(source => source.IsBaseline);
        var conflicts = _plan.Conflicts
            .Where(conflict => !_conflictSelections.ContainsKey(conflict.Id))
            .Where(conflict => GetSelectableSources(conflict).Any(source => source.Id == baseline.Id))
            .ToArray();
        if (conflicts.Length == 0)
        {
            SetStatus("기준 원본으로 선택할 수 있는 미해결 충돌이 없습니다.");
            return;
        }

        var result = MessageBox.Show(
            this,
            $"미해결 충돌 {conflicts.Length:N0}개에서 기준 원본을 명시적으로 선택합니다. 추가 원본의 같은 경로 파일과 .meta는 Base에 넣지 않습니다. 계속할까요?",
            "기준 원본 유지 확인",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (result != DialogResult.Yes)
        {
            return;
        }

        foreach (var conflict in conflicts)
        {
            _conflictSelections[conflict.Id] = baseline.Id;
        }

        _planValidated = false;
        RefreshConflictList();
    }

    private void ValidatePlan()
    {
        if (_plan is null)
        {
            return;
        }

        var validation = BaseBuilderServices.ValidatePlan(_plan, _conflictSelections);
        if (validation.IsValid)
        {
            _planValidated = true;
            _createZipButton.Enabled = !_isBusy;
            SetStatus("검증 통과: 모든 충돌 선택, .meta, 폴더 .meta, Unity GUID 검사가 완료되었습니다. Base ZIP을 생성할 수 있습니다.");
            return;
        }

        _createZipButton.Enabled = false;
        _planValidated = false;
        var firstGuidCollision = validation.GuidCollisions.FirstOrDefault();
        var firstProblem = validation.Errors.FirstOrDefault()
            ?? (firstGuidCollision is null
                ? "알 수 없는 검증 오류"
                : $"GUID {firstGuidCollision.Guid}가 {string.Join(", ", firstGuidCollision.RelativeMetaPaths)}에서 중복됩니다.");
        SetStatus($"검증 실패: {firstProblem} (오류 {validation.Errors.Count:N0}, GUID 중복 {validation.GuidCollisions.Count:N0})");
    }

    private async Task CreateZipAsync()
    {
        if (_plan is null)
        {
            return;
        }

        var validation = BaseBuilderServices.ValidatePlan(_plan, _conflictSelections);
        _planValidated = validation.IsValid;
        if (!validation.IsValid)
        {
            ValidatePlan();
            return;
        }

        try
        {
            var plan = _plan;
            var conflictSelections = new Dictionary<string, string>(_conflictSelections, StringComparer.Ordinal);
            var outputPath = _outputPathTextBox.Text.Trim();
            var zipName = _zipNameTextBox.Text;
            SetBusy(true);
            var progress = new Progress<BuildProgress>(UpdateProgress);
            var result = await Task.Run(async () => await BaseBuilderServices.CreateBasePackageAsync(
                plan,
                conflictSelections,
                outputPath,
                zipName,
                progress,
                CancellationToken.None));
            _progressBar.Value = 100;
            SetStatus($"Base 생성 완료: {result.ZipPath}{Environment.NewLine}파일 {result.FileEntryCount:N0}개 / {FormatBytes(result.SizeBytes)} / SHA-256 {result.Sha256}{Environment.NewLine}병합 보고서: {result.ReportPath}");
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

    private void OpenOutputDirectory()
    {
        var outputPath = _outputPathTextBox.Text.Trim();
        if (!Directory.Exists(outputPath))
        {
            ShowError("출력 폴더가 아직 없습니다. Base ZIP을 생성한 뒤 다시 시도하세요.");
            return;
        }

        Process.Start(new ProcessStartInfo(outputPath) { UseShellExecute = true });
    }

    private void UpdateProgress(BuildProgress progress)
    {
        _statusLabel.Text = progress.Status;
        if (progress.TotalFiles > 0)
        {
            _progressBar.Style = ProgressBarStyle.Continuous;
            _progressBar.Value = Math.Clamp(progress.CompletedFiles * 100 / progress.TotalFiles, 0, 100);
        }
        else
        {
            _progressBar.Style = ProgressBarStyle.Marquee;
        }
    }

    private void UpdateProgress(DeltaProgress progress)
    {
        _statusLabel.Text = progress.Status;
        if (progress.TotalItems > 0)
        {
            _progressBar.Style = ProgressBarStyle.Continuous;
            _progressBar.Value = Math.Clamp(progress.CompletedItems * 100 / progress.TotalItems, 0, 100);
        }
        else
        {
            _progressBar.Style = ProgressBarStyle.Marquee;
        }
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        _baselinePathTextBox.Enabled = !isBusy;
        _seedIndexPathTextBox.Enabled = !isBusy;
        _outputPathTextBox.Enabled = !isBusy;
        _zipNameTextBox.Enabled = !isBusy;
        _additionalSourcesListBox.Enabled = !isBusy;
        _contributionZipListBox.Enabled = !isBusy;
        _selectBaselineButton.Enabled = !isBusy;
        _selectSeedIndexButton.Enabled = !isBusy;
        _createSeedIndexButton.Enabled = !isBusy;
        _selectOutputButton.Enabled = !isBusy;
        _analyzeButton.Enabled = !isBusy;
        _addSourceButton.Enabled = !isBusy;
        _removeSourceButton.Enabled = !isBusy;
        _addContributionButton.Enabled = !isBusy;
        _removeContributionButton.Enabled = !isBusy;
        _validateButton.Enabled = !isBusy && _plan is not null;
        _createZipButton.Enabled = !isBusy && _plan is not null && _planValidated;
        _applySelectionButton.Enabled = !isBusy && _candidateSourceComboBox.SelectedItem is SourceChoice;
        _keepBaselineButton.Enabled = !isBusy && _plan is not null && _plan.Conflicts.Count > 0;
        _openOutputButton.Enabled = !isBusy;
        if (!isBusy)
        {
            _progressBar.Style = ProgressBarStyle.Continuous;
            if (_invalidateAfterBusy)
            {
                _invalidateAfterBusy = false;
                InvalidatePlan();
            }
        }
    }

    private IEnumerable<SourceChoice> GetSelectableSources(MergeConflict conflict)
    {
        if (_plan is null)
        {
            return [];
        }

        return BaseBuilderServices.GetSelectableSources(_plan, conflict)
            .Select(source => GetSource(source.Id))
            .Where(source => source is not null)
            .Cast<ExternalAssetsSource>()
            .Select(source => new SourceChoice(source.Id, source.DisplayName));
    }

    private ExternalAssetsSource? GetSource(string sourceId) =>
        _plan?.Sources.FirstOrDefault(source => string.Equals(source.Id, sourceId, StringComparison.Ordinal));

    private static string GetConflictKindText(ConflictKind kind) => kind switch
    {
        ConflictKind.SameRelativePath => "같은 상대 경로",
        ConflictKind.CaseOnlyPathCollision => "대소문자 경로 충돌",
        ConflictKind.FileDirectoryCollision => "파일/폴더 충돌",
        _ => kind.ToString(),
    };

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsPathInside(string candidatePath, string rootPath)
    {
        var candidateFullPath = Path.GetFullPath(candidatePath);
        var rootFullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        return string.Equals(candidateFullPath, rootFullPath, StringComparison.OrdinalIgnoreCase)
            || candidateFullPath.StartsWith(rootFullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private void SetStatus(string message)
    {
        _statusLabel.Text = message;
    }

    private void ShowError(string message)
    {
        _progressBar.Style = ProgressBarStyle.Continuous;
        SetStatus($"오류: {message}");
        MessageBox.Show(this, message, "External Assets Base Builder", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private sealed record SourceChoice(string Id, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private sealed record StagedContributionSource(
        string DisplayName,
        string ZipPath,
        string StagingRootPath,
        string ExternalAssetsPath,
        IReadOnlySet<string> RequiredDirectoryMetaPaths,
        IReadOnlyDictionary<string, string> ExpectedPayloadSha256,
        string ContributionId);
}
