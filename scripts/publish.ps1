param([string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repositoryRoot 'artifacts/MouseControl' }
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$project = Join-Path $repositoryRoot 'src/MouseControl.App/MouseControl.App.csproj'

dotnet publish $project -c Release -r win-x64 --self-contained true -o $OutputDirectory `
    -p:DebugType=None -p:DebugSymbols=false --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$executable = Join-Path $OutputDirectory 'MouseControl.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'Published executable is missing.' }
Write-Output "Standalone executable: $executable"
Get-FileHash -LiteralPath $executable -Algorithm SHA256
