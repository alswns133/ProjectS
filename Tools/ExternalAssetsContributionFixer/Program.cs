using System.IO.Compression;
using System.Text.Json;
using ProjectS.ExternalAssetsDelta;

// baseline(=seed)과 여러 Contribution ZIP을 함께 대조해, GUID가 겹치는 항목(에셋+.meta)만
// 제거한 정리본 ZIP들을 만든다. BaseBuilder 병합 검증(GUID 중복 하드블록)을 통과시키는 것이 목적.
//
// 우선권: baseline 이 항상 최우선(내 것 유지). 그다음 명령줄에 준 ZIP 순서대로.
// 먼저 등장한 경로가 GUID를 차지하고, 같은 GUID를 "다른 경로"가 또 쓰면 그 항목을 제거한다.
// 같은 경로가 같은 GUID를 쓰는 것은 충돌이 아니라 same-path(합칠 때 BaseBuilder가 처리)이므로 남긴다.

if (args.Length < 3)
{
    Console.WriteLine("사용법: ProjectSExternalAssetsContributionFixer <seed-index.json> <출력 폴더> <입력1.zip> [입력2.zip ...]");
    Console.WriteLine("  * 입력 ZIP은 우선순위 순서로 나열하세요(먼저 = 우선). baseline은 항상 최우선입니다.");
    return 1;
}

var seedPath = args[0];
var outputDirectory = Path.GetFullPath(args[1]);
var inputZips = args.Skip(2).Select(Path.GetFullPath).ToArray();

foreach (var input in inputZips)
{
    if (!File.Exists(input))
    {
        Console.WriteLine($"입력 ZIP을 찾을 수 없습니다: {input}");
        return 1;
    }
}

Directory.CreateDirectory(outputDirectory);
var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };

SeedIndex seed;
try
{
    seed = await ExternalAssetsDeltaServices.LoadSeedIndexAsync(seedPath, CancellationToken.None);
}
catch (Exception exception)
{
    Console.WriteLine($"seed-index.json을 읽지 못했습니다: {exception.Message}");
    return 1;
}

// guid -> 그 GUID를 차지한 경로. baseline(루트 + seed의 모든 .meta)을 먼저 채운다(최우선).
var claimedGuidPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    [seed.ExternalAssetsRootGuid] = "<Assets/ExternalAssets.meta>",
};
foreach (var entry in seed.Entries.Where(entry => entry.MetaTargetKind is not null && entry.Guid is not null))
{
    claimedGuidPath[entry.Guid!] = entry.RelativePath;
}

var totalRemoved = 0;
foreach (var inputZip in inputZips)
{
    Console.WriteLine();
    Console.WriteLine($"=== {Path.GetFileName(inputZip)} ===");

    ContributionManifest manifest;
    using (var archive = ZipFile.OpenRead(inputZip))
    {
        var manifestEntry = archive.GetEntry("contribution.json");
        if (manifestEntry is null)
        {
            Console.WriteLine("  contribution.json이 없습니다. Contributor가 만든 ZIP이 맞는지 확인하세요. 건너뜁니다.");
            continue;
        }

        await using var stream = manifestEntry.Open();
        manifest = await JsonSerializer.DeserializeAsync<ContributionManifest>(stream, jsonOptions)
            ?? throw new InvalidOperationException("contribution.json 파싱 실패");
    }

    var removeRelativePaths = new HashSet<string>(StringComparer.Ordinal);
    var removed = new List<(string MetaPath, string Guid, string Owner)>();
    var folderCollision = false;

    foreach (var entry in manifest.Entries
                 .Where(entry => entry.MetaTargetKind is not null && entry.Guid is not null)
                 .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal))
    {
        if (claimedGuidPath.TryGetValue(entry.Guid!, out var owner)
            && !string.Equals(owner, entry.RelativePath, StringComparison.OrdinalIgnoreCase))
        {
            if (entry.MetaTargetKind == ContributionMetaTargetKind.Folder)
            {
                Console.WriteLine($"  폴더 .meta GUID 충돌은 자동 처리하지 않습니다(수동 확인 필요): {entry.RelativePath} = {owner}");
                folderCollision = true;
                continue;
            }

            removeRelativePaths.Add(entry.RelativePath);
            removeRelativePaths.Add(entry.RelativePath[..^".meta".Length]);
            removed.Add((entry.RelativePath, entry.Guid!, owner));
        }
        else
        {
            // 이 GUID를 이 경로가 차지(뒤 기여의 다른 경로가 같은 GUID면 그때 제거됨)
            claimedGuidPath[entry.Guid!] = entry.RelativePath;
        }
    }

    if (folderCollision)
    {
        Console.WriteLine("  폴더 .meta 충돌이 있어 이 ZIP은 정리본을 만들지 않았습니다. 수동 확인 후 다시 시도하세요.");
        continue;
    }

    if (removed.Count == 0)
    {
        Console.WriteLine("  충돌 없음 → 정리 불필요(원본 그대로 사용하면 됩니다).");
        continue;
    }

    Console.WriteLine($"  충돌 {removed.Count}건 → payload {removeRelativePaths.Count}개(에셋+meta) 제거");
    foreach (var item in removed.Take(50))
    {
        Console.WriteLine($"    - {item.MetaPath}  (GUID {item.Guid} == {item.Owner})");
    }

    if (removed.Count > 50)
    {
        Console.WriteLine($"    ... 외 {removed.Count - 50}건");
    }

    var keptEntries = manifest.Entries.Where(entry => !removeRelativePaths.Contains(entry.RelativePath)).ToList();
    var newManifest = new ContributionManifest
    {
        SchemaVersion = manifest.SchemaVersion,
        Kind = manifest.Kind,
        ContributionId = manifest.ContributionId,
        BaselineId = manifest.BaselineId,
        BaselineContentSha256 = manifest.BaselineContentSha256,
        ExternalAssetsRootGuid = manifest.ExternalAssetsRootGuid,
        CreatedAtUtc = manifest.CreatedAtUtc,
        ContributorName = manifest.ContributorName,
        Note = manifest.Note,
        Directories = manifest.Directories,
        Entries = keptEntries,
    };

    var removedPayloadNames = removeRelativePaths
        .Select(path => ExternalAssetsDeltaFormat.PayloadPrefix + path)
        .ToHashSet(StringComparer.Ordinal);

    var outputZip = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(inputZip) + "_fixed.zip");
    if (File.Exists(outputZip))
    {
        Console.WriteLine($"  출력이 이미 있습니다(건너뜀): {outputZip}");
        continue;
    }

    var temporaryZip = outputZip + ".tmp-" + Guid.NewGuid().ToString("N");
    try
    {
        using (var source = ZipFile.OpenRead(inputZip))
        using (var destination = ZipFile.Open(temporaryZip, ZipArchiveMode.Create))
        {
            var manifestEntry = destination.CreateEntry("contribution.json", CompressionLevel.Optimal);
            await using (var stream = manifestEntry.Open())
            {
                await JsonSerializer.SerializeAsync(stream, newManifest, jsonOptions);
            }

            foreach (var entry in source.Entries)
            {
                if (entry.FullName == "contribution.json" || removedPayloadNames.Contains(entry.FullName))
                {
                    continue;
                }

                // 이미 압축된 에셋을 다시 최적압축하면 수 GB에서 너무 느리다.
                // 재압축은 Fastest로(대부분 바이너리 에셋이라 크기 차이도 작다).
                var copied = destination.CreateEntry(entry.FullName, CompressionLevel.Fastest);
                await using var input = entry.Open();
                await using var output = copied.Open();
                await input.CopyToAsync(output);
            }
        }

        File.Move(temporaryZip, outputZip);
    }
    finally
    {
        if (File.Exists(temporaryZip))
        {
            File.Delete(temporaryZip);
        }
    }

    try
    {
        var loaded = await ExternalAssetsDeltaServices.LoadContributionPackageAsync(outputZip, CancellationToken.None);
        Console.WriteLine($"  생성·검증 완료: {outputZip} (남은 항목 {loaded.Manifest.Entries.Count}개)");
        totalRemoved += removed.Count;
    }
    catch (Exception exception)
    {
        Console.WriteLine($"  경고: 생성된 ZIP 재검증 실패: {exception.Message}");
    }
}

Console.WriteLine();
Console.WriteLine($"끝. 총 제거 충돌 {totalRemoved}건. _fixed.zip을 BaseBuilder에 (원본 대신) 추가하세요.");
Console.WriteLine("충돌 항목은 baseline 또는 먼저 넣은 기여의 버전이 유지됩니다.");
return 0;
