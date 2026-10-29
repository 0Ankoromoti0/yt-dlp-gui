param([ValidateSet('Debug','Release')][string]$Configuration = 'Release', [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot
Set-Location -LiteralPath $taskRoot
$taskDotnet = Join-Path $taskRoot '.dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) { $taskDotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.build\cli'
$env:NUGET_PACKAGES = Join-Path $taskRoot '.build\nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ROOT = Split-Path $taskDotnet
$taskOutput = Join-Path $taskRoot 'artifacts\YtDlpGui'
& $taskDotnet publish src/YtDlpGui/YtDlpGui.csproj -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=false -o $taskOutput --disable-build-servers
if ($LASTEXITCODE -ne 0) { throw 'アプリの発行に失敗しました。' }
$taskTools = Join-Path $taskOutput 'tools'
New-Item -ItemType Directory -Force -Path $taskTools | Out-Null
foreach ($taskTool in @('yt-dlp.exe','ffmpeg.exe','ffprobe.exe','deno.exe')) {
    $taskSource = Join-Path $taskRoot "tools\$taskTool"
    if (-not (Test-Path -LiteralPath $taskSource)) { $taskSource = (Get-Command $taskTool -ErrorAction Stop).Source }
    $taskTarget = Join-Path $taskTools $taskTool
    if ($taskSource -ne $taskTarget) { Copy-Item -LiteralPath $taskSource -Destination $taskTarget -Force }
}
Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination $taskOutput -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'THIRD-PARTY-NOTICES.md') -Destination $taskOutput -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'LICENSE') -Destination $taskOutput -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'VERIFICATION.md') -Destination $taskOutput -Force
$taskLicense = Join-Path $taskRoot 'licenses'
if (Test-Path -LiteralPath $taskLicense) { Copy-Item -LiteralPath $taskLicense -Destination $taskOutput -Recurse -Force }
if (-not $SkipTests) {
    & $taskDotnet build tests/YtDlpGui.Tests/YtDlpGui.Tests.csproj -c $Configuration --disable-build-servers
    if ($LASTEXITCODE -ne 0) { throw 'テストのビルドに失敗しました。' }
    & $taskDotnet "tests/YtDlpGui.Tests/bin/$Configuration/net10.0/YtDlpGui.Tests.dll" $taskRoot --integration
    if ($LASTEXITCODE -ne 0) { throw 'テストに失敗しました。artifacts/testsのログを確認してください。' }
}
Write-Output "起動: $taskOutput\YtDlpGui.exe"
