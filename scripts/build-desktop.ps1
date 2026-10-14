param([switch]$Zip, [switch]$CreateShortcut, [switch]$SkipTests, [switch]$PackageOnly)
$ErrorActionPreference = 'Stop'
if ($PackageOnly -and $CreateShortcut) { throw '-PackageOnly does not install an app or create a shortcut. Omit -CreateShortcut.' }
if ($PackageOnly) { $Zip = $true }
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = Join-Path $projectRoot 'app-bin'
$buildRoot = Join-Path $projectRoot '.build'
$stage = Join-Path $buildRoot ('publish-' + [Guid]::NewGuid().ToString('N'))
$previous = Join-Path $buildRoot ('previous-' + [Guid]::NewGuid().ToString('N'))
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
function Assert-WorkspacePath([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    if (!$resolved.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Build target outside workspace: $resolved" }
    if (Test-Path -LiteralPath $resolved) {
        if ((Get-Item -LiteralPath $resolved -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Build target cannot be a link: $resolved" }
    }
}
Push-Location $projectRoot
try {
    foreach ($target in @($buildRoot, $output, $stage, $previous)) { Assert-WorkspacePath $target }
    if (!$SkipTests) {
        Write-Host 'Running native C# tests...'
        & $dotnet run --project tests-native -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Native tests failed; existing app is unchanged.' }
    } else { Write-Host 'Functional tests skipped as requested; compiling/publishing only.' }
    Write-Host 'Publishing self-contained Windows x64 app...'
    & $dotnet publish src-desktop/MultiAgentHub.csproj -c Release -r win-x64 --self-contained true -o $stage
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed; existing app is unchanged.' }
    foreach ($name in @('app.ico', 'LICENSE', 'README.md', 'CHANGELOG.md', 'CONTRIBUTING.md', 'SECURITY.md')) { Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $stage }
    $docs = Join-Path $stage 'docs'
    New-Item -ItemType Directory -Path $docs -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $projectRoot 'docs') -Filter '*.md' -File | Copy-Item -Destination $docs
    $runtimeNotices = Join-Path $stage 'licenses'
    New-Item -ItemType Directory -Path $runtimeNotices -Force | Out-Null
    foreach ($name in @('LICENSE.txt', 'ThirdPartyNotices.txt')) { Copy-Item -LiteralPath (Join-Path (Split-Path $dotnet -Parent) $name) -Destination $runtimeNotices }
    Get-ChildItem -LiteralPath $stage -Filter '*.pdb' -File | Remove-Item -Force
    if (!(Test-Path -LiteralPath (Join-Path $stage 'MultiAgentHub.exe'))) { throw 'Publish did not produce MultiAgentHub.exe.' }
    $packageSource = $stage
    if (!$PackageOnly) {
        $running = Get-Process MultiAgentHub -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $output 'MultiAgentHub.exe') }
        if ($running) { throw "New build ready in $stage. Close Multi-Agent Hub to update app-bin, or use -PackageOnly to package without interrupting it." }
        if (Test-Path -LiteralPath $output) { Move-Item -LiteralPath $output -Destination $previous }
        try { Move-Item -LiteralPath $stage -Destination $output }
        catch { if (Test-Path -LiteralPath $previous) { Move-Item -LiteralPath $previous -Destination $output }; throw }
        $packageSource = $output
    }
    if ($CreateShortcut) {
        $shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Multi-Agent Hub.lnk'
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = Join-Path $output 'MultiAgentHub.exe'
        $shortcut.IconLocation = $shortcut.TargetPath + ',0'
        $shortcut.WorkingDirectory = $output
        $shortcut.Save()
        Write-Host "Shortcut created: $shortcutPath"
    }
    if ($Zip) {
        $release = Join-Path $projectRoot 'release'
        Assert-WorkspacePath $release
        New-Item -ItemType Directory -Path $release -Force | Out-Null
        $archive = Join-Path $release 'MultiAgentHub-win-x64.zip'
        $temporaryArchive = Join-Path $buildRoot ('package-' + [Guid]::NewGuid().ToString('N') + '.zip')
        Assert-WorkspacePath $archive
        Assert-WorkspacePath $temporaryArchive
        Compress-Archive -Path (Join-Path $packageSource '*') -DestinationPath $temporaryArchive
        Move-Item -LiteralPath $temporaryArchive -Destination $archive -Force
        Write-Host "Release archive: $archive"
    }
    if ($PackageOnly) { Write-Host 'Package ready. The installed app and its running tasks were not changed.' }
    else { Write-Host "App ready: $(Join-Path $output 'MultiAgentHub.exe')" }
    if (Test-Path -LiteralPath $previous) { Write-Host "Previous build retained: $previous" }
} finally { Pop-Location }
