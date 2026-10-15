param(
    [string]$EditorPath = 'B:\Games\Unity 2019.4.40f1\Editor\Unity.exe',
    [string]$GamePath = 'B:\SteamLibrary\steamapps\common\Derail Valley',
    [string]$ToolboxDll,
    [int]$TimeoutSeconds = 180
)
$ErrorActionPreference = 'Stop'
$fixtureRoot = Split-Path -Parent $PSScriptRoot
if (-not $ToolboxDll) { $ToolboxDll = Join-Path $fixtureRoot 'build\DadsToolbox.dll' }
if (-not (Test-Path -LiteralPath $EditorPath)) { throw "Unity Editor missing: $EditorPath" }
if (-not (Test-Path -LiteralPath $ToolboxDll)) { throw 'Build the Toolbox DLL before running the native fixture.' }
$fixtureProjectPath = Join-Path $PSScriptRoot 'UnityFixture'
$fixtureEvidencePath = Join-Path $fixtureRoot 'evidence\skin-damage'
New-Item -ItemType Directory -Force -Path $fixtureEvidencePath | Out-Null
$env:TOOLBOX_FIXTURE_DLL = [IO.Path]::GetFullPath($ToolboxDll)
$env:TOOLBOX_GAME_PATH = [IO.Path]::GetFullPath($GamePath)
$env:TOOLBOX_FIXTURE_OUT = $fixtureEvidencePath
$fixtureLogPath = Join-Path $fixtureEvidencePath 'unity-windowed.log'
$fixtureCheckPath = Join-Path $fixtureEvidencePath 'native-fixture-checks.txt'
if (Test-Path -LiteralPath $fixtureCheckPath) { Move-Item -LiteralPath $fixtureCheckPath -Destination ($fixtureCheckPath + '.previous') -Force }
# This installed Unity 2019 desktop entitlement rejects batch mode. Use its
# working desktop path in a hidden window and let the fixture exit the Editor.
$fixtureArgs = @('-projectPath', ('"' + $fixtureProjectPath + '"'), '-executeMethod', 'NativeSkinDamageChecks.Run', '-logFile', ('"' + $fixtureLogPath + '"'))
$fixtureProcess = Start-Process -FilePath $EditorPath -ArgumentList $fixtureArgs -WindowStyle Hidden -PassThru
$fixtureTimer = [Diagnostics.Stopwatch]::StartNew()
while (-not $fixtureProcess.WaitForExit(1000)) {
    if ($fixtureTimer.Elapsed.TotalSeconds -gt $TimeoutSeconds) {
        Stop-Process -Id $fixtureProcess.Id
        throw "Isolated Unity fixture timed out. See $fixtureLogPath"
    }
}
if (-not (Test-Path -LiteralPath $fixtureCheckPath)) { throw "Unity did not produce a test verdict. See $fixtureLogPath" }
$fixtureChecks = Get-Content -LiteralPath $fixtureCheckPath -Raw
Write-Output $fixtureChecks
if ($fixtureProcess.ExitCode -ne 0 -or $fixtureChecks -notmatch 'Checks: \d+; failures: 0') { throw 'Native SkinDamageFix fixture failed.' }
& (Join-Path $PSScriptRoot 'Write-NativeReceipt.ps1') -ToolboxDll $ToolboxDll -GamePath $GamePath -EvidencePath $fixtureEvidencePath -Kind SkinDamage
