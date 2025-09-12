Write-Host "Starting SpeakEZ (Release)..." -ForegroundColor Green
$exePath = ".\src\UI\bin\Release\net10.0\win-x64\publish\Mel.UI.exe"

if (Test-Path $exePath) {
    & $exePath --debug
} else {
    Write-Host "Release build not found. Building now..." -ForegroundColor Yellow
    Set-Location -Path "src\UI"
    dotnet publish -c Release -r win-x64 --self-contained
    Set-Location -Path "..\.."
    
    if (Test-Path $exePath) {
        Write-Host "Starting SpeakEZ..." -ForegroundColor Green
        & $exePath --debug
    } else {
        Write-Host "Failed to build release version." -ForegroundColor Red
    }
}

Write-Host "Press any key to exit..." -ForegroundColor Yellow
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")