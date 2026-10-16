param([string]$GamePath = 'B:\SteamLibrary\steamapps\common\Derail Valley')
$ErrorActionPreference = 'Stop'
$taskBuildRoot = Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Path $taskBuildRoot -Force | Out-Null
$taskManagedRoot = Join-Path $GamePath 'DerailValley_Data\Managed'
$taskReferences = @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Xml.dll', 'Assembly-CSharp.dll',
    'UnityEngine.dll', 'UnityEngine.CoreModule.dll', 'UnityEngine.IMGUIModule.dll',
    'UnityEngine.ParticleSystemModule.dll', 'UnityEngine.AudioModule.dll', 'UnityEngine.PhysicsModule.dll',
    'DV.RailTrack.dll', 'DV.PointSet.dll', 'DV.OriginShiftInfo.dll', 'DV.Utils.dll', 'net.smkd.vector3d.dll',
    'DV.ThingTypes.dll', 'DV.Simulation.dll', 'DV.LayeredAudio.dll', 'DV.NAudio.dll',
    'DV.UI.dll', 'DV.Interaction.dll', 'DV.CabControls.Spec.dll', 'Rewired_Core.dll',
    'UnityModManager\0Harmony.dll', 'UnityModManager\UnityModManager.dll')
$taskSdkVersion = (& 'C:\Program Files\dotnet\dotnet.exe' --version).Trim()
if ($LASTEXITCODE -ne 0) { throw 'A .NET SDK is required.' }
$taskCompiler = Join-Path 'C:\Program Files\dotnet\sdk' "$taskSdkVersion\Roslyn\bincore\csc.dll"
$taskOutput = Join-Path $taskBuildRoot 'DadsToolbox.dll'
$taskSources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File | Sort-Object Name | Select-Object -ExpandProperty FullName)
$taskArgs = @('/nologo', '/nostdlib+', '/target:library', '/optimize+', '/deterministic+', '/langversion:latest', "/out:`"$taskOutput`"")
foreach ($taskRef in $taskReferences) {
    $taskReferencePath = Join-Path $taskManagedRoot $taskRef
    if (-not (Test-Path -LiteralPath $taskReferencePath)) { throw "Missing game reference: $taskReferencePath" }
    $taskArgs += "/reference:`"$taskReferencePath`""
}
$taskArgs += $taskSources | ForEach-Object { '"' + $_ + '"' }
$taskResponse = Join-Path $taskBuildRoot 'build.rsp'
[IO.File]::WriteAllLines($taskResponse, $taskArgs, [Text.UTF8Encoding]::new($false))
Set-Content -LiteralPath (Join-Path $taskBuildRoot 'build.log') -Value '' -Encoding utf8
& 'C:\Program Files\dotnet\dotnet.exe' $taskCompiler /noconfig "@$taskResponse" 2>&1 | Tee-Object -FilePath (Join-Path $taskBuildRoot 'build.log')
if ($LASTEXITCODE -ne 0) { throw 'Toolbox compilation failed.' }
$taskReceipt = [ordered]@{
    output = $taskOutput; outputSha256 = (Get-FileHash -LiteralPath $taskOutput).Hash
    compiler = $taskCompiler; compilerSha256 = (Get-FileHash -LiteralPath $taskCompiler).Hash
    sources = @($taskSources | ForEach-Object { Get-FileHash -LiteralPath $_ | Select-Object Path, Hash })
    references = @($taskReferences | ForEach-Object { Get-FileHash -LiteralPath (Join-Path $taskManagedRoot $_) | Select-Object Path, Hash })
    scope = 'Dad''s Toolbox development package: HUD Guard, CCL Control Fix, Loco Sound Pitch, two Skin Manager fixes, CCL Smoke Fix and DoubleTrack sign clearance. No game launch or installation.'
}
$taskReceipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskBuildRoot 'build-receipt.json') -Encoding utf8
Write-Output "Built $taskOutput"

