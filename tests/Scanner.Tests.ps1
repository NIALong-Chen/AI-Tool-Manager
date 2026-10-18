param([string]$FixtureRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../src/Metadata.ps1')
. (Join-Path $PSScriptRoot '../src/Inventory.ps1')
if(!$FixtureRoot){$FixtureRoot=Join-Path ([IO.Path]::GetTempPath()) ('AI-Tool-Manager-Test-'+[guid]::NewGuid().ToString('N'))}
[void][IO.Directory]::CreateDirectory($FixtureRoot)
function Put($name,$text){$p=Join-Path $FixtureRoot $name;[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($p));[IO.File]::WriteAllText($p,$text,[Text.Encoding]::UTF8)}
function Assert($ok,$name){if(!$ok){throw "FAIL: $name"};Write-Output "PASS: $name"}
Put '.ai-manager-test' 'isolated test only'
Put '.codex/skills/sample-skill/SKILL.md' '---
name: sample-skill
description: 真实文档摘要，用来验证本地技能说明的读取。
---
# 使用
提供目标和文件，在客户端调用技能。
'
Put '.codex/config.toml' '[mcp_servers.demo]
command = "DO_NOT_RUN"
[mcp_servers.disabled]
enabled = false
command = "DO_NOT_RUN"
[mcp_servers."name.with.dot"]
env = { API_KEY = "secret-marker-123" }
'
Put '.cursor/mcp.json' '{
// comment outside strings
"mcpServers": {
 "cursor-test": {"url":"https://example.invalid/a//b", "env":{"TOKEN":"secret-marker-123"}},
 "cursor-disabled": {"disabled":true},
},
}'
Put '.claude/settings.json' '{"enabledPlugins":{"test@market":false,"unknown@market":"ask"}}'
Put 'Downloads/setup.exe' 'not an executable'
Put 'Downloads/普通归档.zip' 'not an archive'
Put 'tools/project/.git/config' '[remote "origin"]
 url = https://user:private-token@github.com/example/my-tool.git
'
Put 'tools/project/README.md' '# 本地源码工具
这个真实测试项目的说明来自本地 README，用于检查源码功能说明是否被正确提取。
## Usage
Read the source documentation before running.
'
Put 'tools/unpacked/README.md' '# Unpacked tool
This is an extracted source project with documentation and a project manifest.
'
Put 'tools/unpacked/pyproject.toml' '[project]
name="test"'
Put '.codex/plugins/cache/provider/test/1/.codex-plugin/plugin.json' '{"name":"doc-plugin","description":"本地插件的真实功能说明。"}'
Put '.codex/plugins/cache/provider/test/1/skills/deep/SKILL.md' '---
name: deep
description: >
  这是分行的技能说明。
  第二行也应保留。
---'
Put 'tools/templates/mcp.json' '{"mcpServers":{"template":{"command":"DO_NOT_RUN"}}}'
Put 'tools/broken/mcp.json' '{invalid'
Put 'tools/node_modules/ignore/SKILL.md' 'ignored'
$before=@{};Get-ChildItem -LiteralPath $FixtureRoot -Recurse -File -Force|ForEach-Object {$before[$_.FullName]=(Get-FileHash -LiteralPath $_.FullName).Hash}
$r=Get-AiInventory -HomePath $FixtureRoot -AdditionalRoots @((Join-Path $FixtureRoot 'tools')) -SkipInstalledApps
Assert (@($r.Items|Where-Object Category -eq 'skills').Count -eq 2) 'skills and plugin-bundled skills'
Assert (@($r.Items|Where-Object Category -eq 'mcp').Count -eq 6) 'TOML and JSONC MCP configurations'
Assert (@($r.Items|Where-Object {$_.Name -in @('disabled','cursor-disabled') -and $_.Status -eq '配置中已禁用'}).Count -eq 2) 'explicit disabled state from TOML and JSON'
Assert (@($r.Items|Where-Object {$_.Name -eq 'template' -and $_.Status -eq '附带配置或示例'}).Count -eq 1) 'templates do not imply live configuration'
Assert (($r.Items|Where-Object Name -eq 'unknown@market').Status -eq '存在插件配置') 'unrecognized plugin flag never implies installed files or disabled state'
Assert (@($r.Items|Where-Object Category -eq 'projects').Count -eq 2) 'Git and unpacked projects'
Assert (@($r.Items|Where-Object Category -eq 'downloads').Count -eq 2) 'downloaded files without execution'
Assert (($r.Items|Where-Object Name -eq 'sample-skill').Description -match '真实文档摘要') 'skill description read from actual document'
Assert (($r.Items|Where-Object Name -eq 'deep').Description -match '第二行') 'folded YAML descriptions'
Assert (($r.Items|Where-Object Name -eq 'doc-plugin').Description -match '真实功能') 'plugin manifest descriptions'
Assert (($r.Items|Where-Object Name -eq 'project').RepositoryUrl -eq 'https://github.com/example/my-tool') 'repository credentials stripped'
Assert (($r|ConvertTo-Json -Depth 8) -notmatch 'secret-marker-123|private-token|DO_NOT_RUN|待验证') 'no configuration secrets, commands or ambiguous status in results'
Assert (@($r.Warnings).Count -eq 1) 'malformed file warning'
Assert (@($r.Items.Id|Select-Object -Unique).Count -eq $r.Items.Count) 'stable unique identities'
$unchanged=$true;foreach($f in $before.Keys){if((Get-FileHash -LiteralPath $f).Hash -ne $before[$f]){$unchanged=$false}}
Assert ($unchanged -and @(Get-ChildItem $FixtureRoot -Recurse -File -Force).Count -eq $before.Count) 'scan leaves all file contents and file counts unchanged'
$r2=Get-AiInventory -HomePath $FixtureRoot -AdditionalRoots @((Join-Path $FixtureRoot 'tools'),(Join-Path $FixtureRoot 'tools')) -SkipInstalledApps
Assert ($r2.Items.Count -eq $r.Items.Count) 'overlapping roots deduplicated'
$r3=Get-AiInventory -HomePath $FixtureRoot -SkipInstalledApps -Control @{Cancel=$true}
Assert ($r3.Cancelled -and $r3.Items.Count -eq 0) 'cancellation before scan'
Write-Output ('SCANNER_TESTS_OK; Fixture='+$FixtureRoot)
