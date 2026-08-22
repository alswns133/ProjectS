param(
    [string]$ZipPath,

    [Parameter(Mandatory = $true)]
    [string]$DriveFileId,

    [string]$ChannelId,

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

$driveFileSource = $DriveFileId.Trim()
if ($driveFileSource -match '^[A-Za-z0-9_-]{10,}$') {
    $DriveFileId = $driveFileSource
}
else {
    try {
        $uri = [Uri]$driveFileSource
    }
    catch {
        throw '제한된 Google Drive 파일 링크 또는 파일 ID를 -DriveFileId로 입력하세요.'
    }

    if ($uri.Host -ine 'drive.google.com') {
        throw 'Google Drive 파일 링크만 사용할 수 있습니다.'
    }

    $match = [regex]::Match($uri.AbsolutePath, '/file/d/([^/?#]+)', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($match.Success) {
        $DriveFileId = [Uri]::UnescapeDataString($match.Groups[1].Value)
    }
    else {
        $DriveFileId = ''
        foreach ($pair in $uri.Query.TrimStart('?').Split('&', [System.StringSplitOptions]::RemoveEmptyEntries)) {
            $parts = $pair.Split('=', 2)
            if ($parts.Length -eq 2 -and [Uri]::UnescapeDataString($parts[0]) -ieq 'id') {
                $DriveFileId = [Uri]::UnescapeDataString($parts[1])
                break
            }
        }
    }
}

if ($DriveFileId -notmatch '^[A-Za-z0-9_-]{10,}$') {
    throw 'Google Drive 파일 ID를 링크에서 찾을 수 없습니다.'
}

if ([string]::IsNullOrWhiteSpace($ChannelId)) {
    if ($Version -ne 1) {
        throw 'v2 이상 patch manifest에는 기존 manifest의 channelId를 -ChannelId로 입력하세요. 여러 패치 누적은 Publisher 사용을 권장합니다.'
    }

    $ChannelId = 'projects-externalassets-' + [guid]::NewGuid().ToString('N')
}

if ($ChannelId -notmatch '^[A-Za-z0-9_-]{8,}$') {
    throw 'channelId는 8자 이상의 영문, 숫자, 밑줄, 하이픈만 사용할 수 있습니다.'
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $OutputPath = Join-Path $projectRoot 'ExternalAssetsReleases\manifest.json'
}

$outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $outputFullPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$hash = (Get-FileHash -LiteralPath $resolvedZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = [ordered]@{
    schemaVersion = 2
    channelId = $ChannelId
    latestVersion = $Version
    packages = @(
        [ordered]@{
            version = $Version
            type = $Type
            name = [System.IO.Path]::GetFileName($resolvedZipPath)
            driveFileId = $DriveFileId
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
Write-Host "Channel ID: $ChannelId"
Write-Host "SHA-256: $hash"
