param([string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\SULFUR', [string]$BepInExCore = "$GamePath\BepInEx\core")
$ErrorActionPreference = 'Stop'
$managed = Join-Path $GamePath 'Sulfur_Data\Managed'
if (!(Test-Path -LiteralPath $managed)) { throw 'Game Managed folder was not found.' }
if (!(Test-Path -LiteralPath (Join-Path $BepInExCore 'BepInEx.dll'))) { throw 'Set -BepInExCore to the BepInEx 5 core folder.' }
# PowerShell 7 bundles Roslyn. Compile against the game runtime, not the host's .NET runtime.
foreach ($assembly in @('Microsoft.CodeAnalysis.dll','Microsoft.CodeAnalysis.CSharp.dll')) {
    Add-Type -Path (Join-Path $PSHOME $assembly)
}
$refs = [Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
foreach ($dll in Get-ChildItem -LiteralPath $managed -Filter '*.dll') {
    $refs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($dll.FullName))
}
foreach ($name in @('BepInEx.dll','0Harmony.dll')) {
    $refs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile((Join-Path $BepInExCore $name)))
}
$trees = [Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
foreach ($file in Get-ChildItem -LiteralPath "$PSScriptRoot\src" -Filter '*.cs') {
    $trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($file.FullName)))
}
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('SulfurCraftingArcade', $trees, $refs, $options)
$out = Join-Path $PSScriptRoot 'BepInEx\plugins\SulfurCraftingArcade'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$stream = [IO.File]::Create((Join-Path $out 'SulfurCraftingArcade.dll'))
try { $result = $compilation.Emit($stream) } finally { $stream.Dispose() }
$result.Diagnostics | Where-Object Severity -eq Error | ForEach-Object ToString
if (!$result.Success) { throw 'Compilation failed.' }
Write-Output "Built $out\SulfurCraftingArcade.dll"
