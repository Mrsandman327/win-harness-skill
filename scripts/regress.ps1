# 回归测试（无副作用）：只读检查，不改窗口状态、不发破坏性输入
# 用法: powershell -NoProfile -ExecutionPolicy Bypass -File scripts\regress.ps1
$ErrorActionPreference = 'Continue'
$W = Join-Path $PSScriptRoot 'win-harness.exe'
if (-not (Test-Path $W)) { throw "找不到 $W" }
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

# --- 目标窗口（优先计算器：WinUI，结构稳定） ---
$calc = (J @('windows')).data | Where-Object { $_.proc -match 'ApplicationFrameHost|CalculatorApp' } | Select-Object -First 1
if ($calc) {
    $H = "hwnd:$($calc.hwnd)"
    $r = J @('tree', '-Window', $H, '-Depth', '3');           Check 'tree' ($r.ok -and $r.data.count -gt 0) "count=$($r.data.count)"
    $r = J @('find', '-Window', $H, '-AutomationId', 'num7Button'); Check 'find aid' ($r.ok -and $r.data.count -ge 1) "count=$($r.data.count)"
    $r = J @('read', '-Window', $H, '-AutomationId', 'CalculatorResults'); Check 'read' ($r.ok -and $r.data.text) "len=$($r.data.text.Length)"
    $r = J @('getprop', '-Window', $H, '-AutomationId', 'num7Button'); Check 'getprop' $r.ok 'ok'
    $r = J @('screenshot', '-Window', $H, '-Capture', 'window', '-Out', "$T\calc.png")
    Check 'screenshot window' ($r.ok -and $r.data.capture -eq 'window' -and $null -ne $r.data.occluded) "capture=$($r.data.capture) occ=$($r.data.occluded)"
    $r = J @('ocr', '-Window', $H, '-NoWords');               Check 'ocr -NoWords' ($r.ok -and ($null -eq $r.data.lines[0].words)) "lines=$($r.data.lines.Count)"
    $r = J @('screenshot', '-Window', $H, '-Capture', 'screen', '-Out', "$T\occ.png")
    Check 'occlusion fields' ($r.ok -and $null -ne $r.data.occluded) "occ=$($r.data.occluded) samples=$($r.data.covered_samples)"
} else { Check 'calc window' $false 'not running - skipped' }

# --- 微信（自绘，只读） ---
$wx = (J @('windows')).data | Where-Object { $_.proc -match 'Weixin' -and $_.w -gt 500 } | Select-Object -First 1
if ($wx) {
    $r = J @('ocr', '-Window', "hwnd:$($wx.hwnd)", '-Capture', 'window')
    Check 'ocr wechat window' ($r.ok -and $r.data.lines.Count -gt 0) "lines=$($r.data.lines.Count) upscale=$($r.data.upscale)"
} else { Check 'wechat window (skipped)' $true 'not visible' }

# --- 区域 OCR ---
$r = J @('ocr', '-X', '0', '-Y', '0', '-Width', '300', '-Height', '80'); Check 'ocr region' $r.ok "lines=$($r.data.lines.Count)"

# --- shell ---
$r = J @('surfaces'); Check 'surfaces' ($r.ok -and $r.data.count -eq 6) "count=$($r.data.count)"
$r = J @('tree', '-Window', 'shell:taskbar', '-Depth', '2'); Check 'shell taskbar tree' ($r.ok -and $r.data.count -gt 0) "count=$($r.data.count) backend=$($r.data.backend)"
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
"总计: $pass / $($results.Count)"
"临时目录: $T"
