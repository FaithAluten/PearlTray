param([switch]$Test, [string]$OutputDirectory = "dist", [string]$PackageName = "PearlTray-windows.zip")
$ErrorActionPreference = "Stop"
$framework = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
$compiler = Join-Path $framework "csc.exe"
if (-not (Test-Path -LiteralPath $compiler)) {
    throw ".NET Framework 4.8 is required to build PearlTray."
}
$output = Join-Path $PSScriptRoot $OutputDirectory
New-Item -ItemType Directory -Path $output -Force | Out-Null
$references = @(
    "/r:System.dll", "/r:System.Core.dll", "/r:System.Drawing.dll",
    "/r:System.Windows.Forms.dll", "/r:System.Net.Http.dll",
    "/r:System.Web.Extensions.dll", "/r:System.Security.dll"
)
$sources = @("Program.cs", "Settings.cs", "SettingsAppearance.cs", "SettingsForm.cs", "WebhookSettingsForm.cs", "HashrateMonitor.cs", "WalletMonitor.cs", "WebhookClient.cs", "TickerClient.cs", "ExchangeRateClient.cs", "AccountClient.cs", "WalletClient.cs", "TrayIconRenderer.cs", "MetricMenuRenderer.cs", "AssemblyInfo.cs") |
    ForEach-Object { Join-Path $PSScriptRoot $_ }
$iconTool = Join-Path $output "CreateIcon.exe"
& $compiler /nologo /target:exe /optimize+ /warnaserror+ "/out:$iconTool" `
    /r:System.dll /r:System.Drawing.dll (Join-Path $PSScriptRoot "tools\CreateIcon.cs") `
    (Join-Path $PSScriptRoot "TrayIconRenderer.cs")
if ($LASTEXITCODE -ne 0) { throw "Icon tool compilation failed." }
$appIcon = Join-Path $output "PearlTray.ico"
& $iconTool $appIcon
if ($LASTEXITCODE -ne 0) { throw "Icon generation failed." }
$executable = Join-Path $output "PearlTray.exe"
$fontResources = @("Regular", "ExtraBold") | ForEach-Object {
    "/resource:$PSScriptRoot\assets\JetBrainsMono-$_.ttf,PearlTray.JetBrainsMono-$_.ttf"
}
& $compiler /nologo /target:winexe /optimize+ /warnaserror+ /codepage:65001 /utf8output `
    "/out:$executable" "/win32manifest:$PSScriptRoot\app.manifest" "/win32icon:$appIcon" @fontResources @references @sources
if ($LASTEXITCODE -ne 0) { throw "PearlTray compilation failed." }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "app.config") -Destination "$executable.config" -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "README.md") -Destination (Join-Path $output "README.md") -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "THIRD-PARTY-NOTICES.txt") -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "assets\OFL-JetBrainsMono.txt") -Destination $output -Force
$startupScripts = @("Enable-Startup.bat", "Disable-Startup.bat") |
    ForEach-Object { Join-Path $output $_ }
foreach ($name in @("Enable-Startup.bat", "Disable-Startup.bat")) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $output -Force
}
if ($Test) {
    $testExecutable = Join-Path $output "PearlTray.Tests.exe"
    & $compiler /nologo /target:exe /optimize+ /warnaserror+ /codepage:65001 /utf8output `
        "/out:$testExecutable" "/r:$executable" @references `
        (Join-Path $PSScriptRoot "tests\PearlTray.Tests.cs")
    if ($LASTEXITCODE -ne 0) { throw "Test compilation failed." }
    & $testExecutable
    if ($LASTEXITCODE -ne 0) { throw "PearlTray tests failed." }
    $dpiTestExecutable = Join-Path $output "PearlTray.DpiTests.exe"
    & $compiler /nologo /target:exe /optimize+ /warnaserror+ /codepage:65001 /utf8output `
        "/out:$dpiTestExecutable" "/win32manifest:$PSScriptRoot\app.manifest" `
        "/r:$executable" @references (Join-Path $PSScriptRoot "tests\PearlTray.Tests.cs")
    if ($LASTEXITCODE -ne 0) { throw "DPI test compilation failed." }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "app.config") -Destination "$dpiTestExecutable.config" -Force
    & $dpiTestExecutable --settings-summary
    if ($LASTEXITCODE -ne 0) { throw "Settings summary DPI tests failed." }
}
$release = Join-Path $PSScriptRoot $PackageName
# Package only public runtime files; never include local settings or test artifacts.
Compress-Archive -LiteralPath (@(
    $executable, "$executable.config", (Join-Path $output "README.md"),
    (Join-Path $output "THIRD-PARTY-NOTICES.txt"), (Join-Path $output "OFL-JetBrainsMono.txt")
) + $startupScripts) -DestinationPath $release -Force
Write-Output "Built: $executable"
Write-Output "Package: $release"
