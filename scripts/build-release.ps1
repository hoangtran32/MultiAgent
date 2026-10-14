param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$buildRoot = Join-Path $projectRoot '.build'
$releaseRoot = Join-Path $projectRoot 'release'
$stage = Join-Path $buildRoot ('single-exe-' + [Guid]::NewGuid().ToString('N'))
foreach ($target in @($buildRoot, $releaseRoot, $stage)) {
    if (!$target.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Release target outside project.' }
    if ((Test-Path -LiteralPath $target) -and ((Get-Item -LiteralPath $target -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Release target cannot be a link.' }
}
$version = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'src-desktop/MultiAgentHub.csproj') -Raw)).Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a numeric release version.' }
$executable = Join-Path $releaseRoot "MultiAgentHub-$version-win-x64.exe"
$checksum = "$executable.sha256"
foreach ($target in @($executable, $checksum)) {
    if ((Test-Path -LiteralPath $target) -and ((Get-Item -LiteralPath $target -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Release file cannot be a link.' }
}
Push-Location $projectRoot
try {
    if (!$SkipTests) {
        dotnet run --project tests-native -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Native tests failed; release not replaced.' }
    } else { Write-Host 'Functional tests skipped; publishing only.' }
    dotnet publish src-desktop/MultiAgentHub.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $stage --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Release publish failed.' }
    $files = @(Get-ChildItem -LiteralPath $stage -File -Recurse -Force)
    if ($files.Count -ne 1 -or $files[0].Name -ne 'MultiAgentHub.exe') { throw 'Expected exactly one published executable.' }
    if (($files[0].VersionInfo.ProductVersion -split '\+')[0] -ne $version) { throw 'Published executable version mismatch.' }
    $hash = (Get-FileHash -LiteralPath $files[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
    Move-Item -LiteralPath $files[0].FullName -Destination $executable -Force
    [IO.File]::WriteAllText($checksum, "$hash  $([IO.Path]::GetFileName($executable))" + [Environment]::NewLine)
    Write-Host "EXE: $executable"
    Write-Host "SHA-256: $hash"
    Write-Host 'Installed apps, shortcuts and user profiles were not changed.'
} finally { Pop-Location }
