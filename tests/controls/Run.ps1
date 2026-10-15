param(
    [string]$GamePath = 'B:\SteamLibrary\steamapps\common\Derail Valley',
    [string]$ToolboxDll
)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$taskEvidence=Join-Path $taskRoot 'evidence\controls'
New-Item -ItemType Directory -Force -Path $taskEvidence | Out-Null
if(-not $ToolboxDll){$ToolboxDll=Join-Path $taskRoot 'build\DadsToolbox.dll'}
$taskManaged=Join-Path $GamePath 'DerailValley_Data\Managed'
$taskDotnet='C:\Program Files\dotnet\dotnet.exe'
$taskCsc='C:\Program Files\dotnet\sdk\10.0.401\Roslyn\bincore\csc.dll'
$taskInputs=@($PSCommandPath,(Join-Path $PSScriptRoot 'RunNative.ps1'),(Join-Path $PSScriptRoot 'NativeControlsChecks.cs'),(Join-Path $PSScriptRoot 'LifecycleChecks.cs'),(Join-Path $PSScriptRoot 'RegressionProgram.cs'),(Join-Path $PSScriptRoot 'RegressionStubs.cs'),(Join-Path $PSScriptRoot 'RegressionSwitchStub.cs'),(Join-Path $PSScriptRoot 'Program.cs'),(Join-Path $PSScriptRoot 'RegressionChecks.csproj'),(Join-Path $PSScriptRoot 'MathChecks.csproj'),(Join-Path $PSScriptRoot 'Directory.Build.props'))
$taskInputs+=Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Filter 'Controls*.cs' -File | Select-Object -ExpandProperty FullName
$taskAuditRoot='C:\Users\james\Desktop\Derail Valley Mods\Mods (code)\DVToolbox_Audit_2026-10-13\controls\native'
$taskInputs+=Join-Path $taskAuditRoot 'DV.KeyboardInput.MouseScrollKeyboardInput.cs'
$taskInputs+=Join-Path $taskAuditRoot 'DV.KeyboardInput.ToggleValueKeyboardInput.cs'
$taskReferences=@('mscorlib.dll','System.dll','System.Core.dll','Assembly-CSharp.dll','DV.UI.dll','DV.ThingTypes.dll','DV.Interaction.dll','DV.CabControls.Spec.dll','DV.Simulation.dll','Rewired_Core.dll','UnityEngine.CoreModule.dll','UnityEngine.PhysicsModule.dll','UnityEngine.JSONSerializeModule.dll','UnityModManager\0Harmony.dll','UnityModManager\UnityModManager.dll')
$taskInputs+=$taskReferences | ForEach-Object{Join-Path $taskManaged $_}
$taskInputs+=$taskCsc
$taskCanonical='C:\Users\james\Desktop\Derail Valley Mods\Mods (code)\DVCCLControlFix_Development_2026-10-14'
$taskInputs+=@('Main.cs','Connections.cs','ConnectionMath.cs','dist\DVCCLControlFix\DVCCLControlFix.dll') | ForEach-Object {Join-Path $taskCanonical $_}

$taskRegressionLog=Join-Path $taskEvidence 'source-regression.log'
& $taskDotnet run --project (Join-Path $PSScriptRoot 'RegressionChecks.csproj') -p:NuGetAudit=false -p:RestoreSources= 2>&1 | Tee-Object -FilePath $taskRegressionLog
if($LASTEXITCODE -ne 0){throw 'Adapted production-source control regressions failed.'}
$taskMathLog=Join-Path $taskEvidence 'math.log'
& $taskDotnet run --project (Join-Path $PSScriptRoot 'MathChecks.csproj') -p:NuGetAudit=false -p:RestoreSources= 2>&1 | Tee-Object -FilePath $taskMathLog
if($LASTEXITCODE -ne 0){throw 'Original23 arithmetic/key-plan checks failed.'}

$taskExe=Join-Path $taskEvidence 'ControlsLifecycleChecks.exe'
$taskArgs=@('/nologo','/nostdlib+','/target:exe','/optimize+','/langversion:latest',('/out:"'+$taskExe+'"'),('/reference:"'+$ToolboxDll+'"'))
foreach($taskReference in $taskReferences){$taskArgs+='/reference:"'+(Join-Path $taskManaged $taskReference)+'"'}
$taskArgs+='"'+(Join-Path $PSScriptRoot 'LifecycleChecks.cs')+'"'
$taskRsp=Join-Path $taskEvidence 'lifecycle.rsp'
[IO.File]::WriteAllLines($taskRsp,$taskArgs,[Text.UTF8Encoding]::new($false))
& $taskDotnet $taskCsc /noconfig "@$taskRsp" 2>&1 | Tee-Object -FilePath (Join-Path $taskEvidence 'lifecycle-build.log')
if($LASTEXITCODE -ne 0){throw 'Controls lifecycle fixture compilation failed.'}
[IO.File]::WriteAllText(($taskExe+'.config'),'<?xml version="1.0"?><configuration><runtime><loadFromRemoteSources enabled="true" /></runtime></configuration>')
& (Join-Path $PSScriptRoot 'RunNative.ps1') -GamePath $GamePath -ToolboxDll $ToolboxDll -FixtureExe $taskExe
$taskExit=0
$taskRegressionSummary=[regex]::Match((Get-Content -LiteralPath $taskRegressionLog -Raw),'SUMMARY (\d+) passed, (\d+) failed')
if(-not $taskRegressionSummary.Success){throw 'Controls regression verdict missing.'}
if(-not ((Get-Content -LiteralPath $taskMathLog -Raw).Contains('23 checks passed in total.'))){throw 'Original23 verdict missing.'}
$taskLifecycle=Get-Content -LiteralPath (Join-Path $taskEvidence 'lifecycle-result.json') -Raw | ConvertFrom-Json
$taskPassed=[int]$taskRegressionSummary.Groups[1].Value+23+[int]$taskLifecycle.passed
$taskFailed=[int]$taskRegressionSummary.Groups[2].Value+[int]$taskLifecycle.failed
$taskVerdict=Join-Path $taskEvidence 'result.json'
[ordered]@{passed=$taskPassed;failed=$taskFailed;sourceRegression=[int]$taskRegressionSummary.Groups[1].Value;arithmetic=23;lifecycle=[int]$taskLifecycle.passed} | ConvertTo-Json | Set-Content -LiteralPath $taskVerdict -Encoding utf8
$taskInputs+=@($taskRegressionLog,$taskMathLog,(Join-Path $taskEvidence 'lifecycle-checks.txt'),(Join-Path $taskEvidence 'lifecycle-result.json'),(Join-Path $taskEvidence 'clone-baseline.txt'))
$taskInputs+=Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'UnityFixture\Assets\Editor') -Filter '*.cs' -File | Select-Object -ExpandProperty FullName
$taskInputs+=Join-Path $PSScriptRoot 'UnityFixture\ProjectSettings\ProjectVersion.txt'
$taskInputs+=Join-Path $PSScriptRoot 'UnityFixture\Packages\manifest.json'
[ordered]@{
    toolbox=Get-FileHash -LiteralPath $ToolboxDll | Select-Object Path,Hash
    inputs=@($taskInputs | Sort-Object -Unique | ForEach-Object {Get-FileHash -LiteralPath $_ | Select-Object Path,Hash})
    verdict=Get-FileHash -LiteralPath $taskVerdict | Select-Object Path,Hash
    passed=$taskPassed;failed=$taskFailed;exitCode=$taskExit
    scope='Actual adapted source regressions using explicit managed Unity/control/input stand-ins and audited native input method excerpts; original23 pure math/key-plan checks; exact DLL lifecycle with actual installed UMM/Harmony/native method targets and inactive synthetic Unity component/shared-port/authored-input/scheduler reference remapping. ActionReference fields are omitted identically by native authored input, original standalone candidate and merged component in this fixture, so their cloning remains unavailable/unverified. Live native controls, Rewired focus, physics, real reach, real locomotive prefabs, save/load and gameplay not executed.'
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskEvidence 'receipt.json') -Encoding utf8
Write-Output "CONTROLS: $taskPassed passed, $taskFailed failed."
