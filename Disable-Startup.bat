@echo off
setlocal DisableDelayedExpansion
powershell.exe -NoLogo -NoProfile -NonInteractive -Command "$ErrorActionPreference = 'Stop'; try { $startup = [Environment]::GetFolderPath('Startup'); if ([string]::IsNullOrWhiteSpace($startup)) { throw 'Windows could not locate your Startup folder.' }; $path = Join-Path $startup 'PearlTray.lnk'; if (Test-Path -LiteralPath $path -PathType Container) { throw 'A folder named PearlTray.lnk exists in Startup; it was left untouched.' }; if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force; if (Test-Path -LiteralPath $path) { throw 'The Startup shortcut could not be removed.' }; Write-Host 'Startup disabled for the current Windows user.' } else { Write-Host 'Startup is already disabled for the current Windows user.' }; exit 0 } catch { Write-Host ('ERROR: ' + $_.Exception.Message); exit 1 }"
set "PEARLTRAY_EXIT_CODE=%ERRORLEVEL%"
echo.
pause
exit /b %PEARLTRAY_EXIT_CODE%
