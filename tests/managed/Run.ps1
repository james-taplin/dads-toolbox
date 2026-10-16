param([string]$GamePath = 'B:\SteamLibrary\steamapps\common\Derail Valley')
$ErrorActionPreference = 'Stop'
$taskLegacyRoot = if ($env:TOOLBOX_LEGACY_MODS) { $env:TOOLBOX_LEGACY_MODS } else { Join-Path $GamePath 'Mods' }
$taskLegacySigns = Join-Path $taskLegacyRoot 'DoubleTrackSignClearance\DoubleTrackSignClearance.dll'
$taskManaged = Join-Path $GamePath 'DerailValley_Data\Managed'
$taskWorkspace = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$taskEvidence = Join-Path $taskWorkspace 'evidence\managed'
New-Item -ItemType Directory -Path $taskEvidence -Force | Out-Null
$taskCompiler = 'C:\Program Files\dotnet\sdk\10.0.401\Roslyn\bincore\csc.dll'
$taskRefs = @('mscorlib.dll','System.dll','System.Core.dll','Assembly-CSharp.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityModManager\UnityModManager.dll','UnityModManager\0Harmony.dll')
$taskBase = @('/nologo','/nostdlib+','/optimize+','/langversion:latest')
foreach ($taskRef in $taskRefs) { $taskBase += '/reference:"' + (Join-Path $taskManaged $taskRef) + '"' }
$taskFake = Join-Path $taskEvidence 'SkinManagerMod.dll'
$taskFakeRsp = Join-Path $taskEvidence 'fake.rsp'
[IO.File]::WriteAllLines($taskFakeRsp, ($taskBase + @('/target:library',('/out:"' + $taskFake + '"'),('"' + (Join-Path $PSScriptRoot 'FakeSkinManager.cs') + '"'))), [Text.UTF8Encoding]::new($false))
& 'C:\Program Files\dotnet\dotnet.exe' $taskCompiler /noconfig "@$taskFakeRsp"
if ($LASTEXITCODE -ne 0) { throw 'Synthetic API target compilation failed.' }
$taskToolbox = Join-Path $taskWorkspace 'build\DadsToolbox.dll'
$taskExe = Join-Path $taskEvidence 'ManagedChecks.exe'
$taskRsp = Join-Path $taskEvidence 'managed.rsp'
[IO.File]::WriteAllLines($taskRsp, ($taskBase + @('/target:exe',('/out:"' + $taskExe + '"'),('/reference:"' + $taskToolbox + '"'),('"' + (Join-Path $PSScriptRoot 'ManagedChecks.cs') + '"'))), [Text.UTF8Encoding]::new($false))
& 'C:\Program Files\dotnet\dotnet.exe' $taskCompiler /noconfig "@$taskRsp"
if ($LASTEXITCODE -ne 0) { throw 'Managed fixture compilation failed.' }
[IO.File]::WriteAllText(($taskExe + '.config'), '<?xml version="1.0"?><configuration><runtime><loadFromRemoteSources enabled="true" /></runtime></configuration>')
& $taskExe $taskManaged $taskToolbox $taskFake (Join-Path $taskEvidence 'results') (Join-Path $GamePath 'Mods\DoubleTrack\DoubleTrack.dll') $taskLegacySigns 2>&1 | Tee-Object -FilePath (Join-Path $taskEvidence 'test.log')
$taskExit = $LASTEXITCODE
[ordered]@{
    exitCode = $taskExit
    toolbox = Get-FileHash -LiteralPath $taskToolbox | Select-Object Path,Hash
    sources = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object { Get-FileHash -LiteralPath $_.FullName | Select-Object Path,Hash })
    umm = Get-FileHash -LiteralPath (Join-Path $taskManaged 'UnityModManager\UnityModManager.dll') | Select-Object Path,Hash
    harmony = Get-FileHash -LiteralPath (Join-Path $taskManaged 'UnityModManager\0Harmony.dll') | Select-Object Path,Hash
    optionalMods = @((Join-Path $GamePath 'Mods\DoubleTrack\DoubleTrack.dll'),$taskLegacySigns) | ForEach-Object { Get-FileHash -LiteralPath $_ | Select-Object Path,Hash }
    runner = Get-FileHash -LiteralPath $PSCommandPath | Select-Object Path,Hash
    scope = 'Actual installed UMM/Harmony/DoubleTrack/standalone metadata and compiled Toolbox parent lifecycle, shared settings, conflicts and dependency changes. Synthetic Skin Manager patch target. No native scene or game process.'
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskEvidence 'receipt.json') -Encoding utf8
if ($taskExit -ne 0) { throw "Managed checks failed with exit $taskExit" }
