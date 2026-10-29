param([ValidateSet('win-x64','osx-arm64','osx-x64')][string]$Runtime)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot
$taskTools = Join-Path $taskRoot 'tools'
$taskSources = Join-Path $taskRoot 'artifacts/engine-sources'
New-Item -ItemType Directory -Force $taskTools,$taskSources | Out-Null
$taskYtVersion = '2026.08.19'
$taskDenoVersion = 'v2.9.7'
$taskYtAsset = if ($Runtime -eq 'win-x64') { 'yt-dlp.exe' } else { 'yt-dlp_macos' }
$taskSuffix = if ($Runtime -eq 'win-x64') { '.exe' } else { '' }
$taskYtUrl = "https://github.com/yt-dlp/yt-dlp/releases/download/$taskYtVersion"
Invoke-WebRequest "$taskYtUrl/$taskYtAsset" -OutFile "$taskTools/yt-dlp$taskSuffix"
$taskChecksums = (Invoke-WebRequest "$taskYtUrl/SHA2-256SUMS").Content
$taskExpected = (($taskChecksums -split "`n" | Where-Object { $_.TrimEnd().EndsWith(" $taskYtAsset") }) -split '\s+')[0]
if (-not $taskExpected -or (Get-FileHash "$taskTools/yt-dlp$taskSuffix" -Algorithm SHA256).Hash -ne $taskExpected) { throw 'yt-dlp checksum mismatch' }
$taskTarget = switch ($Runtime) { 'win-x64' { 'x86_64-pc-windows-msvc' }; 'osx-arm64' { 'aarch64-apple-darwin' }; 'osx-x64' { 'x86_64-apple-darwin' } }
$taskDenoUrl = "https://github.com/denoland/deno/releases/download/$taskDenoVersion/deno-$taskTarget.zip"
Invoke-WebRequest $taskDenoUrl -OutFile "$taskSources/deno.zip"
$taskDenoHash = switch ($Runtime) {
    'win-x64' { 'a0c3101b4158d1dfb7d6a78a7bf0f3de80c96bb423c152beec8beb22786f2238' }
    'osx-arm64' { '5cd46d6268f6f78f5d88bdc7159d20bd44cdaa4b3303474839f87ec6fe7ae25c' }
    'osx-x64' { '95daaff11c116a52ad54785e7914c8e9c9cdcaba793c5ed929c74ca2d8e6259a' }
}
if ((Get-FileHash "$taskSources/deno.zip" -Algorithm SHA256).Hash -ne $taskDenoHash) { throw 'Deno checksum mismatch' }
Expand-Archive "$taskSources/deno.zip" $taskTools -Force
Remove-Item -LiteralPath "$taskSources/deno.zip"
Invoke-WebRequest "$taskYtUrl/yt-dlp.tar.gz" -OutFile "$taskSources/yt-dlp-$taskYtVersion.tar.gz"
Invoke-WebRequest "https://github.com/denoland/deno/archive/refs/tags/$taskDenoVersion.tar.gz" -OutFile "$taskSources/deno-$taskDenoVersion.tar.gz"
@{ ytDlp = $taskYtVersion; deno = $taskDenoVersion; runtime = $Runtime; ytDlpUrl = "$taskYtUrl/$taskYtAsset"; denoUrl = $taskDenoUrl } | ConvertTo-Json | Set-Content "$taskSources/versions.json"
if (-not $IsWindows) { & chmod +x "$taskTools/yt-dlp" "$taskTools/deno"; if ($LASTEXITCODE -ne 0) { throw 'chmod failed' } }
