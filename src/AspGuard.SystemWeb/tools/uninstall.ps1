# Removes the AspGuard analyzers from projects that use packages.config.
param($installPath, $toolsPath, $package, $project)

if ($project.Type -ne "C#") { return }
if (-not $project.Object.AnalyzerReferences) { return }

$analyzersPath = Join-Path (Join-Path (Split-Path -Path $toolsPath -Parent) "analyzers") "dotnet\cs"
if (-not (Test-Path $analyzersPath)) { return }

foreach ($analyzer in Get-ChildItem -Path "$analyzersPath\*.dll" -Exclude *.resources.dll)
{
    try { $project.Object.AnalyzerReferences.Remove($analyzer.FullName) } catch { }
}
