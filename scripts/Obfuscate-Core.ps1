param(
    [Parameter(Mandatory)][string]$Assembly,
    [Parameter(Mandatory)][string]$ToolPath,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$Assembly = [System.IO.Path]::GetFullPath($Assembly)
$ToolPath = [System.IO.Path]::GetFullPath($ToolPath)
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $Assembly -PathType Leaf) -or [System.IO.Path]::GetFileName($Assembly) -ne 'MouseControl.Core.dll') {
    throw 'A compiled MouseControl.Core.dll is required.'
}
if (-not (Test-Path -LiteralPath $ToolPath -PathType Leaf)) { throw 'Obfuscar executable is missing.' }

# Fresh staging prevents transformed assemblies from becoming subsequent inputs.
$runDirectory = Join-Path $OutputDirectory ('run-' + [Guid]::NewGuid().ToString('N'))
$inputDirectory = Join-Path $runDirectory 'input'
$resultDirectory = Join-Path $runDirectory 'output'
[System.IO.Directory]::CreateDirectory($inputDirectory) | Out-Null
[System.IO.Directory]::CreateDirectory($resultDirectory) | Out-Null
Copy-Item -LiteralPath $Assembly -Destination (Join-Path $inputDirectory 'MouseControl.Core.dll')

$configuration = New-Object System.Xml.XmlDocument
$root = $configuration.CreateElement('Obfuscator')
$configuration.AppendChild($root) | Out-Null
function Add-Variable([string]$name, [string]$value) {
    $variable = $configuration.CreateElement('Var')
    $variable.SetAttribute('name', $name)
    $variable.SetAttribute('value', $value)
    $root.AppendChild($variable) | Out-Null
}
Add-Variable 'InPath' $inputDirectory
Add-Variable 'OutPath' $resultDirectory
Add-Variable 'KeepPublicApi' 'true'
Add-Variable 'HidePrivateApi' 'true'
Add-Variable 'RenameProperties' 'false'
Add-Variable 'RenameEvents' 'false'
Add-Variable 'HideStrings' 'false'

$dotnetRoot = Split-Path -Parent (Get-Command dotnet -ErrorAction Stop).Source
$referencePacks = Join-Path $dotnetRoot 'packs/Microsoft.NETCore.App.Ref'
$referencePack = Get-ChildItem -LiteralPath $referencePacks -Directory |
    Where-Object { $_.Name -match '^10\.\d+\.\d+$' } |
    Sort-Object { [Version]$_.Name } -Descending | Select-Object -First 1
if ($null -eq $referencePack) { throw 'The .NET 10 reference pack is missing.' }
$search = $configuration.CreateElement('AssemblySearchPath')
$search.SetAttribute('path', (Join-Path $referencePack.FullName 'ref/net10.0'))
$root.AppendChild($search) | Out-Null
$module = $configuration.CreateElement('Module')
$module.SetAttribute('file', (Join-Path $inputDirectory 'MouseControl.Core.dll'))
$module.AppendChild($configuration.CreateElement('SkipEnums')) | Out-Null
$root.AppendChild($module) | Out-Null
$configurationPath = Join-Path $runDirectory 'obfuscar.xml'
$configuration.Save($configurationPath)

& $ToolPath $configurationPath
if ($LASTEXITCODE -ne 0) { throw 'Core obfuscation failed.' }
$transformed = Join-Path $resultDirectory 'MouseControl.Core.dll'
if (-not (Test-Path -LiteralPath $transformed -PathType Leaf)) { throw 'Transformed Core assembly is missing.' }
function Get-Digest([string]$path) {
    $stream = [System.IO.File]::OpenRead($path)
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
    finally { $algorithm.Dispose(); $stream.Dispose() }
}
$inputHash = Get-Digest $Assembly
$outputHash = Get-Digest $transformed
if ($inputHash -eq $outputHash) { throw 'Obfuscation did not transform the assembly.' }
$bundledAssembly = Join-Path $OutputDirectory 'MouseControl.Core.dll'
Copy-Item -LiteralPath $transformed -Destination $bundledAssembly -Force
@{
    tool = 'Obfuscar.GlobalTool 2.2.50'
    assembly = 'MouseControl.Core.dll'
    inputHash = $inputHash
    outputHash = $outputHash
    runDirectory = $runDirectory
    publicApiPreserved = $true
    stringHiding = $false
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'report.json') -Encoding UTF8
Write-Output "Obfuscated Core: $bundledAssembly"
