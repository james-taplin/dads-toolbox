param(
    [string]$EditorPath='B:\Games\Unity 2019.4.40f1\Editor\Unity.exe',
    [string]$GamePath='B:\SteamLibrary\steamapps\common\Derail Valley',
    [string]$ToolboxDll,
    [string]$FixtureExe,
    [int]$TimeoutSeconds=180
)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$taskEvidence=Join-Path $taskRoot 'evidence\controls'
$taskProject=Join-Path $PSScriptRoot 'UnityFixture'
New-Item -ItemType Directory -Force -Path (Join-Path $taskProject 'Assets\Editor'),(Join-Path $taskProject 'Packages'),(Join-Path $taskProject 'ProjectSettings') | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NativeControlsChecks.cs') -Destination (Join-Path $taskProject 'Assets\Editor\NativeControlsChecks.cs') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'tests\UnityFixture\Packages\manifest.json') -Destination (Join-Path $taskProject 'Packages\manifest.json') -Force
[IO.File]::WriteAllText((Join-Path $taskProject 'ProjectSettings\ProjectVersion.txt'),"m_EditorVersion: 2019.4.40f1`nm_EditorVersionWithRevision: 2019.4.40f1 (ffc62b691db5)`n")
$env:TOOLBOX_GAME_PATH=[IO.Path]::GetFullPath($GamePath)
$env:TOOLBOX_FIXTURE_DLL=[IO.Path]::GetFullPath($ToolboxDll)
$env:TOOLBOX_CONTROLS_FIXTURE=[IO.Path]::GetFullPath($FixtureExe)
$env:TOOLBOX_FIXTURE_OUT=[IO.Path]::GetFullPath($taskEvidence)
$taskVerdict=Join-Path $taskEvidence 'lifecycle-result.json'
if(Test-Path -LiteralPath $taskVerdict){Move-Item -LiteralPath $taskVerdict -Destination ($taskVerdict+'.previous') -Force}
$taskArguments=@('-projectPath',('"'+$taskProject+'"'),'-executeMethod','NativeControlsChecks.Run','-logFile',('"'+(Join-Path $taskEvidence 'native-editor.log')+'"'))
$taskProcess=Start-Process -FilePath $EditorPath -ArgumentList $taskArguments -WindowStyle Hidden -PassThru
[ordered]@{processId=$taskProcess.Id;editorPath=$EditorPath;projectPath=$taskProject;toolboxDll=$ToolboxDll;startedUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskEvidence 'native-process.json') -Encoding utf8
$taskTimer=[Diagnostics.Stopwatch]::StartNew()
while(-not $taskProcess.WaitForExit(1000))
{
    if($taskTimer.Elapsed.TotalSeconds -gt $TimeoutSeconds){Stop-Process -Id $taskProcess.Id;throw 'Isolated native Controls fixture timed out.'}
}
if(-not(Test-Path -LiteralPath $taskVerdict)){throw 'Unity produced no Controls lifecycle verdict. See evidence/controls/native-editor.log.'}
$taskResult=Get-Content -LiteralPath $taskVerdict -Raw | ConvertFrom-Json
Get-Content -LiteralPath (Join-Path $taskEvidence 'lifecycle-checks.txt')
if($taskProcess.ExitCode -ne 0 -or $taskResult.failed -ne 0){throw 'Native Controls lifecycle fixture failed.'}
Write-Output "Native Controls lifecycle: $($taskResult.passed) passed, $($taskResult.failed) failed."
