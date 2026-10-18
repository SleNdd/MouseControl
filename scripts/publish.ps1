param([string]$OutputDirectory, [switch]$Plain)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repositoryRoot 'artifacts/MouseControl' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$launcherStaging = Join-Path $repositoryRoot 'artifacts/launcher'
$runDirectory = Join-Path $launcherStaging ('publish-' + [guid]::NewGuid().ToString('N'))
$payloadDirectory = Join-Path $runDirectory 'app'
$project = Join-Path $repositoryRoot 'src/MouseControl.App/MouseControl.App.csproj'

dotnet publish $project -c Release -r win-x64 --self-contained true -o $payloadDirectory -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false --nologo
if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }

if (-not $Plain) {
    $toolDirectory = Join-Path $repositoryRoot 'artifacts/tools/obfuscar/2.2.50'
    $obfuscar = Join-Path $toolDirectory 'obfuscar.console.exe'
    if (-not (Test-Path -LiteralPath $obfuscar -PathType Leaf)) {
        dotnet tool install Obfuscar.GlobalTool --tool-path $toolDirectory --version 2.2.50
        if ($LASTEXITCODE -ne 0) { throw 'Obfuscar installation failed.' }
    }
    $protectedDirectory = Join-Path $runDirectory 'protected-core'
    & (Join-Path $PSScriptRoot 'Obfuscate-Core.ps1') -Assembly (Join-Path $payloadDirectory 'MouseControl.Core.dll') -ToolPath $obfuscar -OutputDirectory $protectedDirectory
    Copy-Item -LiteralPath (Join-Path $protectedDirectory 'MouseControl.Core.dll') -Destination (Join-Path $payloadDirectory 'MouseControl.Core.dll') -Force
    # Keep the previous documented artifact-verification location available.
    $verificationDirectory = Join-Path $repositoryRoot 'src/MouseControl.App/obj/Release/net10.0-windows10.0.19041.0/win-x64/obfuscated-core'
    [IO.Directory]::CreateDirectory($verificationDirectory) | Out-Null
    Copy-Item -LiteralPath (Join-Path $protectedDirectory 'MouseControl.Core.dll') -Destination (Join-Path $verificationDirectory 'MouseControl.Core.dll') -Force
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$payloadArchive = Join-Path $launcherStaging 'app.zip'
$tempArchive = Join-Path $runDirectory 'app.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($payloadDirectory, $tempArchive, [IO.Compression.CompressionLevel]::Optimal, $false)
Move-Item -LiteralPath $tempArchive -Destination $payloadArchive -Force

dotnet publish (Join-Path $repositoryRoot 'src/MouseControl.Launcher/MouseControl.Launcher.csproj') -c Release -r win-x64 --self-contained true -o $OutputDirectory -p:DebugType=None -p:DebugSymbols=false --nologo
if ($LASTEXITCODE -ne 0) { throw 'Launcher publish failed.' }
$executable = Join-Path $OutputDirectory 'MouseControl.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'Published executable is missing.' }
Write-Output "Standalone executable: $executable"
Get-FileHash -LiteralPath $executable -Algorithm SHA256
