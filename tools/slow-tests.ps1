# Runs the fast tier and lists every unmarked test method that took longer than -Threshold seconds
# (all theory cases summed). Each one listed belongs in the slow tier: [Trait("Category", "Slow")].
# A parallel run times a test up to twice its time alone, so the default sits past the one-second line.
param(
    [double]$Threshold = 2.0,
    [string]$Target = "source/Resonalyze.sln"
)

$results = Join-Path ([System.IO.Path]::GetTempPath()) ("resonalyze-slow-tests-" + [guid]::NewGuid().ToString("N"))
dotnet test $Target -c Release --filter "Category!=Hardware&Category!=Slow" --logger trx --results-directory $results
$testExit = $LASTEXITCODE

$methods = @{}
foreach ($file in Get-ChildItem $results -Filter *.trx) {
    [xml]$trx = Get-Content $file.FullName -Raw
    foreach ($result in $trx.TestRun.Results.UnitTestResult) {
        $name = $result.testName.Split('(')[0]
        $methods[$name] = $methods[$name] + [TimeSpan]::Parse($result.duration).TotalSeconds
    }
}
Remove-Item $results -Recurse -Force

$slow = $methods.GetEnumerator() | Where-Object { $_.Value -ge $Threshold } | Sort-Object Value -Descending
foreach ($method in $slow) {
    "{0,7:N1} s  {1}" -f $method.Value, $method.Key
}

if ($testExit -ne 0) { exit $testExit }
if ($slow) {
    Write-Host "$(@($slow).Count) method(s) at or over $Threshold s belong in the slow tier."
    exit 1
}
Write-Host "No unmarked method reached $Threshold s."
