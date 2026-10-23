param([switch]$NoOpen)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET 8 SDK kurulu olmali.' }
    & (Join-Path $PSScriptRoot 'create_icon.ps1')
    $outputDirectory = Join-Path $PSScriptRoot 'dist\Riftevora-3.1.0-win-x64'
    dotnet publish 'src\SkinForge.UI\SkinForge.UI.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $outputDirectory --nologo
    if ($LASTEXITCODE -ne 0) { throw "Derleme basarisiz: $LASTEXITCODE" }
    $catalogFile = Join-Path $outputDirectory 'Resources\catalog-v2.json'
    if (-not (Test-Path -LiteralPath $catalogFile)) { throw 'Dagitim katalog dosyasi eksik: Resources\catalog-v2.json' }
    Write-Host "Hazir: $outputDirectory\Riftevora.exe" -ForegroundColor Green
    if (-not $NoOpen) { Invoke-Item -LiteralPath $outputDirectory }
}
finally { Pop-Location }

