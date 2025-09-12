Write-Host "Starting SpeakEZ with debug mode..." -ForegroundColor Green
Set-Location -Path "src\UI"
dotnet run -- --debug
Write-Host "Press any key to exit..." -ForegroundColor Yellow
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")