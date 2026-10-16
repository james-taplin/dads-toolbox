param([string]$GamePath = 'B:\SteamLibrary\steamapps\common\Derail Valley')
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Build.ps1') -GamePath $GamePath
$taskValidation = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'evidence\validation.json') | ConvertFrom-Json
$taskBinaryHash = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'build\DadsToolbox.dll')).Hash
if ($taskValidation.binarySha256 -ne $taskBinaryHash -or $taskValidation.failed -ne 0) {
    throw 'Successful validation of this exact DLL is required before packaging.'
}
$taskPackageRoot = Join-Path $PSScriptRoot 'package\DadsToolbox'
$taskReleaseRoot = Join-Path $PSScriptRoot 'releases'
New-Item -ItemType Directory -Path $taskPackageRoot, $taskReleaseRoot -Force | Out-Null
$taskInfo = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'Info.json') | ConvertFrom-Json
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Info.json') -Destination $taskPackageRoot -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'build\DadsToolbox.dll') -Destination $taskPackageRoot -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\README.md') -Destination $taskPackageRoot -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\ISSUE_AND_FIX.md') -Destination $taskPackageRoot -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'evidence\validation.json') -Destination $taskPackageRoot -Force

# Explicit file list prevents a stale staging file or dependency assembly from
# entering a later package. UMM requires exactly one first-level mod directory.
$taskPackageFiles = @('Info.json', 'DadsToolbox.dll', 'README.md', 'ISSUE_AND_FIX.md', 'validation.json')
$taskZip = Join-Path $taskReleaseRoot ("DadsToolbox-" + $taskInfo.Version + '-dev.zip')
Add-Type -AssemblyName System.IO.Compression
$taskStream = [IO.File]::Open($taskZip, [IO.FileMode]::Create)
$taskArchive = [IO.Compression.ZipArchive]::new($taskStream, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($taskName in $taskPackageFiles) {
        $taskZipEntry = $taskArchive.CreateEntry('DadsToolbox/' + $taskName)
        $taskZipEntry.LastWriteTime = [DateTimeOffset]::new(2026, 10, 16, 0, 0, 0, [TimeSpan]::Zero)
        $taskInput = [IO.File]::OpenRead((Join-Path $taskPackageRoot $taskName))
        $taskOutput = $taskZipEntry.Open()
        try { $taskInput.CopyTo($taskOutput) } finally { $taskInput.Dispose(); $taskOutput.Dispose() }
    }
} finally { $taskArchive.Dispose(); $taskStream.Dispose() }

$taskReceipt = [ordered]@{
    version = $taskInfo.Version; status = 'development candidate; in-game acceptance pending'
    package = $taskZip; packageSha256 = (Get-FileHash -LiteralPath $taskZip).Hash
    files = @($taskPackageFiles | ForEach-Object { Get-FileHash -LiteralPath (Join-Path $taskPackageRoot $_) | Select-Object Path, Hash })
    installed = $false; gameLaunched = $false; publiclyUploaded = $false
}
$taskReceipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskReleaseRoot 'package-receipt.json') -Encoding utf8
& (Join-Path $PSScriptRoot 'VerifyPackage.ps1') -Path $taskZip
Write-Output "Packaged $taskZip"
