$ErrorActionPreference = 'Stop'
$projectDirectory = $PSScriptRoot
$modelFile = Join-Path $projectDirectory 'models\hand_landmarker.task'
$pythonExecutable = Join-Path $projectDirectory '.venv\Scripts\python.exe'
Push-Location -LiteralPath $projectDirectory
try {
    & $pythonExecutable -m PyInstaller --noconfirm --onedir --name SignCoachVision --collect-all mediapipe --add-data "$modelFile;models" --distpath '..\SignCoachUnity\Build\Vision' --workpath 'output\stage5\pyinstaller' --specpath 'output\stage5' main.py
    if ($LASTEXITCODE -ne 0) { throw '视觉服务打包失败' }
} finally { Pop-Location }
