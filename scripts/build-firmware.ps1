param(
    [string]$ArduinoCli = 'arduino-cli',
    [string]$ConfigFile,
    [string]$OutputDirectory,
    [ValidatePattern('^0x[0-9A-Fa-f]{4}$')][string]$UsbVendorId,
    [ValidatePattern('^0x[0-9A-Fa-f]{4}$')][string]$UsbProductId,
    [ValidatePattern('^[A-Za-z0-9 ._-]{1,64}$')][string]$UsbManufacturer,
    [ValidatePattern('^[A-Za-z0-9 ._-]{1,64}$')][string]$UsbProductName
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repositoryRoot 'artifacts/firmware' }
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$buildDirectory = Join-Path $OutputDirectory '.build'
$cliArguments = @()
if ($ConfigFile) { $cliArguments += @('--config-file', [System.IO.Path]::GetFullPath($ConfigFile)) }
$cliArguments += @('compile', '--fqbn', 'arduino:avr:leonardo', '--build-path', $buildDirectory, '--clean', '--output-dir', $OutputDirectory)
if ($UsbVendorId) { $cliArguments += @('--build-property', "build.vid=$UsbVendorId") }
if ($UsbProductId) { $cliArguments += @('--build-property', "build.pid=$UsbProductId") }
if ($UsbManufacturer) { $cliArguments += @('--build-property', ('build.usb_manufacturer="' + $UsbManufacturer + '"')) }
if ($UsbProductName) { $cliArguments += @('--build-property', ('build.usb_product="' + $UsbProductName + '"')) }
$cliArguments += (Join-Path $repositoryRoot 'firmware/MouseControlLeonardo')
& $ArduinoCli @cliArguments
if ($LASTEXITCODE -ne 0) { throw 'Firmware compilation failed.' }
Write-Output "Firmware output: $OutputDirectory"
