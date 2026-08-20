using System.Diagnostics;
using ProjectS.ExternalAssetsDelta;

namespace ProjectS.ExternalAssetsContributor;

/// <summary>
/// 팀원이 자기 PC의 ExternalAssets를 seed 목록과 비교해 변경분만 제출용 ZIP으로 만드는 UI다.
/// 원본 폴더, seed 파일, 기존 ZIP을 수정하지 않는다.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly TextBox _externalAssetsPathTextBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _seedIndexPathTextBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _contributorNameTextBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _noteTextBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _outputPathTextBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _zipNameTextBox = new() { Dock = DockStyle.Fill };
    private readonly Button _selectExternalAssetsButton;
    private readonly Button _selectSeedIndexButton;
    private readonly Button _selectOutputButton;
    private readonly Button _analyzeButton;
    private readonly Button _createZipButton;
    private readonly Button _openOutputButton;
    private readonly ListView _changesList = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
        HideSelection = false,
    };
    private readonly Label _summaryLabel = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly Label _statusLabel = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly ProgressBar _progressBar = new() { Dock = DockStyle.Fill };

    private ExternalAssetsComparison? _comparison;
    private bool _isBusy;

    public MainForm()
    {
        Text = "ProjectS External Assets Contributor";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 680);
        Size = new Size(1140, 790);

        _selectExternalAssetsButton = CreateButton("찾기", SelectExternalAssets);
        _selectSeedIndexButton = CreateButton("찾기", SelectSeedIndex);
        _selectOutputButton = CreateButton("찾기", SelectOutputDirectory);
        _analyzeButton = CreateButton("기준과 비교 분석", () => _ = AnalyzeAsync());
        _createZipButton = CreateButton("Contribution ZIP 생성", () => _ = CreateContributionZipAsync());
        _openOutputButton = CreateButton("출력 폴더 열기", OpenOutputDirectory);

        _changesList.Columns.Add("상대 경로", 480);
        _changesList.Columns.Add("종류", 100);
        _changesList.Columns.Add("비교 결과", 120);
        _changesList.Columns.Add("설명", 340);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 3,
            RowCount = 11,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (var row = 0; row < 8; row++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        AddRow(layout, 0, "내 ExternalAssets", _externalAssetsPathTextBox, _selectExternalAssetsButton);
        AddRow(layout, 1, "기준 목록 (seed-index.json)", _seedIndexPathTextBox, _selectSeedIndexButton);
        AddRow(layout, 2, "제출자 이름", _contributorNameTextBox, null);
        AddRow(layout, 3, "메모 (선택)", _noteTextBox, null);
        AddRow(layout, 4, "출력 폴더", _outputPathTextBox, _selectOutputButton);
        AddRow(layout, 5, "Contribution ZIP 이름", _zipNameTextBox, null);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
        };
        actions.Controls.AddRange([_analyzeButton, _createZipButton, _openOutputButton]);
        layout.Controls.Add(actions, 1, 6);
        layout.SetColumnSpan(actions, 2);

        layout.Controls.Add(new Label { Text = "변경 요약", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 7);
        layout.Controls.Add(_summaryLabel, 1, 7);
        layout.SetColumnSpan(_summaryLabel, 2);

        layout.Controls.Add(_changesList, 0, 8);
        layout.SetColumnSpan(_changesList, 3);

        layout.Controls.Add(_statusLabel, 0, 9);
        layout.SetColumnSpan(_statusLabel, 3);
        layout.Controls.Add(_progressBar, 0, 10);
        layout.SetColumnSpan(_progressBar, 3);

        Controls.Add(layout);

        _externalAssetsPathTextBox.TextChanged += (_, _) => InvalidateComparison();
        _seedIndexPathTextBox.TextChanged += (_, _) => InvalidateComparison();
        _contributorNameTextBox.TextChanged += (_, _) => UpdateCreateZipAvailability();
        _zipNameTextBox.TextChanged += (_, _) => UpdateCreateZipAvailability();
        Load += (_, _) => Initialize();
    }

    private static Button CreateButton(string text, Action onClick)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control content, Control? trailing)
    {
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        layout.Controls.Add(content, 1, row);
        if (trailing is not null)
        {
            layout.Controls.Add(trailing, 2, row);
        }
    }

    private void Initialize()
    {
        var projectRoot = FindProjectRoot(AppContext.BaseDirectory);
        if (projectRoot is null)
        {
            SetStatus("내 ExternalAssets 폴더와 배포 담당자가 준 seed-index.json을 선택하세요. 이 도구는 원본을 수정하지 않습니다.");
            return;
        }

        var externalAssetsPath = Path.Combine(projectRoot, "Assets", "ExternalAssets");
        if (Directory.Exists(externalAssetsPath))
        {
            _externalAssetsPathTextBox.Text = externalAssetsPath;
        }

        _outputPathTextBox.Text = Path.Combine(projectRoot, "ExternalAssetsContributions");
        _zipNameTextBox.Text = $"Contribution_{Environment.UserName}_{DateTime.Now:yyyyMMdd}.zip";
        _contributorNameTextBox.Text = Environment.UserName;
        SetStatus("seed-index.json을 선택한 뒤 ‘기준과 비교 분석’을 누르세요. Added/Modified만 ZIP에 들어가며 Missing은 보고만 합니다.");
    }

    private void SelectExternalAssets()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "내 Unity 프로젝트 또는 Assets/ExternalAssets 폴더를 선택하세요.",
        };
        if (Directory.Exists(_externalAssetsPathTextBox.Text))
        {
            dialog.InitialDirectory = _externalAssetsPathTextBox.Text;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _externalAssetsPathTextBox.Text = dialog.SelectedPath;
        }
    }

    private void SelectSeedIndex()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "배포 담당자가 만든 seed-index.json을 선택하세요.",
            Filter = "Seed index JSON|*.json|모든 파일|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _seedIndexPathTextBox.Text = dialog.FileName;
        }
    }

    private void SelectOutputDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Contribution ZIP을 저장할 폴더를 선택하세요.",
        };
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
            SetBusy(true);
            _comparison = null;
            _changesList.Items.Clear();
            SetStatus("기준 목록과 로컬 ExternalAssets를 해시로 비교하는 중입니다. 최초 비교는 원본 용량에 따라 시간이 걸릴 수 있습니다.");
            var localPath = _externalAssetsPathTextBox.Text.Trim();
            var seedIndexPath = _seedIndexPathTextBox.Text.Trim();
            var progress = new Progress<DeltaProgress>(UpdateProgress);
            var seed = await ExternalAssetsDeltaServices.LoadSeedIndexAsync(seedIndexPath, CancellationToken.None);
            _comparison = await ExternalAssetsDeltaServices.CompareLocalExternalAssetsAsync(localPath, seed, progress, CancellationToken.None);
            RefreshComparisonList();

            if (_comparison.HasErrors)
            {
                SetStatus("비교는 끝났지만 오류가 있어 Contribution ZIP을 만들 수 없습니다. 아래 오류를 해결한 뒤 다시 분석하세요.");
                return;
            }

            var changedCount = _comparison.Entries.Count(entry => entry.ChangeKind is DeltaComparisonKind.Added or DeltaComparisonKind.Modified);
            UpdateCreateZipAvailability();
            SetStatus(changedCount == 0
                ? "비교 완료: 제출할 Added/Modified 파일이 없습니다."
                : $"비교 완료: 변경 파일 {changedCount:N0}개를 확인했습니다. Contribution ZIP을 만들 수 있습니다.");
        }
        catch (Exception exception)
        {
            _comparison = null;
            _changesList.Items.Clear();
            ShowError(exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task CreateContributionZipAsync()
    {
        if (_comparison is null || _comparison.HasErrors)
        {
            return;
        }

        try
        {
            var outputDirectory = _outputPathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new InvalidOperationException("Contribution ZIP을 저장할 출력 폴더를 선택하세요.");
            }

            var zipName = _zipNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(zipName))
            {
                throw new InvalidOperationException("Contribution ZIP 파일명을 입력하세요.");
            }

            if (!zipName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                zipName += ".zip";
            }

            if (zipName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || zipName.Contains(Path.DirectorySeparatorChar)
                || zipName.Contains(Path.AltDirectorySeparatorChar))
            {
                throw new InvalidOperationException("Contribution ZIP 이름에는 폴더 경로를 넣을 수 없습니다.");
            }

            var outputPath = Path.Combine(Path.GetFullPath(outputDirectory), zipName);
            if (File.Exists(outputPath))
            {
                throw new InvalidOperationException($"같은 이름의 ZIP이 이미 있습니다: {outputPath}");
            }

            SetBusy(true);
            var progress = new Progress<DeltaProgress>(UpdateProgress);
            var result = await ExternalAssetsDeltaServices.CreateContributionPackageAsync(
                _comparison,
                new ContributionBuildOptions
                {
                    OutputZipPath = outputPath,
                    ContributorName = _contributorNameTextBox.Text.Trim(),
                    Note = string.IsNullOrWhiteSpace(_noteTextBox.Text) ? null : _noteTextBox.Text.Trim(),
                },
                progress,
                CancellationToken.None);
            _progressBar.Value = 100;
            SetStatus($"Contribution ZIP 생성 완료: {result.ZipPath}{Environment.NewLine}파일 {result.Manifest.Entries.Count:N0}개 / {FormatBytes(result.SizeBytes)} / SHA-256 {result.ArchiveSha256}{Environment.NewLine}이 ZIP만 팀별 제한된 제출 Drive 폴더에 업로드하세요.");
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

    private void RefreshComparisonList()
    {
        _changesList.BeginUpdate();
        _changesList.Items.Clear();
        if (_comparison is null)
        {
            _summaryLabel.Text = "분석 전";
            _changesList.EndUpdate();
            return;
        }

        var added = 0;
        var modified = 0;
        var missing = 0;
        foreach (var entry in _comparison.Entries
                     .Where(entry => entry.ChangeKind != DeltaComparisonKind.Unchanged)
                     .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            switch (entry.ChangeKind)
            {
                case DeltaComparisonKind.Added:
                    added++;
                    break;
                case DeltaComparisonKind.Modified:
                    modified++;
                    break;
                case DeltaComparisonKind.Missing:
                    missing++;
                    break;
            }

            var item = new ListViewItem(entry.RelativePath);
            item.SubItems.Add(entry.EntryType == SeedEntryType.Directory ? "폴더" : "파일");
            item.SubItems.Add(GetChangeKindText(entry.ChangeKind));
            item.SubItems.Add(GetEntryDescription(entry));
            _changesList.Items.Add(item);
        }

        foreach (var issue in _comparison.Issues
                     .OrderByDescending(issue => issue.Severity)
                     .ThenBy(issue => issue.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var item = new ListViewItem(issue.RelativePath ?? "<전체>");
            item.SubItems.Add("검증");
            item.SubItems.Add(issue.Severity == DeltaIssueSeverity.Error ? "오류" : "경고");
            item.SubItems.Add($"{issue.Code}: {issue.Message}");
            _changesList.Items.Add(item);
        }

        _summaryLabel.Text = $"추가: {added:N0} | 수정: {modified:N0} | 기준에만 있음(삭제 안 함): {missing:N0} | 경고: {_comparison.Issues.Count(issue => issue.Severity == DeltaIssueSeverity.Warning):N0} | 오류: {_comparison.Issues.Count(issue => issue.Severity == DeltaIssueSeverity.Error):N0}";
        _changesList.EndUpdate();
    }

    private static string GetChangeKindText(DeltaComparisonKind kind) => kind switch
    {
        DeltaComparisonKind.Added => "추가됨",
        DeltaComparisonKind.Modified => "수정됨",
        DeltaComparisonKind.Missing => "기준에만 있음",
        DeltaComparisonKind.Unchanged => "같음",
        _ => kind.ToString(),
    };

    private static string GetEntryDescription(DeltaComparisonEntry entry) => entry.ChangeKind switch
    {
        DeltaComparisonKind.Added => "로컬에만 있습니다. 실제 파일과 .meta를 제출 ZIP에 넣습니다.",
        DeltaComparisonKind.Modified => "기준과 해시가 다릅니다. 파일과 .meta를 함께 제출 ZIP에 넣습니다.",
        DeltaComparisonKind.Missing => "내 PC에 없지만 삭제 요청으로 처리하지 않습니다.",
        _ => string.Empty,
    };

    private void OpenOutputDirectory()
    {
        var outputPath = _outputPathTextBox.Text.Trim();
        if (!Directory.Exists(outputPath))
        {
            ShowError("출력 폴더가 아직 없습니다. Contribution ZIP을 생성한 뒤 다시 시도하세요.");
            return;
        }

        Process.Start(new ProcessStartInfo(outputPath) { UseShellExecute = true });
    }

    private void InvalidateComparison()
    {
        if (_isBusy)
        {
            return;
        }

        _comparison = null;
        _createZipButton.Enabled = false;
        _changesList.Items.Clear();
        _summaryLabel.Text = "입력이 변경되었습니다. 다시 비교 분석하세요.";
    }

    private void UpdateProgress(DeltaProgress progress)
    {
        _statusLabel.Text = progress.Status;
        if (progress.TotalItems <= 0)
        {
            _progressBar.Style = ProgressBarStyle.Marquee;
            return;
        }

        _progressBar.Style = ProgressBarStyle.Continuous;
        _progressBar.Value = Math.Clamp(progress.CompletedItems * 100 / progress.TotalItems, 0, 100);
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        _externalAssetsPathTextBox.Enabled = !isBusy;
        _seedIndexPathTextBox.Enabled = !isBusy;
        _contributorNameTextBox.Enabled = !isBusy;
        _noteTextBox.Enabled = !isBusy;
        _outputPathTextBox.Enabled = !isBusy;
        _zipNameTextBox.Enabled = !isBusy;
        _selectExternalAssetsButton.Enabled = !isBusy;
        _selectSeedIndexButton.Enabled = !isBusy;
        _selectOutputButton.Enabled = !isBusy;
        _analyzeButton.Enabled = !isBusy;
        _openOutputButton.Enabled = !isBusy;
        UpdateCreateZipAvailability();
        if (!isBusy)
        {
            _progressBar.Style = ProgressBarStyle.Continuous;
        }
    }

    private void SetStatus(string message) => _statusLabel.Text = message;

    private void UpdateCreateZipAvailability()
    {
        _createZipButton.Enabled = !_isBusy
            && _comparison is { HasErrors: false }
            && _comparison.Entries.Any(entry => entry.ChangeKind is DeltaComparisonKind.Added or DeltaComparisonKind.Modified)
            && !string.IsNullOrWhiteSpace(_contributorNameTextBox.Text)
            && !string.IsNullOrWhiteSpace(_zipNameTextBox.Text);
    }

    private void ShowError(string message)
    {
        _progressBar.Style = ProgressBarStyle.Continuous;
        SetStatus($"오류: {message}");
        MessageBox.Show(this, message, "External Assets Contributor", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private static string? FindProjectRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "Assets"))
                && Directory.Exists(Path.Combine(current.FullName, "Packages"))
                && Directory.Exists(Path.Combine(current.FullName, "ProjectSettings")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
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
}
