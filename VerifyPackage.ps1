param([string]$Path)
$ErrorActionPreference = 'Stop'
if (-not $Path) {
    $taskVersion = (Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'Info.json') | ConvertFrom-Json).Version
    $Path = Join-Path $PSScriptRoot ('releases\DadsToolbox-' + $taskVersion + '-dev.zip')
}
Add-Type -AssemblyName System.IO.Compression
$taskExpected = @('Info.json', 'DadsToolbox.dll', 'README.md', 'ISSUE_AND_FIX.md', 'validation.json')
$taskArchive = [IO.Compression.ZipFile]::OpenRead($Path)
$taskChecked = @()
try {
    if ($taskArchive.Entries.Count -ne $taskExpected.Count) { throw 'Unexpected archive entry count.' }
    foreach ($taskName in $taskExpected) {
        $taskMatching = @($taskArchive.Entries | Where-Object FullName -CEQ ('DadsToolbox/' + $taskName))
        if ($taskMatching.Count -ne 1) { throw "Missing/duplicate archive entry: $taskName" }
        $taskInput = $taskMatching[0].Open()
        $taskHasher = [Security.Cryptography.SHA256]::Create()
        try { $taskHash = [BitConverter]::ToString($taskHasher.ComputeHash($taskInput)).Replace('-', '') }
        finally { $taskHasher.Dispose(); $taskInput.Dispose() }
        $taskStagedHash = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot ('package\DadsToolbox\' + $taskName))).Hash
        if ($taskHash -ne $taskStagedHash) { throw "Archive content mismatch: $taskName" }
        $taskChecked += [pscustomobject]@{ entry = $taskMatching[0].FullName; bytes = $taskMatching[0].Length; sha256 = $taskHash }
    }
} finally { $taskArchive.Dispose() }
$taskValidation = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'evidence\validation.json') | ConvertFrom-Json
$taskActualBinary = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'build\DadsToolbox.dll')).Hash
if ($taskActualBinary -ne $taskValidation.binarySha256) { throw 'Validation report does not describe the built DLL.' }
if ($taskValidation.failed -ne 0) { throw 'Validation reports failed checks.' }
$taskSummedPassed = 0
$taskSummedFailed = 0
foreach ($taskCheck in $taskValidation.checks) {
    $taskSummedPassed += [int]$taskCheck.passed
    $taskSummedFailed += [int]$taskCheck.failed
}
if ($taskValidation.passed -ne $taskSummedPassed -or $taskValidation.failed -ne $taskSummedFailed -or $taskValidation.checks.Count -ne 9) {
    throw 'Packaged validation must contain all nine suites and correct aggregate totals.'
}
if (($taskChecked | Where-Object entry -CEQ 'DadsToolbox/DadsToolbox.dll').sha256 -ne $taskActualBinary) { throw 'Packaged DLL differs from build.' }
$taskInfo = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'Info.json') | ConvertFrom-Json
if ($taskInfo.EntryMethod -ne 'DadsToolbox.Main.Load' -or $taskInfo.Id -ne 'DadsToolbox') { throw 'Unexpected UMM identity.' }
foreach ($taskOptional in @('SkinManagerMod','DoubleTrack','DVCustomCarLoader','HeadsUpDisplay_994','em_h6f_main','SteamSizePitch','DVCCLControlFix','DVRollingStockCompatibility','CCLSmokeFix','DoubleTrackSignClearance')) {
    if ($taskInfo.Requirements -contains $taskOptional) { throw "Optional dependency became a required dependency: $taskOptional" }
}
$taskModules = @('SkinDamageFix','SkinRegistrationFix','DoubleTrackSigns','HudGuard','CclControlFix','LocoSoundPitch','SmokeFix')
if ($taskValidation.version -ne $taskInfo.Version -or $taskValidation.modules.Count -ne $taskModules.Count -or
    @($taskModules | Where-Object { $_ -cnotin $taskValidation.modules }).Count -ne 0) { throw 'Validation does not describe all seven current internal modules.' }
foreach ($taskSuite in @('combined','hud','smoke','controls','sound')) {
    $taskChecks = @($taskValidation.checks | Where-Object id -CEQ $taskSuite)
    $taskReceiptPath = Join-Path $PSScriptRoot ('evidence\' + $taskSuite + '\receipt.json')
    if ($taskChecks.Count -ne 1 -or $taskChecks[0].failed -ne 0 -or $taskChecks[0].passed -le 0 -or
        $taskChecks[0].receiptSha256 -ne (Get-FileHash -LiteralPath $taskReceiptPath).Hash) {
        throw "Current passing $taskSuite receipt is missing from packaged validation."
    }
}
$taskReceipt = [ordered]@{
    passed = $true; singleRoot = 'DadsToolbox'; archiveEntriesReadAndHashed = $taskChecked
    packageSha256 = (Get-FileHash -LiteralPath $Path).Hash
    validatedBinarySha256 = $taskActualBinary
    redistributedDependencyDlls = $false
}
$taskReceipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'releases\archive-verification.json') -Encoding utf8
Write-Output 'Verified all five entries, single UMM root, validated DLL hash and optional dependency metadata.'

