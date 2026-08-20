param(
    [string]$ZipPath,

    [string]$DownloadUrl = 'https://drive.google.com/file/d/1f4kBUL5BDWEl2HB7EEc3iF0tyJHALCd_/view?usp=drive_link',

    [ValidateRange(1, 2147483647)]
    [int]$Version = 1,

    [ValidateSet('base', 'patch')]
    [string]$Type = 'base',

    [string]$OutputPath
)

if ([string]::IsNullOrWhiteSpace($ZipPath)) {
    Add-Type -AssemblyName System.Windows.Forms
    $dialog = [System.Windows.Forms.OpenFileDialog]::new()
    $dialog.Title = 'manifest.json을 만들 ZIP 파일을 선택하세요.'
    $dialog.Filter = 'ZIP 파일 (*.zip)|*.zip|모든 파일 (*.*)|*.*'
    $dialog.InitialDirectory = Join-Path ([System.Environment]::GetFolderPath('UserProfile')) 'Downloads'

    if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
        throw 'ZIP 파일 선택이 취소되었습니다.'
    }

    $ZipPath = $dialog.FileName
}

$resolvedZipPath = (Resolve-Path -LiteralPath $ZipPath -ErrorAction Stop).Path
if (-not (Test-Path -LiteralPath $resolvedZipPath -PathType Leaf)) {
    throw "ZIP 파일을 찾을 수 없습니다: $ZipPath"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path (Split-Path -Parent $resolvedZipPath) 'manifest.json'
}

$outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $outputFullPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$hash = (Get-FileHash -LiteralPath $resolvedZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = [ordered]@{
    schemaVersion = 1
    latestVersion = $Version
    packages = @(
        [ordered]@{
            version = $Version
            type = $Type
            name = [System.IO.Path]::GetFileName($resolvedZipPath)
            downloadUrl = $DownloadUrl
            sha256 = $hash
            removedPaths = @()
        }
    )
}

$json = $manifest | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText(
    $outputFullPath,
    $json + [System.Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))

Write-Host "생성 완료: $outputFullPath"
Write-Host "SHA-256: $hash"
