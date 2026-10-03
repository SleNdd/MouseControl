param([string]$OutputDirectory, [switch]$Plain)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repositoryRoot 'artifacts/MouseControl' }
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$project = Join-Path $repositoryRoot 'src/MouseControl.App/MouseControl.App.csproj'

$publishArguments = @('publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $OutputDirectory,
    '-p:DebugType=None', '-p:DebugSymbols=false', '--nologo')
if (-not $Plain) {
    $toolDirectory = Join-Path $repositoryRoot 'artifacts/tools/obfuscar/2.2.50'
    $obfuscar = Join-Path $toolDirectory 'obfuscar.console.exe'
    if (-not (Test-Path -LiteralPath $obfuscar -PathType Leaf)) {
        dotnet tool install Obfuscar.GlobalTool --tool-path $toolDirectory --version 2.2.50
        if ($LASTEXITCODE -ne 0) { throw 'Obfuscar installation failed.' }
    }
    $publishArguments += @('-p:ObfuscateCore=true', "-p:ObfuscarExecutable=$obfuscar")
}
dotnet @publishArguments
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$executable = Join-Path $OutputDirectory 'MouseControl.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'Published executable is missing.' }
Write-Output "Standalone executable: $executable"
Get-FileHash -LiteralPath $executable -Algorithm SHA256
