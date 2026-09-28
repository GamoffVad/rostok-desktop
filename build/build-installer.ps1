# Сборка установщика и генератора серийных номеров «Ростка».
#   pwsh build/build-installer.ps1
# Результат в artifacts/:
#   Rostok-Setup-<версия>.exe   — установщик (один файл, .NET внутри, программа встроена)
#   Rostok-KeyGen/              — генератор серийных номеров для администратора
#                                  (секретный ключ keys/rostok-license.private.pem кладётся рядом, если он есть на этой машине)
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

Write-Host "1/4 Публикую программу…"
dotnet publish (Join-Path $root "src/Rostok.Desktop") -c $Configuration -r $Runtime --self-contained true -o (Join-Path $out "app") -nologo -v q
if ($LASTEXITCODE) { throw "Не удалось опубликовать программу" }

Write-Host "2/4 Упаковываю программу для установщика…"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $out "payload/Rostok.zip"
[System.IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $out "app"), $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

Write-Host "3/4 Собираю установщик…"
dotnet publish (Join-Path $root "src/Rostok.Setup") -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:PayloadZip="$zip" -o (Join-Path $out "setup") -nologo -v q
if ($LASTEXITCODE) { throw "Не удалось собрать установщик" }
Move-Item (Join-Path $out "setup/Rostok-Setup.exe") (Join-Path $out "Rostok-Setup-$version.exe")

Write-Host "4/4 Собираю генератор серийных номеров…"
dotnet publish (Join-Path $root "src/Rostok.KeyGen") -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -o (Join-Path $out "Rostok-KeyGen") -nologo -v q
if ($LASTEXITCODE) { throw "Не удалось собрать генератор" }

Remove-Item (Join-Path $out "setup"), (Join-Path $out "app"), (Join-Path $out "payload") -Recurse -Force
Get-ChildItem (Join-Path $out "Rostok-KeyGen") -Filter *.pdb | Remove-Item
Write-Host ""
Write-Host "Готово:" -ForegroundColor Green
Get-ChildItem $out -Recurse -File | ForEach-Object { "  {0,-60} {1,8:N1} МБ" -f $_.FullName.Substring($out.Length + 1), ($_.Length / 1MB) }
if (-not (Test-Path (Join-Path $out "Rostok-KeyGen/rostok-license.private.pem"))) {
    Write-Host "Секретного ключа нет рядом с генератором: выберите файл ключа в самом генераторе." -ForegroundColor Yellow
}
