$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
$compilerPath = Join-Path $frameworkRoot 'csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw '需要 Windows 自带的 .NET Framework C# 编译器。' }
New-Item -ItemType Directory -Force (Join-Path $projectRoot 'qa'), (Join-Path $projectRoot 'assets') | Out-Null
$assetTool = Join-Path $projectRoot 'qa\AssetBuilder.exe'
& $compilerPath /nologo /target:exe /optimize+ /warnaserror+ "/out:$assetTool" /reference:System.Drawing.dll (Join-Path $projectRoot 'assets\AssetBuilder.cs')
if ($LASTEXITCODE -ne 0) { throw '图标生成工具编译失败。' }
& $assetTool (Join-Path $projectRoot 'assets')
if ($LASTEXITCODE -ne 0) { throw '图标生成失败。' }
$referenceNames = @('PresentationCore', 'PresentationFramework', 'WindowsBase')
$references = @('/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Xaml.dll', '/reference:System.Web.Extensions.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll')
foreach ($name in $referenceNames) { $references += "/reference:$(Join-Path $frameworkRoot "WPF\$name.dll")" }
$sourceFiles = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
$outputPath = Join-Path $projectRoot '轻启.exe'
$arguments = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/warn:4', '/warnaserror+', '/utf8output', "/out:$outputPath", "/win32icon:$(Join-Path $projectRoot 'assets\orbit.ico')", "/win32manifest:$(Join-Path $projectRoot 'src\app.manifest')", "/resource:$(Join-Path $projectRoot 'src\Theme.xaml'),Orbit.Theme") + $references + $sourceFiles
& $compilerPath @arguments
if ($LASTEXITCODE -ne 0) { throw '轻启编译失败。' }
Write-Output "已生成：$outputPath"
