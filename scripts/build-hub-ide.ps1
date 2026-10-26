param([Parameter(Mandatory)][string]$EditorSource)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$EditorSource = [IO.Path]::GetFullPath($EditorSource)
$pin = Get-Content -LiteralPath (Join-Path $projectRoot 'ide-product/upstream.json') -Raw | ConvertFrom-Json
$version = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'src-desktop/MultiAgentHub.csproj') -Raw)).Project.PropertyGroup.Version
$head = (git -C $projectRoot rev-parse HEAD).Trim()
node (Join-Path $projectRoot 'ide-product/prepare.cjs') $EditorSource
if ($LASTEXITCODE -ne 0) { throw 'Source product preparation failed.' }
$env:ELECTRON_SKIP_BINARY_DOWNLOAD = '1'
$env:PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD = '1'
$env:NODE_OPTIONS = '--max-old-space-size=6144'
if (!(Test-Path -LiteralPath 'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\VC\Tools\MSVC')) { throw 'This pinned editor requires the VS 2022 C++ toolchain; use the windows-2022 CI runner.' }
$env:vs2022_install = 'C:\Program Files\Microsoft Visual Studio\2022\Enterprise'
Push-Location $EditorSource
try {
    npm ci
    if ($LASTEXITCODE -ne 0) { throw 'Editor dependency build failed.' }
    npm run gulp vscode-win32-x64-min
    if ($LASTEXITCODE -ne 0) { throw 'Hub Code source compilation failed.' }
} finally { Pop-Location }
$built = Join-Path ([IO.Path]::GetDirectoryName($EditorSource)) 'VSCode-win32-x64'
$app = Join-Path $built 'resources/app'
$product = Get-Content -LiteralPath (Join-Path $app 'product.json') -Raw | ConvertFrom-Json
if ($product.nameShort -ne 'Hub Code' -or $product.hubSourceCommit -ne $head -or !(Test-Path -LiteralPath (Join-Path $built 'Hub Code.exe'))) { throw 'Output is not our Hub Code product.' }
$extension = Join-Path $app 'extensions/multiagent-hub'
New-Item -ItemType Directory -Path $extension -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $projectRoot 'ide-extension') -File | Copy-Item -Destination $extension
$backend = Join-Path $app 'hub-backend'
dotnet publish (Join-Path $projectRoot 'src-desktop/MultiAgentHub.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o $backend --nologo
if ($LASTEXITCODE -ne 0) { throw 'Bundled Hub backend compilation failed.' }
# Make Agent available on direct Hub Code launches as well as the desktop entry point.
@{ version=$version; sourceCommit=$head; editorCommit=$pin.commit } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $built 'hub-product.json') -Encoding utf8
$release = Join-Path $projectRoot 'release'
New-Item -ItemType Directory -Path $release -Force | Out-Null
$archive = Join-Path $release 'hub-ide.zip'
if (Test-Path -LiteralPath $archive) { throw 'Use a fresh build directory; refusing to reuse an old editor bundle.' }
[IO.Compression.ZipFile]::CreateFromDirectory($built, $archive, [IO.Compression.CompressionLevel]::Optimal, $false)
$manifest = @{
    Version=$version; SourceCommit=$head; EditorCommit=$pin.commit
    ArchiveBytes=(Get-Item -LiteralPath $archive).Length
    Sha256=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    ExecutableSha256=(Get-FileHash -LiteralPath (Join-Path $built 'Hub Code.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath ($archive+'.json') -Encoding utf8
Write-Output ('Built own Hub Code bundle: '+$archive)
