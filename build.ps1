param([switch]$Console, [switch]$Install)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$wpf  = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF"
$dist = Join-Path $root "dist"
if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }

$out = Join-Path $dist "SayAllVoice.exe"
if (Test-Path $out) { Remove-Item $out -Force }

$sources = @(Get-ChildItem (Join-Path $root "src") -Filter *.cs | ForEach-Object { $_.FullName })
$res = "/resource:" + (Join-Path $root "src\commands.default.json") + ",commands.default.json"

$ico = Join-Path $root "assets\app.ico"
$target = if ($Console) { "-target:exe" } else { "-target:winexe" }

$args = @(
  "-nologo", $target, "-platform:anycpu", "-optimize+", "-warn:4", "-codepage:65001",
  "-out:$out",
  "-r:System.dll", "-r:System.Core.dll", "-r:System.Drawing.dll",
  "-r:System.Windows.Forms.dll", "-r:Microsoft.CSharp.dll",
  "-lib:$wpf", "-r:UIAutomationClient.dll", "-r:UIAutomationTypes.dll", "-r:WindowsBase.dll", "-r:System.Speech.dll",
  $res, "/resource:$ico,app.ico", ("/resource:" + (Join-Path $root "src\pinyin.txt") + ",pinyin.txt"), "-win32icon:$ico"
) + $sources

& $csc $args
if ($LASTEXITCODE -ne 0) { throw "compile failed ($LASTEXITCODE)" }
Write-Host "OK -> $out" -ForegroundColor Green

Get-Item $out | Select-Object Name, Length, LastWriteTime | Format-List
if (-not $Install) { return }

# -Install：部署到用户目录运行（受限目录里启动的进程写不了 HKCU Run，拉起 Claude 也会失败）。
$run = Join-Path $env:USERPROFILE "SayAllVoice"
if (-not (Test-Path $run)) { New-Item -ItemType Directory -Path $run | Out-Null }
Get-Process SayAllVoice -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300
Copy-Item $out $run -Force
New-Item -ItemType Directory -Force "$run\asr" | Out-Null
Copy-Item (Join-Path $root "asr\asr.cjs") "$run\asr" -Force
Write-Host "installed -> $run" -ForegroundColor Green
