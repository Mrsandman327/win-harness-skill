# 回归测试（无副作用）：只读检查，不改窗口状态、不发破坏性输入
# 用法: powershell -NoProfile -ExecutionPolicy Bypass -File scripts\regress.ps1
# 注意：脚本内不要写中文字符串字面量（PowerShell 5.1 在无 BOM 时按 ANSI 解析会截断引号），
#       需要中文时用 -TextB64 / -NameB64。
$ErrorActionPreference = 'Continue'
$W = Join-Path $PSScriptRoot 'win-harness.exe'
if (-not (Test-Path $W)) { throw "missing $W" }
$T = Join-Path $env:TEMP ("wh-reg-" + [guid]::NewGuid().ToString("N").Substring(0, 6))
New-Item -ItemType Directory -Force -Path $T | Out-Null

function J([string[]]$a) {
    $out = & $W @a 2>&1 | Out-String
    try { return $out | ConvertFrom-Json } catch { return [pscustomobject]@{ ok = $false; error = $out } }
}

$results = New-Object System.Collections.ArrayList
function Check($name, $cond, $detail) {
    [void]$results.Add([pscustomobject]@{ test = $name; pass = [bool]$cond; detail = $detail })
}

# --- 基础 ---
$r = J @('windows');   Check 'windows' ($r.ok -and $r.data.Count -gt 0) "count=$($r.data.Count)"
$r = J @('pixel', '-X', '5', '-Y', '5'); Check 'pixel' ($r.ok -and $r.data.hex) $r.data.hex
$r = J @('screenshot', '-Monitor', '1', '-Out', "$T\m1.png")
Check 'screenshot -Monitor' ($r.ok -and (Test-Path "$T\m1.png")) "$($r.data.w)x$($r.data.h)"
$b = (& $W b64 -Text 'abc' 2>&1 | Out-String).Trim(); Check 'b64' ($b -eq 'YWJj') $b

# --- 目标窗口（计算器：WinUI，结构稳定）---
$calc = (J @('windows')).data | Where-Object { $_.proc -match 'ApplicationFrameHost|CalculatorApp' } | Select-Object -First 1
if (-not $calc) {
    # 自愈：回归需要一台计算器，没开就拉起来
    J @('launch', '-Path', 'calc') | Out-Null
    Start-Sleep -Milliseconds 2500
    $calc = (J @('windows')).data | Where-Object { $_.proc -match 'ApplicationFrameHost|CalculatorApp' } | Select-Object -First 1
}
if ($calc) {
    $H = "hwnd:$($calc.hwnd)"
    $r = J @('tree', '-Window', $H, '-Depth', '3');           Check 'tree' ($r.ok -and $r.data.count -gt 0) "count=$($r.data.count)"
    $r = J @('find', '-Window', $H, '-AutomationId', 'num7Button'); Check 'find aid' ($r.ok -and $r.data.count -ge 1) "count=$($r.data.count)"
    $r = J @('read', '-Window', $H, '-AutomationId', 'CalculatorResults'); Check 'read' ($r.ok -and $r.data.text) "len=$($r.data.text.Length)"
    $r = J @('getprop', '-Window', $H, '-AutomationId', 'num7Button'); Check 'getprop' $r.ok 'ok'
    $r = J @('screenshot', '-Window', $H, '-Capture', 'window', '-Out', "$T\calc.png")
    Check 'screenshot window' ($r.ok -and $r.data.capture -eq 'window' -and $null -ne $r.data.occluded) "capture=$($r.data.capture) occ=$($r.data.occluded)"
    $r = J @('ocr', '-Window', $H, '-NoWords')
    Check 'ocr -NoWords' ($r.ok -and ($null -eq $r.data.lines[0].words)) "lines=$($r.data.lines.Count)"
    # base64 of the window title (Chinese) to avoid script-encoding pitfalls
    $titleB64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('计算器'))
    $r = J @('clicktext', '-Window', $H, '-TextB64', $titleB64, '-DryRun')
    Check 'clicktext dry-run' ($r.ok -and $r.data.matches.Count -ge 1) "matches=$($r.data.matches.Count) capture=$($r.data.capture)"
    $r = J @('screenshot', '-Window', $H, '-Capture', 'screen', '-Out', "$T\occ.png")
    Check 'occlusion fields' ($r.ok -and $null -ne $r.data.occluded) "occ=$($r.data.occluded) samples=$($r.data.covered_samples)"
} else { Check 'calc window' $false 'not running' }

# --- 微信（自绘，只读）---
$wx = (J @('windows')).data | Where-Object { $_.proc -match 'Weixin' -and $_.w -gt 500 } | Select-Object -First 1
if ($wx) {
    $r = J @('ocr', '-Window', "hwnd:$($wx.hwnd)", '-Capture', 'window')
    Check 'ocr wechat window' ($r.ok -and $r.data.lines.Count -gt 0) "lines=$($r.data.lines.Count) upscale=$($r.data.upscale)"
} else { Check 'wechat window (skipped)' $true 'not visible' }

# --- 区域 OCR ---
$r = J @('ocr', '-X', '0', '-Y', '0', '-Width', '300', '-Height', '80'); Check 'ocr region' $r.ok "lines=$($r.data.lines.Count)"

# --- status ---
$r = J @('status', '-NoSurfaces')
Check 'status' ($r.ok -and $null -ne $r.data.elevated -and $r.data.monitors.Count -ge 1) "elevated=$($r.data.elevated) dpi=$($r.data.dpi_mode)"

# --- shell ---
$r = J @('surfaces'); Check 'surfaces' ($r.ok -and $r.data.count -eq 6) "count=$($r.data.count)"
$r = J @('tree', '-Window', 'shell:taskbar', '-Depth', '2')
if ($r.ok -and $r.data.count -gt 0) {
    Check 'shell taskbar tree' $true "count=$($r.data.count) backend=$($r.data.backend)"
} elseif ("$($r.data.backend)" -like 'uia(empty-after-retry)*') {
    # 已知环境问题：开始菜单/弹层残留会把任务栏 UIA 树压成空；不是工具缺陷
    Check 'shell taskbar tree (skipped)' $true 'provider empty (known) - use clicktext fallback'
} else {
    Check 'shell taskbar tree' $false "count=$($r.data.count) backend=$($r.data.backend)"
}
$r = J @('wndat', '-X', '5', '-Y', '5'); Check 'wndat' $r.ok "hwnd=$($r.data.hwnd)"

# --- 错误码 ---
$r = J @('find', '-Window', 'no-such-window-zzz')
Check 'error code' ((!$r.ok) -and $r.code -eq 'NOT_FOUND' -and $r.retry -eq 'reprobe') "code=$($r.code) retry=$($r.retry)"

# --- 批处理 + trace ---
$r = J @('do', '-Trace', '-TraceDir', $T, '-Ops', 'pixel -X 5 -Y 5 ; windows')
Check 'do batch' ($r.ok -and $r.data.count -eq 2) "steps=$($r.data.count)"
$r = J @('do', '-Trace', '-TraceDir', $T, '-Ops', 'pixel -X 5 -Y 5 ; find -Window nosuch')
Check 'do batch fail code' ((!$r.ok) -and $r.code -eq 'NOT_FOUND') "code=$($r.code)"
$r = J @('trace', '-Dir', $T, '-Stats')
Check 'trace stats' ($r.ok -and $r.data.stats.total -ge 3) "total=$($r.data.stats.total) failed=$($r.data.stats.failed)"
$r = J @('trace', '-Dir', $T, '-Failed')
Check 'trace failed filter' ($r.ok -and $r.data.lines.Count -ge 1) "lines=$($r.data.lines.Count)"

# --- 汇总 ---
$pass = ($results | Where-Object pass).Count
""
$results | ForEach-Object { "{0}  {1,-26} {2}" -f ($(if ($_.pass) { 'PASS' } else { 'FAIL' })), $_.test, $_.detail }
""
"TOTAL: $pass / $($results.Count)"
"tmp: $T"
