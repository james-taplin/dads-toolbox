param([string]$GamePath = 'B:\SteamLibrary\steamapps\common\Derail Valley')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$taskManaged = Join-Path $GamePath 'DerailValley_Data\Managed'
$taskEvidence = Join-Path $taskRoot 'evidence\combined'
New-Item -ItemType Directory -Force -Path $taskEvidence | Out-Null
$taskBinary = Join-Path $taskRoot 'build\DadsToolbox.dll'
$taskExe = Join-Path $taskEvidence 'CombinedChecks.exe'
$taskCompiler = 'C:\Program Files\dotnet\sdk\10.0.401\Roslyn\bincore\csc.dll'
$taskFramework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$taskReferences = @('mscorlib.dll','System.dll','System.Core.dll','System.Xml.dll') | ForEach-Object { Join-Path $taskFramework $_ }
$taskReferences += @('Assembly-CSharp.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityModManager\UnityModManager.dll','UnityModManager\0Harmony.dll') | ForEach-Object { Join-Path $taskManaged $_ }
$taskSource = Join-Path $PSScriptRoot 'CombinedChecks.cs'
$taskArgs = @('/nologo','/nostdlib+','/optimize+','/langversion:latest','/target:exe',('/out:"' + $taskExe + '"'),('/reference:"' + $taskBinary + '"'))
$taskArgs += $taskReferences | ForEach-Object { '/reference:"' + $_ + '"' }
$taskArgs += '"' + $taskSource + '"'
$taskResponse = Join-Path $taskEvidence 'compile.rsp'
[IO.File]::WriteAllLines($taskResponse, $taskArgs, [Text.UTF8Encoding]::new($false))
& 'C:\Program Files\dotnet\dotnet.exe' $taskCompiler /noconfig "@$taskResponse"
if ($LASTEXITCODE -ne 0) { throw 'Combined parent fixture compilation failed.' }
[IO.File]::WriteAllText(($taskExe + '.config'), '<?xml version="1.0"?><configuration><runtime><loadFromRemoteSources enabled="true" /></runtime></configuration>')
& $taskExe $taskManaged $taskBinary $taskEvidence 2>&1 | Tee-Object -FilePath (Join-Path $taskEvidence 'test.log')
$taskExit = $LASTEXITCODE
$taskVerdict = Join-Path $taskEvidence 'checks.txt'
if ($taskExit -ne 0 -or -not (Test-Path -LiteralPath $taskVerdict)) { throw "Combined parent fixture failed: exit $taskExit" }
$taskText = Get-Content -Raw -LiteralPath $taskVerdict
if ($taskText -notmatch 'RESULT: (\d+) passed, 0 failed\.') { throw 'Combined parent verdict is not passing.' }
[ordered]@{
    exitCode = $taskExit; passed = [int]$Matches[1]; failed = 0
    toolbox = Get-FileHash -LiteralPath $taskBinary | Select-Object Path,Hash
    inputs = @(@($taskReferences) + @($taskSource,$PSCommandPath,$taskCompiler) | ForEach-Object { Get-FileHash -LiteralPath $_ | Select-Object Path,Hash })
    verdict = Get-FileHash -LiteralPath $taskVerdict | Select-Object Path,Hash
    scope = 'Exact combined DLL parent, seven independent preferences/all 128 native UMM XML combinations, older settings and real sibling sound-settings migration with byte preservation/precedence/retry, sole callback/event ownership, off/disabled and default-selected dependency-absent/inactive lifecycles, standalone conflicts, fresh recreation, injected failed cleanup with all seven cleanup attempts and foreign-owner preservation. No native Unity/game/audio execution.'
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskEvidence 'receipt.json') -Encoding utf8
Write-Output "Recorded combined parent validation for $((Get-FileHash -LiteralPath $taskBinary).Hash)"

