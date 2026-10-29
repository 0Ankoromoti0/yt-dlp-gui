$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot
$taskBuild = Join-Path $taskRoot '.build'
New-Item -ItemType Directory -Force -Path $taskBuild | Out-Null
$taskInstaller = Join-Path $taskBuild 'dotnet-install.ps1'
Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $taskInstaller
& $taskInstaller -Channel 10.0 -InstallDir (Join-Path $taskRoot '.dotnet') -NoPath
if ($LASTEXITCODE -ne 0) { throw '.NET SDKの準備に失敗しました。' }
