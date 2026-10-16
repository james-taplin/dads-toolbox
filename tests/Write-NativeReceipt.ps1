param([string]$ToolboxDll, [string]$GamePath, [string]$EvidencePath,
      [ValidateSet('SkinDamage','SignClearance','SkinRegistration')][string]$Kind)
$ErrorActionPreference = 'Stop'
$taskLegacyRoot = if ($env:TOOLBOX_LEGACY_MODS) { $env:TOOLBOX_LEGACY_MODS } else { Join-Path $GamePath 'Mods' }
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskVerdict = Join-Path $EvidencePath 'native-fixture-checks.txt'
$taskText = Get-Content -Raw -LiteralPath $taskVerdict
if ($taskText -notmatch 'Checks: (\d+); failures: 0') { throw 'A passing native fixture verdict is required.' }
$taskChecks = [int]$Matches[1]
if ($taskText -notmatch 'Unity ([0-9a-z.]+)') { throw 'Native fixture did not identify Unity.' }
$taskUnity = $Matches[1]
$taskInputs = @($ToolboxDll,
    (Join-Path $GamePath 'DerailValley_Data\Managed\Assembly-CSharp.dll'),
    (Join-Path $GamePath 'DerailValley_Data\Managed\UnityModManager\UnityModManager.dll'),
    (Join-Path $GamePath 'DerailValley_Data\Managed\UnityModManager\0Harmony.dll'),
    (Join-Path $GamePath 'Mods\SkinManagerMod\SkinManagerMod.dll'),
    $PSCommandPath,
    (Join-Path $PSScriptRoot 'UnityFixture\ProjectSettings\ProjectVersion.txt'),
    (Join-Path $PSScriptRoot 'UnityFixture\Packages\manifest.json'))
if ($Kind -eq 'SignClearance') {
    $taskInputs += @((Join-Path $GamePath 'Mods\DoubleTrack\DoubleTrack.dll'),
        (Join-Path $taskLegacyRoot 'DoubleTrackSignClearance\DoubleTrackSignClearance.dll'),
        (Join-Path $PSScriptRoot 'UnityFixture\Assets\Editor\NativeSignClearanceChecks.cs'),
        (Join-Path $PSScriptRoot 'Run-NativeSignClearanceFixture.ps1'))
    $taskInputs += @('DV.RailTrack.dll','DV.PointSet.dll','DV.OriginShiftInfo.dll','DV.Utils.dll','net.smkd.vector3d.dll') |
        ForEach-Object { Join-Path $GamePath ('DerailValley_Data\Managed\' + $_) }
    $taskScope = 'Actual Toolbox parent, installed UMM/DoubleTrack sign list, native registry/RailTrack point sets and Unity Transforms in isolated Unity Editor. Generated rail/list contents and controlled callback dispatch; no full game boot, generation coroutine, real map traversal, saves or GUI.'
} elseif ($Kind -eq 'SkinRegistration') {
    $taskInputs += @((Join-Path $GamePath 'Mods\DVCustomCarLoader\CCL.Importer.dll'),
        (Join-Path $GamePath 'Mods\DVCustomCarLoader\CCL.Types.dll'),
        (Join-Path $PSScriptRoot 'UnityFixture\Assets\Editor\NativeSkinRegistrationChecks.cs'),
        (Join-Path $PSScriptRoot 'Run-NativeSkinRegistrationFixture.ps1'),
        (Join-Path $GamePath 'DerailValley_Data\Managed\DV.ThingTypes.dll'))
    $taskScope = 'Actual installed UMM activation dispatch, Skin Manager callback/config discovery and PNG loading. Actual CCL callback target with fixture prefix replacing its bundle-import body by controlled livery registration. No injected skins or companion mod; full clean game/CCL boot, real pack import, streaming and save/reload remain untested.'
} else {
    $taskInputs += @((Join-Path $PSScriptRoot 'UnityFixture\Assets\Editor\NativeSkinDamageChecks.cs'),
        (Join-Path $PSScriptRoot 'UnityFixture\Assets\FixtureRuntime\FixtureSkinMessages.cs'),
        (Join-Path $PSScriptRoot 'UnityFixture\Assets\FixtureRuntime\DadsToolbox.NativeFixture.asmdef'),
        (Join-Path $PSScriptRoot 'Run-NativeSkinDamageFixture.ps1'),
        (Join-Path $GamePath 'DerailValley_Data\Managed\DV.ThingTypes.dll'))
    $taskScope = 'Installed native material handler and Skin Manager callback with the compiled Toolbox Harmony prefix in isolated Unity objects. Manually constructed explosion flag/callback transitions; no physical explosion, streaming or SaveManager.'
}
[ordered]@{
    date = '2026-10-16'; unity = $taskUnity; checks = $taskChecks; failures = 0
    verdict = $taskVerdict; verdictSha256 = (Get-FileHash -LiteralPath $taskVerdict).Hash
    inputs = @($taskInputs | ForEach-Object { Get-FileHash -LiteralPath $_ | Select-Object Path,Hash })
    scope = $taskScope
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $EvidencePath 'receipt.json') -Encoding utf8
