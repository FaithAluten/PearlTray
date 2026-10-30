@echo off
setlocal DisableDelayedExpansion
set "PEARLTRAY_STARTUP_DIR=%~dp0"
powershell.exe -NoLogo -NoProfile -NonInteractive -Command "$ErrorActionPreference = 'Stop'; try { $root = $env:PEARLTRAY_STARTUP_DIR; $exe = Join-Path $root 'PearlTray.exe'; if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'PearlTray.exe was not found. Place this BAT beside PearlTray.exe and try again.' }; $startup = [Environment]::GetFolderPath('Startup'); if ([string]::IsNullOrWhiteSpace($startup)) { throw 'Windows could not locate your Startup folder.' }; [void][IO.Directory]::CreateDirectory($startup); $path = Join-Path $startup 'PearlTray.lnk'; if (Test-Path -LiteralPath $path -PathType Container) { throw 'A folder named PearlTray.lnk already exists in Startup.' }; $shell = New-Object -ComObject WScript.Shell; $link = $shell.CreateShortcut($path); $link.TargetPath = $exe; $link.Arguments = ''; $link.WorkingDirectory = $root; $link.IconLocation = $exe + ',0'; $link.Description = 'PearlTray'; $link.Save(); $saved = $shell.CreateShortcut($path); if ($saved.TargetPath -ne $exe -or $saved.WorkingDirectory.TrimEnd('\') -ne $root.TrimEnd('\')) { throw 'Startup shortcut verification failed.' }; Write-Host 'Startup enabled for the current Windows user.'; Write-Host 'PearlTray will start when you next sign in.'; Write-Host 'Run this BAT again if you move the application folder.'; exit 0 } catch { Write-Host ('ERROR: ' + $_.Exception.Message); exit 1 }"
set "PEARLTRAY_EXIT_CODE=%ERRORLEVEL%"
echo.
pause
exit /b %PEARLTRAY_EXIT_CODE%
