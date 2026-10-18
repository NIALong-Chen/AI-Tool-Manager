$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.Encoding]::UTF8
$project=Split-Path $PSScriptRoot -Parent
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('AI-Tool-Manager-'+[guid]::NewGuid().ToString('N'))
& (Join-Path $PSScriptRoot 'Scanner.Tests.ps1') -FixtureRoot $fixture
$report=Join-Path $fixture 'native-report.json'
$start=New-Object Diagnostics.ProcessStartInfo
$start.FileName=Join-Path $project 'AI工具管理器.exe'
$start.Arguments='--self-test --test-home "'+$fixture+'" --test-output "'+$report+'"'
$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle='Hidden'
$process=[Diagnostics.Process]::Start($start)
if(!$process.WaitForExit(115000)){throw '原生界面测试仍未退出，请检查测试窗口。'}
if($process.ExitCode -ne 0){throw ([IO.File]::ReadAllText($report))}
$result=[IO.File]::ReadAllText($report)|ConvertFrom-Json
if(!$result.Success){throw $result.Message}
$result.Checks | ForEach-Object {Write-Output ('PASS: '+$_)}
Write-Output ('ALL_TESTS_OK; report='+$report)
