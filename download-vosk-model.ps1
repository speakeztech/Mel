# Download and extract Vosk model for SpeakEZ

$modelUrl = "https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip"
$modelZip = "vosk-model-small-en-us-0.15.zip"
$modelsDir = "src\UI\models"

Write-Host "Downloading Vosk model..." -ForegroundColor Green
Write-Host "This is a ~40MB download"

# Download the model
Invoke-WebRequest -Uri $modelUrl -OutFile $modelZip -UseBasicParsing

Write-Host "Extracting model..." -ForegroundColor Yellow

# Extract to models directory
Expand-Archive -Path $modelZip -DestinationPath $modelsDir -Force

# Clean up zip file
Remove-Item $modelZip

Write-Host "✅ Vosk model installed successfully!" -ForegroundColor Green
Write-Host "Model location: $modelsDir\vosk-model-small-en-us-0.15" -ForegroundColor Cyan
Write-Host ""
Write-Host "You can now run: .\run-debug.ps1" -ForegroundColor Yellow