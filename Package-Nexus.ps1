param([string]$BaseZip, [string]$ReleaseSuffix = 'rc.4')
$ErrorActionPreference = 'Stop'
$taskVersion = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Info.json') -Raw | ConvertFrom-Json).Version + '-' + $ReleaseSuffix
if (-not $BaseZip) { $BaseZip = Join-Path $PSScriptRoot ('releases\DadsToolbox-' + $taskVersion + '.zip') }
$taskOutput = Join-Path $PSScriptRoot ('releases\DadsToolbox-' + $taskVersion + '-beta.zip')
if ([IO.Path]::GetFullPath($BaseZip) -eq [IO.Path]::GetFullPath($taskOutput)) { throw 'Preserve the original validated archive.' }
Add-Type -AssemblyName System.IO.Compression
$taskOriginal = @('Info.json','DadsToolbox.dll','README.md','ISSUE_AND_FIX.md','validation.json')
$taskBytes = @{}
$taskArchive = [IO.Compression.ZipFile]::OpenRead($BaseZip)
try {
    if ($taskArchive.Entries.Count -ne 5) { throw 'Expected the original five-file validated UMM archive.' }
    foreach ($taskName in $taskOriginal) {
        $taskEntry = @($taskArchive.Entries | Where-Object FullName -CEQ ('DadsToolbox/' + $taskName))
        if ($taskEntry.Count -ne 1) { throw "Unexpected base archive: $taskName" }
        $taskInput = $taskEntry[0].Open(); $taskMemory = [IO.MemoryStream]::new()
        try { $taskInput.CopyTo($taskMemory); $taskBytes[$taskName] = $taskMemory.ToArray() }
        finally { $taskInput.Dispose(); $taskMemory.Dispose() }
    }
} finally { $taskArchive.Dispose() }
function Get-ByteHash([byte[]]$Bytes) {
    $taskHasher = [Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($taskHasher.ComputeHash($Bytes)).Replace('-','') }
    finally { $taskHasher.Dispose() }
}
$taskValidation = [Text.Encoding]::UTF8.GetString($taskBytes['validation.json']).TrimStart([char]0xFEFF) | ConvertFrom-Json
if ($taskValidation.failed -ne 0 -or $taskValidation.binarySha256 -ne (Get-ByteHash $taskBytes['DadsToolbox.dll'])) { throw 'Base DLL differs from its passing validation.' }
$taskBytes['LICENSE.txt'] = [IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'LICENSE'))
$taskBytes['CREDITS.txt'] = [IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'CREDITS.txt'))
$taskStream = [IO.File]::Open($taskOutput,[IO.FileMode]::Create)
$taskZip = [IO.Compression.ZipArchive]::new($taskStream,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($taskName in ($taskOriginal + @('LICENSE.txt','CREDITS.txt'))) {
        $taskEntry = $taskZip.CreateEntry('DadsToolbox/' + $taskName,[IO.Compression.CompressionLevel]::Optimal)
        $taskEntry.LastWriteTime = [DateTimeOffset]::new(2026,10,30,0,0,0,[TimeSpan]::Zero)
        $taskDest = $taskEntry.Open()
        try { $taskDest.Write($taskBytes[$taskName],0,$taskBytes[$taskName].Length) } finally { $taskDest.Dispose() }
    }
} finally { $taskZip.Dispose(); $taskStream.Dispose() }
$taskCheck = [IO.Compression.ZipFile]::OpenRead($taskOutput)
try {
    if ($taskCheck.Entries.Count -ne 7) { throw 'Nexus archive must contain seven files.' }
    foreach ($taskEntry in $taskCheck.Entries) {
        $taskName = $taskEntry.FullName.Substring('DadsToolbox/'.Length)
        if (-not $taskEntry.FullName.StartsWith('DadsToolbox/') -or -not $taskBytes.ContainsKey($taskName)) { throw 'Unexpected archive entry.' }
        $taskMemory = [IO.MemoryStream]::new(); $taskInput = $taskEntry.Open()
        try { $taskInput.CopyTo($taskMemory); if ((Get-ByteHash $taskMemory.ToArray()) -ne (Get-ByteHash $taskBytes[$taskName])) { throw 'Archive verification failed.' } }
        finally { $taskMemory.Dispose(); $taskInput.Dispose() }
    }
} finally { $taskCheck.Dispose() }
[ordered]@{
    zip = $taskOutput; zipSha256 = (Get-FileHash -LiteralPath $taskOutput).Hash
    baseZip = $BaseZip; baseZipSha256 = (Get-FileHash -LiteralPath $BaseZip).Hash
    binarySha256 = $taskValidation.binarySha256; unchangedRuntimeFiles = $taskOriginal
    added = @('LICENSE.txt','CREDITS.txt'); entriesVerified = 7; published = $false
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'releases\nexus-beta-receipt.json') -Encoding utf8
Write-Output "Verified Nexus beta: $taskOutput (same validated DLL; MIT licence and credits added)."
