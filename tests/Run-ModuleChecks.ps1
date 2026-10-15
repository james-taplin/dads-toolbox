param(
    [ValidateSet('hud','consist','smoke')][string]$Kind,
    [string]$GamePath='B:\SteamLibrary\steamapps\common\Derail Valley',
    [string]$ToolboxDll
)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
if(!$ToolboxDll){$ToolboxDll=Join-Path $taskRoot 'build\DadsToolbox.dll'}
$ToolboxDll=[IO.Path]::GetFullPath($ToolboxDll)
$taskSource=Join-Path $PSScriptRoot $Kind
$taskOutput=Join-Path $taskRoot "evidence\$Kind"
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
Copy-Item -LiteralPath $ToolboxDll -Destination (Join-Path $taskOutput 'DadsToolbox.dll') -Force
$taskBuild=Get-Content -LiteralPath (Join-Path $taskRoot 'build\build-receipt.json') -Raw | ConvertFrom-Json
$taskCompiler=$taskBuild.compiler
$taskRefs=@($taskBuild.references.Path)
$taskSources=@(Get-ChildItem -LiteralPath $taskSource -File -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$taskMain=if($Kind -eq 'smoke'){'SmokeModuleChecks'}else{'IntegrationTests'}
$taskExe=Join-Path $taskOutput 'Checks.exe'
$taskArgs=@('/nologo','/nostdlib+','/target:exe','/optimize+','/langversion:latest',('/main:'+$taskMain),('/out:"'+$taskExe+'"'),('/reference:"'+$ToolboxDll+'"'))
$taskArgs+=@($taskRefs | ForEach-Object {'/reference:"'+$_+'"'})
$taskArgs+=@($taskSources | ForEach-Object {'"'+$_+'"'})
$taskRsp=Join-Path $taskOutput 'checks.rsp'
[IO.File]::WriteAllLines($taskRsp,$taskArgs,[Text.UTF8Encoding]::new($false))
& 'C:\Program Files\dotnet\dotnet.exe' $taskCompiler /noconfig "@$taskRsp"
if($LASTEXITCODE -ne 0){throw "$Kind fixture compilation failed"}
[IO.File]::WriteAllText(($taskExe+'.config'),'<?xml version="1.0"?><configuration><runtime><loadFromRemoteSources enabled="true" /></runtime></configuration>')
$taskLog=Join-Path $taskOutput 'checks.txt'
& $taskExe $GamePath $taskOutput 2>&1 | Tee-Object -FilePath $taskLog
$taskExit=$LASTEXITCODE
$taskText=Get-Content -LiteralPath $taskLog -Raw
$taskPassed=0; $taskFailed=0
if($taskText -match 'RESULT passed=(\d+) failed=(\d+)'){$taskPassed=[int]$Matches[1];$taskFailed=[int]$Matches[2]}
elseif($taskText -match 'PASS (\d+) checks'){$taskPassed=[int]$Matches[1]}
else{throw "No final $Kind test verdict"}
if($Kind -eq 'hud'){
    $taskCold=Join-Path $taskOutput 'ColdChecks.exe'
    $taskArgs=$taskArgs | ForEach-Object {if($_ -like '/main:*'){'/main:ColdStartChecks'}elseif($_ -like '/out:*'){'/out:"'+$taskCold+'"'}else{$_}}
    [IO.File]::WriteAllLines($taskRsp,$taskArgs,[Text.UTF8Encoding]::new($false))
    & 'C:\Program Files\dotnet\dotnet.exe' $taskCompiler /noconfig "@$taskRsp"
    if($LASTEXITCODE -ne 0){throw 'Cold HUD compilation failed'}
    Copy-Item -LiteralPath ($taskExe+'.config') -Destination ($taskCold+'.config') -Force
    & $taskCold $GamePath $taskOutput 2>&1 | Tee-Object -FilePath (Join-Path $taskOutput 'cold.txt')
    if($LASTEXITCODE -ne 0){$taskExit=1;$taskFailed++}else{$taskPassed+=4}
}
$taskInputs=@($taskSources)+@($PSCommandPath)+@($taskRefs)
Add-Content -LiteralPath $taskLog -Value "RESULT: $taskPassed passed, $taskFailed failed." -Encoding utf8
if($Kind -eq 'hud'){$taskInputs+=Join-Path $taskOutput 'cold.txt'}
[ordered]@{
    toolbox=Get-FileHash -LiteralPath $ToolboxDll | Select-Object Path,Hash
    inputs=@($taskInputs | ForEach-Object {Get-FileHash -LiteralPath $_ | Select-Object Path,Hash})
    verdict=Get-FileHash -LiteralPath $taskLog | Select-Object Path,Hash
    passed=$taskPassed; failed=$taskFailed; exitCode=$taskExit
    scope="$Kind canonical regression fixture adapted to reference exact combined DLL. Actual installed managed targets/UMM/Harmony; controlled native-body/speed boundaries; no game process or installation."
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskOutput 'receipt.json') -Encoding utf8
if($taskExit -ne 0 -or $taskFailed -gt 0){throw "$Kind checks failed"}
