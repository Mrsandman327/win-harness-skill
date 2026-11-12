# 一致性校验：文档声明的命令/统计数字 vs 实际产物
# 用法: powershell -NoProfile -ExecutionPolicy Bypass -File scripts\check-consistency.ps1
# 退出码 = 不一致项数量（0 表示全对），可直接用于提交前检查。
$ErrorActionPreference = 'Continue'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $PSScriptRoot 'win-harness.exe'
$skillPath = Join-Path $root 'SKILL.md'
$readmePath = Join-Path $root 'README.md'
$regressPath = Join-Path $PSScriptRoot 'regress.ps1'

$fail = 0
function Say($ok, $msg) {
    if ($ok) { "PASS  $msg" } else { "FAIL  $msg"; $script:fail++ }
}

# --- 1) help 命令集合 ---
$help = ((& $exe help 2>&1 | Out-String) | ConvertFrom-Json).data.help
$helpCmds = [regex]::Matches($help, '(?m)^  ([a-z][a-z0-9]*)\s') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique

# --- 2) SKILL 命令表（只扫「命令速查」章节，避免把 -Capture/字段表里的裸标识符当成命令）---
$skill = Get-Content $skillPath -Encoding UTF8
$start = ($skill | Select-String -Pattern '^## 命令速查' | Select-Object -First 1).LineNumber
$end = ($skill | Select-String -Pattern '^## ' | Where-Object { $_.LineNumber -gt $start } | Select-Object -First 1).LineNumber
$section = $skill[($start)..($end - 2)]
$skillCmds = $section | Where-Object { $_ -match '^\| `[a-z]+' } |
    ForEach-Object { [regex]::Match($_, '^\| `([a-z][a-z0-9]*)').Groups[1].Value } | Sort-Object -Unique

$docOnly = @($skillCmds | Where-Object { $helpCmds -notcontains $_ })
$helpOnly = @($helpCmds | Where-Object { $skillCmds -notcontains $_ })
Say ($docOnly.Count -eq 0) ("SKILL 命令表都在 help 里" + $(if ($docOnly.Count) { " (多余: $($docOnly -join ','))" } else { "" }))
Say ($helpOnly.Count -eq 0) ("help 命令都在 SKILL 命令表里" + $(if ($helpOnly.Count) { " (缺文档: $($helpOnly -join ','))" } else { "" }))

# --- 3) README 统计数字 ---
$readme = Get-Content $readmePath -Encoding UTF8 -Raw

# 命令数：help 主命令 + b64（b64 走特殊路径，不出现在 help）
$actualCmd = $helpCmds.Count
if ($readme -match '命令数：\*\*(\d+)\*\*') {
    $claim = [int]$Matches[1]
    Say ($claim -eq $actualCmd) "README 命令数 $claim = 实际 $actualCmd"
} else { Say $false 'README 未找到命令数声明' }

# C# 文件数与总行数
$cs = Get-ChildItem (Join-Path $PSScriptRoot 'src\*.cs')
$actualFiles = $cs.Count
$actualLines = ($cs | ForEach-Object { (Get-Content $_.FullName).Count } | Measure-Object -Sum).Sum
if ($readme -match '源码：\*\*(\d+) 个 C# 文件\*\* / 约 \*\*(\d+) 行\*\*') {
    $cf = [int]$Matches[1]; $cl = [int]$Matches[2]
    Say ($cf -eq $actualFiles) "README 文件数 $cf = 实际 $actualFiles"
    $diff = [math]::Abs($cl - $actualLines) / [math]::Max(1, $actualLines)
    Say ($diff -le 0.03) "README 行数 $cl ≈ 实际 $actualLines（偏差 $([math]::Round($diff*100))%）"
} else { Say $false 'README 未找到源码统计声明' }

# exe 体积
$kb = [int][math]::Round((Get-Item $exe).Length / 1kb)
if ($readme -match '约 \*\*(\d+) KB\*\*') {
    $ckb = [int]$Matches[1]
    Say ([math]::Abs($ckb - $kb) -le 3) "README exe 体积 $ckb KB ≈ 实际 $kb KB"
} else { Say $false 'README 未找到 exe 体积声明' }

# --- 4) 回归项数：只要求"够多"，不追精确值（条件分支会让静态计数与运行时总数不同）---
$regress = Get-Content $regressPath -Encoding UTF8 -Raw
$actualChecks = ([regex]::Matches($regress, "Check '" )).Count
Say ($actualChecks -ge 20) "regress.ps1 检查项 $actualChecks 项（README 声明 20+）"

""
if ($fail -eq 0) { "OK: 文档与产物一致" } else { "发现 $fail 处不一致" }
exit $fail
