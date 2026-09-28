# Сборка установщика «Ростка».
#   pwsh build/build-installer.ps1
# Результат: artifacts/Rostok-Setup-<версия>.exe — один файл, .NET внутри, программа встроена.
# Генератор серийных номеров собирается в отдельном закрытом репозитории администратора.
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "artifacts"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $out "payload") | Out-Null

[xml]$proj = Get-Content (Join-Path $root "src/Rostok.Desktop/Rostok.Desktop.csproj")
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
Write-Host "Росток $version" -ForegroundColor Magenta

Write-Host "1/3 Публикую программу…"
dotnet publish (Join-Path $root "src/Rostok.Desktop") -c $Configuration -r $Runtime --self-contained true -o (Join-Path $out "app") -nologo -v q
if ($LASTEXITCODE) { throw "Не удалось опубликовать программу" }

Write-Host "2/3 Упаковываю программу для установщика…"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $out "payload/Rostok.zip"
[System.IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $out "app"), $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

Write-Host "3/3 Собираю установщик…"
dotnet publish (Join-Path $root "src/Rostok.Setup") -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:PayloadZip="$zip" -o (Join-Path $out "setup") -nologo -v q
if ($LASTEXITCODE) { throw "Не удалось собрать установщик" }
Move-Item (Join-Path $out "setup/Rostok-Setup.exe") (Join-Path $out "Rostok-Setup-$version.exe")

Remove-Item (Join-Path $out "setup"), (Join-Path $out "app"), (Join-Path $out "payload") -Recurse -Force
Write-Host ""
Write-Host "Готово:" -ForegroundColor Green
Get-ChildItem $out -Recurse -File | ForEach-Object { "  {0,-60} {1,8:N1} МБ" -f $_.FullName.Substring($out.Length + 1), ($_.Length / 1MB) }
