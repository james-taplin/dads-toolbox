param(
    [string]$ToolboxDll,
    [string]$GamePath = 'B:\SteamLibrary\steamapps\common\Derail Valley',
    [string]$StandaloneDll = 'B:\SteamLibrary\steamapps\common\Derail Valley\Mods\DVLocoBogieVisuals\DVLocoBogieVisuals.dll',
    [string]$EditorPath = 'B:\Games\Unity 2019.4.40f1\Editor\Unity.exe',
    [int]$TimeoutSeconds = 240
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $ToolboxDll) { $ToolboxDll = Join-Path $taskRoot 'build\DadsToolbox.dll' }
foreach ($taskRequired in @($ToolboxDll, $StandaloneDll, $EditorPath)) {
    if (-not (Test-Path -LiteralPath $taskRequired)) { throw "Missing bogie fixture input: $taskRequired" }
}
if ((Get-FileHash -LiteralPath $StandaloneDll).Hash -ne '282F452670B3AFA2E238C08ED0E5B220CD2B7457280E2D2E876CD20A5DFED86B') {
    throw 'The bogie reference must be the preserved working standalone 0.4.2 DLL.'
}
$taskEvidence = Join-Path $taskRoot 'evidence\bogies'
$taskProject = Join-Path $PSScriptRoot 'UnityFixture'
New-Item -ItemType Directory -Force -Path $taskEvidence,(Join-Path $taskProject 'Assets\Editor'),(Join-Path $taskProject 'Packages'),(Join-Path $taskProject 'ProjectSettings') | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NativeBogieChecks.cs') -Destination (Join-Path $taskProject 'Assets\Editor\NativeBogieChecks.cs') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'tests\UnityFixture\Packages\manifest.json') -Destination (Join-Path $taskProject 'Packages\manifest.json') -Force
[IO.File]::WriteAllLines((Join-Path $taskProject 'ProjectSettings\ProjectVersion.txt'),@('m_EditorVersion: 2019.4.40f1','m_EditorVersionWithRevision: 2019.4.40f1 (ffc62b691db5)'))
$taskInitialHash = (Get-FileHash -LiteralPath $ToolboxDll).Hash
$env:TOOLBOX_BOGIE_OUT = [IO.Path]::GetFullPath($taskEvidence)
$env:TOOLBOX_GAME_PATH = [IO.Path]::GetFullPath($GamePath)
$env:TOOLBOX_FIXTURE_DLL = [IO.Path]::GetFullPath($ToolboxDll)
$env:TOOLBOX_BOGIE_STANDALONE = [IO.Path]::GetFullPath($StandaloneDll)
$taskVerdict = Join-Path $taskEvidence 'native-fixture-checks.txt'
$taskResult = Join-Path $taskEvidence 'result.json'
foreach ($taskPrevious in @($taskVerdict,$taskResult)) {
    if (Test-Path -LiteralPath $taskPrevious) { Move-Item -LiteralPath $taskPrevious -Destination ($taskPrevious + '.previous') -Force }
}
$taskLog = Join-Path $taskEvidence 'native-editor.log'
$taskArguments = @('-projectPath',('"' + $taskProject + '"'),'-executeMethod','NativeBogieChecks.Run','-logFile',('"' + $taskLog + '"'))
$taskProcess = Start-Process -FilePath $EditorPath -ArgumentList $taskArguments -WindowStyle Hidden -PassThru
$taskTimer = [Diagnostics.Stopwatch]::StartNew()
while (-not $taskProcess.WaitForExit(1000)) {
    if ($taskTimer.Elapsed.TotalSeconds -gt $TimeoutSeconds) { Stop-Process -Id $taskProcess.Id; throw "Native bogie fixture timed out: $taskLog" }
}
if (-not (Test-Path -LiteralPath $taskVerdict) -or -not (Test-Path -LiteralPath $taskResult)) { throw "Native bogie fixture did not produce a verdict: $taskLog" }
$taskChecks = Get-Content -LiteralPath $taskVerdict -Raw
$taskCounts = Get-Content -LiteralPath $taskResult -Raw | ConvertFrom-Json
Write-Output $taskChecks
if ($taskProcess.ExitCode -ne 0 -or $taskCounts.failed -ne 0 -or $taskCounts.passed -lt 35) { throw 'Native bogie fixture failed.' }
if ((Get-FileHash -LiteralPath $ToolboxDll).Hash -ne $taskInitialHash -or -not $taskChecks.Contains('Toolbox SHA256: ' + $taskInitialHash)) { throw 'Toolbox binary changed during bogie checks.' }
$taskInputs = @($PSCommandPath,(Join-Path $PSScriptRoot 'NativeBogieChecks.cs'),$StandaloneDll,$taskVerdict,$taskResult)
$taskInputs += Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -File | Where-Object { $_.Name -like '*Bogie*.cs' -or $_.Name -in @('Main.cs') } | Select-Object -ExpandProperty FullName
$taskInputs += @(Get-ChildItem -LiteralPath (Join-Path $GamePath 'DerailValley_Data\Managed') -File -Filter '*.dll' | Where-Object { $_.Name -in @('Assembly-CSharp.dll','DV.Utils.dll','DV.ThingTypes.dll','WorldStreamer.dll','UnityEngine.CoreModule.dll','UnityEngine.PhysicsModule.dll','UnityEngine.AnimationModule.dll') } | Select-Object -ExpandProperty FullName)
$taskInputs += @('UnityModManager.dll','0Harmony.dll') | ForEach-Object { Join-Path $GamePath ('DerailValley_Data\Managed\UnityModManager\' + $_) }
[ordered]@{
    toolbox = Get-FileHash -LiteralPath $ToolboxDll | Select-Object Path,Hash
    inputs = @($taskInputs | Sort-Object -Unique | ForEach-Object { Get-FileHash -LiteralPath $_ | Select-Object Path,Hash })
    verdict = Get-FileHash -LiteralPath $taskVerdict | Select-Object Path,Hash
    passed = [int]$taskCounts.passed; failed = [int]$taskCounts.failed; exitCode = 0
    scope = 'Exact combined DLL in isolated Unity Play Mode with real meshes, Renderers, rigidbodies, game Bogie/TrainCar and UMM. Standalone comparison covers filter constants/output and three controlled single/constrained/long-articulated visual patterns, warmed Present timings, proxy work units, source replacement and restoration. Actual enable/disable/destroy callbacks, synthetic layouts and explicit world/iterator inputs; only fixture-owned game startup callbacks suppressed. No live game/world packs, real player camera scheduling, native constraint solver scheduling, GPU draw, track physics, gameplay smoothing or performance acceptance.'
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskEvidence 'receipt.json') -Encoding utf8
