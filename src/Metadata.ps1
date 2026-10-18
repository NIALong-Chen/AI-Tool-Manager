function Read-ToolText([string]$File) {
 $f=Get-Item -LiteralPath $File -Force -ErrorAction Stop
 if($f.Length -gt 2MB -or ($f.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw '说明文件过大或为链接'}
 [IO.File]::ReadAllText($f.FullName)
}
function Convert-ToolJson([string]$Text) {
 $b=New-Object Text.StringBuilder;$quoted=$false;$escaped=$false
 for($i=0;$i -lt $Text.Length;$i++){
  $c=$Text[$i]
  if($quoted){[void]$b.Append($c);if($escaped){$escaped=$false}elseif($c -eq '\'){$escaped=$true}elseif($c -eq '"'){$quoted=$false};continue}
  if($c -eq '"'){$quoted=$true;[void]$b.Append($c);continue}
  if($c -eq '/' -and $i+1 -lt $Text.Length -and $Text[$i+1] -eq '/') {while($i -lt $Text.Length -and [int]$Text[$i] -ne 10){$i++};[void]$b.Append(' ');continue}
  if($c -eq '/' -and $i+1 -lt $Text.Length -and $Text[$i+1] -eq '*') {$i+=2;while($i+1 -lt $Text.Length -and !($Text[$i] -eq '*' -and $Text[$i+1] -eq '/')){$i++};$i++;[void]$b.Append(' ');continue}
  [void]$b.Append($c)
 }
 $clean=$b.ToString();$b=New-Object Text.StringBuilder;$quoted=$false;$escaped=$false
 for($i=0;$i -lt $clean.Length;$i++){$c=$clean[$i];if($quoted){[void]$b.Append($c);if($escaped){$escaped=$false}elseif($c -eq '\'){$escaped=$true}elseif($c -eq '"'){$quoted=$false};continue};if($c -eq '"'){$quoted=$true};if($c -eq ','){$j=$i+1;while($j -lt $clean.Length -and [char]::IsWhiteSpace($clean[$j])){$j++};if($j -lt $clean.Length -and $clean[$j] -in @('}',']')){continue}};[void]$b.Append($c)}
 $b.ToString()|ConvertFrom-Json -ErrorAction Stop
}
function Get-ToolMetadata($Item) {
 $description='';$usage=@();$doc='';$raw='';$source='未找到本地功能文档';$title=$Item.Name;$repo='';$base=$Item.Path
 if(Test-Path -LiteralPath $base -PathType Leaf){$base=[IO.Path]::GetDirectoryName($base)}
 try{
  if($Item.Category -eq 'skills'){
   $doc=$Item.Path;$raw=Read-ToolText $doc;$source='本地 SKILL.md'
   if($raw -match '(?s)\A\s*---\r?\n(.*?)\r?\n---'){
    $yaml=$matches[1]
    if($yaml -match '(?m)^name:\s*(.+)$'){$candidate=$matches[1].Trim().Trim('"',"'");if($candidate.Length -lt 120){$title=$candidate}}
    if($yaml -match '(?m)^description:\s*(.*)$'){
     $description=$matches[1].Trim()
     if($description -match '^[>|][-+]?$'){$lines=@();$capture=$false;foreach($line in ($yaml -split '\r?\n')){if($line -match '^description:'){$capture=$true;continue};if($capture){if($line -match '^\S'){break};$lines+=$line.Trim()}};$description=$lines -join ' '}
     $description=$description.Trim('"',"'")
    }
   }
  }elseif($Item.Category -eq 'plugins'){
   $manifest=if([IO.Path]::GetExtension($Item.Path) -eq '.json'){$Item.Path}else{Join-Path $Item.Path 'package.json'}
   if(Test-Path -LiteralPath $manifest){$m=Convert-ToolJson (Read-ToolText $manifest);if($m.description -is [string]){$description=$m.description};if($m.displayName -is [string]){$title=$m.displayName};$source='本地插件清单';if($base -match '\\[.]((codex)|(claude))-plugin$'){$base=[IO.Path]::GetDirectoryName($base)}}
  }
  if(!$doc -and $Item.Category -in @('skills','plugins','projects') -and (Test-Path -LiteralPath $base -PathType Container)){
   foreach($n in @('README.zh-CN.md','README_CN.md','README_zh.md','README.md','README.txt')){$p=Join-Path $base $n;if(Test-Path -LiteralPath $p -PathType Leaf){$doc=$p;break}}
   if($doc){$raw=Read-ToolText $doc;if(!$description){foreach($line in ($raw -split '\r?\n')){if($line.Trim().Length -gt 30 -and $line -notmatch '^\s*(#|!|<|\[|\x60|\|)'){$description=$line.Trim();break}}};$source='本地 README 摘要'}
  }
  if($raw){$capture=$false;$lines=@();foreach($line in ($raw -split '\r?\n')){if($line -match '^#{1,3}\s+.*(使用|快速开始|Usage|Quick.?start|Getting.started|How.to.use)'){$capture=$true;continue};if($capture){if($line -match '^#{1,3}\s'){break};if($line.Trim()){$lines+=$line};if(($lines -join [Environment]::NewLine).Length -gt 3500){break}}};if($lines.Count){$u=$lines -join [Environment]::NewLine;$usage=@($u.Substring(0,[Math]::Min(4000,$u.Length)))}}
  if($Item.Category -eq 'projects'){$git=Join-Path $base '.git/config';if(Test-Path -LiteralPath $git){$g=Read-ToolText $git;if($g -match '(?im)^\s*url\s*=\s*(?:https://(?:[^/\s@]+@)?github\.com/|git@github\.com:)([a-zA-Z0-9_.-]+)/([a-zA-Z0-9_.-]+)\s*$'){$repo='https://github.com/'+$matches[1]+'/'+($matches[2] -replace '\.git$','')}}}
 }catch{$source='本地说明无法读取，显示类型说明';$doc=''}
 if(!$description){switch($Item.Category){
  'skills'{$description='本地存在技能文件，但未提取到功能描述。请阅读原始说明，并在所属客户端确认是否可以调用。'}
  'mcp'{$description='本地发现 MCP 服务配置。未找到可确认的功能文档，请在所属客户端查看服务提供者、功能与权限；此处尚未连接服务。'}
  'plugins'{$description='本地存在插件文件或配置，未找到功能描述。请在客户端确认安装、启用和权限，并查看原始说明。'}
  'projects'{$description='本地发现源码项目，未提取到功能摘要。请先阅读 README；运行环境与依赖尚未检查。'}
  'downloads'{$description='这是安装程序或归档文件。发现文件不代表软件已安装，也不能确认它属于 AI 工具；请核对来源与发布方。'}
  'apps'{$description='系统登记或 PATH 中存在软件入口。尚未启动软件检查，请从原有入口打开并查看软件内帮助。';if($Item.Name -match '(?i)ollama'){$description='在本地管理和运行语言模型。模型可用性与速度取决于模型、硬件和本机环境。'}elseif($Item.Name -match '(?i)codex|claude|cursor|windsurf|gemini|aider|opencode'){$description='AI 辅助工作或开发工具。请在软件内选择项目和模型，并确认账户、权限与可用功能。'}elseif($Item.Name -match '(?i)ChatGPT'){$description='用于与 AI 对话、分析资料和辅助写作的应用。具体功能以本机软件与账户可用项为准。'}}
 }}
 if(!$usage.Count){switch($Item.Category){
  'skills'{$usage=@('在所属客户端的技能列表中确认此技能。','提供任务目标和必要文件，让客户端调用该技能。','核对生成结果；工具管家不会执行 SKILL.md 中的内容。')}
  'mcp'{$usage=@('在所属客户端查看服务提供者、配置和权限。','按服务文档准备依赖或账户，并核对启用状态。','在客户端确认连接成功后再使用；本工具不会修改配置或启动服务。')}
  'plugins'{$usage=@('在所属客户端的插件管理中查找此插件。','查看文档、权限和依赖，并确认启用状态。','在客户端中使用插件提供的功能。')}
  'projects'{$usage=@('先阅读 README，核对来源与系统要求。','按原始文档准备环境和依赖。','自行根据文档启动；工具管家不会执行项目命令。')}
  'downloads'{$usage=@('核对下载来源、发布方与完整性。','需要时在资源管理器中自行安装或解压。','完成后刷新清单；源码目录可通过“管理目录”加入。')}
  'apps'{$usage=@('从 Windows 开始菜单或原有入口打开软件。','根据软件帮助配置账户、模型或项目。','安装登记不代表软件一定能够正常运行。')}
 }}
 if($description.Length -gt 1800){$description=$description.Substring(0,1800)+'…'}
 [pscustomobject]@{Title=$title;Description=$description;Usage=[string[]]$usage;DocPath=$doc;DocSource=$source;RepositoryUrl=$repo}
}
