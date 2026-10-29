param(
    [Parameter(Mandatory=$true)][string]$SourcePath,
    [string]$RunName = 'standalone_021_reference',
    [Parameter(Mandatory=$true)][string]$OutputRoot,
    [string]$AdaptedCoreRoot,
    [string]$GameRoot = 'B:\SteamLibrary\steamapps\common\Derail Valley',
    [string]$NativeSourcePath = 'C:\Users\james\Desktop\Derail Valley Mods\5AT\reference\audio098\native_contract\ChuffClipsSimReader.cs'
)
$ErrorActionPreference = 'Stop'
if ($RunName -notmatch '^[a-zA-Z0-9_.-]+$') { throw 'RunName must be a simple directory name.' }
$runRoot = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) $RunName
$null = New-Item -ItemType Directory -Path $runRoot -Force
$SourcePath = (Resolve-Path -LiteralPath $SourcePath).Path
$originalSourcePath = $SourcePath
$SourcePath = Join-Path $runRoot 'TestedPluginSource.cs'
[IO.File]::WriteAllText($SourcePath, [IO.File]::ReadAllText($originalSourcePath))
if ($AdaptedCoreRoot) {
    # The Unity/native adapters use their own type identities. Compile the
    # current Reader/Loop algorithms with only their namespace normalized and
    # a thin original loader bridge, rather than pretending this is the DLL.
    $hostText = [IO.File]::ReadAllText($SourcePath)
    $hostText = $hostText.Replace('internal static readonly HashSet<string> Reported', 'internal static bool Enabled = true;' + [Environment]::NewLine + '        internal static bool Once(string key) => Reported.Add(key);' + [Environment]::NewLine + '        internal static void Log(string text) => Mod.Logger.Log(text);' + [Environment]::NewLine + '        internal static readonly HashSet<string> Reported')
    [IO.File]::WriteAllText($SourcePath,$hostText)
}
$sourceSnapshots = @($SourcePath)
$sourceInputs = @($originalSourcePath)
if ((Split-Path -Leaf $originalSourcePath) -eq 'Main.cs') {
    $coreFiles = if ($AdaptedCoreRoot) { Get-Item -LiteralPath (Join-Path $AdaptedCoreRoot 'ChuffReaderAdaptation.cs'),(Join-Path $AdaptedCoreRoot 'ChuffLoopAdaptation.cs') } else { Get-ChildItem -LiteralPath (Split-Path $originalSourcePath) -Filter '*.cs' -File | Where-Object { $_.FullName -ne $originalSourcePath } }
    foreach ($sourceFile in $coreFiles) {
        $copyPath = Join-Path $runRoot ('Tested_' + $sourceFile.Name)
        $coreText = [IO.File]::ReadAllText($sourceFile.FullName)
        if ($AdaptedCoreRoot) { $coreText = $coreText.Replace('namespace DadsToolbox.Chuffs','namespace MultiCylinderChuffFix') }
        [IO.File]::WriteAllText($copyPath, $coreText)
        $sourceSnapshots += $copyPath
        $sourceInputs += $sourceFile.FullName
    }
}
$nativeText = [IO.File]::ReadAllText($NativeSourcePath)
function Get-BracedDeclaration([string]$declaration, [string]$text = $nativeText) {
    $start = $text.IndexOf($declaration, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Native declaration missing: $declaration" }
    $open = $text.IndexOf('{', $start)
    $depth = 0
    for ($i = $open; $i -lt $text.Length; $i++) {
        if ($text[$i] -eq '{') { $depth++ }
        if ($text[$i] -eq '}') {
            $depth--
            if ($depth -eq 0) { return $text.Substring($start, $i - $start + 1) }
        }
    }
    throw "Unclosed declaration: $declaration"
}
# These native declarations contain no string literals/comments with braces.
# Fail closed if the extracted contract unexpectedly changes its recognizable landmarks.
$onChuff = Get-BracedDeclaration 'private void OnChuff(float chuffIndexFloat)'
if (-not $onChuff.Contains('float value = exhaustPressurePort.Value;') -or -not $onChuff.Contains('ashChuffClips.Play(')) { throw 'Unexpected native OnChuff contract.' }
$updateLoops = Get-BracedDeclaration 'private void UpdateLoops()'
$init = Get-BracedDeclaration 'public override void Init(TrainCar car, SimulationFlow simFlow)'
$deinit = Get-BracedDeclaration 'public override void Deinit()'
$waterInput = Get-BracedDeclaration 'private float WaterChuffVolumeInput'
$loopClass = Get-BracedDeclaration 'public class ChuffLoop'
$ashMatch = [regex]::Match($nativeText, 'private float AshChuffVolumeInput => ashesInPipesPort\.Value;')
if (-not $ashMatch.Success) { throw 'Native ash volume property missing.' }
$nativeParts = @($init, $deinit, $onChuff, $updateLoops, $waterInput, $loopClass, $ashMatch.Value)
$extractedPath = Join-Path $runRoot 'NativeDeclarations.verbatim.txt'
[IO.File]::WriteAllText($extractedPath, ($nativeParts -join "`r`n`r`n"))
$readerTemplate = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'ReaderTemplate.cs.txt'))
$readerText = $readerTemplate.Replace('/*NATIVE_INIT*/', $init).Replace('/*NATIVE_DEINIT*/', $deinit).Replace('/*NATIVE_ONCHUFF*/', $onChuff).Replace('/*NATIVE_UPDATELOOPS*/', $updateLoops).Replace('/*NATIVE_WATERINPUT*/', $waterInput).Replace('/*NATIVE_LOOPCLASS*/', $loopClass).Replace('/*NATIVE_ASHINPUT*/', $ashMatch.Value)
$generatedPath = Join-Path $runRoot 'GeneratedNativeReader.cs'
[IO.File]::WriteAllText($generatedPath, $readerText)
$layeredSourcePath = Join-Path (Split-Path $NativeSourcePath) 'LayeredAudio.cs'
$layeredText = [IO.File]::ReadAllText($layeredSourcePath)
$layerSetPitch = Get-BracedDeclaration 'public void SetPitch(float value, bool linearPitchLerp = false, float minPitch = 1f, float maxPitch = 1f)' $layeredText
$pitchAdapterTemplate = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'NativePitchAdapter.cs.txt'))
$pitchAdapterPath = Join-Path $runRoot 'GeneratedNativePitchAdapter.cs'
[IO.File]::WriteAllText($pitchAdapterPath, $pitchAdapterTemplate.Replace('/*NATIVE_LAYER_SETPITCH*/', $layerSetPitch))
[IO.File]::WriteAllText((Join-Path $runRoot 'NativeLayerSetPitch.verbatim.txt'), $layerSetPitch)
$stockMetadataPath = Join-Path (Split-Path $NativeSourcePath) 'stock_audio_summary.json'
$stockMetadata = Get-Content -Raw -LiteralPath $stockMetadataPath | ConvertFrom-Json
$stockLines = [Collections.Generic.List[string]]::new()
$stockLines.Add('using UnityEngine; public static class StockLoopMetadata { public static readonly StockLoopCase[] Cases = new[] {')
foreach ($chuff in $stockMetadata.chuffs) {
    foreach ($loop in $chuff.loops) {
        foreach ($layer in $loop.layered.layers) {
            if (-not $layer.usePitch) { throw 'Expected stock loop pitch curve.' }
            $points = @($layer.pitch | ForEach-Object { 'new float[] {' + ([string]::Join(',', @($_ | ForEach-Object { ([double]$_).ToString('R', [Globalization.CultureInfo]::InvariantCulture) + 'f' }))) + '}' })
            $label = ($loop.layered.path + '/' + $layer.name).Replace('\', '\\').Replace('"', '\"')
            $clipName = ([string]$layer.clip.m_Name).Replace('\', '\\').Replace('"', '\"')
            $pitch = ([double]$layer.sourcePitch).ToString('R', [Globalization.CultureInfo]::InvariantCulture)
            $stockLines.Add('new StockLoopCase("' + $label + '","' + $clipName + '",' + $pitch + 'f,new float[][] {' + ([string]::Join(',', $points)) + '}),')
        }
    }
}
$stockLines.Add('}; }')
$stockCasesPath = Join-Path $runRoot 'GeneratedStockLoopMetadata.cs'
[IO.File]::WriteAllText($stockCasesPath, [string]::Join("`r`n", $stockLines))
$managed = Join-Path $GameRoot 'DerailValley_Data\Managed'
$harmony = Join-Path $managed 'UnityModManager\0Harmony.dll'
$compiler = 'C:\Program Files\dotnet\sdk\10.0.401\Roslyn\bincore\csc.dll'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$exePath = Join-Path $runRoot 'AudioRegression.exe'
$refs = @('mscorlib.dll','System.dll','System.Core.dll') | ForEach-Object { Join-Path $managed $_ }
$argsList = @($compiler, '/nologo', '/noconfig', '/nostdlib+', '/target:exe', '/platform:x64', '/optimize-', '/debug:portable', '/langversion:latest', ('/out:' + $exePath))
$argsList += @($refs + $harmony) | ForEach-Object { '/reference:' + $_ }
$argsList += $sourceSnapshots + @((Join-Path $PSScriptRoot 'Stubs.cs'), $generatedPath, $pitchAdapterPath, $stockCasesPath, (Join-Path $PSScriptRoot 'AudioRegression.cs'), (Join-Path $PSScriptRoot 'AdaptationRegression.cs'))
$command = '& ' + "'$dotnet' " + (($argsList | ForEach-Object { "'" + $_.Replace("'", "''") + "'" }) -join ' ')
$provenance = @(
    'Fixture integration test; not a Unity Editor or in-game playback test.',
    'Compiler command (PowerShell):', $command,
    'Execution command (PowerShell):', ('& ' + "'$exePath'"),
    'Runtime native declarations are exact substrings from NativeSourcePath; no body edits.',
    'Only preceding NoInlining attribute and fixture fields/helpers are added.',
    'SHA-256 inputs:'
)
$hashPaths = $sourceInputs + $sourceSnapshots + @($NativeSourcePath, $layeredSourcePath, $stockMetadataPath, $extractedPath, $generatedPath, $pitchAdapterPath, $stockCasesPath, $harmony, $compiler, $dotnet) + $refs + @((Join-Path $PSScriptRoot 'Stubs.cs'), (Join-Path $PSScriptRoot 'ReaderTemplate.cs.txt'), (Join-Path $PSScriptRoot 'NativePitchAdapter.cs.txt'), (Join-Path $PSScriptRoot 'AudioRegression.cs'), (Join-Path $PSScriptRoot 'AdaptationRegression.cs'), $PSCommandPath)
$provenance += $hashPaths | ForEach-Object { $h = Get-FileHash -LiteralPath $_ -Algorithm SHA256; $h.Hash + '  ' + $h.Path }
$provenance | Set-Content -LiteralPath (Join-Path $runRoot 'provenance.txt') -Encoding utf8
$compileOutput = & $dotnet @argsList 2>&1
$compileExit = $LASTEXITCODE
[IO.File]::WriteAllText((Join-Path $runRoot 'compile.log'), ("CompilerExit=$compileExit`r`n" + ($compileOutput -join "`r`n")))
if ($compileExit -ne 0) { $compileOutput | Write-Output; exit $compileExit }
Copy-Item -LiteralPath $harmony -Destination (Join-Path $runRoot '0Harmony.dll') -Force
[IO.File]::WriteAllText(($exePath + '.config'), '<?xml version="1.0" encoding="utf-8"?><configuration><startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" /></startup></configuration>')
$testOutput = & $exePath 2>&1
$testExit = $LASTEXITCODE
$testOutput | Set-Content -LiteralPath (Join-Path $runRoot 'results.log') -Encoding utf8
$testOutput | Write-Output
('EXIT_CODE=' + $testExit) | Add-Content -LiteralPath (Join-Path $runRoot 'results.log')
'SHA-256 outputs:' | Add-Content -LiteralPath (Join-Path $runRoot 'provenance.txt')
@($exePath, ($exePath + '.config'), (Join-Path $runRoot '0Harmony.dll'), (Join-Path $runRoot 'compile.log'), (Join-Path $runRoot 'results.log')) | ForEach-Object {
    $h = Get-FileHash -LiteralPath $_ -Algorithm SHA256
    $h.Hash + '  ' + $h.Path
} | Add-Content -LiteralPath (Join-Path $runRoot 'provenance.txt')
exit $testExit
