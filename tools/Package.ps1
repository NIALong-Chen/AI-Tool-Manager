param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$version=([IO.File]::ReadAllText((Join-Path $project 'VERSION'))).Trim()
$exe=Join-Path $project 'AI工具管理器.exe'
if(!(Test-Path -LiteralPath $exe -PathType Leaf)){throw '请先运行 tools/Build.ps1。'}
if([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion -ne $version){throw 'EXE 与 VERSION 不一致，请重新构建。'}
if(!$OutputDirectory){$OutputDirectory=Join-Path $project 'releases'}
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$names=@('README.md','VERSION','CHANGELOG.md','CONTRIBUTING.md','SECURITY.md','.gitignore','.gitattributes','src','tools','tests','docs','design','AI工具管理器.exe')
$files=@($names|ForEach-Object {Join-Path $project $_})
foreach($file in $files){if(!(Test-Path -LiteralPath $file)){throw ('缺少文件：'+$file)}}
$archive=Join-Path $OutputDirectory ('AI-Tool-Manager-v'+$version+'.zip')
Compress-Archive -LiteralPath $files -DestinationPath $archive -Force
$exeHash=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
$zipHash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'SHA256SUMS.txt'),($exeHash+'  AI工具管理器.exe'+[Environment]::NewLine+$zipHash+'  '+[IO.Path]::GetFileName($archive)+[Environment]::NewLine),(New-Object Text.UTF8Encoding $false))
Write-Output ('PACKAGE_OK; archive='+$archive)
