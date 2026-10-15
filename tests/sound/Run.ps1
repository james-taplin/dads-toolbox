param(
    [string]$ToolboxDll,
    [string]$GamePath = 'B:\SteamLibrary\steamapps\common\Derail Valley',
    [string]$EditorPath = 'B:\Games\Unity 2019.4.40f1\Editor\Unity.exe',
    [switch]$RulesOnly,
    [int]$TimeoutSeconds = 240
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $ToolboxDll) { $ToolboxDll = Join-Path $taskRoot 'build\DadsToolbox.dll' }
$taskEvidence = Join-Path $taskRoot 'evidence\sound'
New-Item -ItemType Directory -Force -Path $taskEvidence | Out-Null
$taskManaged = Join-Path $GamePath 'DerailValley_Data\Managed'
$taskSdk = (& 'C:\Program Files\dotnet\dotnet.exe' --version).Trim()
$taskCompiler = "C:\Program Files\dotnet\sdk\$taskSdk\Roslyn\bincore\csc.dll"
$taskRulesExe = Join-Path $PSScriptRoot 'RulesTests.exe'
$taskArgs = @('/nologo', '/nostdlib+', '/target:exe', '/langversion:latest', "/out:`"$taskRulesExe`"")
foreach ($taskRef in @('mscorlib.dll','System.dll','System.Core.dll')) {
    $taskArgs += '/reference:"' + (Join-Path $taskManaged $taskRef) + '"'
}
$taskArgs += '"' + (Join-Path $taskRoot 'src\SoundPitchRules.cs') + '"'
$taskArgs += '"' + (Join-Path $PSScriptRoot 'RulesTests.cs') + '"'
$taskRsp = Join-Path $PSScriptRoot 'rules.rsp'
[IO.File]::WriteAllLines($taskRsp, $taskArgs)
& 'C:\Program Files\dotnet\dotnet.exe' $taskCompiler /noconfig "@$taskRsp"
if ($LASTEXITCODE -ne 0) { throw 'Sound rules compilation failed.' }
$taskRules = & $taskRulesExe 2>&1
$taskRulesCode = $LASTEXITCODE
$taskRules | Set-Content -LiteralPath (Join-Path $taskEvidence 'rules-checks.txt') -Encoding utf8
Write-Output $taskRules
if ($taskRulesCode -ne 0) { throw 'Sound rules checks failed.' }
if ($RulesOnly) { return }
if (-not (Test-Path -LiteralPath $ToolboxDll)) { throw 'Compile the current Toolbox DLL before native sound checks.' }
$taskInitialDllHash = (Get-FileHash -LiteralPath $ToolboxDll).Hash
if (-not (Test-Path -LiteralPath $EditorPath)) { throw 'Unity 2019 Editor is required for native audio checks.' }
$taskProject = Join-Path $PSScriptRoot 'UnityFixture'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NativeSoundChecks.cs') -Destination (Join-Path $taskProject 'Assets\Editor\NativeSoundChecks.cs') -Force
$env:TOOLBOX_SOUND_OUT = $taskEvidence
$env:TOOLBOX_GAME_PATH = [IO.Path]::GetFullPath($GamePath)
$env:TOOLBOX_FIXTURE_DLL = [IO.Path]::GetFullPath($ToolboxDll)
$taskLog = Join-Path $taskEvidence 'unity-windowed.log'
$taskVerdict = Join-Path $taskEvidence 'native-fixture-checks.txt'
if (Test-Path -LiteralPath $taskVerdict) { Move-Item -LiteralPath $taskVerdict -Destination ($taskVerdict + '.previous') -Force }
$taskFixtureArgs = @('-projectPath', ('"' + $taskProject + '"'), '-executeMethod', 'NativeSoundChecks.Run', '-logFile', ('"' + $taskLog + '"'))
$taskProcess = Start-Process -FilePath $EditorPath -ArgumentList $taskFixtureArgs -WindowStyle Hidden -PassThru
$taskTimer = [Diagnostics.Stopwatch]::StartNew()
while (-not $taskProcess.WaitForExit(1000)) {
    if ($taskTimer.Elapsed.TotalSeconds -gt $TimeoutSeconds) {
        Stop-Process -Id $taskProcess.Id
        throw "Isolated sound Editor timed out. See $taskLog"
    }
}
if (-not (Test-Path -LiteralPath $taskVerdict)) { throw "Unity did not produce a sound verdict. See $taskLog" }
$taskChecks = Get-Content -LiteralPath $taskVerdict -Raw
Write-Output $taskChecks
if ($taskProcess.ExitCode -ne 0 -or $taskChecks -notmatch 'Checks: (\d+); failures: 0') { throw 'Native sound checks failed.' }
$taskNativeCount = [int]$Matches[1]
if ((Get-FileHash -LiteralPath $ToolboxDll).Hash -ne $taskInitialDllHash -or $taskChecks -notmatch ('Toolbox SHA256: ' + $taskInitialDllHash)) {
    throw 'Toolbox binary changed during the fixture; rerun all sound checks against the final binary.'
}
$taskRulesText = $taskRules -join "`n"
if ($taskRulesText -notmatch '(\d+)') { throw 'Rules check count missing.' }
$taskRuleCount = [int]$Matches[1]
$taskInputs = @((Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Filter 'Sound*.cs' -File).FullName)
$taskInputs += Join-Path $PSScriptRoot 'NativeSoundChecks.cs'
$taskInputs += Join-Path $PSScriptRoot 'RulesTests.cs'
$taskInputs += Join-Path $PSScriptRoot 'Run.ps1'
$taskInputs += Join-Path $taskEvidence 'rules-checks.txt'
$taskInputs += @(Get-ChildItem -LiteralPath $taskManaged -Filter '*.dll' -File | Where-Object { $_.Name -in @('Assembly-CSharp.dll','DV.ThingTypes.dll','DV.Utils.dll','DV.Simulation.dll','DV.LayeredAudio.dll','DV.NAudio.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.AudioModule.dll') } | Select-Object -ExpandProperty FullName)
$taskInputs += Join-Path $taskManaged 'UnityModManager\0Harmony.dll'
$taskInputs += Join-Path $taskManaged 'UnityModManager\UnityModManager.dll'
$taskReceipt = [ordered]@{
    toolbox = Get-FileHash -LiteralPath $ToolboxDll | Select-Object Path,Hash
    inputs = @($taskInputs | ForEach-Object { Get-FileHash -LiteralPath $_ | Select-Object Path,Hash })
    verdict = Get-FileHash -LiteralPath $taskVerdict | Select-Object Path,Hash
    passed = $taskNativeCount + $taskRuleCount
    failed = 0
    exitCode = 0
    scope = 'Source-linked preserved rules plus exact-DLL native Unity audio hook, ownership and lifecycle fixtures. Seeded catalogue and controlled inactive car. No game boot, discovery, audible playback or performance acceptance.'
}
$taskReceipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskEvidence 'receipt.json') -Encoding utf8
