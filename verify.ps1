$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$exePath = Join-Path $projectRoot '轻启.exe'
$qaRoot = Join-Path $projectRoot 'qa'
foreach ($mode in @('--self-test', '--ui-test', '--responsiveness-test', '--icon-test', '--preview')) {
    $verificationProcess = Start-Process -FilePath $exePath -ArgumentList $mode, ('"' + $qaRoot + '"') -PassThru -WindowStyle Hidden
    if (-not $verificationProcess.WaitForExit(45000)) { $verificationProcess.Kill(); throw "$mode 验证超时。" }
    $verificationProcess.Refresh()
    if ($verificationProcess.ExitCode -ne 0) { throw "$mode 验证失败，请检查 qa 中的结果文件。" }
}
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
$probePath = Join-Path $qaRoot 'ProbeApp.exe'
& (Join-Path $frameworkRoot 'csc.exe') /nologo /target:winexe /warnaserror+ "/out:$probePath" (Join-Path $projectRoot 'tests\ProbeApp.cs')
if ($LASTEXITCODE -ne 0) { throw '启动探针编译失败。' }
$windowProbe = Join-Path $qaRoot 'WindowProbe.exe'
& (Join-Path $frameworkRoot 'csc.exe') /nologo /target:exe /warnaserror+ "/out:$windowProbe" (Join-Path $projectRoot 'tests\WindowProbe.cs')
if ($LASTEXITCODE -ne 0) { throw '窗口探针编译失败。' }
$integrationRoot = Join-Path $qaRoot ('integration-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Force $integrationRoot | Out-Null
$integrationLog = [System.Collections.Generic.List[string]]::new()
function Assert-Orbit([bool]$condition, [string]$label) {
    if (-not $condition) { throw "FAIL: $label" }
    $integrationLog.Add("PASS: $label")
}
$probeShortcut = Join-Path $integrationRoot '应用快捷方式.lnk'
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($probeShortcut)
$link.TargetPath = $probePath
$link.Save()
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link)
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
$groupId = [guid]::NewGuid().ToString('N')
$markerOne = Join-Path $integrationRoot '中文应用一'
$markerTwo = Join-Path $integrationRoot '中文应用二'
$config = @{
    Version = 1
    Settings = @{ ExitAfterLaunch = $true; DelayMs = 150 }
    Groups = @(@{
        Id = $groupId; Name = '真实启动验证'; Color = 'blue'
        Items = @(
            @{ Id = [guid]::NewGuid().ToString('N'); Name = '探针一'; Target = $probePath; Arguments = ('"' + $markerOne + '" "中文参数带空格"'); WorkingDirectory = '' },
            @{ Id = [guid]::NewGuid().ToString('N'); Name = '快捷方式探针'; Target = $probeShortcut; Arguments = ('"' + $markerTwo + '"'); WorkingDirectory = '' }
        )
    })
}
$configPath = Join-Path $integrationRoot 'config.json'
[IO.File]::WriteAllText($configPath, ($config | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
$launcherProcess = Start-Process -FilePath $exePath -ArgumentList '--data-dir', ('"' + $integrationRoot + '"'), '--group', $groupId -PassThru -WindowStyle Hidden
if (-not $launcherProcess.WaitForExit(15000)) { $launcherProcess.Kill(); throw '自动退出验证超时。' }
$launcherProcess.Refresh()
Assert-Orbit ($launcherProcess.ExitCode -eq 0) '分组启动后启动器自动正常退出'
Assert-Orbit (Test-Path -LiteralPath ($markerOne + '.started')) '分组启动真实 EXE'
Assert-Orbit (Test-Path -LiteralPath ($markerTwo + '.started')) '分组启动真实 .lnk 快捷方式'
Assert-Orbit (([IO.File]::ReadAllText($markerOne + '.started')) -eq '中文参数带空格') '中文与空格参数完整传递'
Assert-Orbit (-not (Test-Path -LiteralPath ($markerTwo + '.finished'))) '启动器退出时目标应用仍在独立运行'
$deadline = [Diagnostics.Stopwatch]::StartNew()
while (-not (Test-Path -LiteralPath ($markerTwo + '.finished')) -and $deadline.ElapsedMilliseconds -lt 8000) { Start-Sleep -Milliseconds 100 }
Assert-Orbit ((Test-Path -LiteralPath ($markerOne + '.finished')) -and (Test-Path -LiteralPath ($markerTwo + '.finished'))) '目标应用在启动器退出后完成后续工作'
# A group shortcut calls the same persisted group without manual selection.
$shortcutPath = Join-Path $integrationRoot '启动分组.lnk'
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcutPath)
$link.TargetPath = $exePath
$link.Arguments = '--data-dir "' + $integrationRoot + '" --group ' + $groupId
$link.IconLocation = $exePath + ',0'
$link.Save()
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link)
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
$markerThree = Join-Path $integrationRoot '分组快捷方式探针'
$config.Groups[0].Items = @(@{ Id = [guid]::NewGuid().ToString('N'); Name = '快捷方式启动'; Target = $exePath; Arguments = ('--probe "' + $markerThree + '"'); WorkingDirectory = '' })
[IO.File]::WriteAllText($configPath, ($config | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
$shortcutProcess = Start-Process -FilePath $shortcutPath -PassThru -WindowStyle Hidden
if ($null -ne $shortcutProcess) { if (-not $shortcutProcess.WaitForExit(15000)) { $shortcutProcess.Kill(); throw '分组快捷方式验证超时。' } }
$deadline.Restart()
while (-not (Test-Path -LiteralPath $markerThree) -and $deadline.ElapsedMilliseconds -lt 8000) { Start-Sleep -Milliseconds 100 }
Assert-Orbit (Test-Path -LiteralPath $markerThree) '桌面分组快捷方式直接启动指定分组'
# Exercise the actual mutex, application entry point and IPC between independent processes.
$markerFour = Join-Path $integrationRoot '已有窗口接收分组请求'
$config.Settings.ExitAfterLaunch = $false
$config.Groups[0].Items[0].Arguments = '--probe "' + $markerFour + '"'
[IO.File]::WriteAllText($configPath, ($config | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
$configHash = (Get-FileHash -LiteralPath $configPath).Hash
$primaryProcess = Start-Process -FilePath $exePath -ArgumentList '--data-dir', ('"' + $integrationRoot + '"') -PassThru -WindowStyle Hidden
try {
    $deadline.Restart()
    do { Start-Sleep -Milliseconds 100; $primaryProcess.Refresh() } while ($primaryProcess.MainWindowHandle -eq 0 -and $deadline.ElapsedMilliseconds -lt 6000)
    Assert-Orbit ($primaryProcess.MainWindowHandle -ne 0) '独立主实例建立可唤起窗口'
    $secondaryProcess = Start-Process -FilePath $exePath -ArgumentList '--data-dir', ('"' + $integrationRoot + '"'), '--group', $groupId -PassThru -WindowStyle Hidden
    if (-not $secondaryProcess.WaitForExit(6000)) { $secondaryProcess.Kill(); throw '第二实例转发超时。' }
    $secondaryProcess.Refresh()
    Assert-Orbit ($secondaryProcess.ExitCode -eq 0) '第二实例转发分组请求后正常退出'
    $deadline.Restart()
    while (-not (Test-Path -LiteralPath $markerFour) -and $deadline.ElapsedMilliseconds -lt 6000) { Start-Sleep -Milliseconds 100 }
    Assert-Orbit (Test-Path -LiteralPath $markerFour) '已有主窗口真实执行第二实例的分组启动请求'
    $primaryProcess.Refresh()
    Assert-Orbit (-not $primaryProcess.HasExited) '关闭自动退出时已有主实例继续运行'
    $activationProcess = Start-Process -FilePath $exePath -ArgumentList '--data-dir', ('"' + $integrationRoot + '"') -PassThru -WindowStyle Hidden
    if (-not $activationProcess.WaitForExit(6000)) { $activationProcess.Kill(); throw '重复打开唤起超时。' }
    $activationProcess.Refresh()
    Assert-Orbit ($activationProcess.ExitCode -eq 0) '重复打开轻启唤起同一已有实例'
    Assert-Orbit ((Get-FileHash -LiteralPath $configPath).Hash -eq $configHash) '重复打开与启动请求不改写分组数据'
    & $windowProbe --visible $primaryProcess.Id
    Assert-Orbit ($LASTEXITCODE -eq 0) '重复打开将已有主窗口真实显示'
    & $windowProbe --close $primaryProcess.Id
    Assert-Orbit ($LASTEXITCODE -eq 0) '向本次测试主窗口发送 Windows 关闭事件'
    if (-not $primaryProcess.WaitForExit(6000)) { throw '主实例关闭超时。' }
    $primaryProcess.Refresh()
    Assert-Orbit ($primaryProcess.ExitCode -eq 0 -and (Test-Path -LiteralPath (Join-Path $integrationRoot 'window.json'))) '主实例正常关闭并保存独立窗口偏好'
} finally {
    $primaryProcess.Refresh()
    if (-not $primaryProcess.HasExited) { $primaryProcess.Kill() }
}
# Copy only the executable to a separate folder and exercise a full WPF launch.
$portableRoot = Join-Path $qaRoot ('single-file-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$portableData = Join-Path $integrationRoot 'single-file-data'
New-Item -ItemType Directory -Path $portableRoot, $portableData -Force | Out-Null
$portableExe = Join-Path $portableRoot '轻启.exe'
Copy-Item -LiteralPath $exePath -Destination $portableExe
Assert-Orbit ((Get-ChildItem -LiteralPath $portableRoot -File | Measure-Object).Count -eq 1) '独立运行目录仅包含轻启 EXE'
$portableMarker = Join-Path $portableData 'single-file-opened.txt'
$config.Settings.ExitAfterLaunch = $true
$config.Groups[0].Items[0].Target = $portableExe
$config.Groups[0].Items[0].Arguments = '--probe "' + $portableMarker + '"'
[IO.File]::WriteAllText((Join-Path $portableData 'config.json'), ($config | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
$portableProcess = Start-Process -FilePath $portableExe -ArgumentList '--data-dir', ('"' + $portableData + '"'), '--group', $groupId -PassThru -WindowStyle Hidden
if (-not $portableProcess.WaitForExit(15000)) { $portableProcess.Kill(); throw '单文件独立运行验证超时。' }
$portableProcess.Refresh()
Assert-Orbit ($portableProcess.ExitCode -eq 0) '无配套配置文件的 EXE 启动并正常退出'
Assert-Orbit ((Test-Path -LiteralPath $portableMarker) -and (Test-Path -LiteralPath (Join-Path $portableData 'window.json'))) '单文件 EXE 完成 WPF 加载与分组真实启动'
[IO.File]::WriteAllLines((Join-Path $qaRoot 'integration-results.txt'), $integrationLog, [Text.UTF8Encoding]::new($false))
Get-Content -LiteralPath (Join-Path $qaRoot 'test-results.txt') -Encoding UTF8
Get-Content -LiteralPath (Join-Path $qaRoot 'ui-test-results.txt') -Encoding UTF8
Get-Content -LiteralPath (Join-Path $qaRoot 'responsiveness-results.txt') -Encoding UTF8
Get-Content -LiteralPath (Join-Path $qaRoot 'icon-test-results.txt') -Encoding UTF8
Get-Content -LiteralPath (Join-Path $qaRoot 'integration-results.txt') -Encoding UTF8
Write-Output '所有检查通过。'
