$ErrorActionPreference = 'Stop'
$taskBinary = Join-Path $PSScriptRoot 'build\DadsToolbox.dll'
$taskHash = (Get-FileHash -LiteralPath $taskBinary).Hash
$taskBuild = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'build\build-receipt.json') | ConvertFrom-Json
$taskManaged = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'evidence\managed\receipt.json') | ConvertFrom-Json
$taskNative = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'evidence\skin-damage\receipt.json') | ConvertFrom-Json
$taskSigns = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'evidence\sign-clearance\receipt.json') | ConvertFrom-Json
$taskRegistration = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'evidence\skin-startup\receipt.json') | ConvertFrom-Json
$taskInfo = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'Info.json') | ConvertFrom-Json
if ($taskBuild.outputSha256 -ne $taskHash -or $taskManaged.toolbox.Hash -ne $taskHash -or
    $taskNative.inputs[0].Hash -ne $taskHash -or $taskSigns.inputs[0].Hash -ne $taskHash -or $taskRegistration.inputs[0].Hash -ne $taskHash -or
    $taskManaged.exitCode -ne 0 -or $taskNative.failures -ne 0 -or $taskSigns.failures -ne 0 -or $taskRegistration.failures -ne 0) {
    throw 'Passing build and all four fixture receipts must describe the current DLL.'
}
foreach ($taskInput in @($taskBuild.sources) + @($taskBuild.references) + @($taskManaged.sources) + @($taskManaged.runner) + @($taskManaged.optionalMods) + @($taskManaged.umm) + @($taskManaged.harmony) + @($taskNative.inputs) + @($taskSigns.inputs) + @($taskRegistration.inputs)) {
    if ((Get-FileHash -LiteralPath $taskInput.Path).Hash -ne $taskInput.Hash) { throw "Validation input changed: $($taskInput.Path)" }
}
$taskManagedText = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'evidence\managed\results\checks.txt')
if ($taskManagedText -notmatch 'RESULT: (\d+) passed, 0 failed\.') { throw 'Managed verdict is not passing.' }
$taskManagedPassed = [int]$Matches[1]
$taskNativeText = Get-Content -Raw -LiteralPath $taskNative.verdict
if ($taskNativeText -notmatch 'Checks: (\d+); failures: 0') { throw 'Native verdict is not passing.' }
$taskNativePassed = [int]$Matches[1]
if ($taskNativePassed -ne $taskNative.checks) { throw 'Native count differs from receipt.' }
$taskSignsText = Get-Content -Raw -LiteralPath $taskSigns.verdict
if ($taskSignsText -notmatch 'Checks: (\d+); failures: 0') { throw 'Native sign verdict is not passing.' }
$taskSignsPassed = [int]$Matches[1]
if ($taskSignsPassed -ne $taskSigns.checks) { throw 'Native sign count differs from receipt.' }
$taskRegistrationText = Get-Content -Raw -LiteralPath $taskRegistration.verdict
if ($taskRegistrationText -notmatch 'Checks: (\d+); failures: 0') { throw 'Native registration verdict is not passing.' }
$taskRegistrationPassed = [int]$Matches[1]
if ($taskRegistrationPassed -ne $taskRegistration.checks) { throw 'Native registration count differs from receipt.' }
foreach ($taskReceipt in @($taskNative,$taskSigns,$taskRegistration)) {
    if ((Get-FileHash -LiteralPath $taskReceipt.verdict).Hash -ne $taskReceipt.verdictSha256) { throw 'Native verdict changed after receipt.' }
}
$taskAdditional = @()
foreach ($taskSuite in @('combined','hud','smoke','controls','sound')) {
    $taskReceiptPath = Join-Path $PSScriptRoot ('evidence\' + $taskSuite + '\receipt.json')
    $taskReceipt = Get-Content -Raw -LiteralPath $taskReceiptPath | ConvertFrom-Json
    if ($taskReceipt.toolbox.Hash -ne $taskHash -or $taskReceipt.exitCode -ne 0 -or
        $taskReceipt.failed -ne 0 -or $taskReceipt.passed -le 0) {
        throw "Passing $taskSuite receipt must describe the current combined DLL."
    }
    foreach ($taskInput in @($taskReceipt.inputs) + @($taskReceipt.toolbox) + @($taskReceipt.verdict)) {
        if (-not $taskInput.Path -or -not $taskInput.Hash -or
            (Get-FileHash -LiteralPath $taskInput.Path).Hash -ne $taskInput.Hash) {
            throw "$taskSuite validation input or verdict changed: $($taskInput.Path)"
        }
    }
    $taskText = Get-Content -Raw -LiteralPath $taskReceipt.verdict.Path
    if ($taskSuite -eq 'sound') {
        if ($taskText -notmatch 'Checks: (\d+); failures: 0') { throw 'Sound native verdict is not passing.' }
        $taskNativeSoundPassed = [int]$Matches[1]
        if ($taskText -notmatch ('Toolbox SHA256: ' + $taskHash)) { throw 'Sound native verdict does not identify current DLL.' }
        $taskRulePath = Join-Path $PSScriptRoot 'evidence\sound\rules-checks.txt'
        if ($taskRulePath -notin @($taskReceipt.inputs.Path)) { throw 'Sound rules verdict is not covered by receipt input hashes.' }
        $taskRulesText = Get-Content -Raw -LiteralPath $taskRulePath
        if ($taskRulesText -notmatch 'PASS: (\d+) rule and boundary checks\.') { throw 'Sound rules verdict is not passing.' }
        if ($taskNativeSoundPassed + [int]$Matches[1] -ne $taskReceipt.passed) { throw 'Sound aggregate count differs from source/native verdicts.' }
    } elseif ($taskSuite -eq 'controls') {
        $taskControlCounts = $taskText | ConvertFrom-Json
        if ($taskControlCounts.failed -ne 0 -or $taskControlCounts.passed -ne $taskReceipt.passed -or
            $taskControlCounts.passed -ne ($taskControlCounts.sourceRegression + $taskControlCounts.arithmetic + $taskControlCounts.lifecycle)) {
            throw 'Controls aggregate verdict is not passing or counts differ.'
        }
        $taskRegressionPath = Join-Path $PSScriptRoot 'evidence\controls\source-regression.log'
        $taskMathPath = Join-Path $PSScriptRoot 'evidence\controls\math.log'
        $taskLifecyclePath = Join-Path $PSScriptRoot 'evidence\controls\lifecycle-result.json'
        foreach ($taskPath in @($taskRegressionPath,$taskMathPath,$taskLifecyclePath)) {
            if ($taskPath -notin @($taskReceipt.inputs.Path)) { throw 'Controls sub-verdict is not covered by receipt input hashes.' }
        }
        $taskRegressionText = Get-Content -Raw -LiteralPath $taskRegressionPath
        if ($taskRegressionText -notmatch 'SUMMARY (\d+) passed, 0 failed' -or [int]$Matches[1] -ne $taskControlCounts.sourceRegression) { throw 'Controls source regression verdict differs.' }
        $taskMathText = Get-Content -Raw -LiteralPath $taskMathPath
        if ($taskMathText -notmatch '(\d+) checks passed in total\.' -or [int]$Matches[1] -ne $taskControlCounts.arithmetic) { throw 'Controls arithmetic verdict differs.' }
        $taskLifecycle = Get-Content -Raw -LiteralPath $taskLifecyclePath | ConvertFrom-Json
        if ($taskLifecycle.failed -ne 0 -or $taskLifecycle.passed -ne $taskControlCounts.lifecycle) { throw 'Controls native lifecycle verdict differs.' }
    } elseif ($taskText -notmatch 'RESULT: (\d+) passed, 0 failed\.' -or [int]$Matches[1] -ne $taskReceipt.passed) {
        throw "$taskSuite passing verdict count does not match receipt."
    }
    $taskAdditional += [ordered]@{
        id = $taskSuite; passed = [int]$taskReceipt.passed; failed = 0; scope = $taskReceipt.scope
        evidence = 'evidence/' + $taskSuite + '/' + [IO.Path]::GetFileName($taskReceipt.verdict.Path)
        receiptSha256 = (Get-FileHash -LiteralPath $taskReceiptPath).Hash
    }
}
$taskAdditionalPassed = 0
foreach ($taskCheck in $taskAdditional) { $taskAdditionalPassed += [int]$taskCheck['passed'] }
$taskValidation = [ordered]@{
    date = '2026-10-16'; package = 'DadsToolbox'; version = $taskInfo.Version; status = 'development candidate; isolated integration verified; live-world acceptance pending'
    binarySha256 = $taskHash; passed = $taskManagedPassed + $taskNativePassed + $taskSignsPassed + $taskRegistrationPassed + $taskAdditionalPassed; failed = 0
    modules = @('SkinDamageFix','SkinRegistrationFix','DoubleTrackSigns','HudGuard','CclControlFix','LocoSoundPitch','SmokeFix')
    features = @('HUD Guard','CCL Control Fix','Loco Sound Pitch','Skin Manager Fix','CCL Smoke Fix','DoubleTrack sign clearance')
    supportedOptionalDependencies = @([ordered]@{
        id = 'SkinManagerMod'; ummVersion = '4.4.1'; assemblyVersion = '4.2.0.0'
        sha256 = ($taskNative.inputs | Where-Object { $_.Path -like '*\SkinManagerMod\SkinManagerMod.dll' }).Hash
    }, [ordered]@{ id = 'DoubleTrack'; ummVersion = '2.1.1'; sha256 = ($taskSigns.inputs | Where-Object { $_.Path -like '*\DoubleTrack\DoubleTrack.dll' }).Hash },
    [ordered]@{ id = 'DVCustomCarLoader'; ummVersion = '3.1.9'; requiredFor = 'Embedded skin registration and CCL controls'; sha256 = ($taskRegistration.inputs | Where-Object { $_.Path -like '*\DVCustomCarLoader\CCL.Importer.dll' }).Hash },
    [ordered]@{ id = 'HeadsUpDisplay_994'; ummVersion = '1.1.2'; requiredFor = 'HUD Guard' })
    checks = @(
        [ordered]@{ passed = $taskManagedPassed; failed = 0; scope = $taskManaged.scope; evidence = 'evidence/managed/results/checks.txt' },
        [ordered]@{ passed = $taskNativePassed; failed = 0; unityVersion = $taskNative.unity; scope = $taskNative.scope; evidence = 'evidence/skin-damage/native-fixture-checks.txt' },
        [ordered]@{ passed = $taskSignsPassed; failed = 0; unityVersion = $taskSigns.unity; scope = $taskSigns.scope; evidence = 'evidence/sign-clearance/native-fixture-checks.txt' },
        [ordered]@{ passed = $taskRegistrationPassed; failed = 0; unityVersion = $taskRegistration.unity; scope = $taskRegistration.scope; evidence = 'evidence/skin-startup/native-fixture-checks.txt' }
    ) + $taskAdditional
    sourceHashes = @($taskBuild.sources | ForEach-Object { [ordered]@{ path = 'src/' + [IO.Path]::GetFileName($_.Path); sha256 = $_.Hash } })
    reproduced = 'Skin Manager scorch overwrite; six embedded skin configs rejected before controlled CCL registration and automatically discovered after it with the fix; native DoubleTrack sign transform corrections.'
    preserved = @('Native exploded flag and thresholds', 'Normal healthy paint application', 'Changed-while-disabled theme selection', 'Independent vehicle damage flags', 'Other Harmony patch owners', 'Sign height and facing', 'Unlisted objects', 'Desired options across dependencies/conflicts', 'Independent module lifecycle')
    remaining = @('Full clean UMM/game boot with real embedded CCL pack and no companion', 'Actual CCL bundle import and CCL 3.1.11 runtime acceptance', 'Real HUD force/power/slip and Unity speed acquisition', 'Rendered native smoke appearance', 'Live CCL controls including both Leader cabs/Rewired/focus/physics and releases', 'Listening acceptance of steam/non-steam sound, pooling, custom audio/cab exclusions and performance', 'Cold migration from existing standalone fixes with original settings preserved', 'External 4K skin transition and SM-disabled gameplay', 'DoubleTrack world-generation coroutine and real map traversal', 'Physical explosion and complete TrainCarExplosion orchestration', 'Real vehicle interior/exterior streaming', 'SaveManager save/reload', '5AT-specific actual appearance', 'Live repair UI and GUI')
    historicalAttempts = 'Initial licence/fixture-setup failures are retained separately; the final native fixture passes. Original 5AT project fixture was not rerun with this patch.'
    installed = $false; gameLaunched = $false; publiclyUploaded = $false
}
$taskSummedPassed = 0
$taskSummedFailed = 0
foreach ($taskCheck in $taskValidation['checks']) {
    $taskSummedPassed += [int]$taskCheck['passed']
    $taskSummedFailed += [int]$taskCheck['failed']
}
if ($taskValidation['passed'] -ne $taskSummedPassed -or $taskValidation['failed'] -ne $taskSummedFailed) {
    throw 'Aggregate validation totals must equal the sum of every verified suite.'
}
$taskValidation | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'evidence\validation.json') -Encoding utf8
Write-Output "Recorded $($taskValidation.passed) passing checks for DLL $taskHash"

