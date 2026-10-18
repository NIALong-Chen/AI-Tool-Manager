$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
if(!(Test-Path (Join-Path $framework 'csc.exe'))){$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319'}
$automation=Get-ChildItem -Path (Join-Path $env:WINDIR 'Microsoft.NET/assembly/GAC_MSIL/System.Management.Automation/*/System.Management.Automation.dll') | Select-Object -First 1
if(!$automation){throw '需要 Windows PowerShell 5.1 系统组件。'}
Add-Type -AssemblyName System.Drawing
$bmp=New-Object Drawing.Bitmap 64,64;$g=[Drawing.Graphics]::FromImage($bmp);$g.SmoothingMode='AntiAlias';$g.Clear([Drawing.Color]::FromArgb(70,89,208));$pen=New-Object Drawing.Pen ([Drawing.Color]::White),3
foreach($y in @(22,32,42)){$points=[Drawing.PointF[]]@((New-Object Drawing.PointF 13,$y),(New-Object Drawing.PointF 32,($y-10)),(New-Object Drawing.PointF 51,$y),(New-Object Drawing.PointF 32,($y+10)));$g.DrawPolygon($pen,$points)}
$icon=[Drawing.Icon]::FromHandle($bmp.GetHicon());$ico=Join-Path $project 'src/app.ico';$stream=[IO.File]::Create($ico);try{$icon.Save($stream)}finally{$stream.Dispose();$icon.Dispose();$g.Dispose();$bmp.Dispose();$pen.Dispose()}
$arguments=@('/nologo','/target:winexe','/platform:anycpu','/optimize+','/utf8output',('/out:'+(Join-Path $project 'AI工具管理器.exe')),('/win32icon:'+$ico),('/win32manifest:'+(Join-Path $project 'src/app.manifest')))
foreach($dll in @('System.dll','System.Core.dll','System.Web.Extensions.dll','System.Windows.Forms.dll','System.Drawing.dll','System.Xaml.dll')){$arguments+=('/reference:'+(Join-Path $framework $dll))}
foreach($dll in @('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll')){$arguments+=('/reference:'+(Join-Path $framework ('WPF/'+$dll)))}
$arguments+=('/reference:'+$automation.FullName)
foreach($res in @('MainWindow.xaml','Inventory.ps1','Metadata.ps1','app.ico')){$arguments+=('/resource:'+(Join-Path $project ('src/'+$res))+','+$res)}
$arguments+=(Join-Path $project 'src/Program.cs')
& (Join-Path $framework 'csc.exe') $arguments
if($LASTEXITCODE -ne 0){throw '编译失败。'}
Write-Output 'BUILD_OK'
