# Windows PowerShell 5.1; read-only discovery. Never returns raw configuration values.
function Get-AiInventory {
 param([string]$HomePath=$env:USERPROFILE,[string[]]$AdditionalRoots=@(),[switch]$SkipInstalledApps,[hashtable]$Control=@{})
 $roots=New-Object 'System.Collections.Generic.List[object]'
 $items=New-Object 'System.Collections.Generic.List[object]'; $warnings=New-Object 'System.Collections.Generic.List[string]'; $seen=@{}; $visited=@{}; $budget=15000; $script:scanCount=0
 function Add-Entry($name,$category,$status,$client,$location,$evidence) {
  if(!$name){return};$key=("$category|$location|$name").ToLowerInvariant();if($seen.ContainsKey($key)){return};$seen[$key]=$true
  $cat=switch($category){'Skills'{'skills'};'MCP 服务'{'mcp'};'插件与扩展'{'plugins'};'GitHub / 本地项目'{'projects'};'已安装应用'{'apps'};'安装包与归档'{'downloads'}}
  $note='';$state=$status
  switch($cat){
   'skills'{$state='技能文件已找到';$note='客户端加载情况未检测';if($location -match '\\plugins\\'){$state='插件附带技能';$note='来自插件目录，启用情况未检测'}}
   'mcp'{$state=if($status -eq '已配置'){'已写入客户端配置'}elseif($status -eq '禁用'){'配置中已禁用'}else{'附带配置或示例'};$note='尚未测试连接'}
   'plugins'{$state='插件文件已找到';$note='启用情况未检测';if($status -eq '已配置'){$state='存在插件配置';$note='插件文件与加载情况未检测'};if($evidence -match '标记为启用'){$state='配置标记为启用';$note='实际加载未检测'}elseif($evidence -match '标记为禁用'){$state='配置中已禁用';$note='根据客户端配置'}}
   'projects'{$state='源码目录已找到';$note='依赖与启动未检查'}
   'downloads'{$state=if($location -match '\.(exe|msi|msix|msixbundle|appx)$'){'安装包已下载'}else{'归档文件已下载'};$note='不代表已安装，来源未确认'}
   'apps'{$state=if($client -eq 'PATH 命令入口'){'命令入口已找到'}else{'系统中有安装记录'};$note='尚未启动检查'}
  }
  $sha=[Security.Cryptography.SHA256]::Create();try{$id=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($key)))).Replace('-','').Substring(0,24)}finally{$sha.Dispose()}
  $modified='';try{$modified=(Get-Item -LiteralPath $location -ErrorAction Stop).LastWriteTimeUtc.Ticks.ToString()}catch{}
  $items.Add([pscustomobject]@{Id=$id;Name=[string]$name;Category=[string]$cat;Status=[string]$state;Note=[string]$note;Client=[string]$client;Path=[string]$location;Evidence=[string]$evidence;Modified=$modified})
 }
 function Read-Small($file) { if((Get-Item -LiteralPath $file -ErrorAction Stop).Length -gt 2MB){throw '文件超过 2MB，已跳过'}; [IO.File]::ReadAllText($file) }
 function Read-Config($file,$client,$status='已配置') {
  if($Control.Cancel){return}
  if(!(Test-Path -LiteralPath $file -PathType Leaf)){return}
  try {
   $raw=Read-Small $file; $found=0
   if([IO.Path]::GetExtension($file) -eq '.toml') {
    foreach($m in [regex]::Matches($raw,'(?m)^\s*\[mcp_servers\.(?:"([^"\r\n]+)"|([A-Za-z0-9_-]+))\]\s*(?:#.*)?$')) {
     $n=$m.Groups[1].Value;if(!$n){$n=$m.Groups[2].Value};$tail=$raw.Substring($m.Index+$m.Length);$next=[regex]::Match($tail,'(?m)^\s*\[');if($next.Success){$tail=$tail.Substring(0,$next.Index)};$serverStatus=$status;if($status -eq '已配置' -and $tail -match '(?m)^\s*(enabled\s*=\s*false|disabled\s*=\s*true)\s*(#.*)?$'){$serverStatus='禁用'};Add-Entry $n 'MCP 服务' $serverStatus $client $file $(if($serverStatus -eq '禁用'){'客户端配置显式标记服务为禁用。'}elseif($status -eq '已配置'){'已知配置位置存在 MCP 配置段；未验证连接或启用状态。'}else{'发现候选 MCP 配置段；可能是模板或缓存，未确认客户端加载。'});$found++
    }
    if($raw -match '(?m)^\s*\[mcp_servers' -and !$found){$warnings.Add("无法识别 MCP TOML 段：$file")}
   } else {
    $obj=Convert-ToolJson $raw
    $maps=@(); if($obj.mcpServers){$maps+=,$obj.mcpServers}; if($obj.mcp.servers){$maps+=,$obj.mcp.servers}; if($obj.servers -and $file -match 'mcp.json$'){$maps+=,$obj.servers}
    foreach($map in $maps){foreach($p in $map.PSObject.Properties){$entryStatus=$status;if($status -eq '已配置' -and (($p.Value.disabled -is [bool] -and $p.Value.disabled) -or ($p.Value.enabled -is [bool] -and !$p.Value.enabled))){$entryStatus='禁用'};Add-Entry $p.Name 'MCP 服务' $entryStatus $client $file $(if($entryStatus -eq '禁用'){'客户端配置显式标记服务为禁用。'}elseif($status -eq '已配置'){'已知配置位置存在 MCP 配置项；未验证连接或启用状态。'}else{'发现候选 MCP 配置项；可能是模板或缓存，未确认客户端加载。'})}}
    if($obj.enabledPlugins){foreach($p in $obj.enabledPlugins.PSObject.Properties){$enabled=($p.Value -eq $true); Add-Entry $p.Name '插件与扩展' '已配置' $client $file $(if($p.Value -isnot [bool]){'插件配置项存在，但启用标记不是布尔值；未检查插件文件。'}elseif($enabled){'插件配置标记为启用；未验证运行状态。'}else{'插件配置标记为禁用；未验证安装文件。'})}}
   }
  } catch {$warnings.Add("配置未读取（格式或访问限制）：$file")}
 }
 function Walk($dir,$depth,$client,$mode) {
  if($Control.Cancel -or $script:scanCount -ge $budget){return}; if(!(Test-Path -LiteralPath $dir -PathType Container)){return}
  try {
   $di=Get-Item -LiteralPath $dir -Force -ErrorAction Stop
   if($di.Attributes -band [IO.FileAttributes]::ReparsePoint){return}
   $key=$di.FullName.ToLowerInvariant(); if($visited.ContainsKey($key)){return}; $visited[$key]=$true; $script:scanCount++
   if(Test-Path -LiteralPath (Join-Path $dir 'SKILL.md') -PathType Leaf) {Add-Entry $di.Name 'Skills' '待验证' $client (Join-Path $di.FullName 'SKILL.md') '存在 SKILL.md；未执行内容，未确认客户端加载或启用。'}
   if((Test-Path -LiteralPath (Join-Path $dir '.git')) -or (($mode -in @('custom','downloads')) -and (Test-Path -LiteralPath (Join-Path $dir 'README.md')) -and @(@('package.json','pyproject.toml','requirements.txt','Cargo.toml','environment.yml') | Where-Object {Test-Path -LiteralPath (Join-Path $dir $_)}).Count -gt 0)){Add-Entry $di.Name 'GitHub / 本地项目' '待验证' $client $di.FullName '存在 Git 标记，或 README 与项目清单；依赖与启动尚未检查。'}
   if($mode -eq 'extensions') {
    $manifest=Join-Path $dir 'package.json'
    if(Test-Path -LiteralPath $manifest -PathType Leaf){try{$p=(Read-Small $manifest)|ConvertFrom-Json; if($p.publisher -and $p.engines.vscode){Add-Entry $p.name '插件与扩展' '待验证' $client $di.FullName '存在编辑器扩展清单；未验证客户端加载，扩展未必与 AI 有关。'}}catch{$warnings.Add("扩展清单无法解析：$manifest")}}
   }
   if($di.Name -in @('.claude-plugin','.codex-plugin')) {
    $manifest=Join-Path $dir 'plugin.json'; if(Test-Path -LiteralPath $manifest){try{$p=(Read-Small $manifest)|ConvertFrom-Json; Add-Entry $p.name '插件与扩展' '待验证' $client $manifest '存在客户端插件清单；缓存或源码不代表已启用。'}catch{$warnings.Add("插件清单无法解析：$manifest")}}
   }
   $children=@(Get-ChildItem -LiteralPath $dir -Force -ErrorAction Stop)
   if($mode -eq 'downloads' -or $mode -eq 'custom') {foreach($f in $children){if(!$f.PSIsContainer -and $f.Extension -match '^\.(exe|msi|msix|msixbundle|zip|7z|rar|tar|gz|whl|appx)$'){Add-Entry $f.Name '安装包与归档' '已下载' $client $f.FullName '仅发现文件；未执行、未解压、未确认是否为 AI 工具。'}}}
   foreach($f in $children){if(!$f.PSIsContainer -and $f.Name -in @('.mcp.json','mcp.json')){Read-Config $f.FullName $client '待验证'}}
   if($depth -gt 0){foreach($sub in $children){if($Control.Cancel){break}; if($sub.PSIsContainer -and $sub.Name -notin @('node_modules','.git','.venv','venv','dist','build','__pycache__','GPUCache','.sandbox-secrets','.aws','.ssh','.azure','.gnupg','credentials','secrets','.tmp','sessions','archived_sessions','logs','visualizations')){Walk $sub.FullName ($depth-1) $client $mode}}}
  }catch{$warnings.Add("目录无法完整扫描：$dir")}
 }
 Read-Config (Join-Path $HomePath '.codex/config.toml') 'Codex'; Read-Config (Join-Path $HomePath '.claude.json') 'Claude Code'; Read-Config (Join-Path $HomePath '.claude/settings.json') 'Claude Code'
 Read-Config (Join-Path $HomePath '.cursor/mcp.json') 'Cursor'; Read-Config (Join-Path $HomePath '.windsurf/mcp.json') 'Windsurf'
 Read-Config (Join-Path $HomePath '.codeium/windsurf/mcp_config.json') 'Windsurf'
 Read-Config (Join-Path $HomePath 'AppData/Roaming/Claude/claude_desktop_config.json') 'Claude Desktop'
 Read-Config (Join-Path $HomePath 'AppData/Roaming/Code/User/mcp.json') 'VS Code'
 $downloads=Join-Path $HomePath 'Downloads'
 if($HomePath -eq $env:USERPROFILE){try{$known=(Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders' -ErrorAction Stop).'{374DE290-123F-4565-9164-39C4925E467B}';if($known){$expanded=[Environment]::ExpandEnvironmentVariables($known);if($expanded -match '^[A-Za-z]:\\'){$downloads=$expanded}}}catch{}}
 foreach($spec in @(@('.codex','Codex',10),@('.claude','Claude Code',8),@('.agents','共享 Skills',5),@('.cursor','Cursor',4),@('.vscode/extensions','VS Code',1),@('.windsurf','Windsurf',4),@('Downloads','下载目录',3),@('source/repos','本地项目',3))) {
  $mode='standard'; if($spec[0] -eq 'Downloads'){$mode='downloads'}; if($spec[0] -eq '.vscode/extensions'){$mode='extensions'}
  $p=if($spec[0] -eq 'Downloads'){$downloads}else{Join-Path $HomePath $spec[0]};$roots.Add([pscustomobject]@{Path=$p;Label=$spec[1];Depth=[int]$spec[2]});Walk $p ([int]$spec[2]) $spec[1] $mode
 }
 foreach($dir in $AdditionalRoots){$roots.Add([pscustomobject]@{Path=$dir;Label='自选目录';Depth=6});Read-Config (Join-Path $dir '.mcp.json') '项目配置'; Read-Config (Join-Path $dir 'mcp.json') '项目配置'; Walk $dir 6 '自选目录' 'custom'; Read-Config (Join-Path $dir '.codex/config.toml') '项目 Codex'; Read-Config (Join-Path $dir '.claude/settings.json') '项目 Claude'}
 if(!$SkipInstalledApps -and !$Control.Cancel){
  foreach($command in @('codex','claude','ollama','aider','gemini','opencode')) {
   $entry=Get-Command $command -CommandType Application,ExternalScript -ErrorAction SilentlyContinue | Select-Object -First 1
   if($entry -and (Test-Path -LiteralPath $entry.Source -PathType Leaf)){Add-Entry $command '已安装应用' '已安装' 'PATH 命令入口' $entry.Source 'PATH 中存在命令入口文件；未执行命令，未验证依赖和运行状态。'}
  }
  foreach($reg in @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*')) {
   foreach($a in @(Get-ItemProperty -Path $reg -ErrorAction SilentlyContinue)) {
    if($a.DisplayName -match '(?i)(\bCodex\b|\bClaude\b|\bChatGPT\b|\bCursor\b|\bWindsurf\b|\bOllama\b|\bLM Studio\b|\bJan\b|\bAnythingLLM\b|\bGPT4All\b|\bCherry Studio\b|\bComfyUI\b|\bGitHub Copilot\b)') {
     $loc=$a.PSPath -replace '^Microsoft.PowerShell.Core\\Registry::','Registry::'; $proof='Windows 卸载注册表存在应用登记；未验证应用能否运行。'
     if($a.InstallLocation -and (Test-Path -LiteralPath $a.InstallLocation -PathType Container)){$loc=$a.InstallLocation; $proof+=' 安装目录存在。'}
     Add-Entry $a.DisplayName '已安装应用' '已安装' 'Windows 应用登记' $loc $proof
    }
   }
  }
  try{foreach($a in @(Get-AppxPackage -ErrorAction Stop | Where-Object {$_.Name -match '(?i)(OpenAI|ChatGPT|Claude|Ollama|LMStudio|Windsurf)'})){Add-Entry $a.Name '已安装应用' '已安装' 'Windows 应用包' $a.InstallLocation '当前用户存在 Windows 应用包登记；未启动应用。'}}catch{$warnings.Add('无法读取 Windows 应用包清单；其余结果仍可使用。')}
 }
 if($script:scanCount -ge $budget){$warnings.Add('达到 15000 个目录上限，部分位置未扫描。')}; if($Control.Cancel){$warnings.Add('扫描已取消，显示已完成部分。')}
 foreach($entry in $items){if($Control.Cancel){break};$metadata=Get-ToolMetadata $entry;foreach($p in $metadata.PSObject.Properties){$entry|Add-Member -NotePropertyName $p.Name -NotePropertyValue $p.Value}}
 [pscustomobject]@{Roots=@($roots.ToArray());Items=@($items.ToArray());Warnings=@($warnings.ToArray());Directories=$script:scanCount;Cancelled=[bool]$Control.Cancel;ScannedAt=(Get-Date).ToString('yyyy-MM-dd HH:mm:ss')}
}
