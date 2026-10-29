param(
    [string]$ToolboxDll,
    [string]$GamePath='B:\SteamLibrary\steamapps\common\Derail Valley',
    [string]$ChuffReferenceRoot='C:\Users\james\Desktop\Derail Valley Mods\Leader\plugin\MultiCylinderChuffFix',
    [string]$NativeSourcePath='C:\Users\james\Desktop\Derail Valley Mods\5AT\reference\audio098\native_contract\ChuffClipsSimReader.cs',
    [string]$EditorPath='B:\Games\Unity 2019.4.40f1\Editor\Unity.exe',
    [int]$TimeoutSeconds=300,
    [switch]$ManagedOnly
)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if(-not $ToolboxDll){$ToolboxDll=Join-Path $taskRoot 'build\DadsToolbox.dll'}
$taskEvidence=Join-Path $taskRoot 'evidence\chuff'
$taskAdapter=Join-Path $PSScriptRoot 'managed\Run-AdapterChecks.ps1'
New-Item -ItemType Directory -Path $taskEvidence -Force | Out-Null
$taskSource=Join-Path $ChuffReferenceRoot 'src\Main.cs'
$taskBaseline=Join-Path $ChuffReferenceRoot 'verification\routing_20261028\baseline_020\src\Main.cs'
$taskReferenceDll=Join-Path $ChuffReferenceRoot 'dist\0.2.1\MultiCylinderChuffFix.dll'
if(-not(Test-Path -LiteralPath $taskReferenceDll)){
    $taskReferenceDll=Join-Path $ChuffReferenceRoot 'dist\0.2.1\MultiCylinderChuffFix\MultiCylinderChuffFix.dll'
}
if((Get-FileHash -LiteralPath $taskReferenceDll).Hash -ne '6F9E29BA4FF3427B6E2B334FE6C97942E6382C63EA7D0DE30C8F9B8A2C63582A'){throw 'Chuff reference must be preserved 0.2.1.'}
$taskInitialHash=(Get-FileHash -LiteralPath $ToolboxDll).Hash
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $taskAdapter -SourcePath $taskBaseline -RunName negative_020 -OutputRoot $taskEvidence -GameRoot $GamePath -NativeSourcePath $NativeSourcePath *> (Join-Path $taskEvidence 'negative-launch.log')
$taskNegativeExit=$LASTEXITCODE
$taskNegativeLog=Join-Path $taskEvidence 'negative_020\results.log'
$taskNegative=Get-Content -LiteralPath $taskNegativeLog -Raw
if($taskNegativeExit -ne 1 -or $taskNegative -notmatch 'RESULT passed=109 failed=3 total=112'){throw 'The 0.2.0 negative control must reproduce exactly 109 passes / 3 routing failures.'}
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $taskAdapter -SourcePath $taskSource -RunName current_source_adapter -OutputRoot $taskEvidence -AdaptedCoreRoot (Join-Path $taskRoot 'src') -GameRoot $GamePath -NativeSourcePath $NativeSourcePath *> (Join-Path $taskEvidence 'adapter-launch.log')
if($LASTEXITCODE -ne 0){throw 'Current Chuff source-adapter checks failed; see evidence/chuff/adapter-launch.log.'}
$taskAdapterLog=Join-Path $taskEvidence 'current_source_adapter\results.log'
$taskAdapterText=Get-Content -LiteralPath $taskAdapterLog -Raw
if($taskAdapterText -notmatch 'RESULT passed=(\d+) failed=0 total=112'){throw 'Expected all 112 current source-adapter cases to pass.'}
$taskAdapterCount=[int]$Matches[1]
Write-Output "Source-adapter: $taskAdapterCount passed; negative0.2.0:109 pass /3 expected routing failures."
if($ManagedOnly){return}
if(-not(Test-Path -LiteralPath $EditorPath)){throw 'Unity2019 Editor is required for native Chuff checks.'}
$taskProject=Join-Path $PSScriptRoot 'UnityFixture'
New-Item -ItemType Directory -Force -Path (Join-Path $taskProject 'Assets\Editor'),(Join-Path $taskProject 'Packages'),(Join-Path $taskProject 'ProjectSettings') | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NativeChuffChecks.cs') -Destination (Join-Path $taskProject 'Assets\Editor\NativeChuffChecks.cs') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'tests\UnityFixture\Packages\manifest.json') -Destination (Join-Path $taskProject 'Packages\manifest.json') -Force
[IO.File]::WriteAllLines((Join-Path $taskProject 'ProjectSettings\ProjectVersion.txt'),@('m_EditorVersion: 2019.4.40f1','m_EditorVersionWithRevision: 2019.4.40f1 (ffc62b691db5)'))
$env:TOOLBOX_CHUFF_OUT=[IO.Path]::GetFullPath($taskEvidence)
$env:TOOLBOX_GAME_PATH=[IO.Path]::GetFullPath($GamePath)
$env:TOOLBOX_FIXTURE_DLL=[IO.Path]::GetFullPath($ToolboxDll)
$taskVerdict=Join-Path $taskEvidence 'native-fixture-checks.txt'
$taskResult=Join-Path $taskEvidence 'result.json'
foreach($taskFile in @($taskVerdict,$taskResult)){if(Test-Path -LiteralPath $taskFile){Move-Item -LiteralPath $taskFile -Destination ($taskFile+'.previous') -Force}}
$taskEditorLog=Join-Path $taskEvidence 'native-editor.log'
$taskArgs=@('-projectPath',('"'+$taskProject+'"'),'-executeMethod','NativeChuffChecks.Run','-logFile',('"'+$taskEditorLog+'"'))
$taskProcess=Start-Process -FilePath $EditorPath -ArgumentList $taskArgs -WindowStyle Hidden -PassThru
$taskTimer=[Diagnostics.Stopwatch]::StartNew()
while(-not $taskProcess.WaitForExit(1000)){if($taskTimer.Elapsed.TotalSeconds -gt $TimeoutSeconds){Stop-Process -Id $taskProcess.Id;throw "Native Chuff fixture timed out: $taskEditorLog"}}
if(-not(Test-Path -LiteralPath $taskVerdict)-or -not(Test-Path -LiteralPath $taskResult)){throw "Native Chuff fixture did not return a verdict: $taskEditorLog"}
$taskChecks=Get-Content -LiteralPath $taskVerdict -Raw
$taskCounts=Get-Content -LiteralPath $taskResult -Raw | ConvertFrom-Json
Write-Output $taskChecks
if($taskProcess.ExitCode -ne 0 -or $taskCounts.failed -ne 0 -or $taskCounts.passed -lt 35){throw 'Native Chuff checks failed.'}
if((Get-FileHash -LiteralPath $ToolboxDll).Hash -ne $taskInitialHash -or -not $taskChecks.Contains('Toolbox SHA256: '+$taskInitialHash)){throw 'Toolbox binary changed during Chuff validation.'}
$taskInputs=@($PSCommandPath,(Join-Path $PSScriptRoot 'NativeChuffChecks.cs'),$taskAdapterLog,$taskNegativeLog,$taskResult,$taskReferenceDll,$NativeSourcePath)
$taskInputs+=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'managed') -File | Select-Object -ExpandProperty FullName)
$taskInputs+=@(Get-ChildItem -LiteralPath (Join-Path $ChuffReferenceRoot 'src') -File -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$taskInputs+=@(Get-ChildItem -LiteralPath (Split-Path $taskBaseline -Parent) -File -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$taskInputs+=@((Join-Path (Split-Path $NativeSourcePath -Parent) 'LayeredAudio.cs'),(Join-Path (Split-Path $NativeSourcePath -Parent) 'stock_audio_summary.json'))
$taskInputs+=@(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -File | Where-Object {$_.Name -like 'Chuff*.cs' -or $_.Name -like 'Sound*.cs' -or $_.Name -eq 'Main.cs'} | Select-Object -ExpandProperty FullName)
foreach($taskAdapterRun in @('current_source_adapter','negative_020')){
    $taskInputs+=@(Get-ChildItem -LiteralPath (Join-Path $taskEvidence $taskAdapterRun) -File | Where-Object {$_.Name -like '*.cs' -or $_.Name -like '*verbatim.txt' -or $_.Name -in @('provenance.txt','compile.log','results.log')} | Select-Object -ExpandProperty FullName)
}
$taskInputs+=@('Assembly-CSharp.dll','DV.Simulation.dll','DV.LayeredAudio.dll','DV.NAudio.dll','UnityEngine.CoreModule.dll','UnityEngine.AudioModule.dll','UnityModManager\0Harmony.dll','UnityModManager\UnityModManager.dll') | ForEach-Object {Join-Path $GamePath ('DerailValley_Data\Managed\'+$_)}
[ordered]@{
    toolbox=Get-FileHash -LiteralPath $ToolboxDll | Select-Object Path,Hash
    inputs=@($taskInputs | Sort-Object -Unique | ForEach-Object {Get-FileHash -LiteralPath $_ | Select-Object Path,Hash})
    verdict=Get-FileHash -LiteralPath $taskVerdict | Select-Object Path,Hash
    passed=$taskAdapterCount+[int]$taskCounts.passed;failed=[int]$taskCounts.failed;exitCode=0
    scope='112 current source-linked Reader/Loop adapter cases with real installed Harmony and verbatim native reader methods, plus exact combined DLL in isolated Unity Play Mode with real reader Init/Deinit/UpdateLoops, seeded native engine/flow/Port identities, muted AudioSource and real AudioClip PCM APIs. Both routing/activation orders, ownership, effective-frequency restoration and lifecycle. The preserved0.2.0 negative control must fail3routing cases and is excluded from candidate failure count. Stub/native boundaries explicit in subverdicts; no game packs, engine physics, native one-shot pool, audible listening or live performance acceptance.'
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskEvidence 'receipt.json') -Encoding utf8
