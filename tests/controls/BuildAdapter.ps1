param([string]$GamePath = 'B:\SteamLibrary\steamapps\common\Derail Valley')
$ErrorActionPreference='Stop'
$taskRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$taskEvidence=Join-Path $taskRoot 'evidence\controls'
New-Item -ItemType Directory -Force -Path $taskEvidence | Out-Null
$taskManaged=Join-Path $GamePath 'DerailValley_Data\Managed'
$taskReferences=@('mscorlib.dll','System.dll','System.Core.dll','Assembly-CSharp.dll','DV.UI.dll','DV.ThingTypes.dll','DV.Interaction.dll','DV.CabControls.Spec.dll','DV.Simulation.dll','Rewired_Core.dll','UnityEngine.CoreModule.dll','UnityEngine.PhysicsModule.dll','UnityModManager\0Harmony.dll','UnityModManager\UnityModManager.dll')
$taskArgs=@('/nologo','/nostdlib+','/target:library','/optimize+','/deterministic+','/langversion:latest',('/out:"'+(Join-Path $taskEvidence 'DadsToolbox.ControlsAdapter.dll')+'"'))
foreach($taskReference in $taskReferences){$taskArgs+='/reference:"'+(Join-Path $taskManaged $taskReference)+'"'}
$taskArgs+=Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Filter 'Controls*.cs' -File | Sort-Object Name | ForEach-Object {'"'+$_.FullName+'"'}
$taskRsp=Join-Path $taskEvidence 'adapter-build.rsp'
[IO.File]::WriteAllLines($taskRsp,$taskArgs,[Text.UTF8Encoding]::new($false))
& 'C:\Program Files\dotnet\dotnet.exe' 'C:\Program Files\dotnet\sdk\10.0.401\Roslyn\bincore\csc.dll' /noconfig "@$taskRsp" 2>&1 | Tee-Object -FilePath (Join-Path $taskEvidence 'adapter-build.log')
if($LASTEXITCODE -ne 0){throw 'Controls adapter compilation failed.'}
